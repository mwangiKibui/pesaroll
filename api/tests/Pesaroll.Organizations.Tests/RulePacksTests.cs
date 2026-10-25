using Microsoft.EntityFrameworkCore;
using Pesaroll.Shared.Data;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// PAY-01: the rule pack for a pay date comes from the published packs in the database, read
/// through the app's role like everything else, so a change in the law needs no release.
/// </summary>
public sealed class RulePacksTests
{
    private static RulePacks Packs()
    {
        var tenant = new TenantContext();
        tenant.Set(Guid.CreateVersion7(), []);
        var db = new OrganizationsDbContext(new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")
                       ?? throw new InvalidOperationException("PESAROLL_TEST_DB is not set. Run scripts/check.sh."))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options);
        return new RulePacks(db);
    }

    [Theory]
    [InlineData("2025-02-01", "KE-2025.02", 72_000)]   // NSSF Year 3 from February 2025
    [InlineData("2026-01-31", "KE-2025.02", 72_000)]   // January 2026 pay is still Year 3
    [InlineData("2026-02-01", "KE-2026.02", 108_000)]  // Year 4 from February 2026
    [InlineData("2026-12-31", "KE-2026.02", 108_000)]
    public async Task The_pack_in_force_on_the_pay_date_is_used(string payDate, string version, decimal nssfUpperLimit)
    {
        var pack = await Packs().InForceAsync("KE", DateOnly.Parse(payDate), CancellationToken.None);

        Assert.Equal(version, pack.Version);
        Assert.Equal(nssfUpperLimit, pack.Nssf.UpperLimit);
    }

    [Theory]
    [InlineData("KE", "2025-01-31")]   // before Kenya's first pack
    [InlineData("UG", "2026-06-30")]   // no Uganda packs yet
    public async Task No_pack_for_the_date_is_PAY_2010(string country, string payDate)
    {
        var error = await Assert.ThrowsAsync<AppException>(() =>
            Packs().InForceAsync(country, DateOnly.Parse(payDate), CancellationToken.None));

        Assert.Equal("PAY-2010", error.Code);
        Assert.Contains(payDate[..4], error.Message);
    }
}
