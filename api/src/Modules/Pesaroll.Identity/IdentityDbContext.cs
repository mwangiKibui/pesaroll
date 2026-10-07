using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Pesaroll.Identity;

/// <summary>
/// Data access for accounts and sessions. Row-level security limits it to the signed-in
/// person; the steps before sign-in go through the narrow database functions in
/// database/schema/identity/signin_functions.sql.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<MembershipRow> Memberships => Set<MembershipRow>();
    public DbSet<SignupRole> SignupRoles => Set<SignupRole>();
    public DbSet<SignupRoleGroup> SignupRoleGroups => Set<SignupRoleGroup>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().Property(u => u.CreatedAt).ValueGeneratedOnAdd();
        model.Entity<User>().Property(u => u.UpdatedAt).ValueGeneratedOnAdd();
        model.Entity<User>().Property(u => u.FailedSigninCount).ValueGeneratedOnAdd();
        model.Entity<Session>().Property(s => s.CreatedAt).ValueGeneratedOnAdd();
        model.Entity<MembershipRow>().ToTable("memberships");
        model.Entity<SignupRoleGroup>().HasKey(g => g.Code);
    }

    /// <summary>Runs a database function and reads its rows, through the tenant interceptor.</summary>
    internal async Task<List<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> read,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        await Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(read(reader));
            }

            return rows;
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }
}
