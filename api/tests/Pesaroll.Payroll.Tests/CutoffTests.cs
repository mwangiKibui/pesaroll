using Pesaroll.Payroll.Engine;

namespace Pesaroll.Payroll.Tests;

/// <summary>
/// PAY-04: a schedule may use a cut-off. What it ticks that happens after the cut-off waits for the
/// next run: a joiner is left out (after_cutoff), a leaver is paid to the cut-off, a pay change keeps
/// the old rate, and overtime and one-off amounts entered late aren't paid. The next run pays what
/// was held back as lines owed from the last period. Review edits and corrections ignore the cut-off.
/// </summary>
public sealed class CutoffTests
{
    private static readonly RulePack Pack = RulePack.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulePacks", "KE-2026.02.json")));
    private static readonly Guid Schedule = Guid.CreateVersion7();
    private static readonly RunPeriod October = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
    private static readonly RunPeriod November = new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
    private static readonly DateOnly LongAgo = new(2024, 1, 15);
    private static readonly DateOnly OnCutoff = new(2026, 10, 25);
    private static readonly DateOnly AfterCutoff = new(2026, 10, 27);
    private static readonly string[] Everything = ["overtime", "new_employees", "leavers", "unpaid_leave", "pay_changes", "one_off"];

    private static readonly ItemDefinition Overtime = new(Guid.CreateVersion7(), "OT", "Overtime", "earning", true, false, "per_unit", 300, null, Unit: "hour");
    private static readonly ItemDefinition Commission = new(Guid.CreateVersion7(), "COMM", "Commission", "earning", true, false, "each_run", null, null);
    private static readonly ItemDefinition Advance = new(Guid.CreateVersion7(), "ADV", "Salary advance", "deduction", false, false, "each_run", null, null);
    private static readonly ItemDefinition Loan = new(Guid.CreateVersion7(), "LOAN", "Staff loan", "deduction", false, false, "fixed", null, null);
    private static readonly ItemDefinition House = new(Guid.CreateVersion7(), "HOUSE", "House allowance", "earning", true, false, "percent_of_basic", null, 10);

    private static Candidate Person(IReadOnlyList<BasicPayRow>? pay = null, DateOnly? hired = null, DateOnly? left = null, IReadOnlyList<TimeEntry>? time = null,
        IReadOnlyList<RunAmount>? amounts = null, LastPaid? last = null)
    {
        var from = hired ?? LongAgo;
        return new Candidate(Guid.CreateVersion7(), "C-01", "Grace Njeri", from, left, [new JobRow(Guid.CreateVersion7(), from, null, Schedule)],
            pay ?? [Pay(31_000, from)], Time: time, Amounts: amounts, Last: last);
    }

    private static BasicPayRow Pay(decimal amount, DateOnly? from = null, DateOnly? to = null, string basis = "monthly") =>
        new(Guid.CreateVersion7(), from ?? LongAgo, to, amount, basis, "KES");

    private static RunSetup Run(RunPeriod? period = null, string[]? appliesTo = null, bool cutoff = true, LastRun? last = null,
        IReadOnlyCollection<Guid>? only = null, IReadOnlyList<ItemAssignment>? assignments = null) =>
        new(period ?? October, Schedule, only, Pack, new Dictionary<string, LineName>(), [Overtime, Commission, Advance, Loan, House],
            assignments ?? [], "monthly", Cutoff: cutoff ? new RunCutoff(OnCutoff, appliesTo ?? Everything) : null, Last: last);

    /// <summary>November's run, after an approved October run that used the cut-off.</summary>
    private static RunSetup NextRun(string[]? appliesTo = null, IReadOnlyList<ItemAssignment>? assignments = null) =>
        Run(November, cutoff: false, last: new LastRun(October, new RunCutoff(OnCutoff, appliesTo ?? Everything)), assignments: assignments);

    private static WorkedPerson Work(RunSetup setup, Candidate person) => Assert.Single(PayRunCalculator.Work(setup, [person]));

    private static WorkedLine? Line(WorkedPerson person, string code, bool owed = false) =>
        person.Lines.SingleOrDefault(l => l.Code == code && (l.Owed is not null) == owed);

