using Pesaroll.Payroll.Engine;

namespace Pesaroll.Payroll.Tests;

/// <summary>
/// PAY-10: weekly, fortnightly and daily runs. Each run pays the wage for its schedule; statutory
/// deductions are worked out on the month to date (this run and the person's approved runs paid
/// earlier in the same month) and each run takes only what earlier ones didn't, so a month adds up
/// to a monthly payroll. A schedule (or one person) may make some deductions or none.
/// </summary>
public sealed class OtherSchedulesTests
{
    private static readonly RulePack Pack = RulePack.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulePacks", "KE-2026.02.json")));
    private static readonly Guid Weekly = Guid.CreateVersion7();
    private static readonly DateOnly[] OctoberFridays = [new(2026, 10, 2), new(2026, 10, 9), new(2026, 10, 16), new(2026, 10, 23)];

    private static Candidate Person(decimal wage, string basis = "weekly", StatutoryChoice? choice = null, MonthToDate? earlier = null, Guid? id = null)
    {
        var from = new DateOnly(2024, 1, 15);
        return new Candidate(id ?? Guid.CreateVersion7(), "C-01", "Peter Kamau", from, null,
            [new JobRow(Guid.CreateVersion7(), from, null, Weekly)],
            [new BasicPayRow(Guid.CreateVersion7(), from, null, wage, basis, "KES")], choice, earlier);
    }

    private static RunSetup Week(DateOnly payDate, StatutoryChoice? choice = null, string frequency = "weekly") =>
        new(new RunPeriod(payDate.AddDays(-6), payDate), Weekly, null, Pack, new Dictionary<string, LineName>(), [], [], frequency, choice ?? StatutoryChoice.All);

    private static decimal Statutory(WorkedPerson person, string key) => person.Lines.Where(l => l.StatutoryKey == key).Sum(l => l.Amount);

    [Fact]
    public void Four_weekly_runs_add_up_to_the_monthly_payroll()
    {
        var id = Guid.CreateVersion7();
        var paid = new List<WorkedPerson>();
        foreach (var friday in OctoberFridays)
            paid.Add(Assert.Single(PayRunCalculator.Work(Week(friday), [Person(25_000, id: id, earlier: MonthToDate.From(paid.SelectMany(p => p.Lines)))])));

        Assert.All(paid, p => Assert.Equal(25_000m, p.Totals!.GrossPay));
        Assert.Equal(6_000m, paid.Sum(p => Statutory(p, "nssf_employee")));
        Assert.Equal(2_750m, paid.Sum(p => Statutory(p, "shif")));
        Assert.Equal(1_500m, paid.Sum(p => Statutory(p, "ahl_employee")));
        Assert.Equal(19_308.35m, paid.Sum(p => Statutory(p, "paye")));
        Assert.Equal(50m, paid.Sum(p => Statutory(p, "nita")));          // once a month
        Assert.Equal(70_441.65m, paid.Sum(p => p.Totals!.NetPay));
        // The first week's PAYE is low: it has the month's personal relief to itself.
        Assert.True(Statutory(paid[0], "paye") < Statutory(paid[3], "paye"));
    }

    [Fact]
    public void A_line_keeps_the_month_to_date_and_what_earlier_runs_took()
    {
        var first = Assert.Single(PayRunCalculator.Work(Week(OctoberFridays[0]), [Person(25_000)]));
        var second = Assert.Single(PayRunCalculator.Work(Week(OctoberFridays[1]), [Person(25_000, earlier: MonthToDate.From(first.Lines))]));

        var nssf = second.Lines.Single(l => l.StatutoryKey == "nssf_employee");
        Assert.Equal(Statutory(first, "nssf_employee"), nssf.Earlier);
        Assert.Equal(PayCalculator.Calculate(new PayInputs { GrossCashPay = 50_000 }, Pack).Amount("nssf_employee"), nssf.MonthToDate);
        Assert.Equal(nssf.MonthToDate - nssf.Earlier, nssf.Amount);
        Assert.Equal(50_000m, nssf.Base);
    }

