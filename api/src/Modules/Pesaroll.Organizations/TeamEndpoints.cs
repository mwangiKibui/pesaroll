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
/// to each), the group whose role they get (administrators, hr, payroll, approvers), and all
/// companies or chosen ones.
/// </summary>
public sealed record InviteRequest(string? FirstName, string? LastName, string? Email, string? Phone, string? Group, bool AllCompanies, List<Guid>? CompanyIds);

/// <summary>An invitation not yet accepted or cancelled: open, or expired (resend it for a new link).</summary>
public sealed record InvitationRow(Guid Id, string FirstName, string LastName, string? Email, string? Phone, string Group, bool AllCompanies,
    IReadOnlyList<Guid> CompanyIds, string Status, DateTimeOffset ExpiresAt, DateTimeOffset SentAt, int SendCount);

/// <summary>
/// ACC-03, inviting colleagues into the organization "as HR, for these companies", by email or SMS.
/// Colleagues never sign themselves up. The link (7 days, once) carries a random token whose
/// SHA-256 alone is kept; resending replaces it; revoking ends it. Administrators only
/// (team.manage for every company). Accepting is AUTH-08 (/api/invitations in Identity).
/// </summary>
public static class TeamEndpoints
{
    public static readonly string[] Groups = ["administrators", "hr", "payroll", "approvers"];
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
        }).Produces<List<InvitationRow>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

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

            if (request.Group is null || !Groups.Contains(request.Group))
            {
                errors["group"] = "Choose a group: Administrators, HR, Payroll or Approvers.";
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
                GroupKey = request.Group!, AllCompanies = request.AllCompanies, InvitedBy = userId, TokenHash = SHA256.HashData(token),
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
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

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
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

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
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
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
            ["firstName"] = invitation.FirstName, ["inviter"] = inviter, ["organization"] = organization, ["role"] = RoleNames[invitation.GroupKey],
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

    private static InvitationRow ToRow(Invitation i, DateTimeOffset now) =>
        new(i.Id, i.FirstName, i.LastName, i.Email, i.Phone, i.GroupKey, i.AllCompanies, i.Companies.Select(c => c.CompanyId).ToList(),
            i.ExpiresAt > now ? "open" : "expired", i.ExpiresAt, i.SentAt, i.SendCount);
}
