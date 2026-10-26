using Pesaroll.Payroll.Engine;

namespace Pesaroll.Payroll.Tests;

/// <summary>
/// PAY-04: what is entered for one run. Amounts for items entered each run (commission, an advance)
/// are paid or taken; an accountant reviewing the run can change or remove a person's item for that
/// run, add one they don't have, set their basic pay or a statutory amount (PAYE follows it), or
/// choose which statutory deductions they make. Each such edit keeps its reason and marks the
/// person adjusted; routine amounts don't.
/// </summary>
public sealed class RunEditsTests
{
    private static readonly RulePack Pack = RulePack.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulePacks", "KE-2026.02.json")));
    private static readonly Guid Schedule = Guid.CreateVersion7();
    private static readonly RunPeriod October = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
    private static readonly DateOnly LongAgo = new(2024, 1, 15);
    private static readonly Guid ShifItem = Guid.CreateVersion7();

    private static readonly ItemDefinition Commission = new(Guid.CreateVersion7(), "COMM", "Commission", "earning", true, false, "each_run", null, null);
    private static readonly ItemDefinition Advance = new(Guid.CreateVersion7(), "ADV", "Salary advance", "deduction", false, false, "each_run", null, null);
    private static readonly ItemDefinition Loan = new(Guid.CreateVersion7(), "LOAN", "Staff loan", "deduction", false, false, "fixed", null, null);
    private static readonly ItemDefinition Lunch = new(Guid.CreateVersion7(), "LUNCH", "Lunch", "earning", false, false, "fixed", 2_000, null);
    private static readonly ItemDefinition House = new(Guid.CreateVersion7(), "HOUSE", "House allowance", "earning", true, false, "percent_of_basic", null, 15);

    private static Candidate Person(decimal pay = 100_000, string basis = "monthly", IReadOnlyList<RunAmount>? amounts = null, StatutoryChoice? runChoice = null,
        MonthToDate? earlier = null)
    {
        var id = Guid.CreateVersion7();
        return new Candidate(id, "E-01", "Amina Otieno", LongAgo, null, [new JobRow(Guid.CreateVersion7(), LongAgo, null, Schedule)],
            [new BasicPayRow(Guid.CreateVersion7(), LongAgo, null, pay, basis, "KES")], Earlier: earlier, Amounts: amounts, RunChoice: runChoice);
    }

    private static RunSetup Run(Candidate? givenTo = null, RunPeriod? period = null, string frequency = "monthly") =>
        new(period ?? October, Schedule, null, Pack, new Dictionary<string, LineName>
        {
            ["basic"] = new("BASIC", "Basic pay", null),
            ["shif"] = new("SHIF", "SHIF", ShifItem),
        }, [Commission, Advance, Loan, Lunch, House],
        [
            new ItemAssignment(Loan.Id, givenTo?.EmployeeId, 3_000, null, new DateOnly(2026, 1, 1), null),
            new ItemAssignment(House.Id, null, null, null, new DateOnly(2026, 1, 1), null),
        ], frequency);

    private static WorkedPerson Work(Candidate person, RunSetup? setup = null) => Assert.Single(PayRunCalculator.Work(setup ?? Run(person), [person]));

    private static WorkedLine? Line(WorkedPerson person, string code) => person.Lines.SingleOrDefault(l => l.Code == code);

    [Fact]
    public void Amounts_entered_for_the_run_are_paid_or_taken_without_marking_anyone_adjusted()
    {
        var amina = Work(Person(amounts: [new RunAmount(Commission.Id, 5_000), new RunAmount(Advance.Id, 2_000)]));
        var plain = Work(Person());

        Assert.Equal((5_000m, true), (Line(amina, "COMM")!.Amount, Line(amina, "COMM")!.Taxable));
        Assert.Equal(("deduction", 2_000m), (Line(amina, "ADV")!.Kind, Line(amina, "ADV")!.Amount));
        Assert.Equal(plain.Totals!.GrossPay + 5_000m, amina.Totals!.GrossPay);
        Assert.True(amina.Totals.Paye > plain.Totals.Paye);              // commission is taxed
        Assert.DoesNotContain("adjusted", amina.Notes);
    }

