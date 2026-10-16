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

/// <summary>An access area (ACC-04): Employees, Locations, ...; each has View and Manage.</summary>
public sealed record AccessArea(string Key, AreaName Name);

public sealed record AreaName(string En, string Sw);

/// <summary>What a group may do in one area: none, view or manage.</summary>
public sealed record AreaLevel(string Area, string Level);

/// <summary>
/// One of the organization's groups (administrators, hr, payroll, approvers), as the organization has it:
/// whether it is still the standard, how many people are in it (any companies), and its level per area.
/// </summary>
public sealed record TeamGroup(string Key, string Name, string? Description, bool Standard, bool Custom, bool AllCompanies, IReadOnlyList<Guid> CompanyIds,
    int People, IReadOnlyList<AreaLevel> Areas);

/// <summary>A group the organization adds (ACC-08): a name, an optional description, a level per area, and the companies it is for (all to start).</summary>
public sealed record NewGroupRequest(string? Name, string? Description, Dictionary<string, string>? Areas, bool? AllCompanies, List<Guid>? CompanyIds);

/// <summary>One of the organization's own groups, changed: its name, description and companies (left out: unchanged).</summary>
public sealed record GroupDetailsRequest(string? Name, string? Description, bool? AllCompanies, List<Guid>? CompanyIds);

public sealed record TeamGroups(IReadOnlyList<AccessArea> Areas, IReadOnlyList<TeamGroup> Items);

/// <summary>A group's new access: a level (none, view or manage) per area; areas left out are none.</summary>
public sealed record GroupAccessRequest(Dictionary<string, string>? Areas);

/// <summary>Someone's new access: the groups whose roles they get (one or more), and all companies or chosen ones.</summary>
public sealed record AccessRequest(List<string>? Groups, bool AllCompanies, List<Guid>? CompanyIds);

/// <summary>
/// ACC-03, inviting colleagues into the organization "as HR, for these companies", by email or SMS.
/// Colleagues never sign themselves up. The link (7 days, once) carries a random token whose
/// SHA-256 alone is kept; resending replaces it; revoking ends it. Changes need team.manage for every
/// company; seeing the team, its invitations and groups needs team.view (ACC-04). Accepting is AUTH-08 (/api/invitations in Identity).
/// </summary>
public static class TeamEndpoints
{
    public static readonly string[] Groups = ["administrators", "hr", "payroll", "approvers"];

    /// <summary>The organization's group keys: the standard ones, then its own (ACC-08) by name. Row-level security keeps them to this organization.</summary>
    private static async Task<List<string>> GroupKeysAsync(OrganizationsDbContext db, CancellationToken ct) =>
        [.. Groups, .. await db.Database.SqlQuery<string>(
            $"select key as \"Value\" from roles where organization_id is not null and deleted_at is null order by name_en").ToListAsync(ct)];

    /// <summary>The groups asked for, in the usual order, or null when there are none or one isn't the organization's.</summary>
    private static async Task<string[]?> GroupsFromAsync(List<string>? asked, OrganizationsDbContext db, CancellationToken ct)
    {
        var groups = (asked ?? []).Distinct().ToList();
        var known = await GroupKeysAsync(db, ct);
        return groups.Count > 0 && groups.All(known.Contains) ? known.Where(groups.Contains).ToArray() : null;
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
            Access.Require(tenant, Permissions.TeamView);
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

            var groups = await GroupsFromAsync(request.Groups, db, ct);
            if (groups is null)
            {
                errors["groups"] = "Choose one or more of your groups.";
            }

            var companyIds = (request.CompanyIds ?? []).Distinct().ToList();
            if (groups is not null && !request.AllCompanies && companyIds.Count > 0)
            {
                var limits = (await GroupsAsync(db, ct)).Items.Where(g => groups.Contains(g.Key) && !g.AllCompanies);
                if (limits.Any(g => companyIds.Any(c => !g.CompanyIds.Contains(c))))
                {
                    throw OnboardingErrors.GroupCompanyNotAllowed();
                }
            }

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

        // ACC-04: a wrong group or company fixed before they accept. The link and its 7 days stay the same.
        invitations.MapPut("/{id:guid}/access", async (Guid id, AccessRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (_, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var invitation = await OpenAsync(db, id, ct);
            var (groups, companyIds) = await ValidAccessAsync(request, db, ct);
            var now = clock.GetUtcNow();
            invitation.GroupKeys = groups;
            invitation.AllCompanies = request.AllCompanies;
            invitation.Companies.Clear();
            if (!request.AllCompanies)
            {
                invitation.Companies.AddRange(companyIds.Select(c => new InvitationCompany { OrganizationId = organizationId, InvitationId = invitation.Id, CompanyId = c }));
            }

            invitation.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToRow(invitation, now));
        }).Produces<InvitationRow>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

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

        // ACC-04: what each group can do, per area; changing it applies to everyone in the group, in this organization only.
        var groups = app.MapGroup("/api/team/groups").WithTags("Team");
        groups.MapGet("/", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            return Results.Ok(await GroupsAsync(db, ct));
        }).Produces<TeamGroups>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .RequirePermission(Permissions.TeamView);

