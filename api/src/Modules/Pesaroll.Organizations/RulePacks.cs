using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Payroll.Engine;
using Pesaroll.Shared.Errors;

namespace Pesaroll.Organizations;

/// <summary>A row of <c>rule_packs</c> (PAY-01): reference data the app reads and never writes.</summary>
public sealed class RulePackRecord
{
    public required string Version { get; set; }
    public required string CountryCode { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public required string Rules { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// Chooses the rule pack for a country and pay date: the latest published one that started on or
/// before it. A published pack never changes, so each is read and parsed once per process.
/// </summary>
public sealed class RulePacks(OrganizationsDbContext db)
{
    private static readonly ConcurrentDictionary<string, RulePack> Parsed = new();

    public async Task<RulePack> InForceAsync(string country, DateOnly payDate, CancellationToken ct)
    {
        var record = await db.RulePacks.AsNoTracking()
            .Where(p => p.CountryCode == country && p.EffectiveFrom <= payDate && p.PublishedAt != null)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct)
            ?? throw new AppException("PAY-2010",
                $"We don't have the statutory rules for {country} on {payDate:d MMMM yyyy} yet, so pay can't be worked out.", 422);
        return Parsed.GetOrAdd(record.Version, _ => RulePack.Parse(record.Rules));
    }
}