    [Fact]
    public void An_items_amount_can_be_changed_or_removed_for_one_run_with_the_reason_kept()
    {
        var halved = Person(amounts: [new RunAmount(Loan.Id, 1_500, "Agreed to halve it this month")]);
        var none = Person(amounts: [new RunAmount(Loan.Id, 0, "Paid off in cash")]);

        var half = Work(halved);
        Assert.Equal((1_500m, "Agreed to halve it this month"), (Line(half, "LOAN")!.Amount, Line(half, "LOAN")!.Reason));
        Assert.Contains("adjusted", half.Notes);
        var off = Work(none);
        Assert.Null(Line(off, "LOAN"));
        Assert.Contains("adjusted", off.Notes);
        Assert.Equal(3_000m, Line(Work(Person()), "LOAN")!.Amount);      // unchanged without an edit
    }

    [Fact]
    public void An_item_someone_doesnt_have_can_be_added_for_one_run_and_an_allowance_set()
    {
        var amina = Work(Person(amounts: [new RunAmount(Lunch.Id, 2_500, "Worked through lunch on the stocktake"), new RunAmount(House.Id, 10_000, "Capped")]));

        Assert.Equal(2_500m, Line(amina, "LUNCH")!.Amount);
        var house = Line(amina, "HOUSE")!;
        Assert.Equal((10_000m, (decimal?)null, (decimal?)null), (house.Amount, house.Base, house.Rate));   // set, not 15% of basic
    }

    [Fact]
    public void Basic_pay_can_be_set_for_one_run_even_for_a_rate_with_no_time_entered()
    {
        var salaried = Work(Person(amounts: [new RunAmount(null, 80_000, "Unpaid leave agreed off the books")]));
        var casual = Work(Person(450, "daily", amounts: [new RunAmount(null, 3_000, "Paid for the week's harvest")]));

        Assert.Equal(80_000m, salaried.Totals!.BasicPay);
        Assert.Equal(12_000m, Line(salaried, "HOUSE")!.Amount);          // allowances follow the basic pay set: 15% of 80,000
        Assert.Contains("adjusted", salaried.Notes);
        Assert.Null(casual.ExcludedReason);
        Assert.Equal(3_000m, casual.Totals!.BasicPay);
        Assert.Equal("Paid for the week's harvest", Line(casual, "BASIC")!.Reason);
    }

    [Fact]
    public void A_statutory_amount_set_for_one_run_replaces_the_rules_and_paye_follows_it()
    {
        var amina = Work(Person(50_000, amounts: [new RunAmount(ShifItem, 150, "Agreed with the employee")]));

        var shif = amina.Lines.Single(l => l.StatutoryKey == "shif");
        Assert.Equal((150m, "Agreed with the employee"), (shif.Amount, shif.Reason));
        var house = Pack.Round(50_000m * 0.15m);
        var engine = PayCalculator.Calculate(new PayInputs { GrossCashPay = 50_000m + house }, Pack, StatutoryChoice.All,
            new Dictionary<string, decimal> { ["shif"] = 150m });
        Assert.Equal(engine.Amount("paye"), amina.Totals!.Paye);
        Assert.Contains("adjusted", amina.Notes);
    }

    [Fact]
    public void In_a_later_run_of_the_month_the_amount_set_is_this_runs_share()
    {
        var week = new RunPeriod(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 16));
        var earlier = new MonthToDate(10_000m, 0, new Dictionary<string, decimal> { ["shif"] = 300m });
        var amina = Work(Person(10_000, "weekly", amounts: [new RunAmount(ShifItem, 150, "Agreed")], earlier: earlier),
            Run(period: week, frequency: "weekly"));

        var shif = amina.Lines.Single(l => l.StatutoryKey == "shif");
        Assert.Equal((150m, 450m, 300m), (shif.Amount, shif.MonthToDate!.Value, shif.Earlier));
    }

    [Fact]
    public void A_choice_of_statutory_deductions_for_one_run_replaces_the_schedules_and_the_persons()
    {
        var person = Person(runChoice: new StatutoryChoice(["paye"])) with { Choice = StatutoryChoice.All };
        var amina = Work(person);

        Assert.Equal(["paye"], amina.Deductions!);
        Assert.DoesNotContain(amina.Lines, l => l.StatutoryKey is "shif" or "nssf_employee" or "ahl_employee");
        Assert.Contains("adjusted", amina.Notes);
    }
}