    [Fact]
    public void A_joiner_after_the_cutoff_waits_for_the_next_run_which_pays_their_days_owed_and_the_new_period()
    {
        var october = Work(Run(), Person(hired: AfterCutoff));
        Assert.Equal("after_cutoff", october.ExcludedReason);

        var november = Work(NextRun(), Person(hired: AfterCutoff, last: new LastPaid("after_cutoff", [])));
        Assert.Equal(31_000m, november.Totals!.BasicPay);
        var owed = Line(november, "BASIC", owed: true)!;
        Assert.Equal((5_000m, October), (owed.Amount, owed.Owed));            // 31,000 x 5 / 31: 27 to 31 October
        Assert.Equal(36_000m, november.Totals.GrossPay);
        Assert.Contains("owed", november.Notes);
    }

    [Fact]
    public void A_joiner_on_the_cutoff_day_is_paid_in_the_run()
    {
        var grace = Work(Run(), Person(hired: OnCutoff));

        Assert.Null(grace.ExcludedReason);
        Assert.Equal(7_000m, grace.Totals!.BasicPay);                         // 25 to 31 October
    }

    [Fact]
    public void A_leaver_after_the_cutoff_is_paid_to_it_and_the_next_run_pays_the_rest()
    {
        var left = new DateOnly(2026, 10, 28);
        var october = Work(Run(), Person(left: left));
        Assert.Equal(25_000m, october.Totals!.BasicPay);                      // 1 to 25 October
        Assert.Contains("paid_to_cutoff", october.Notes);

        var november = Work(NextRun(), Person(left: left, last: new LastPaid(null, ["left", "prorated", "paid_to_cutoff"], Basic: 25_000)));
        Assert.Null(november.ExcludedReason);
        Assert.Equal(0m, november.Totals!.BasicPay);
        Assert.Null(Line(november, "BASIC"));
        Assert.Equal(3_000m, Line(november, "BASIC", owed: true)!.Amount);    // 26 to 28 October
        Assert.Equal(3_000m, november.Totals.GrossPay);
    }

    [Fact]
    public void A_leavers_final_dues_entered_in_the_next_run_are_paid_with_what_they_are_owed()
    {
        var everyone = new ItemAssignment(Loan.Id, null, 2_000, null, new DateOnly(2026, 1, 1), null);
        var november = Work(NextRun(assignments: [everyone]), Person(left: new DateOnly(2026, 10, 28), amounts: [new RunAmount(Commission.Id, 6_000)],
            last: new LastPaid(null, ["paid_to_cutoff"], Basic: 25_000)));

        Assert.Equal(6_000m, Line(november, "COMM")!.Amount);
        Assert.Null(Line(november, "LOAN"));                                  // nothing of the new period's own
        Assert.Equal(9_000m, november.Totals!.GrossPay);
    }

    [Fact]
    public void A_leaver_held_back_isnt_paid_again_by_a_later_run_or_without_one_before()
    {
        var left = new DateOnly(2026, 10, 28);

        Assert.Empty(PayRunCalculator.Work(NextRun(), [Person(left: left)]));                                     // nothing held back for them
        Assert.Empty(PayRunCalculator.Work(Run(November, cutoff: false), [Person(left: left, last: new LastPaid(null, ["paid_to_cutoff"], Basic: 25_000))]));
    }

    [Fact]
    public void A_pay_change_from_after_the_cutoff_keeps_the_old_rate_and_the_next_run_pays_the_difference()
    {
        Candidate Grace(LastPaid? last = null) => Person([Pay(31_000, to: AfterCutoff.AddDays(-1)), Pay(62_000, AfterCutoff)], last: last);

        var october = Work(Run(), Grace());
        Assert.Equal(31_000m, october.Totals!.BasicPay);
        Assert.Contains("pay_change_next_run", october.Notes);
        Assert.DoesNotContain("pay_changed", october.Notes);

        var november = Work(NextRun(), Grace(new LastPaid(null, ["pay_change_next_run"], Basic: 31_000)));
        Assert.Equal(62_000m, november.Totals!.BasicPay);
        Assert.Equal(5_000m, Line(november, "BASIC", owed: true)!.Amount);    // 31,000 more a month for 27 to 31 October
    }

    [Fact]
    public void Allowances_on_basic_pay_follow_the_basic_pay_owed()
    {
        var everyone = new ItemAssignment(House.Id, null, null, null, new DateOnly(2026, 1, 1), null);
        var november = Work(NextRun(assignments: [everyone]), Person(hired: AfterCutoff, last: new LastPaid("after_cutoff", [])));

        Assert.Equal(3_100m, Line(november, "HOUSE")!.Amount);
        Assert.Equal(500m, Line(november, "HOUSE", owed: true)!.Amount);       // 10% of the 5,000 owed
    }

