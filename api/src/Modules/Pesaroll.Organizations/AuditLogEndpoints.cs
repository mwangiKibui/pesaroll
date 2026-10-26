using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// An entry in the audit log (PLAT-03). An action (Kind "action"): what was done by name, to which
/// subject, with its safe details. A change (Kind "change"): a row of TableName added, changed or
/// deleted, with the columns changed and their values before and after, masked where they are
/// personal (pay, ID numbers, accounts). UserName is who did it, when they are still in the organization.
/// </summary>
public sealed record AuditEntry(
    Guid Id, DateTimeOffset At, string Kind, string Action, Guid? CompanyId, Guid? UserId, string? UserName, string? SubjectType, Guid? SubjectId,
    string? SubjectName, JsonElement? Details, string? TableName, Guid? RecordId, IReadOnlyList<string>? Changed, JsonElement? OldValues,
    JsonElement? NewValues, string? RequestRef, string Source);

public sealed record AuditPage(IReadOnlyList<AuditEntry> Items, int Total, int PageNumber, int PageSize);

/// <summary>How many entries, and how many different people made them.</summary>
public sealed record AuditSummary(int Entries, int People);

/// <summary>
/// Reading the audit log (PLAT-03): the entries, newest first, with filters ("who worked out payroll
/// on the 25th?"), counts ("how many people approved payrolls this month?") and one record's history.
/// All need audit_log.view, and show only the companies the person has it for; entries with no
/// company (schedules, workflows, the team) only to someone who has it for every company. Row-level
/// security keeps it to their organization. Days (from, to) are Kenyan days.
/// </summary>
public static class AuditLogEndpoints
{
    public const int MaxPageSize = 100;
    private const int MaxHistory = 500;
    private static readonly TimeSpan Kenya = TimeSpan.FromHours(3);

    private sealed record Row(Guid Id, DateTimeOffset At, string Kind, string Action, Guid? CompanyId, Guid? UserId, string? UserName, string? SubjectType,
        Guid? SubjectId, string? SubjectName, string? Details, string? TableName, Guid? RecordId, string[]? Changed, string? OldValues, string? NewValues,
        string? RequestRef, string Source);

    private sealed record Counts(int Entries, int People);

    private const string Select = """
        select l.id, l.at, l.kind, l.action, l.company_id, l.user_id, nullif(trim(concat(u.first_name, ' ', u.last_name)), '') as user_name,
               l.subject_type, l.subject_id, l.subject_name, l.details::text as details, l.table_name, l.record_id, l.changed,
               l.old_values::text as old_values, l.new_values::text as new_values, l.request_ref, l.source
        from audit_log l left join users u on u.id = l.user_id
        """;

    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var log = app.MapGroup("/api/audit-log").WithTags("Audit log");

