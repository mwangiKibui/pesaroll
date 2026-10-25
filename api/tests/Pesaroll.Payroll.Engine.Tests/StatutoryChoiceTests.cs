namespace Pesaroll.Payroll.Engine.Tests;

/// <summary>
/// PAY-10: an employer may make only some statutory deductions (a schedule of casuals with none, or
/// SHIF and the Housing Levy only). What isn't made is nil and takes nothing off taxable pay.
/// </summary>
public sealed class StatutoryChoiceTests
{
    private static PayResult Pay(decimal gross, params string[] makes) =>
        PayCalculator.Calculate(new PayInputs { GrossCashPay = gross }, Packs.Year4, new StatutoryChoice(makes));

    [Fact]
    public void Every_group_made_is_the_same_as_no_choice()
    {
        var all = Pay(100_000m, StatutoryChoice.Groups.ToArray());

        var none = PayCalculator.Calculate(new PayInputs { GrossCashPay = 100_000m }, Packs.Year4);
        Assert.Equal(none.Deductions.Concat(none.EmployerCosts), all.Deductions.Concat(all.EmployerCosts));
        Assert.Equal(none.NetPay, all.NetPay);
    }

    [Fact]
    public void Nothing_made_takes_nothing()
    {
        var pay = Pay(100_000m);

        Assert.All(pay.Deductions.Concat(pay.EmployerCosts), l => Assert.Equal(0m, l.Amount));
        Assert.Equal(100_000m, pay.NetPay);
    }

    [Fact]
    public void Paye_alone_is_on_pay_with_nothing_taken_before_it()
    {
        var pay = Pay(100_000m, "paye");

        Assert.Equal(100_000m, pay.TaxablePay);   // no NSSF, SHIF or Housing Levy to take off first
        Assert.Equal(PayCalculator.TaxOn(100_000m, Packs.Year4) - 2_400m, pay.Amount("paye"));
        Assert.Equal(0m, pay.Amount("nssf_employee"));
        Assert.Equal(0m, pay.Amount("nita"));
    }

    [Fact]
    public void Shif_and_housing_levy_only()
    {
        var pay = Pay(9_900m, "shif", "housing_levy");

        Assert.Equal(300m, pay.Amount("shif"));            // the minimum
        Assert.Equal(148.50m, pay.Amount("ahl_employee"));
        Assert.Equal(148.50m, pay.Amount("ahl_employer"));
        Assert.Equal(0m, pay.Amount("paye"));
        Assert.Equal(0m, pay.Amount("nssf_employer"));
        Assert.Equal(9_900m - 300m - 148.50m, pay.NetPay);
    }

    [Fact]
    public void Each_line_belongs_to_a_group()
    {
        Assert.Equal("nssf", StatutoryChoice.GroupOf("nssf_employer"));
        Assert.Equal("housing_levy", StatutoryChoice.GroupOf("ahl_employee"));
        Assert.Equal("paye", StatutoryChoice.GroupOf("paye"));
        Assert.Throws<ArgumentException>(() => new StatutoryChoice(["pension"]));
    }
}
