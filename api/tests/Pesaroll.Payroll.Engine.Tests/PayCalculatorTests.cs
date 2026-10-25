namespace Pesaroll.Payroll.Engine.Tests;

/// <summary>
/// PAY-01: Kenya's statutory deductions for a month's pay, worked out from the rule pack alone.
/// Each rule is checked on its own here; PAY-02's golden cases check whole payslips.
/// </summary>
public sealed class PayCalculatorTests
{
    private static PayResult Pay(decimal gross, RulePack? pack = null, PayInputs? inputs = null) =>
        PayCalculator.Calculate((inputs ?? new PayInputs()) with { GrossCashPay = gross }, pack ?? Packs.Year4);

    [Fact]
    public void Kes_100000_gives_70441_65_net()
    {
        var pay = Pay(100_000m);

        Assert.Equal("KE-2026.02", pay.RulePackVersion);
        Assert.Equal(6_000m, pay.Amount("nssf_employee"));
        Assert.Equal(2_750m, pay.Amount("shif"));
        Assert.Equal(1_500m, pay.Amount("ahl_employee"));
        Assert.Equal(89_750m, pay.TaxablePay);
        Assert.Equal(21_708.35m, pay.TaxBeforeRelief);
        Assert.Equal(2_400m, pay.PersonalRelief);
        Assert.Equal(19_308.35m, pay.Amount("paye"));
        Assert.Equal(70_441.65m, pay.NetPay);
    }

    [Fact]
    public void Employer_pays_matching_nssf_housing_levy_and_nita()
    {
        var pay = Pay(100_000m);

        Assert.Equal(6_000m, pay.Amount("nssf_employer"));
        Assert.Equal(1_500m, pay.Amount("ahl_employer"));
        Assert.Equal(50m, pay.Amount("nita"));
        Assert.Equal(["nssf_employer", "ahl_employer", "nita"], pay.EmployerCosts.Select(l => l.Key));
        Assert.Equal(["nssf_employee", "shif", "ahl_employee", "paye"], pay.Deductions.Select(l => l.Key));
    }

    [Theory]
    [InlineData(8_000, 480)]       // below the lower limit: 6% of all of it
    [InlineData(9_000, 540)]       // at the lower limit (Tier I only)
    [InlineData(50_000, 3_000)]    // between the limits
    [InlineData(108_000, 6_480)]   // at the upper limit
    [InlineData(200_000, 6_480)]   // above it: no more than 6% of 108,000
    public void Nssf_year_4_is_6_percent_up_to_108000(decimal gross, decimal nssf)
    {
        Assert.Equal(nssf, Pay(gross).Amount("nssf_employee"));
        Assert.Equal(nssf, Pay(gross).Amount("nssf_employer"));
    }

    [Fact]
    public void Nssf_changes_in_february_2026_with_no_code_change()
    {
        Assert.Equal(4_320m, Pay(200_000m, Packs.Year3).Amount("nssf_employee"));   // 6% of 72,000
        Assert.Equal(6_480m, Pay(200_000m, Packs.Year4).Amount("nssf_employee"));   // 6% of 108,000
    }

    [Theory]
    [InlineData(8_000, 300)]       // 2.75% is 220: the minimum applies
    [InlineData(10_909, 300)]      // 299.9975, still the minimum
    [InlineData(20_000, 550)]
    public void Shif_is_2_75_percent_at_least_300(decimal gross, decimal shif) =>
        Assert.Equal(shif, Pay(gross).Amount("shif"));

    [Fact]
    public void Amounts_round_half_away_from_zero_to_the_cent() =>
        Assert.Equal(500.01m, Pay(18_182m).Amount("shif"));   // 2.75% of 18,182 is 500.005

    [Theory]
    [InlineData(24_000, 2_400)]
    [InlineData(32_333, 4_483.25)]
    [InlineData(500_000, 144_783.35)]
    [InlineData(800_000, 242_283.35)]
    [InlineData(800_100, 242_318.35)]
    public void Tax_follows_each_band(decimal taxable, decimal tax) =>
        Assert.Equal(tax, PayCalculator.TaxOn(taxable, Packs.Year4));

    [Fact]
    public void Relief_bigger_than_the_tax_leaves_no_paye_never_negative()
    {
        var pay = Pay(20_000m);

        Assert.Equal(17_950m, pay.TaxablePay);   // less NSSF 1,200, SHIF 550 and Housing Levy 300
        Assert.Equal(0m, pay.Amount("paye"));
        Assert.Equal(17_950m, pay.NetPay);
    }

    [Fact]
    public void No_pay_has_no_deductions()
    {
        var pay = Pay(0m);

        Assert.All(pay.Deductions, line => Assert.Equal(0m, line.Amount));
        Assert.Equal(0m, pay.NetPay);
        Assert.Equal(50m, pay.Amount("nita"));   // the levy is per employee, paid or not
    }

    [Fact]
    public void Pension_with_nssf_comes_off_taxable_pay_up_to_30000()
    {
        var pay = Pay(300_000m, inputs: new PayInputs { PensionContributions = 40_000m });

        Assert.Equal(30_000m, pay.PensionDeduction);   // NSSF 6,480 plus 40,000, capped
        Assert.Equal(257_250m, pay.TaxablePay);        // less SHIF 8,250 and Housing Levy 4,500
        Assert.Equal(69_558.35m, pay.Amount("paye"));
    }

    [Theory]
    [InlineData(10_000, 1_500, 17_808.35)]   // 15% of the premiums
    [InlineData(50_000, 5_000, 14_308.35)]   // capped at 5,000
    public void Insurance_relief_is_15_percent_up_to_5000(decimal premiums, decimal relief, decimal paye)
    {
        var pay = Pay(100_000m, inputs: new PayInputs { InsurancePremiums = premiums });

        Assert.Equal(relief, pay.InsuranceRelief);
        Assert.Equal(paye, pay.Amount("paye"));
    }

    [Fact]
    public void Mortgage_interest_comes_off_up_to_30000()
    {
        var pay = Pay(100_000m, inputs: new PayInputs { MortgageInterest = 40_000m });

        Assert.Equal(59_750m, pay.TaxablePay);
        Assert.Equal(10_308.35m, pay.Amount("paye"));
    }

    [Fact]
    public void Post_retirement_medical_fund_comes_off_up_to_15000()
    {
        var pay = Pay(100_000m, inputs: new PayInputs { PostRetirementMedical = 20_000m });

        Assert.Equal(74_750m, pay.TaxablePay);
        Assert.Equal(14_808.35m, pay.Amount("paye"));
    }

    [Fact]
    public void Non_cash_benefits_up_to_5000_are_not_taxed()
    {
        var pay = Pay(100_000m, inputs: new PayInputs { NonCashBenefits = 5_000m });

        Assert.Equal(89_750m, pay.TaxablePay);
        Assert.Equal(70_441.65m, pay.NetPay);
    }

    [Fact]
    public void Non_cash_benefits_over_5000_are_taxed_in_full_but_not_in_shif_nssf_or_levy()
    {
        var pay = Pay(100_000m, inputs: new PayInputs { NonCashBenefits = 6_000m });

        Assert.Equal(95_750m, pay.TaxablePay);
        Assert.Equal(2_750m, pay.Amount("shif"));
        Assert.Equal(6_000m, pay.Amount("nssf_employee"));
        Assert.Equal(1_500m, pay.Amount("ahl_employee"));
        Assert.Equal(21_108.35m, pay.Amount("paye"));
        Assert.Equal(68_641.65m, pay.NetPay);
    }

    [Fact]
    public void Negative_inputs_are_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Pay(-1m));
}
