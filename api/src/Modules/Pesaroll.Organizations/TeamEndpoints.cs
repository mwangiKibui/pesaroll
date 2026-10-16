using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pesaroll.Identity;
using Pesaroll.Notifications;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// Inviting a colleague: first and last name, an email and/or Kenyan phone number (the message goes
/// to each), the groups whose roles they get (one or more of administrators, hr, payroll, approvers:
/// HR and Payroll for one person is common), and all companies or chosen ones.
/// </summary>
public sealed record InviteRequest(string? FirstName, string? LastName, string? Email, string? Phone, List<string>? Groups, bool AllCompanies, List<Guid>? CompanyIds);

/// <summary>An invitation not yet accepted or cancelled: open, or expired (resend it for a new link).</summary>
public sealed record InvitationRow(Guid Id, string FirstName, string LastName, string? Email, string? Phone, IReadOnlyList<string> Groups, bool AllCompanies,
    IReadOnlyList<Guid> CompanyIds, string Status, DateTimeOffset ExpiresAt, DateTimeOffset SentAt, int SendCount);

/// <summary>One of a person's groups: its role as a group key (null for a group with several roles), its name and companies.</summary>
public sealed record MemberAccess(string? Group, string Name, bool AllCompanies, IReadOnlyList<Guid> CompanyIds);

/// <summary>Someone in the organization: who, since when, whether it's the person asking, and their groups.</summary>
public sealed record TeamMember(Guid MembershipId, Guid UserId, string FirstName, string LastName, string? Email, string? Phone, DateTimeOffset JoinedAt,
    bool IsYou, IReadOnlyList<MemberAccess> Groups);

/// <summary>Someone's new access: the groups whose roles they get (one or more), and all companies or chosen ones.</summary>
public sealed record AccessRequest(List<string>? Groups, bool AllCompanies, List<Guid>? CompanyIds);

/// <summary>
/// ACC-03, inviting colleagues into the organization "as HR, for these companies", by email or SMS.
/// Colleagues never sign themselves up. The link (7 days, once) carries a random token whose
/// SHA-256 alone is kept; resending replaces it; revoking ends it. Administrators only
/// (team.manage for every company). Accepting is AUTH-08 (/api/invitations in Identity).
/// </summary>
public static class TeamEndpoints
{
    public static readonly string[] Groups = ["administrators", "hr", "payroll", "approvers"];

    /// <summary>The groups asked for, in the usual order, or null when there are none or one isn't known.</summary>
    private static string[]? GroupsFrom(List<string>? asked)
    {
        var groups = (asked ?? []).Distinct().ToList();
        return groups.Count > 0 && groups.All(Groups.Contains) ? Groups.Where(groups.Contains).ToArray() : null;
    }
    private static readonly TimeSpan Lasts = TimeSpan.FromDays(7);
    private static readonly Dictionary<string, string> RoleNames = new()
    {
        ["administrators"] = "an administrator", ["hr"] = "HR", ["payroll"] = "Payroll", ["approvers"] = "an approver",
    };

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var invitations = app.MapGroup("/api/team/invitations").WithTags("Team");

