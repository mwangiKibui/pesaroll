using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// ACC-05: every time pay (or the account someone is paid into) is sent, who saw whose and when goes
/// into sensitive_views, one row per request: never the amounts (CLAUDE.md rule 8). The first part of
/// the audit log (PLAT-03).
/// </summary>
internal static class SensitiveViews
{
    public static async Task RecordAsync(OrganizationsDbContext db, TenantContext tenant, HttpContext http, string kind, IReadOnlyList<Guid> whose,
        CancellationToken ct)
    {
        if (whose.Count == 0 || tenant.UserId is not Guid userId || tenant.OrganizationId is not Guid organizationId)
        {
            return;
        }

        var subjects = whose.Distinct().ToArray();
        await db.Database.ExecuteSqlAsync($"""
            insert into sensitive_views (id, organization_id, user_id, kind, subject_ids, reference)
            values ({Guid.CreateVersion7()}, {organizationId}, {userId}, {kind}, {subjects}, {http.TraceIdentifier})
            """, ct);
    }
}
