using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Shared.Audit;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// ACC-05: every time pay (or the account someone is paid into) is sent, who saw whose and when goes
/// into sensitive_views, one row per request: never the amounts (CLAUDE.md rule 8). The first part of
/// the audit log (PLAT-03).
/// </summary>
public static class SensitiveViews
{
    public static async Task RecordAsync(OrganizationsDbContext db, TenantContext tenant, HttpContext http, string kind, IReadOnlyList<Guid> whose,
        CancellationToken ct)
    {
        if (whose.Count == 0 || tenant.UserId is not Guid userId || tenant.OrganizationId is not Guid organizationId)
        {
            return;
        }

        var subjects = whose.Distinct().ToArray();
        var id = Guid.CreateVersion7();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"""
            insert into sensitive_views (id, organization_id, user_id, kind, subject_ids, reference)
            values ({id}, {organizationId}, {userId}, {kind}, {subjects}, {http.TraceIdentifier})
            """, ct);
        // In the audit log as pay.viewed (PLAT-03): how many people, never whose pay or what it is; whose is in sensitive_views.
        await Audit.RecordAsync(db, AuditActions.PayViewed, null, new AuditSubject("sensitive_view", id, null),
            new Dictionary<string, object?> { ["people"] = subjects.Length }, ct);
        await transaction.CommitAsync(ct);
    }
}
