using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Shared.Data;

/// <summary>
/// Runs every time the app opens a database connection. It switches to the
/// <c>pesaroll_app</c> role, which row-level security applies to, and sets the
/// organization and companies for this request. Both values are always written,
/// empty when there is no tenant, so nothing carries over on a pooled connection.
/// </summary>
public sealed class TenantSessionInterceptor(TenantContext tenant) : DbConnectionInterceptor
{
    public const string AppRole = "pesaroll_app";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateSessionCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = CreateSessionCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateSessionCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            $"set role {AppRole}; " +
            "select set_config('app.org_id', @org_id, false), set_config('app.company_ids', @company_ids, false);";
        AddParameter(command, "org_id", tenant.OrganizationId?.ToString() ?? "");
        AddParameter(command, "company_ids", string.Join(',', tenant.CompanyIds));
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
