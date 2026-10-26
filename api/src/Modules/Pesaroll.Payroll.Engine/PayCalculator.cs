namespace Pesaroll.Payroll.Engine;

/// <summary>A month's pay for one employee, as the statutory rules need it. Amounts in the pack's currency.</summary>
public sealed record PayInputs
{
    /// <summary>Everything paid in cash for the month: basic pay, allowances, overtime, bonuses.</summary>
    public decimal GrossCashPay { get; init; }

    /// <summary>Benefits in kind (car, housing, school fees) at their taxable value.</summary>
    public decimal NonCashBenefits { get; init; }

    /// <summary>The employee's own contributions to a registered pension scheme, besides NSSF.</summary>
    public decimal PensionContributions { get; init; }

    /// <summary>Qualifying life, education and health insurance premiums the employee pays.</summary>
    public decimal InsurancePremiums { get; init; }

    /// <summary>Interest on a mortgage for the home the employee lives in.</summary>
    public decimal MortgageInterest { get; init; }

    /// <summary>Contributions to a registered post-retirement medical fund.</summary>
    public decimal PostRetirementMedical { get; init; }
}

/// <summary>One statutory line: what it's worked out on, at what rate, and the rounded amount.</summary>
public sealed record StatutoryLine(string Key, decimal Base, decimal? Rate, decimal Amount);

/// <summary>
/// Every statutory deduction and employer cost for the month, the steps to PAYE, and net pay
/// (cash pay less statutory deductions; other deductions come with pay runs, PAY-03).
/// Line keys match <c>statutory_items.key</c>.
/// </summary>
public sealed record PayResult(
    string RulePackVersion,
    decimal GrossCashPay,
    decimal TaxableBenefits,
    IReadOnlyList<StatutoryLine> Deductions,
    IReadOnlyList<StatutoryLine> EmployerCosts,
    decimal PensionDeduction,
    decimal MortgageInterestDeduction,
    decimal PostRetirementMedicalDeduction,
    decimal TaxablePay,
    decimal TaxBeforeRelief,
    decimal PersonalRelief,
    decimal InsuranceRelief,
    decimal NetPay)
{
    public decimal Amount(string key) =>
        Deductions.Concat(EmployerCosts).Single(l => l.Key == key).Amount;
}

/// <summary>
/// Which statutory deductions an employer makes (PAY-10), by group: paye, shif, nssf (employee and
/// employer), housing_levy (employee and employer) and nita. A group not made is nil and takes
/// nothing off taxable pay. Monthly payroll makes them all.
/// </summary>
public sealed record StatutoryChoice
{
    public static readonly IReadOnlyList<string> Groups = ["paye", "shif", "nssf", "housing_levy", "nita"];

    public static readonly StatutoryChoice All = new(Groups);

    public IReadOnlySet<string> Makes { get; }

    public StatutoryChoice(IEnumerable<string> makes)
    {
        Makes = makes.ToHashSet();
        if (Makes.FirstOrDefault(g => !Groups.Contains(g)) is { } unknown)
            throw new ArgumentException($"{unknown} is not a statutory group.", nameof(makes));
    }

    public bool Made(string group) => Makes.Contains(group);

    /// <summary>The group a line (statutory_items.key) belongs to.</summary>
    public static string GroupOf(string key) => key switch
    {
        "nssf_employee" or "nssf_employer" => "nssf",
        "ahl_employee" or "ahl_employer" => "housing_levy",
        "paye" or "shif" or "nita" => key,
        _ => throw new ArgumentException($"{key} is not a statutory line.", nameof(key)),
    };
}

