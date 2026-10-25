using Pesaroll.Payroll.Engine;

namespace Pesaroll.Payroll.Tests;

/// <summary>
/// PAY-03: working out each person's pay in a run. Who is in (on the schedule, employed in the
/// month), their basic pay and the organization's earnings and deductions for the month, the
/// statutory lines from the engine (PAY-01), and net pay; who is left out says why.
/// </summary>
public sealed class PayRunCalculatorTests
{
    private static readonly RulePack Pack = RulePack.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulePacks", "KE-2026.02.json")));
    private static readonly RunPeriod October = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
    private static readonly Guid Monthly = Guid.CreateVersion7();
    private static readonly Guid Weekly = Guid.CreateVersion7();

    private static readonly ItemDefinition House = new(Guid.CreateVersion7(), "HOUSE", "House allowance", "earning", Taxable: true, BeforeTax: false, "percent_of_basic", null, 15);
    private static readonly ItemDefinition Lunch = new(Guid.CreateVersion7(), "LUNCH", "Lunch", "earning", Taxable: false, BeforeTax: false, "fixed", 2000, null);
    private static readonly ItemDefinition Pension = new(Guid.CreateVersion7(), "PENSION", "Pension", "deduction", Taxable: false, BeforeTax: true, "fixed", null, null);
    private static readonly ItemDefinition Loan = new(Guid.CreateVersion7(), "LOAN", "Staff loan", "deduction", Taxable: false, BeforeTax: false, "fixed", null, null);
    private static readonly ItemDefinition Medical = new(Guid.CreateVersion7(), "MED-ER", "Medical cover", "employer_cost", Taxable: false, BeforeTax: false, "fixed", 3000, null);
    private static readonly ItemDefinition Overtime = new(Guid.CreateVersion7(), "OT", "Overtime", "earning", Taxable: true, BeforeTax: false, "each_run", null, null);

    private static Candidate Person(decimal basic = 100_000, string basis = "monthly", string currency = "KES", DateOnly? hired = null, DateOnly? left = null,
        Guid? schedule = null, string name = "Amina Otieno")
    {
        var id = Guid.CreateVersion7();
        var from = hired ?? new DateOnly(2024, 1, 15);
        return new Candidate(id, "E-" + id.ToString()[..4], name, from, left,
            [new JobRow(Guid.CreateVersion7(), from, null, schedule ?? Monthly)],
            [new BasicPayRow(Guid.CreateVersion7(), from, null, basic, basis, currency)]);
    }

    private static RunSetup Setup(IReadOnlyList<ItemDefinition>? items = null, IReadOnlyList<ItemAssignment>? given = null, IReadOnlyCollection<Guid>? only = null) =>
        new(October, Monthly, only, Pack, new Dictionary<string, LineName>
        {
            ["basic"] = new("BASIC", "Basic pay", null),
            ["paye"] = new("PAYE", "PAYE", null),
        }, items ?? [], given ?? []);

    private static ItemAssignment Everyone(ItemDefinition item, decimal? amount = null, DateOnly? from = null, DateOnly? to = null) =>
        new(item.Id, null, amount, null, from ?? new DateOnly(2026, 1, 1), to);

    private static ItemAssignment For(Candidate person, ItemDefinition item, decimal? amount = null, DateOnly? from = null, DateOnly? to = null) =>
        new(item.Id, person.EmployeeId, amount, null, from ?? new DateOnly(2026, 1, 1), to);

    private static WorkedPerson One(RunSetup setup, Candidate person) => Assert.Single(PayRunCalculator.Work(setup, [person]));

    private static decimal Line(WorkedPerson person, string code) => person.Lines.Single(l => l.Code == code).Amount;

