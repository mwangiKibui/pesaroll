using Pesaroll.Payroll.Engine;

namespace Pesaroll.Payroll.Tests;

/// <summary>
/// PAY-11: people are paid by their rate. A salary or wage is paid as it is on its own schedule; a
/// daily or hourly rate is paid on any schedule for the days or hours entered for the run (else the
/// days or hours they usually work; else they are left out, never paid by guesswork). Items paid by
/// the hour or day (overtime) pay the hours entered at a multiple of the person's own rate or at an
/// amount a unit. A rate below the minimum wage for their place of work is flagged.
/// </summary>
public sealed class PayRatesTests
{
    private static readonly RulePack Pack = RulePack.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulePacks", "KE-2026.02.json")));
    private static readonly Guid Schedule = Guid.CreateVersion7();
    private static readonly DateOnly Friday = new(2026, 10, 9);

    private static readonly WageOrder Order = new(8, 225, new Dictionary<string, MinimumWage>
    {
        ["cities"] = new(16_113.75m, 775.39m, 144.054m),
        ["municipalities"] = new(14_866.92m, 712.32m, 131.652m),
        ["other"] = new(8_596.494m, 435.66m, 80.507m),
    });

    private static readonly ItemDefinition Overtime = new(Guid.CreateVersion7(), "OVERTIME", "Overtime", "earning", true, false, "per_unit", null, null,
        Unit: "hour", Multiplier: 1.5m);

    private static readonly ItemDefinition Nights = new(Guid.CreateVersion7(), "NIGHT", "Night shift", "earning", true, false, "per_unit", 200m, null,
        Unit: "hour");

    private static Candidate Person(decimal rate, string basis, decimal? usual = null, IReadOnlyList<TimeEntry>? time = null, string? area = "other")
    {
        var from = new DateOnly(2024, 1, 15);
        return new Candidate(Guid.CreateVersion7(), "C-01", "Peter Kamau", from, null,
            [new JobRow(Guid.CreateVersion7(), from, null, Schedule)],
            [new BasicPayRow(Guid.CreateVersion7(), from, null, rate, basis, "KES", usual)],
            Time: time, WageArea: area);
    }