/// <summary>
/// The payroll engine (PAY-01): a pure function of a month's pay and a rule pack. No database, clock or
/// settings: the same inputs and pack always give the same result. Monthly pay only for now.
/// Statutory amounts can be set (PAY-04: an accountant's edit for one run, by line key): an amount
/// set replaces the rules' and is made whatever the choice, and PAYE is worked out on it.
/// </summary>
public static class PayCalculator
{
    public static PayResult Calculate(PayInputs pay, RulePack pack, StatutoryChoice? choice = null, IReadOnlyDictionary<string, decimal>? set = null)
    {
        choice ??= StatutoryChoice.All;
        set ??= new Dictionary<string, decimal>();
        foreach (var (key, amount) in set)
        {
            StatutoryChoice.GroupOf(key);   // a statutory line, or ArgumentException
            ArgumentOutOfRangeException.ThrowIfNegative(amount, key);
        }
        decimal Or(string key, decimal rules) => set.TryGetValue(key, out var amount) ? amount : rules;
        foreach (var (name, value) in new[]
                 {
                     (nameof(pay.GrossCashPay), pay.GrossCashPay), (nameof(pay.NonCashBenefits), pay.NonCashBenefits),
                     (nameof(pay.PensionContributions), pay.PensionContributions), (nameof(pay.InsurancePremiums), pay.InsurancePremiums),
                     (nameof(pay.MortgageInterest), pay.MortgageInterest), (nameof(pay.PostRetirementMedical), pay.PostRetirementMedical),
                 })
            ArgumentOutOfRangeException.ThrowIfNegative(value, name);

        var gross = pay.GrossCashPay;

        // NSSF, SHIF and the Housing Levy are on cash pay; benefits in kind only count for PAYE.
        var nssf = pack.Nssf;
        var pensionable = Math.Min(gross, nssf.UpperLimit);
        var makesNssf = choice.Made("nssf");
        var makesAhl = choice.Made("housing_levy");
        var nssfEmployee = Or("nssf_employee", makesNssf ? NssfOn(gross, nssf.EmployeeRate, nssf, pack) : 0);
        var nssfEmployer = Or("nssf_employer", makesNssf ? NssfOn(gross, nssf.EmployerRate, nssf, pack) : 0);
        var shif = Or("shif", gross == 0 || !choice.Made("shif") ? 0 : pack.Round(Math.Max(gross * pack.Shif.Rate, pack.Shif.Minimum), pack.Shif.Rounding));
        var ahlEmployee = Or("ahl_employee", makesAhl ? pack.Round(gross * pack.Ahl.EmployeeRate, pack.Ahl.Rounding) : 0);
        var ahlEmployer = Or("ahl_employer", makesAhl ? pack.Round(gross * pack.Ahl.EmployerRate, pack.Ahl.Rounding) : 0);

        var taxableBenefits = pay.NonCashBenefits > pack.Benefits.NonCashExemption ? pay.NonCashBenefits : 0;

        var caps = pack.Deductions;
        var pension = caps.PensionIncludesNssf
            ? Math.Min(pay.PensionContributions + nssfEmployee, caps.PensionCap)
            : Math.Min(pay.PensionContributions, caps.PensionCap) + (nssf.ReducesTaxablePay ? nssfEmployee : 0);
        var mortgage = Math.Min(pay.MortgageInterest, caps.MortgageInterestCap);
        var medical = Math.Min(pay.PostRetirementMedical, caps.PostRetirementMedicalCap);

        var taxablePay = Math.Max(0, gross + taxableBenefits - pension - mortgage - medical
                                     - (pack.Shif.ReducesTaxablePay ? shif : 0)
                                     - (pack.Ahl.ReducesTaxablePay ? ahlEmployee : 0));

        var tax = TaxOn(taxablePay, pack);
        var insuranceRelief = pack.Round(Math.Min(pay.InsurancePremiums * pack.Reliefs.Insurance.Rate, pack.Reliefs.Insurance.Cap));
        var personalRelief = Math.Min(pack.Paye.PersonalRelief, tax);
        var paye = Or("paye", choice.Made("paye") ? pack.Round(Math.Max(0, tax - pack.Paye.PersonalRelief - insuranceRelief), pack.Paye.Rounding) : 0);

        StatutoryLine[] deductions =
        [
            new("nssf_employee", pensionable, nssf.EmployeeRate, nssfEmployee),
            new("shif", gross, pack.Shif.Rate, shif),
            new("ahl_employee", gross, pack.Ahl.EmployeeRate, ahlEmployee),
            new("paye", taxablePay, null, paye),
        ];
        StatutoryLine[] employerCosts =
        [
            new("nssf_employer", pensionable, nssf.EmployerRate, nssfEmployer),
            new("ahl_employer", gross, pack.Ahl.EmployerRate, ahlEmployer),
            new("nita", 1, null, Or("nita", choice.Made("nita") ? pack.Nita.EmployerAmount : 0)),
        ];

        return new PayResult(
            pack.Version, gross, taxableBenefits, deductions, employerCosts,
            pension, mortgage, medical, taxablePay, tax, personalRelief, insuranceRelief,
            NetPay: gross - deductions.Sum(l => l.Amount));
    }

    /// <summary>Income tax on taxable pay, band by band, before reliefs, rounded as the pack's PAYE says.</summary>
    public static decimal TaxOn(decimal taxablePay, RulePack pack)
    {
        var paye = pack.Paye;
        decimal tax = 0, from = 0;
        foreach (var band in paye.Bands)
        {
            var to = band.UpTo ?? decimal.MaxValue;
            if (taxablePay <= from) break;
            tax += (Math.Min(taxablePay, to) - from) * band.Rate;
            from = to;
        }
        return pack.Round(tax, paye.Rounding);
    }

    private static decimal NssfOn(decimal gross, decimal rate, NssfRules nssf, RulePack pack)
    {
        var tierOne = Math.Min(gross, nssf.LowerLimit) * rate;
        var tierTwo = Math.Max(0, Math.Min(gross, nssf.UpperLimit) - nssf.LowerLimit) * rate;
        return pack.Round(tierOne, nssf.Rounding) + pack.Round(tierTwo, nssf.Rounding);
    }
}