    [Fact]
    public void The_golden_KES_100000_case_comes_out_the_same_through_a_pay_run()
    {
        var amina = One(Setup(), Person());

        Assert.True(amina.Included);
        Assert.Equal(100_000m, Line(amina, "BASIC"));
        Assert.Equal(6_000m, amina.Lines.Single(l => l.StatutoryKey == "nssf_employee").Amount);
        Assert.Equal(2_750m, amina.Lines.Single(l => l.StatutoryKey == "shif").Amount);
        Assert.Equal(1_500m, amina.Lines.Single(l => l.StatutoryKey == "ahl_employee").Amount);
        Assert.Equal(19_308.35m, Line(amina, "PAYE"));
        var totals = amina.Totals!;
        Assert.Equal(100_000m, totals.GrossPay);
        Assert.Equal(89_750m, totals.TaxablePay);
        Assert.Equal(29_558.35m, totals.TotalDeductions);
        Assert.Equal(70_441.65m, totals.NetPay);
        var engine = PayCalculator.Calculate(new PayInputs { GrossCashPay = 100_000 }, Pack);
        Assert.Equal(engine.EmployerCosts.Sum(l => l.Amount), totals.EmployerCosts);
    }

    [Fact]
    public void Each_statutory_line_keeps_what_it_was_worked_out_on_and_its_rate()
    {
        var nssf = One(Setup(), Person()).Lines.Single(l => l.StatutoryKey == "nssf_employee");

        Assert.Equal("deduction", nssf.Kind);
        Assert.Equal(100_000m, nssf.Base);
        Assert.Equal(Pack.Nssf.EmployeeRate, nssf.Rate);
        // A line the setup doesn't name falls back to its key.
        Assert.Equal("NSSF_EMPLOYEE", nssf.Code);
    }

    [Fact]
    public void A_percentage_of_basic_is_taxable_pay_when_the_item_is_taxable()
    {
        var amina = One(Setup([House], [Everyone(House)]), Person());

        Assert.Equal(15_000m, Line(amina, "HOUSE"));
        var engine = PayCalculator.Calculate(new PayInputs { GrossCashPay = 115_000 }, Pack);
        Assert.Equal(engine.Amount("paye"), Line(amina, "PAYE"));
        Assert.Equal(115_000m, amina.Totals!.GrossPay);
        Assert.Equal(engine.NetPay, amina.Totals.NetPay);
    }

    [Fact]
    public void A_persons_own_amount_replaces_the_one_for_everyone()
    {
        var amina = Person();
        var brian = Person(name: "Brian Mwangi");

        var people = PayRunCalculator.Work(Setup([Lunch], [Everyone(Lunch), For(amina, Lunch, 3_500)]), [amina, brian]);

        Assert.Equal(3_500m, Line(people.Single(p => p.EmployeeId == amina.EmployeeId), "LUNCH"));
        Assert.Equal(2_000m, Line(people.Single(p => p.EmployeeId == brian.EmployeeId), "LUNCH"));   // the item's default
    }

    [Fact]
    public void Only_items_that_apply_in_the_month_are_paid()
    {
        var amina = Person();
        var given = new[]
        {
            For(amina, Loan, 5_000, to: new DateOnly(2026, 9, 30)),             // the loan's last month was September
            For(amina, Pension, 1_000, from: new DateOnly(2026, 11, 1)),        // starts next month
            For(amina, Lunch, 2_500, from: new DateOnly(2026, 10, 31)),         // starts on the month's last day: counts
        };

        var worked = One(Setup([Loan, Pension, Lunch], given), amina);

        Assert.DoesNotContain(worked.Lines, l => l.Code is "LOAN" or "PENSION");
        Assert.Equal(2_500m, Line(worked, "LUNCH"));
    }

    [Fact]
    public void Items_entered_each_payroll_and_archived_items_wait()
    {
        var archived = Lunch with { Id = Guid.CreateVersion7(), Code = "OLD", Status = "archived" };
        var worked = One(Setup([Overtime, archived], [Everyone(Overtime), Everyone(archived, 1_000)]), Person());

        Assert.DoesNotContain(worked.Lines, l => l.Code is "OT" or "OLD");
    }

