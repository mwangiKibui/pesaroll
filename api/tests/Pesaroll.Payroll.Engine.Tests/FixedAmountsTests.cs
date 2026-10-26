namespace Pesaroll.Payroll.Engine.Tests;

/// <summary>
/// PAY-04: an accountant may set a statutory amount for one person in one run (150 to SHA instead of
/// the rate). The amount replaces the rules' for that line, and what is worked out from it follows:
/// SHIF, the Housing Levy and NSSF come off taxable pay, so PAYE is worked out on the amount set.
/// </summary>
public sealed class FixedAmountsTests
{
    private static PayResult Pay(decimal gross, Dictionary<string, decimal>? set = null, StatutoryChoice? choice = null) =>
        PayCalculator.Calculate(new PayInputs { GrossCashPay = gross }, Packs.Year4, choice, set);

    [Fact]
    public void Nothing_set_is_the_rules()
    {
        var rules = Pay(50_000m);

        Assert.Equal(rules.Deductions.Concat(rules.EmployerCosts), Pay(50_000m, []).Deductions.Concat(Pay(50_000m, []).EmployerCosts));
    }

    [Fact]
    public void A_shif_amount_set_replaces_the_rate_and_paye_follows_it()
    {
        var rules = Pay(50_000m);
        var set = Pay(50_000m, new() { ["shif"] = 150m });

        Assert.Equal(1_375m, rules.Amount("shif"));
        Assert.Equal(150m, set.Amount("shif"));
        Assert.Equal(rules.TaxablePay + 1_225m, set.TaxablePay);   // 1,225 less SHIF taken off before tax
        Assert.Equal(PayCalculator.TaxOn(set.TaxablePay, Packs.Year4) - 2_400m, set.Amount("paye"));
        Assert.Equal(50_000m - set.Deductions.Sum(l => l.Amount), set.NetPay);
    }

    [Fact]
    public void Nssf_set_comes_off_taxable_pay_and_paye_set_is_taken_as_it_is()
    {
        var rules = Pay(50_000m);
        var nssf = Pay(50_000m, new() { ["nssf_employee"] = 1_000m });
        var paye = Pay(50_000m, new() { ["paye"] = 4_000m });

        Assert.Equal(rules.TaxablePay + 2_000m, nssf.TaxablePay);   // 3,000 by the rules
        Assert.Equal(4_000m, paye.Amount("paye"));
        Assert.Equal(rules.TaxablePay, paye.TaxablePay);
    }

    [Fact]
    public void An_amount_set_is_made_even_where_its_group_is_not()
    {
        var set = Pay(50_000m, new() { ["shif"] = 300m }, new StatutoryChoice(["paye"]));

        Assert.Equal(300m, set.Amount("shif"));
        Assert.Equal(0m, set.Amount("nssf_employee"));
        Assert.Equal(50_000m - 300m, set.TaxablePay);
    }

    [Fact]
    public void Only_statutory_lines_can_be_set()
    {
        Assert.Throws<ArgumentException>(() => Pay(50_000m, new() { ["bonus"] = 1m }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pay(50_000m, new() { ["shif"] = -1m }));
    }
}