        invitations.MapGet("/", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var open = await db.Invitations.AsNoTracking().Include(i => i.Companies)
                .Where(i => i.AcceptedAt == null && i.RevokedAt == null).OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
            return Results.Ok(open.Select(i => ToRow(i, clock.GetUtcNow())).ToList());
        }).Produces<List<InvitationRow>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden).RequirePermission(Permissions.TeamView);

        invitations.MapPost("/", async (InviteRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http, Notifier notifier,
            IConfiguration configuration, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var errors = new Dictionary<string, string>();
            string Name(string? value, string field, string label)
            {
                var v = value?.Trim() ?? "";
                if (v.Length is 0 or > 60)
                {
                    errors[field] = $"Enter their {label} (up to 60 characters).";
                }

                return v;
            }

            var firstName = Name(request.FirstName, "firstName", "first name");
            var lastName = Name(request.LastName, "lastName", "last name");
            var email = string.IsNullOrWhiteSpace(request.Email) ? null : Identifiers.NormalizeEmail(request.Email);
            var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : Identifiers.NormalizePhone(request.Phone);
            if ((email is null && !string.IsNullOrWhiteSpace(request.Email)) || (phone is null && !string.IsNullOrWhiteSpace(request.Phone)))
            {
                errors["contact"] = "Enter a valid email address or a Kenyan mobile number, like 0712 345 678.";
            }
            else if (email is null && phone is null)
            {
                errors["contact"] = "Enter their email address or mobile number, so the invitation can reach them.";
            }

            var groups = GroupsFrom(request.Groups);
            if (groups is null)
            {
                errors["groups"] = "Choose one or more groups: Administrators, HR, Payroll or Approvers.";
            }

            var companyIds = (request.CompanyIds ?? []).Distinct().ToList();
            if (!request.AllCompanies)
            {
                var known = await db.Companies.Where(c => companyIds.Contains(c.Id) && c.Status == "active").CountAsync(ct);
                if (companyIds.Count == 0 || known != companyIds.Count)
                {
                    errors["companyIds"] = "Choose at least one company, or all companies.";
                }
            }

            if (errors.Count > 0)
            {
                throw OnboardingErrors.InvitationInvalid(errors);
            }

            // Not someone already here, nor someone with an invitation still open.
            var member = await db.Database.SqlQuery<string>($"""
                select u.full_name as "Value" from memberships m join users u on u.id = m.user_id
                where m.organization_id = {organizationId} and m.status = 'active' and (u.email = {email} or u.phone = {phone})
                """).FirstOrDefaultAsync(ct);
            if (member is not null)
            {
                throw OnboardingErrors.AlreadyMember(member);
            }

            var now = clock.GetUtcNow();
            var waiting = await db.Invitations
                .Where(i => i.AcceptedAt == null && i.RevokedAt == null && ((email != null && i.Email == email) || (phone != null && i.Phone == phone)))
                .ToListAsync(ct);
            if (waiting.FirstOrDefault(i => i.ExpiresAt > now) is { } open)
            {
                throw OnboardingErrors.AlreadyInvited(open.Email == email && email is not null ? email : phone!);
            }

            // Expired ones make way for the new one.
            foreach (var old in waiting)
            {
                old.RevokedAt = now;
                old.RevokedBy = userId;
                old.UpdatedAt = now;
            }

            var token = RandomNumberGenerator.GetBytes(32);
            var invitation = new Invitation
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, FirstName = firstName, LastName = lastName, Email = email, Phone = phone,
                GroupKeys = groups!, AllCompanies = request.AllCompanies, InvitedBy = userId, TokenHash = SHA256.HashData(token),
                ExpiresAt = now + Lasts, SentAt = now, SendCount = 1, CreatedAt = now, UpdatedAt = now,
            };
            if (!request.AllCompanies)
            {
                invitation.Companies.AddRange(companyIds.Select(c => new InvitationCompany { OrganizationId = organizationId, InvitationId = invitation.Id, CompanyId = c }));
            }

            db.Invitations.Add(invitation);
            await db.SaveChangesAsync(ct);
            await SendAsync(invitation, token, db, notifier, configuration, ct);
            return Results.Created($"/api/team/invitations/{invitation.Id}", ToRow(invitation, now));
        }).Produces<InvitationRow>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

        // A new link (the old one stops working) and 7 more days, sent again.
        invitations.MapPost("/{id:guid}/resend", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, Notifier notifier,
            IConfiguration configuration, TimeProvider clock, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var invitation = await OpenAsync(db, id, ct);
            var token = RandomNumberGenerator.GetBytes(32);
            var now = clock.GetUtcNow();
            invitation.TokenHash = SHA256.HashData(token);
            invitation.ExpiresAt = now + Lasts;
            invitation.SentAt = now;
            invitation.SendCount++;
            invitation.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await SendAsync(invitation, token, db, notifier, configuration, ct);
            return Results.Ok(ToRow(invitation, now));
        }).Produces<InvitationRow>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

        // Revoking: the link stops working; the row stays, so who was invited when stays known.
        invitations.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var invitation = await OpenAsync(db, id, ct);
            invitation.RevokedAt = clock.GetUtcNow();
            invitation.RevokedBy = userId;
            invitation.UpdatedAt = invitation.RevokedAt.Value;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

        // ONB-11, the people already in: their groups and companies; change them, or remove someone.
        var members = app.MapGroup("/api/team/members").WithTags("Team");
        members.MapGet("/", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var rows = await db.Database.SqlQuery<MemberRow>($"""
                select m.id as membership_id, u.id as user_id, u.first_name, u.last_name, u.email, u.phone, m.created_at as joined_at,
                       g.id as group_id, g.name as group_name, g.all_companies as group_all_companies,
                       (select case when count(*) = 1 then min(r.key) end from security_group_roles gr join roles r on r.id = gr.role_id
                        where gr.security_group_id = g.id) as role_key,
                       array(select gc.company_id from security_group_companies gc where gc.security_group_id = g.id) as group_company_ids
                from memberships m
                join users u on u.id = m.user_id
                left join security_group_members gm on gm.membership_id = m.id and gm.removed_at is null
                left join security_groups g on g.id = gm.security_group_id
                where m.organization_id = {organizationId} and m.status = 'active'
                order by m.created_at, g.name
                """).ToListAsync(ct);
            var team = rows.GroupBy(r => r.MembershipId).Select(p =>
            {
                var first = p.First();
                var groups = p.Where(r => r.GroupId is not null).Select(r => new MemberAccess(GroupKey(r.RoleKey), r.GroupName!, r.GroupAllCompanies == true,
                    r.GroupCompanyIds ?? [])).ToList();
                return new TeamMember(first.MembershipId, first.UserId, first.FirstName, first.LastName, first.Email, first.Phone, first.JoinedAt,
                    first.UserId == userId, groups);
            }).ToList();
            return Results.Ok(team);
        }).Produces<List<TeamMember>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden).RequirePermission(Permissions.TeamView);

        // Into the group for this role and companies, out of the others. Never the last administrator.
        members.MapPut("/{id:guid}/access", async (Guid id, AccessRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var errors = new Dictionary<string, string>();
            var groups = GroupsFrom(request.Groups);
            if (groups is null)
            {
                errors["groups"] = "Choose one or more groups: Administrators, HR, Payroll or Approvers.";
            }

            var companyIds = (request.CompanyIds ?? []).Distinct().ToArray();
            if (!request.AllCompanies
                && (companyIds.Length == 0 || await db.Companies.CountAsync(c => companyIds.Contains(c.Id) && c.Status == "active", ct) != companyIds.Length))
            {
                errors["companyIds"] = "Choose at least one company, or all companies.";
            }

            if (errors.Count > 0)
            {
                throw OnboardingErrors.AccessInvalid(errors);
            }

            await TeamChangeAsync(() => db.Database.ExecuteSqlAsync(
                $"select set_member_access({id}, {groups!}, {request.AllCompanies}, {(request.AllCompanies ? null : companyIds)})", ct));
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

        // Out of the organization and every group. Never the last administrator.
        members.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            await TeamChangeAsync(() => db.Database.ExecuteSqlAsync($"select remove_member({id})", ct));
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

        return app;
    }

    /// <summary>A row of the team query: a person and one of their groups (none: nulls).</summary>
    private sealed record MemberRow(Guid MembershipId, Guid UserId, string FirstName, string LastName, string? Email, string? Phone, DateTimeOffset JoinedAt,
        Guid? GroupId, string? GroupName, bool? GroupAllCompanies, string? RoleKey, Guid[]? GroupCompanyIds);

    /// <summary>A system role's key as the group key the app uses (admin → administrators).</summary>
    private static string? GroupKey(string? roleKey) => roleKey switch
    {
        "admin" => "administrators", "approver" => "approvers", null => null, _ => roleKey,
    };

    /// <summary>The database's refusals (team.sql) as their errors.</summary>
    private static async Task TeamChangeAsync(Func<Task<int>> change)
    {
        try
        {
            await change();
        }
        catch (Npgsql.PostgresException error) when (error.MessageText is "ACC-8001" or "ACC-8007" or "ACC-8008" or "ACC-8009")
        {
            throw error.MessageText switch
            {
                "ACC-8009" => OnboardingErrors.AccessInvalid(new Dictionary<string, string> { ["groups"] = "Choose one or more groups." }),
                "ACC-8007" => OnboardingErrors.LastAdministrator(),
                "ACC-8008" => OnboardingErrors.MemberNotFound(),
                _ => OnboardingErrors.NoPermission(),
            };
        }
    }

    private static async Task<Invitation> OpenAsync(OrganizationsDbContext db, Guid id, CancellationToken ct)
    {
        var invitation = await db.Invitations.Include(i => i.Companies).SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw OnboardingErrors.InvitationNotFound();
        return invitation.AcceptedAt is null && invitation.RevokedAt is null ? invitation : throw OnboardingErrors.InvitationClosed();
    }

    /// <summary>The message, to the email and the phone they were invited by: who, where, as what, and the link. Nothing about pay.</summary>
    private static async Task SendAsync(Invitation invitation, byte[] token, OrganizationsDbContext db, Notifier notifier, IConfiguration configuration,
        CancellationToken ct)
    {
        var organization = await db.Organizations.Where(o => o.Id == invitation.OrganizationId).Select(o => o.Name).SingleAsync(ct);
        var inviter = await db.Database.SqlQuery<string>($"""select full_name as "Value" from users where id = {invitation.InvitedBy}""").SingleAsync(ct);
        var data = new Dictionary<string, string>
        {
            ["firstName"] = invitation.FirstName, ["inviter"] = inviter, ["organization"] = organization, ["role"] = RoleList(invitation.GroupKeys),
            ["link"] = $"{(configuration["App:Url"] ?? "https://app.pesaroll.co.ke").TrimEnd('/')}/invite/{Base64Url.EncodeToString(token)}",
        };
        if (invitation.Email is not null)
        {
            await notifier.QueueAsync(new Message("email", invitation.Email, "team_invitation", data), ct);
        }

        if (invitation.Phone is not null)
        {
            await notifier.QueueAsync(new Message("sms", invitation.Phone, "team_invitation", data), ct);
        }
    }

    /// <summary>"HR and Payroll", "an administrator": the roles as the message reads them.</summary>
    private static string RoleList(IReadOnlyList<string> keys)
    {
        var names = keys.Select(k => RoleNames[k]).ToList();
        return names.Count == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }

    private static InvitationRow ToRow(Invitation i, DateTimeOffset now) =>
        new(i.Id, i.FirstName, i.LastName, i.Email, i.Phone, i.GroupKeys, i.AllCompanies, i.Companies.Select(c => c.CompanyId).ToList(),
            i.ExpiresAt > now ? "open" : "expired", i.ExpiresAt, i.SentAt, i.SendCount);
}