    private static RunSetup Run(string frequency = "weekly", IReadOnlyList<ItemDefinition>? items = null, IReadOnlyList<ItemAssignment>? assignments = null,
        WageOrder? wages = null)
    {
        var period = frequency switch
        {
            "monthly" => new RunPeriod(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)),
            "daily" => new RunPeriod(Friday, Friday),
            _ => new RunPeriod(Friday.AddDays(-6), Friday),
        };
        return new RunSetup(period, Schedule, null, Pack, new Dictionary<string, LineName>(), items ?? [], assignments ?? [], frequency,
            new StatutoryChoice([]), wages ?? Order);
    }

    private static WorkedPerson Work(RunSetup setup, Candidate person) => Assert.Single(PayRunCalculator.Work(setup, [person]));

    private static WorkedLine Basic(WorkedPerson person) => person.Lines.Single(l => l.Code == "BASIC");

    [Fact]
    public void A_daily_rate_on_a_weekly_schedule_pays_the_days_entered()
    {
        var worked = Work(Run(), Person(450, "daily", time: [new TimeEntry(null, 5)]));

        Assert.Null(worked.ExcludedReason);
        Assert.Equal(2_250m, worked.Totals!.BasicPay);
        Assert.Equal((5m, 450m), (Basic(worked).Quantity, Basic(worked).UnitRate));
        Assert.DoesNotContain("usual_time", worked.Notes);
    }

    [Fact]
    public void With_nothing_entered_a_daily_rate_pays_the_usual_days_and_says_so()
    {
        var worked = Work(Run(), Person(450, "daily", usual: 6));

        Assert.Equal(2_700m, worked.Totals!.BasicPay);
        Assert.Contains("usual_time", worked.Notes);
    }

    [Theory]
    [InlineData(null)]      // nothing entered, nothing usual
    [InlineData(0.0)]       // entered: didn't work
    public void With_no_days_a_daily_rate_is_left_out_not_guessed(double? entered)
    {
        var time = entered is { } days ? [new TimeEntry(null, (decimal)days)] : (IReadOnlyList<TimeEntry>?)null;
        var worked = Work(Run(), Person(450, "daily", usual: entered is null ? null : 6, time: time));

        Assert.Equal("no_time", worked.ExcludedReason);
        Assert.Empty(worked.Lines);
    }

    [Fact]
    public void A_daily_rate_on_a_daily_schedule_is_a_day_unless_said_otherwise()
    {
        Assert.Equal(450m, Work(Run("daily"), Person(450, "daily")).Totals!.BasicPay);
        Assert.Equal(225m, Work(Run("daily"), Person(450, "daily", time: [new TimeEntry(null, 0.5m)])).Totals!.BasicPay);
    }

    [Theory]
    [InlineData("weekly", "weekly", null)]
    [InlineData("monthly", "monthly", null)]
    [InlineData("monthly", "daily", null)]          // casuals paid monthly for their days
    [InlineData("fortnightly", "hourly", null)]
    [InlineData("monthly", "weekly", "other_basis")]
    [InlineData("weekly", "monthly", "other_basis")]
    public void A_daily_or_hourly_rate_is_paid_on_any_schedule_and_a_salary_only_on_its_own(string frequency, string basis, string? excluded)
    {
        var worked = Work(Run(frequency), Person(20_000, basis, time: [new TimeEntry(null, 2)]));

        Assert.Equal(excluded, worked.ExcludedReason);
    }

    [Fact]
    public void Overtime_is_paid_at_a_multiple_of_the_persons_own_hourly_rate()
    {
        var worked = Work(Run(items: [Overtime]), Person(150, "hourly", time: [new TimeEntry(null, 46), new TimeEntry(Overtime.Id, 6)]));

        var overtime = worked.Lines.Single(l => l.Code == "OVERTIME");
        Assert.Equal((6m, 225m, 1_350m), (overtime.Quantity, overtime.UnitRate, overtime.Amount));
        Assert.True(overtime.Taxable);
        Assert.Equal(8_250m, worked.Totals!.GrossPay);
        Assert.Equal(6_900m, worked.Totals.BasicPay);
    }

    [Theory]
    [InlineData("monthly", 45_000, 300)]        // 45,000 / 225 hours = 200 an hour; x 1.5
    [InlineData("daily", 800, 150)]             // 800 / 8 hours = 100 an hour; x 1.5
    [InlineData("weekly", 9_000, 260)]          // 9,000 x 52 / 12 / 225 = 173.33; x 1.5 = 260.00
    public void A_salaried_or_daily_persons_hourly_rate_comes_from_the_wage_order(string basis, decimal pay, decimal overtimeRate)
    {
        var worked = Work(Run(basis == "daily" ? "weekly" : basis, items: [Overtime]),
            Person(pay, basis, time: [new TimeEntry(null, 5), new TimeEntry(Overtime.Id, 10)]));

        var overtime = worked.Lines.Single(l => l.Code == "OVERTIME");
        Assert.Equal(overtimeRate, overtime.UnitRate);
        Assert.Equal(overtimeRate * 10, overtime.Amount);
    }

    [Fact]
    public void An_amount_a_unit_pays_the_hours_at_that_amount_and_a_persons_own_terms_win()
    {
        var person = Person(150, "hourly", time: [new TimeEntry(null, 40), new TimeEntry(Nights.Id, 4), new TimeEntry(Overtime.Id, 2)]);
        var own = new ItemAssignment(Overtime.Id, person.EmployeeId, null, null, new DateOnly(2026, 1, 1), null, Multiplier: 2m);
        var flat = new ItemAssignment(Nights.Id, person.EmployeeId, 250m, null, new DateOnly(2026, 1, 1), null);

        var standard = Work(Run(items: [Overtime, Nights]), person);
        var theirs = Work(Run(items: [Overtime, Nights], assignments: [own, flat]), person);

        Assert.Equal(800m, standard.Lines.Single(l => l.Code == "NIGHT").Amount);         // 4 h x 200
        Assert.Equal(1_000m, theirs.Lines.Single(l => l.Code == "NIGHT").Amount);         // 4 h x their 250
        Assert.Equal(600m, theirs.Lines.Single(l => l.Code == "OVERTIME").Amount);        // 2 h x 2 x 150
    }

    [Fact]
    public void Hours_for_an_archived_item_or_none_at_all_pay_nothing_extra()
    {
        var archived = Overtime with { Status = "archived" };
        var worked = Work(Run(items: [archived]), Person(150, "hourly", time: [new TimeEntry(null, 40), new TimeEntry(Overtime.Id, 6)]));

        Assert.DoesNotContain(worked.Lines, l => l.Code == "OVERTIME");
        Assert.Equal(6_000m, worked.Totals!.GrossPay);
    }

    [Theory]
    [InlineData("daily", 450, "cities", true)]             // below 775.39 a day
    [InlineData("daily", 450, "other", false)]             // above 435.66
    [InlineData("hourly", 144.05, "cities", true)]         // below 144.054: never let through by rounding
    [InlineData("monthly", 16_000, "cities", true)]
    [InlineData("monthly", 15_000, "municipalities", false)]
    [InlineData("weekly", 1_900, "other", true)]           // 1,900 x 52 / 12 = 8,233.33 a month, below 8,596.49
    public void A_rate_below_the_minimum_wage_for_the_place_of_work_is_flagged(string basis, decimal rate, string area, bool below)
    {
        var frequency = basis is "daily" or "hourly" ? "weekly" : basis;
        var worked = Work(Run(frequency), Person(rate, basis, time: [new TimeEntry(null, 5)], area: area));

        Assert.Equal(below, worked.Notes.Contains("below_minimum"));
    }

    [Fact]
    public void Without_a_wage_area_or_a_wage_order_nothing_is_checked_and_a_missing_area_is_said()
    {
        Assert.Contains("no_wage_area", Work(Run(), Person(100, "daily", time: [new TimeEntry(null, 5)], area: null)).Notes);
        var noOrder = Work(Run() with { Wages = null }, Person(100, "daily", time: [new TimeEntry(null, 5)]));
        Assert.DoesNotContain("below_minimum", noOrder.Notes);
        Assert.DoesNotContain("no_wage_area", noOrder.Notes);
    }
}
