using System.Text.Json;

namespace Pesaroll.Payroll.Engine;

/// <summary>
/// A country's statutory rates, bands, caps and rounding for pay dates from when it starts (PAY-01).
/// Read from the JSON in <c>rule_packs.rules</c> (the files in database/rule-packs/); the figures are
/// data, never constants in code. Each section may round differently from the pack's default.
/// </summary>
public sealed record RulePack(
    string Version,
    string Country,
    string Currency,
    string Period,
    Rounding Rounding,
    PayeRules Paye,
    NssfRules Nssf,
    ShifRules Shif,
    AhlRules Ahl,
    NitaRules Nita,
    DeductionRules Deductions,
    ReliefRules Reliefs,
    BenefitRules Benefits)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Reads a pack, refusing one that is malformed rather than using part of it.</summary>
    public static RulePack Parse(string json)
    {
        var version = TryVersion(json);
        RulePack? pack;
        try
        {
            pack = JsonSerializer.Deserialize<RulePack>(json, Options);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Rule pack {version} is malformed: {error.Message}", error);
        }
        if (pack is null) throw new InvalidDataException($"Rule pack {version} is empty.");
        pack.Validate();
        return pack;
    }

    public decimal Round(decimal amount, Rounding? section = null) => (section ?? Rounding).Apply(amount);

    private void Validate()
    {
        void Require(bool ok, string what)
        {
            if (!ok) throw new InvalidDataException($"Rule pack {Version}: {what}.");
        }

        static bool IsRate(decimal rate) => rate is >= 0 and <= 1;

        Require(Period == "monthly", $"period is {Period}; only monthly packs are supported");
        foreach (var rounding in new[] { Rounding, Paye.Rounding, Nssf.Rounding, Shif.Rounding, Ahl.Rounding })
            Require(rounding is null || rounding.IsKnown, $"rounding {rounding?.Mode} to {rounding?.Decimals} decimals is unknown");

        Require(Paye.Bands.Count > 0, "PAYE has no bands");
        Require(Paye.Bands[^1].UpTo is null, "the top PAYE band must have no upper limit");
        decimal previous = 0;
        foreach (var band in Paye.Bands.Take(Paye.Bands.Count - 1))
        {
            Require(band.UpTo is { } upTo && upTo > previous, "PAYE bands must rise, and only the top one has no upper limit");
            previous = band.UpTo!.Value;
        }
        Require(Paye.Bands.All(b => IsRate(b.Rate)), "a PAYE rate is not between 0 and 100%");
        Require(Paye.PersonalRelief >= 0, "personal relief is negative");

        Require(IsRate(Nssf.EmployeeRate) && IsRate(Nssf.EmployerRate), "an NSSF rate is not between 0 and 100%");
        Require(Nssf.LowerLimit > 0 && Nssf.LowerLimit < Nssf.UpperLimit, "NSSF's lower limit must be above 0 and below its upper limit");
        Require(IsRate(Shif.Rate) && Shif.Minimum >= 0, "SHIF's rate or minimum is out of range");
        Require(IsRate(Ahl.EmployeeRate) && IsRate(Ahl.EmployerRate), "a Housing Levy rate is not between 0 and 100%");
        Require(Nita.EmployerAmount >= 0, "NITA is negative");
        Require(Deductions.PensionCap >= 0 && Deductions.MortgageInterestCap >= 0 && Deductions.PostRetirementMedicalCap >= 0,
            "a deduction cap is negative");
        Require(IsRate(Reliefs.Insurance.Rate) && Reliefs.Insurance.Cap >= 0, "insurance relief is out of range");
        Require(Benefits.NonCashExemption >= 0, "the non-cash benefit exemption is negative");
    }

    private static string TryVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "(no version)" : "(no version)";
        }
        catch (JsonException)
        {
            return "(not JSON)";
        }
    }
}

/// <summary>How an amount is rounded: to <see cref="Decimals"/> places, <c>halfAwayFromZero</c> or <c>halfEven</c>.</summary>
public sealed record Rounding(int Decimals, string Mode)
{
    internal bool IsKnown => Decimals is >= 0 and <= 4 && Mode is "halfAwayFromZero" or "halfEven";

    public decimal Apply(decimal amount) => Math.Round(amount, Decimals,
        Mode == "halfEven" ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero);
}

/// <summary>A PAYE band: <see cref="Rate"/> on taxable pay up to <see cref="UpTo"/> (none for the top band).</summary>
public sealed record TaxBand(decimal? UpTo, decimal Rate);

public sealed record PayeRules(IReadOnlyList<TaxBand> Bands, decimal PersonalRelief, Rounding? Rounding = null, string? Source = null);

/// <summary>NSSF: Tier I on pay up to the lower limit, Tier II on pay between the limits, each at the same rate.</summary>
public sealed record NssfRules(
    decimal EmployeeRate, decimal EmployerRate, decimal LowerLimit, decimal UpperLimit,
    bool ReducesTaxablePay, Rounding? Rounding = null, string? Source = null);

public sealed record ShifRules(decimal Rate, decimal Minimum, bool ReducesTaxablePay, Rounding? Rounding = null, string? Source = null);

public sealed record AhlRules(decimal EmployeeRate, decimal EmployerRate, bool ReducesTaxablePay, Rounding? Rounding = null, string? Source = null);

public sealed record NitaRules(decimal EmployerAmount, string? Source = null);

/// <summary>What comes off taxable pay, each up to a monthly cap. NSSF counts toward the pension cap when <see cref="PensionIncludesNssf"/>.</summary>
public sealed record DeductionRules(
    decimal PensionCap, bool PensionIncludesNssf, decimal MortgageInterestCap, decimal PostRetirementMedicalCap, string? Source = null);

public sealed record InsuranceRelief(decimal Rate, decimal Cap);

public sealed record ReliefRules(InsuranceRelief Insurance, string? Source = null);

/// <summary>Non-cash benefits worth no more than <see cref="NonCashExemption"/> a month in total aren't taxed; above it, all of it is.</summary>
public sealed record BenefitRules(decimal NonCashExemption, string? Source = null);