        groups.MapPut("/{key}/access", async (string key, GroupAccessRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            if (!(await GroupKeysAsync(db, ct)).Contains(key))
            {
                throw OnboardingErrors.AreaLevelInvalid();
            }

            var keys = await PermissionKeysAsync(request.Areas, db, ct);
            await TeamChangeAsync(() => db.Database.ExecuteSqlAsync($"select set_role_access({key}, {keys})", ct));
            return Results.Ok((await GroupsAsync(db, ct)).Items.Single(g => g.Key == key));
        }).Produces<TeamGroup>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

        // ACC-08: groups the organization adds, with their own name and level per area.
        groups.MapPost("/", async (NewGroupRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var keys = await PermissionKeysAsync(request.Areas, db, ct);
            string? key = null;
            var all = request.AllCompanies ?? true;
            var companies = all ? null : (request.CompanyIds ?? []).Distinct().ToArray();
            await TeamChangeAsync(async () => { key = await db.Database.SqlQuery<string>(
                $"select add_group({request.Name ?? ""}, {request.Description}, {keys}, {all}, {companies}) as \"Value\"").SingleAsync(ct); return 1; });
            return Results.Created($"/api/team/groups/{key}", (await GroupsAsync(db, ct)).Items.Single(g => g.Key == key));
        }).Produces<TeamGroup>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden).RequirePermission(Permissions.Team);

        groups.MapPatch("/{key}", async (string key, GroupDetailsRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            var current = (await GroupsAsync(db, ct)).Items.SingleOrDefault(g => g.Key == key && g.Custom) ?? throw OnboardingErrors.AreaLevelInvalid();
            var all = request.AllCompanies ?? current.AllCompanies;
            var companies = all ? null : (request.AllCompanies is null ? current.CompanyIds : request.CompanyIds ?? []).Distinct().ToArray();
            await TeamChangeAsync(() => db.Database.ExecuteSqlAsync(
                $"select update_group({key}, {request.Name ?? current.Name}, {request.Description}, {all}, {companies})", ct));
            return Results.Ok((await GroupsAsync(db, ct)).Items.Single(g => g.Key == key));
        }).Produces<TeamGroup>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).RequirePermission(Permissions.Team);

        // Only once nobody is in it and no open invitation names it (ACC-8012).
        groups.MapDelete("/{key}", async (string key, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            await TeamChangeAsync(() => db.Database.ExecuteSqlAsync($"select delete_group({key})", ct));
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

        // Back to standard: the group does what the standard role does again.
        groups.MapDelete("/{key}/access", async (string key, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Team);
            if (!Groups.Contains(key))
            {
                throw OnboardingErrors.AreaLevelInvalid();
            }

            await TeamChangeAsync(() => db.Database.ExecuteSqlAsync($"select reset_role_access({key})", ct));
            return Results.Ok((await GroupsAsync(db, ct)).Items.Single(g => g.Key == key));
        }).Produces<TeamGroup>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Team);

        // ONB-11, the people already in: their groups and companies; change them, or remove someone.
        var members = app.MapGroup("/api/team/members").WithTags("Team");
        members.MapGet("/", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.TeamView);
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
            var (groups, companyIds) = await ValidAccessAsync(request, db, ct);
            await TeamChangeAsync(() => db.Database.ExecuteSqlAsync(
                $"select set_member_access({id}, {groups}, {request.AllCompanies}, {(request.AllCompanies ? null : companyIds)})", ct));
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

    private sealed record AreaRow(string Key, string NameEn, string NameSw);
    private sealed record GroupPermissionRow(string GroupKey, string? PermissionKey, bool Standard);
    private sealed record GroupRow(string GroupKey, string Name, string? Description, bool Custom, Guid[] CompanyIds, int People);

    /// <summary>A level per area as permission keys (none: nothing; view or manage: that key), or ACC-8010.</summary>
    private static async Task<string[]> PermissionKeysAsync(Dictionary<string, string>? levels, OrganizationsDbContext db, CancellationToken ct)
    {
        var areas = await db.Database.SqlQuery<string>($"select key as \"Value\" from permission_areas").ToListAsync(ct);
        var asked = levels ?? [];
        return asked.Any(a => !areas.Contains(a.Key) || a.Value is not ("none" or "view" or "manage"))
            ? throw OnboardingErrors.AreaLevelInvalid()
            : asked.Where(a => a.Value != "none").Select(a => $"{a.Key}.{a.Value}").ToArray();
    }

    /// <summary>The organization's groups: the standard four, then its own by name, each with its role.</summary>
    private const string GroupsCte = """
        with x as (
            select v.group_key, r.id as role_id, false as custom, v.sort, coalesce(d.name, v.group_key) as name, d.description
            from (values ('administrators', 'admin', 1), ('hr', 'hr', 2), ('payroll', 'payroll', 3), ('approvers', 'approver', 4)) v (group_key, role_key, sort)
            join roles r on r.key = v.role_key and r.organization_id is null
            left join security_groups d on d.default_key = v.group_key
            union all
            select r.key, r.id, true, 5, r.name_en,
                   (select g.description from security_groups g join security_group_roles gr on gr.security_group_id = g.id and gr.role_id = r.id
                    where g.all_companies order by g.created_at limit 1)
            from roles r where r.organization_id is not null and r.deleted_at is null)
        """;

    /// <summary>The areas, and each group's level per area as this organization has it (row-level security keeps it to this organization).</summary>
    private static async Task<TeamGroups> GroupsAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        var areas = await db.Database.SqlQuery<AreaRow>($"select key, name_en, name_sw from permission_areas order by sort_order").ToListAsync(ct);
        var permissions = await db.Database.SqlQueryRaw<GroupPermissionRow>(GroupsCte + """
            select x.group_key, k.permission_key, rc.role_id is null as standard
            from x
            left join role_customizations rc on rc.role_id = x.role_id
            left join lateral (
                select rp.permission_key from role_permissions rp where rp.role_id = x.role_id and rc.role_id is null
                union all
                select cp.permission_key from role_customization_permissions cp where cp.role_id = x.role_id and rc.role_id is not null) k on true
            """).ToListAsync(ct);
        var rows = await db.Database.SqlQueryRaw<GroupRow>(GroupsCte + """
            select x.group_key, x.name, x.description, x.custom,
                   array(select rc.company_id from role_companies rc where rc.role_id = x.role_id order by rc.company_id) as company_ids,
                   (select count(distinct gm.membership_id)::int from security_group_roles gr
                    join security_group_members gm on gm.security_group_id = gr.security_group_id and gm.removed_at is null
                    join memberships m on m.id = gm.membership_id and m.status = 'active'
                    where gr.role_id = x.role_id) as people
            from x order by x.sort, x.name
            """).ToListAsync(ct);
        string Level(string group, string area)
        {
            var held = permissions.Where(p => p.GroupKey == group).Select(p => p.PermissionKey).ToHashSet();
            return held.Contains($"{area}.manage") ? "manage" : held.Contains($"{area}.view") ? "view" : "none";
        }

        return new TeamGroups(
            areas.Select(a => new AccessArea(a.Key, new AreaName(a.NameEn, a.NameSw))).ToList(),
            rows.Select(r => new TeamGroup(r.GroupKey, r.Name, r.Description,
                !r.Custom && permissions.Where(p => p.GroupKey == r.GroupKey).All(p => p.Standard), r.Custom, r.CompanyIds.Length == 0, r.CompanyIds, r.People,
                areas.Select(a => new AreaLevel(a.Key, Level(r.GroupKey, a.Key))).ToList())).ToList());
    }

    /// <summary>The database's refusals (team.sql) as their errors.</summary>
    private static async Task TeamChangeAsync(Func<Task<int>> change)
    {
        try
        {
            await change();
        }
        catch (Npgsql.PostgresException error) when (error.MessageText is "ACC-8001" or "ACC-8007" or "ACC-8008" or "ACC-8009" or "ACC-8010" or "ACC-8011"
            or "ACC-8012" or "ACC-8013" or "ACC-8014")
        {
            throw error.MessageText switch
            {
                "ACC-8013" => OnboardingErrors.GroupCompanyNotAllowed(),
                "ACC-8014" => OnboardingErrors.GroupCompaniesInUse(),
                "ACC-8011" => OnboardingErrors.GroupNameInvalid(),
                "ACC-8012" => OnboardingErrors.GroupInUse(),
                "ACC-8010" => OnboardingErrors.AreaLevelInvalid(),
                "ACC-8009" => OnboardingErrors.AccessInvalid(new Dictionary<string, string> { ["groups"] = "Choose one or more groups." }),
                "ACC-8007" => OnboardingErrors.LastAdministrator(),
                "ACC-8008" => OnboardingErrors.MemberNotFound(),
                _ => OnboardingErrors.NoPermission(),
            };
        }
    }

    /// <summary>New access, checked: one or more known groups, and all companies or at least one active company (ACC-8009).</summary>
    private static async Task<(string[] Groups, Guid[] CompanyIds)> ValidAccessAsync(AccessRequest request, OrganizationsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var groups = await GroupsFromAsync(request.Groups, db, ct);
        if (groups is null)
        {
            errors["groups"] = "Choose one or more of your groups.";
        }

        var companyIds = (request.CompanyIds ?? []).Distinct().ToArray();
        if (!request.AllCompanies
            && (companyIds.Length == 0 || await db.Companies.CountAsync(c => companyIds.Contains(c.Id) && c.Status == "active", ct) != companyIds.Length))
        {
            errors["companyIds"] = "Choose at least one company, or all companies.";
        }

        return errors.Count > 0 ? throw OnboardingErrors.AccessInvalid(errors) : (groups!, companyIds);
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
            ["firstName"] = invitation.FirstName, ["inviter"] = inviter, ["organization"] = organization, ["role"] = await RoleListAsync(invitation.GroupKeys, db, ct),
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
    private static async Task<string> RoleListAsync(IReadOnlyList<string> keys, OrganizationsDbContext db, CancellationToken ct)
    {
        var own = await db.Database.SqlQuery<GroupNameRow>($"select key, name_en as name from roles where organization_id is not null").ToListAsync(ct);
        var names = keys.Select(k => RoleNames.TryGetValue(k, out var name) ? name : own.FirstOrDefault(o => o.Key == k)?.Name ?? k).ToList();
        return names.Count == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }

    private sealed record GroupNameRow(string Key, string Name);

    private static InvitationRow ToRow(Invitation i, DateTimeOffset now) =>
        new(i.Id, i.FirstName, i.LastName, i.Email, i.Phone, i.GroupKeys, i.AllCompanies, i.Companies.Select(c => c.CompanyId).ToList(),
            i.ExpiresAt > now ? "open" : "expired", i.ExpiresAt, i.SentAt, i.SendCount);
}
