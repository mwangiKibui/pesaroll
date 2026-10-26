using Pesaroll.Payroll.Engine;

namespace Pesaroll.Payroll.Tests;

/// <summary>
/// PAY-04: a salary is paid for the calendar days of the period someone was employed, and when their
/// pay changes inside the period each part is paid at its own rate for its own days. A daily or
/// hourly rate is already paid for the time worked, so it is never prorated.
/// </summary>
public sealed class ProrationTests
{
    private static readonly RulePack Pack = RulePack.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulePacks", "KE-2026.02.json")));
    private static readonly Guid Schedule = Guid.CreateVersion7();
    private static readonly RunPeriod October = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
    private static readonly RunPeriod WeekToFriday = new(new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 9));
    private static readonly DateOnly LongAgo = new(2024, 1, 15);

    private static Candidate Person(IReadOnlyList<BasicPayRow> pay, DateOnly? hired = null, DateOnly? left = null, IReadOnlyList<TimeEntry>? time = null)
    {
        var from = hired ?? LongAgo;
        return new Candidate(Guid.CreateVersion7(), "C-01", "Grace Njeri", from, left,
            [new JobRow(Guid.CreateVersion7(), from, null, Schedule)], pay, Time: time, WageArea: "cities");
    }

    private static BasicPayRow Pay(decimal amount, DateOnly? from = null, DateOnly? to = null, string basis = "monthly") =>
        new(Guid.CreateVersion7(), from ?? LongAgo, to, amount, basis, "KES");

    private static RunSetup Run(RunPeriod? period = null, string frequency = "monthly") =>
        new(period ?? October, Schedule, null, Pack, new Dictionary<string, LineName>(), [], [], frequency);

    private static WorkedPerson Work(RunSetup setup, Candidate person) => Assert.Single(PayRunCalculator.Work(setup, [person]));

    private static WorkedLine Basic(WorkedPerson person) => person.Lines.Single(l => l.Code == "BASIC");

    [Fact]
    public void A_joiner_is_paid_for_the_days_from_their_start()
    {
        var hired = new DateOnly(2026, 10, 17);
        var grace = Work(Run(), Person([Pay(31_000, hired)], hired));

        Assert.Equal(15_000m, grace.Totals!.BasicPay);                  // 31,000 x 15 / 31
        Assert.Equal((31_000m, 0.483871m), (Basic(grace).Base, Basic(grace).Rate));
        Assert.Equal(["joined", "prorated"], grace.Notes.Take(2));
    }

    [Fact]
    public void A_leaver_is_paid_to_their_last_day()
    {
        var grace = Work(Run(), Person([Pay(31_000)], left: new DateOnly(2026, 10, 10)));

        Assert.Equal(10_000m, grace.Totals!.BasicPay);
        Assert.Contains("prorated", grace.Notes);
    }

    [Fact]
    public void A_pay_change_inside_the_period_pays_each_part_at_its_own_rate()
    {
        var rise = new DateOnly(2026, 10, 15);
        var grace = Work(Run(), Person([Pay(31_000, to: rise.AddDays(-1)), Pay(62_000, rise)]));

        Assert.Equal(48_000m, grace.Totals!.BasicPay);                  // 31,000 x 14 / 31 + 62,000 x 17 / 31
        Assert.Equal(((decimal?)null, (decimal?)null), (Basic(grace).Base, Basic(grace).Rate));
        Assert.Contains("pay_changed", grace.Notes);
        Assert.DoesNotContain("prorated", grace.Notes);
    }

    [Fact]
    public void A_joiner_whose_pay_changes_in_their_first_period_is_paid_both_parts()
    {
        var (hired, rise) = (new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 25));
        var grace = Work(Run(), Person([Pay(31_000, hired, rise.AddDays(-1)), Pay(46_500, rise)], hired));

        Assert.Equal(25_500m, grace.Totals!.BasicPay);                  // 31,000 x 15 / 31 + 46,500 x 7 / 31
        Assert.Contains("prorated", grace.Notes);
        Assert.Contains("pay_changed", grace.Notes);
    }

    [Fact]
    public void A_weekly_wage_is_prorated_over_the_week()
    {
        var hired = new DateOnly(2026, 10, 7);
        var grace = Work(Run(WeekToFriday, "weekly"), Person([Pay(7_000, hired, basis: "weekly")], hired));

        Assert.Equal(3_000m, grace.Totals!.BasicPay);
    }

    [Fact]
    public void A_whole_period_is_paid_as_it_is()
    {
        var grace = Work(Run(), Person([Pay(31_000)]));

        Assert.Equal(31_000m, grace.Totals!.BasicPay);
        Assert.Equal(((decimal?)null, (decimal?)null), (Basic(grace).Base, Basic(grace).Rate));
        Assert.DoesNotContain("prorated", grace.Notes);
        Assert.DoesNotContain("pay_changed", grace.Notes);
    }

    [Fact]
    public void A_daily_rate_is_paid_for_the_days_entered_never_prorated()
    {
        var hired = new DateOnly(2026, 10, 7);
        var grace = Work(Run(WeekToFriday, "weekly"), Person([Pay(800, hired, basis: "daily")], hired, time: [new TimeEntry(null, 3)]));

        Assert.Equal(2_400m, grace.Totals!.BasicPay);
        Assert.DoesNotContain("prorated", grace.Notes);
    }

    [Fact]
    public void Days_paid_on_another_basis_are_left_to_the_reviewer_and_said()
    {
        var change = new DateOnly(2026, 10, 15);
        var grace = Work(Run(), Person([Pay(800, to: change.AddDays(-1), basis: "daily"), Pay(31_000, change)]));

        Assert.Equal(17_000m, grace.Totals!.BasicPay);                  // the salary's 17 days; the daily part is not guessed
        Assert.Contains("basis_changed", grace.Notes);
    }

    [Fact]
    public void Allowances_on_basic_pay_follow_the_prorated_pay()
    {
        var house = new ItemDefinition(Guid.CreateVersion7(), "HOUSE", "House allowance", "earning", true, false, "percent_of_basic", null, 10);
        var hired = new DateOnly(2026, 10, 17);
        var person = Person([Pay(31_000, hired)], hired);
        var setup = Run() with { Items = [house], Assignments = [new ItemAssignment(house.Id, null, null, null, new DateOnly(2026, 1, 1), null)] };

        Assert.Equal(1_500m, Work(setup, person).Lines.Single(l => l.Code == "HOUSE").Amount);
    }
}