    [Fact]
    public void A_non_taxable_earning_is_added_after_tax()
    {
        var plain = One(Setup(), Person());
        var withLunch = One(Setup([Lunch], [Everyone(Lunch)]), Person());

        Assert.Equal(Line(plain, "PAYE"), Line(withLunch, "PAYE"));
        Assert.Equal(plain.Totals!.TotalDeductions, withLunch.Totals!.TotalDeductions);
        Assert.Equal(102_000m, withLunch.Totals.GrossPay);
        Assert.Equal(plain.Totals.NetPay + 2_000, withLunch.Totals.NetPay);
    }

    [Fact]
    public void A_pension_deduction_comes_off_before_tax()
    {
        var amina = Person();
        var worked = One(Setup([Pension], [For(amina, Pension, 5_000)]), amina);

        var engine = PayCalculator.Calculate(new PayInputs { GrossCashPay = 100_000, PensionContributions = 5_000 }, Pack);
        Assert.Equal(engine.Amount("paye"), Line(worked, "PAYE"));
        Assert.True(Line(worked, "PAYE") < 19_308.35m);
        Assert.Equal(engine.NetPay - 5_000, worked.Totals!.NetPay);
        Assert.True(worked.Lines.Single(l => l.Code == "PENSION").BeforeTax);
    }

    [Fact]
    public void Other_deductions_come_off_net_pay()
    {
        var amina = Person();
        var worked = One(Setup([Loan], [For(amina, Loan, 10_000)]), amina);

        Assert.Equal(19_308.35m, Line(worked, "PAYE"));
        Assert.Equal(39_558.35m, worked.Totals!.TotalDeductions);
        Assert.Equal(60_441.65m, worked.Totals.NetPay);
    }

    [Fact]
    public void Employer_costs_add_to_the_employers_cost_not_to_pay()
    {
        var worked = One(Setup([Medical], [Everyone(Medical)]), Person());

        Assert.Equal(70_441.65m, worked.Totals!.NetPay);
        var statutory = PayCalculator.Calculate(new PayInputs { GrossCashPay = 100_000 }, Pack).EmployerCosts.Sum(l => l.Amount);
        Assert.Equal(statutory + 3_000, worked.Totals.EmployerCosts);
        Assert.Equal("employer_cost", worked.Lines.Single(l => l.Code == "MED-ER").Kind);
    }

    [Theory]
    [InlineData("weekly", "KES", "other_basis")]
    [InlineData("daily", "KES", "no_time")]          // a daily rate with no days entered (PAY-11)
    [InlineData("monthly", "USD", "other_currency")]
    public void People_the_run_cant_pay_are_left_out_with_the_reason(string basis, string currency, string reason)
    {
        var worked = One(Setup(), Person(basis: basis, currency: currency));

        Assert.False(worked.Included);
        Assert.Equal(reason, worked.ExcludedReason);
        Assert.Empty(worked.Lines);
        Assert.Null(worked.Totals);
    }

    [Fact]
    public void Someone_with_no_pay_set_is_left_out_with_the_reason()
    {
        var nobody = Person() with { Pay = [] };

        Assert.Equal("no_pay", One(Setup(), nobody).ExcludedReason);
    }

    [Fact]
    public void Deductions_above_pay_are_flagged()
    {
        var amina = Person();
        var worked = One(Setup([Loan], [For(amina, Loan, 90_000)]), amina);

        Assert.True(worked.Totals!.NetPay < 0);
        Assert.Contains("negative_net", worked.Notes);
    }