    [Theory]
    [InlineData("weekly", "weekly", true)]
    [InlineData("fortnightly", "fortnightly", true)]
    [InlineData("daily", "daily", true)]
    [InlineData("weekly", "monthly", false)]
    [InlineData("weekly", "daily", false)]       // days worked come with PAY-11
    [InlineData("monthly", "weekly", false)]
    [InlineData("daily", "hourly", false)]
    public void A_run_pays_the_wage_for_its_schedule(string frequency, string basis, bool paid)
    {
        var worked = Assert.Single(PayRunCalculator.Work(Week(OctoberFridays[0], frequency: frequency), [Person(450, basis)]));

        Assert.Equal(paid ? null : "other_basis", worked.ExcludedReason);
        if (paid) Assert.Equal(450m, worked.Totals!.BasicPay);
    }

    [Fact]
    public void A_schedule_that_makes_no_deductions_pays_the_whole_wage()
    {
        var worked = Assert.Single(PayRunCalculator.Work(Week(OctoberFridays[0], new StatutoryChoice([]), "daily"), [Person(450, "daily")]));

        Assert.DoesNotContain(worked.Lines, l => l.StatutoryKey is not null);
        Assert.Equal(450m, worked.Totals!.NetPay);
        Assert.Equal(0m, worked.Totals.EmployerCosts);
        Assert.Contains("no_deductions", worked.Notes);
        Assert.Empty(worked.Deductions!);
    }

    [Fact]
    public void Only_the_deductions_chosen_are_made()
    {
        var worked = Assert.Single(PayRunCalculator.Work(Week(OctoberFridays[0], new StatutoryChoice(["shif", "housing_levy"]), "daily"), [Person(450, "daily")]));

        Assert.Equal(["shif", "ahl_employee", "ahl_employer"], worked.Lines.Where(l => l.StatutoryKey is not null).Select(l => l.StatutoryKey));
        Assert.Equal(300m, Statutory(worked, "shif"));   // the month's minimum, taken on the first day
        Assert.Contains("some_deductions", worked.Notes);
        Assert.Equal(["housing_levy", "shif"], worked.Deductions!.Order());
    }

    [Fact]
    public void A_persons_own_choice_replaces_the_schedules()
    {
        var setup = Week(OctoberFridays[0], new StatutoryChoice([]), "daily");
        var mary = Person(450, "daily", choice: new StatutoryChoice(["shif"]));
        var peter = Person(450, "daily");

        var people = PayRunCalculator.Work(setup, [mary, peter]);

        Assert.Equal(300m, Statutory(people.Single(p => p.EmployeeId == mary.EmployeeId), "shif"));
        Assert.Equal(450m, people.Single(p => p.EmployeeId == peter.EmployeeId).Totals!.NetPay);
    }

    [Fact]
    public void A_pension_paid_later_in_the_month_gives_back_tax_and_is_flagged()
    {
        // Month-to-date pay only grows, so tax only grows, unless something comes off before tax later.
        var big = Assert.Single(PayRunCalculator.Work(Week(OctoberFridays[0]), [Person(60_000)]));
        var pension = new ItemDefinition(Guid.CreateVersion7(), "PENSION", "Pension", "deduction", false, true, "fixed", null, null);
        var setup = Week(OctoberFridays[1]) with { Items = [pension], Assignments = [new ItemAssignment(pension.Id, null, 20_000, null, new DateOnly(2026, 10, 5), null)] };

        var after = Assert.Single(PayRunCalculator.Work(setup, [Person(1_000, earlier: MonthToDate.From(big.Lines))]));

        Assert.True(Statutory(after, "paye") < 0);
        Assert.Contains("refund", after.Notes);
        var month = PayCalculator.Calculate(new PayInputs { GrossCashPay = 61_000, PensionContributions = 20_000 }, Pack).Amount("paye");
        Assert.Equal(month, Statutory(big, "paye") + Statutory(after, "paye"));
    }

    [Fact]
    public void A_monthly_run_with_nothing_earlier_is_as_before()
    {
        var worked = Assert.Single(PayRunCalculator.Work(
            new RunSetup(new RunPeriod(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), Weekly, null, Pack, new Dictionary<string, LineName>(), [], []),
            [Person(100_000, "monthly")]));

        Assert.Equal(19_308.35m, Statutory(worked, "paye"));
        Assert.All(worked.Lines.Where(l => l.StatutoryKey is not null), l => Assert.Equal(0m, l.Earlier));
        Assert.Empty(worked.Notes);
    }
}
