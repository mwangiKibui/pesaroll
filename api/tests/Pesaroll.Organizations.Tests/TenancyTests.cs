using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Organizations;
using Pesaroll.Shared.Data;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// Proves from the API side that row-level security keeps organizations apart.
/// Needs a PostgreSQL with the schema deployed; scripts/check.sh provides one and sets
/// PESAROLL_TEST_DB. Only this test's own rows are reset, so other test projects can share the
/// database. The seed data is written as the database owner, which bypasses
/// row-level security; every assertion reads through the app's interceptor.
/// </summary>
public sealed class TenancyTests : IAsyncLifetime
{
    private static readonly Guid Owner1 = Guid.Parse("00000000-0000-7000-8000-0000000000f1");
    private static readonly Guid Owner2 = Guid.Parse("00000000-0000-7000-8000-0000000000f2");
    private static readonly Guid Org1 = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Org2 = Guid.Parse("00000000-0000-7000-8000-000000000002");
    private static readonly Guid Org1Kenya = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    private static readonly Guid Org1Uganda = Guid.Parse("00000000-0000-7000-8000-0000000000a2");
    private static readonly Guid Org2Kenya = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")
        ?? throw new InvalidOperationException(
            "PESAROLL_TEST_DB is not set. Run scripts/check.sh, which starts a database for these tests.");

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            delete from memberships where organization_id in ('{Org1}', '{Org2}');
            delete from companies where organization_id in ('{Org1}', '{Org2}');
            delete from organizations where id in ('{Org1}', '{Org2}');
            delete from sessions where user_id in ('{Owner1}', '{Owner2}');
            delete from users where id in ('{Owner1}', '{Owner2}');
            insert into users (id, email, first_name, last_name) values
              ('{Owner1}', 'owner1@example.com', 'Owner', 'One'),
              ('{Owner2}', 'owner2@example.com', 'Owner', 'Two');
            insert into organizations (id, name, onboarded_by) values
              ('{Org1}', 'Org One', '{Owner1}'),
              ('{Org2}', 'Org Two', '{Owner2}');
            insert into companies (id, organization_id, legal_name, country_code, currency) values
              ('{Org1Kenya}', '{Org1}', 'One Kenya Ltd', 'KE', 'KES'),
              ('{Org1Uganda}', '{Org1}', 'One Uganda Ltd', 'UG', 'UGX'),
              ('{Org2Kenya}', '{Org2}', 'Two Kenya Ltd', 'KE', 'KES');
            """;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static OrganizationsDbContext ContextFor(TenantContext tenant, string? connectionString = null) =>
        new(new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseNpgsql(connectionString ?? ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options);

    private static TenantContext Tenant(Guid org, params Guid[] companies)
    {
        var tenant = new TenantContext();
        tenant.Set(org, companies);
        return tenant;
    }

    [Fact]
    public async Task Organization_sees_only_its_own_companies()
    {
        await using var db = ContextFor(Tenant(Org1, Org1Kenya, Org1Uganda));

        var companies = await db.Companies.Select(c => c.Id).ToListAsync(CancellationToken.None);

        Assert.Equal([Org1Kenya, Org1Uganda], companies.Order());
    }

    [Fact]
    public async Task Company_scope_narrows_what_a_user_sees()
    {
        await using var db = ContextFor(Tenant(Org1, Org1Kenya));

        var companies = await db.Companies.Select(c => c.Id).ToListAsync(CancellationToken.None);

        Assert.Equal([Org1Kenya], companies);
    }

    [Fact]
    public async Task No_tenant_sees_nothing()
    {
        await using var db = ContextFor(new TenantContext());

        Assert.Equal(0, await db.Companies.CountAsync(CancellationToken.None));
        Assert.Equal(0, await db.Organizations.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cannot_add_a_company_to_another_organization()
    {
        await using var db = ContextFor(Tenant(Org1, Org1Kenya, Org1Uganda));
        db.Companies.Add(new Company
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = Org2,
            LegalName = "Smuggled Ltd",
            CountryCode = "KE",
            Currency = "KES",
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync(CancellationToken.None));

        Assert.Contains("row-level security", error.InnerException?.Message);
    }

    [Fact]
    public async Task A_reused_connection_does_not_leak_the_previous_tenant()
    {
        // One connection in the pool forces the second context to reuse the first one's connection.
        var singleConnection = new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = 1 }.ToString();
        var ct = CancellationToken.None;

        await using (var first = ContextFor(Tenant(Org1, Org1Kenya, Org1Uganda), singleConnection))
        {
            Assert.Equal(2, await first.Companies.CountAsync(ct));
        }

        await using var second = ContextFor(Tenant(Org2, Org2Kenya), singleConnection);
        var companies = await second.Companies.Select(c => c.Id).ToListAsync(ct);

        Assert.Equal([Org2Kenya], companies);
    }
}