        log.MapGet("/", async (Guid? companyId, string? kind, string? action, Guid? userId, Guid? subjectId, DateOnly? from, DateOnly? to, int? page,
            int? pageSize, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var filter = Filter(tenant, http, companyId, kind, action, userId, subjectId, from, to);
            var size = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);
            var number = Math.Max(page ?? 1, 1);
            var total = await db.Database.SqlQueryRaw<int>($"select count(*)::int as \"Value\" from audit_log l where {filter.Where}", filter.Parameters())
                .SingleAsync(ct);
            var rows = await db.Database.SqlQueryRaw<Row>(
                    $"{Select} where {filter.Where} order by l.at desc, l.id desc offset {(number - 1) * size} limit {size}", filter.Parameters())
                .ToListAsync(ct);
            return Results.Ok(new AuditPage(rows.Select(Entry).ToList(), total, number, size));
        }).Produces<AuditPage>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .RequirePermission(Permissions.AuditLogView);

        log.MapGet("/summary", async (Guid? companyId, string? kind, string? action, Guid? userId, Guid? subjectId, DateOnly? from, DateOnly? to,
            OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var filter = Filter(tenant, http, companyId, kind, action, userId, subjectId, from, to);
            var counts = await db.Database.SqlQueryRaw<Counts>(
                    $"select count(*)::int as entries, count(distinct l.user_id)::int as people from audit_log l where {filter.Where}", filter.Parameters())
                .SingleAsync(ct);
            return Results.Ok(new AuditSummary(counts.Entries, counts.People));
        }).Produces<AuditSummary>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .RequirePermission(Permissions.AuditLogView);

        // One record's history, newest first: the changes to its row and the actions done to it.
        log.MapGet("/records/{table}/{id:guid}", async (string table, Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            CancellationToken ct) =>
        {
            var filter = Filter(tenant, http, null, null, null, null, null, null, null);
            var parameters = filter.Parameters().Append(new NpgsqlParameter("table", NpgsqlDbType.Text) { Value = table })
                .Append(new NpgsqlParameter("record", NpgsqlDbType.Uuid) { Value = id }).ToArray();
            var rows = await db.Database.SqlQueryRaw<Row>(
                    $"{Select} where {filter.Where} and ((l.table_name = @table and l.record_id = @record) or l.subject_id = @record) " +
                    $"order by l.at desc, l.id desc limit {MaxHistory}", parameters)
                .ToListAsync(ct);
            return Results.Ok(rows.Select(Entry).ToList());
        }).Produces<List<AuditEntry>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .RequirePermission(Permissions.AuditLogView);

        return app;
    }

    /// <summary>The conditions for what this person may see and asked for, with their parameters (made new for each query).</summary>
    private sealed record Conditions(string Where, Func<object[]> Parameters);

    private static Conditions Filter(TenantContext tenant, HttpContext http, Guid? companyId, string? kind, string? action, Guid? userId, Guid? subjectId,
        DateOnly? from, DateOnly? to)
    {
        CompaniesEndpoints.RequireOrganization(tenant, http);
        var companies = tenant.CompaniesFor(Permissions.AuditLogView).ToArray();
        if (companyId is Guid asked && !companies.Contains(asked)) throw AccessErrors.NoPermission();

        var where = new List<string> { "(l.company_id = any (@companies) or (l.company_id is null and @all))" };
        var values = new List<(string Name, NpgsqlDbType Type, object Value)>
        {
            ("companies", NpgsqlDbType.Array | NpgsqlDbType.Uuid, companies), ("all", NpgsqlDbType.Boolean, tenant.Can(Permissions.AuditLogView)),
        };
        void Add(string condition, string name, NpgsqlDbType type, object value)
        {
            where.Add(condition);
            values.Add((name, type, value));
        }

        if (companyId is Guid company) Add("l.company_id = @company", "company", NpgsqlDbType.Uuid, company);
        if (!string.IsNullOrWhiteSpace(kind)) Add("l.kind = @kind", "kind", NpgsqlDbType.Text, kind);
        if (!string.IsNullOrWhiteSpace(action)) Add("l.action = @action", "action", NpgsqlDbType.Text, action);
        if (userId is Guid user) Add("l.user_id = @user", "user", NpgsqlDbType.Uuid, user);
        if (subjectId is Guid subject) Add("(l.subject_id = @subject or l.record_id = @subject)", "subject", NpgsqlDbType.Uuid, subject);
        if (from is DateOnly first) Add("l.at >= @from", "from", NpgsqlDbType.TimestampTz, new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), Kenya).UtcDateTime);
        if (to is DateOnly last)
            Add("l.at < @to", "to", NpgsqlDbType.TimestampTz, new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue), Kenya).UtcDateTime);
        return new Conditions(string.Join(" and ", where),
            () => values.Select(v => (object)new NpgsqlParameter(v.Name, v.Type) { Value = v.Value }).ToArray());
    }

    private static AuditEntry Entry(Row r) => new(r.Id, r.At, r.Kind, r.Action, r.CompanyId, r.UserId, r.UserName, r.SubjectType, r.SubjectId,
        r.SubjectName, Json(r.Details), r.TableName, r.RecordId, r.Changed, Json(r.OldValues), Json(r.NewValues), r.RequestRef, r.Source);

    private static JsonElement? Json(string? text) => text is null ? null : JsonDocument.Parse(text).RootElement.Clone();
}
