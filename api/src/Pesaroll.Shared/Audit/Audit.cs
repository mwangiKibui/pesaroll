using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Pesaroll.Shared.Audit;

/// <summary>What an action was done to: its type (pay_run, employee…), id and name at the time.</summary>
public sealed record AuditSubject(string Type, Guid Id, string? Name);

/// <summary>
/// Records a named action in the audit log (PLAT-03), through the database's audit_record, which
/// takes who and the request from the connection (TenantSessionInterceptor). It runs in the caller's
/// transaction, so an action is recorded with what it did or not at all. Changes to rows are recorded
/// by the database on its own; this is for what a person did, by name (<see cref="AuditActions"/>).
/// </summary>
public static class Audit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task RecordAsync(DbContext db, string action, Guid? companyId, AuditSubject subject,
        IReadOnlyDictionary<string, object?>? details, CancellationToken ct)
    {
        var json = Details(action, details);
        return db.Database.ExecuteSqlAsync(
            $"select audit_record({action}, {companyId}, {subject.Type}, {subject.Id}, {subject.Name}, {json}::jsonb)", ct);
    }

    /// <summary>The same on a connection the caller holds, in its transaction (an approval's outcome).</summary>
    public static async Task RecordAsync(DbConnection connection, DbTransaction? transaction, string action, Guid? companyId, AuditSubject subject,
        IReadOnlyDictionary<string, object?>? details, CancellationToken ct)
    {
        var json = Details(action, details);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select audit_record(@action, nullif(@company, '')::uuid, @type, @id::uuid, nullif(@name, ''), nullif(@details, '')::jsonb)
            """;
        Add(command, "action", action);
        Add(command, "company", companyId?.ToString() ?? "");
        Add(command, "type", subject.Type);
        Add(command, "id", subject.Id.ToString());
        Add(command, "name", subject.Name ?? "");
        Add(command, "details", json ?? "");
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string? Details(string action, IReadOnlyDictionary<string, object?>? details)
    {
        AuditActions.Check(action, details);
        return details is null || details.Count == 0 ? null : JsonSerializer.Serialize(details, Json);
    }

    private static void Add(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