    [Fact]
    public void Joiners_and_leavers_are_paid_the_month_and_flagged()
    {
        var joiner = Person(hired: new DateOnly(2026, 10, 12));
        // Left on the 20th; their pay rose on the 25th, after they left, so the old pay counts.
        var leaver = Person(left: new DateOnly(2026, 10, 20)) with
        {
            Pay =
            [
                new BasicPayRow(Guid.CreateVersion7(), new DateOnly(2024, 1, 15), new DateOnly(2026, 10, 24), 80_000, "monthly", "KES"),
                new BasicPayRow(Guid.CreateVersion7(), new DateOnly(2026, 10, 25), null, 90_000, "monthly", "KES"),
            ],
        };

        var people = PayRunCalculator.Work(Setup(), [joiner, leaver]);

        var joined = people.Single(p => p.EmployeeId == joiner.EmployeeId);
        Assert.Equal(["joined"], joined.Notes);
        Assert.Equal(100_000m, Line(joined, "BASIC"));
        var left = people.Single(p => p.EmployeeId == leaver.EmployeeId);
        Assert.Equal(["left"], left.Notes);
        Assert.Equal(80_000m, Line(left, "BASIC"));
        Assert.Equal(leaver.Pay[0].Id, left.CompensationId);
    }

    [Fact]
    public void A_regular_run_pays_the_schedules_people_employed_in_the_month()
    {
        var amina = Person();
        var weekly = Person(schedule: Weekly);
        var future = Person(hired: new DateOnly(2026, 11, 2));
        var gone = Person(left: new DateOnly(2026, 9, 30));
        // Moved to the weekly schedule on 15 October: their job on the month's last day counts.
        var moved = Person() with
        {
            Jobs =
            [
                new JobRow(Guid.CreateVersion7(), new DateOnly(2024, 1, 15), new DateOnly(2026, 10, 14), Monthly),
                new JobRow(Guid.CreateVersion7(), new DateOnly(2026, 10, 15), null, Weekly),
            ],
        };

        var people = PayRunCalculator.Work(Setup(), [amina, weekly, future, gone, moved]);

        Assert.Equal([amina.EmployeeId], people.Select(p => p.EmployeeId));
    }

    [Fact]
    public void A_correction_works_out_only_the_people_chosen_and_says_who_it_cant()
    {
        var amina = Person();
        var brian = Person(name: "Brian Mwangi");
        var weekly = Person(schedule: Weekly);

        var people = PayRunCalculator.Work(Setup(only: [amina.EmployeeId, weekly.EmployeeId]), [amina, brian, weekly]);

        Assert.Equal(2, people.Count);
        Assert.True(people.Single(p => p.EmployeeId == amina.EmployeeId).Included);
        Assert.Equal("not_in_period", people.Single(p => p.EmployeeId == weekly.EmployeeId).ExcludedReason);
    }

    [Fact]
    public void Run_totals_add_up_the_people_paid()
    {
        var amina = Person();
        var brian = Person(basic: 50_000, name: "Brian Mwangi");
        var people = PayRunCalculator.Work(Setup([Loan, Medical], [For(amina, Loan, 10_000), Everyone(Medical)]), [amina, brian, Person() with { Pay = [] }]);

        var totals = PayRunCalculator.Totals(people);

        var included = people.Where(p => p.Included).ToList();
        Assert.Equal(2, totals.People);
        Assert.Equal(150_000m, totals.GrossPay);
        Assert.Equal(included.Sum(p => p.Totals!.Paye), totals.Paye);
        Assert.Equal(included.Sum(p => p.Totals!.NetPay), totals.NetPay);
        Assert.Equal(10_000m, totals.OtherDeductions);
        Assert.Equal(6_000m, totals.OtherEmployerCosts);
        Assert.Equal(totals.GrossPay - totals.Paye - totals.NssfEmployee - totals.Shif - totals.AhlEmployee - totals.OtherDeductions, totals.NetPay);
        Assert.Equal(included.Sum(p => p.Totals!.EmployerCosts), totals.NssfEmployer + totals.AhlEmployer + totals.Nita + totals.OtherEmployerCosts);
    }
}