    [Fact]
    public void Overtime_and_one_off_amounts_entered_after_the_cutoff_are_paid_in_the_next_run()
    {
        var late = new DateOnly(2026, 10, 26);
        var time = new[] { new TimeEntry(Overtime.Id, 10, late) };
        var amounts = new[] { new RunAmount(Commission.Id, 4_000, EnteredOn: late), new RunAmount(Advance.Id, 1_000, EnteredOn: OnCutoff) };

        var october = Work(Run(), Person(time: time, amounts: amounts));
        Assert.Null(Line(october, "OT"));
        Assert.Null(Line(october, "COMM"));
        Assert.Equal(1_000m, Line(october, "ADV")!.Amount);                   // entered on the cut-off day
        Assert.Contains("next_run", october.Notes);

        var november = Work(NextRun(), Person(last: new LastPaid(null, ["next_run"], Basic: 31_000, Time: time, Amounts: amounts)));
        Assert.Equal((3_000m, 10m, October), (Line(november, "OT", owed: true)!.Amount, Line(november, "OT", owed: true)!.Quantity, Line(november, "OT", owed: true)!.Owed));
        Assert.Equal(4_000m, Line(november, "COMM", owed: true)!.Amount);
        Assert.Null(Line(november, "ADV", owed: true));                       // paid in October
        Assert.Null(Line(november, "BASIC", owed: true));
        Assert.Equal(31_000m + 3_000m + 4_000m, november.Totals!.GrossPay);
    }

    [Fact]
    public void A_joiner_held_back_has_what_was_entered_for_them_paid_next_run()
    {
        var time = new[] { new TimeEntry(Overtime.Id, 4, OnCutoff) };
        var november = Work(NextRun(), Person(hired: AfterCutoff, last: new LastPaid("after_cutoff", [], Time: time)));

        Assert.Equal(1_200m, Line(november, "OT", owed: true)!.Amount);
    }

    [Fact]
    public void A_daily_rate_joiner_held_back_is_paid_the_days_entered_for_them_next_run()
    {
        var rate = new[] { Pay(1_000, AfterCutoff, basis: "daily") };
        var november = Work(NextRun(), Person(rate, AfterCutoff, time: [new TimeEntry(null, 20)],
            last: new LastPaid("after_cutoff", [], Time: [new TimeEntry(null, 4, AfterCutoff)])));

        Assert.Equal(20_000m, november.Totals!.BasicPay);
        var owed = Line(november, "BASIC", owed: true)!;
        Assert.Equal((4_000m, 4m, 1_000m), (owed.Amount, owed.Quantity, owed.UnitRate));
    }

    [Fact]
    public void Only_what_the_schedule_ticks_waits()
    {
        var grace = Work(Run(appliesTo: ["overtime"]), Person(hired: AfterCutoff, amounts: [new RunAmount(Commission.Id, 4_000, EnteredOn: AfterCutoff)]));

        Assert.Null(grace.ExcludedReason);
        Assert.Equal(4_000m, Line(grace, "COMM")!.Amount);
        Assert.DoesNotContain("next_run", grace.Notes);
    }

    [Fact]
    public void Without_a_cutoff_nothing_waits()
    {
        var grace = Work(Run(cutoff: false), Person(left: new DateOnly(2026, 10, 28), time: [new TimeEntry(Overtime.Id, 10, AfterCutoff)]));

        Assert.Equal(28_000m, grace.Totals!.BasicPay);
        Assert.Equal(3_000m, Line(grace, "OT")!.Amount);
    }

    [Fact]
    public void Review_edits_ignore_the_cutoff()
    {
        var amounts = new[]
        {
            new RunAmount(Loan.Id, 1_500, "Agreed to halve it", AfterCutoff),
            new RunAmount(null, 30_000, "Unpaid day agreed", AfterCutoff),
        };
        var grace = Work(Run(), Person(amounts: amounts));

        Assert.Equal(1_500m, Line(grace, "LOAN")!.Amount);
        Assert.Equal(30_000m, grace.Totals!.BasicPay);
        Assert.DoesNotContain("next_run", grace.Notes);
    }

    [Fact]
    public void A_correction_ignores_the_cutoff()
    {
        var grace = Person(hired: AfterCutoff);
        var worked = Work(Run(only: [grace.EmployeeId]), grace);

        Assert.Null(worked.ExcludedReason);
        Assert.Equal(5_000m, worked.Totals!.BasicPay);
    }
}
