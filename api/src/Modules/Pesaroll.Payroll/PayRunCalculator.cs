using Pesaroll.Payroll.Engine;

namespace Pesaroll.Payroll;

/// <summary>A pay run's period: a calendar month for now (PAY-10 adds weeks and days).</summary>
public sealed record RunPeriod(DateOnly Start, DateOnly End);

/// <summary>An employment row: when it applied and the schedule it pays on.</summary>
public sealed record JobRow(Guid Id, DateOnly From, DateOnly? To, Guid PayScheduleId);

/// <summary>A compensation row: basic pay, how often it is paid, and when it applied.</summary>
public sealed record BasicPayRow(Guid Id, DateOnly From, DateOnly? To, decimal Amount, string Basis, string Currency);

/// <summary>An employee of the run's company, with their jobs and pay over time.</summary>
public sealed record Candidate(
    Guid EmployeeId, string StaffNo, string FullName, DateOnly HireDate, DateOnly? ExitDate, IReadOnlyList<JobRow> Jobs, IReadOnlyList<BasicPayRow> Pay);

/// <summary>An earning, deduction or employer cost the organization set up (pay_items).</summary>
public sealed record ItemDefinition(
    Guid Id, string Code, string Name, string Kind, bool Taxable, bool BeforeTax, string CalcMethod, decimal? DefaultAmount, decimal? Rate,
    string Status = "active");

/// <summary>Who gets an item at the company (employee_pay_items): everyone (no employee) or one person.</summary>
public sealed record ItemAssignment(Guid PayItemId, Guid? EmployeeId, decimal? Amount, decimal? Rate, DateOnly StartsOn, DateOnly? EndsOn);

/// <summary>How a line is shown: the organization's locked item for basic pay or a statutory key.</summary>
public sealed record LineName(string Code, string Name, Guid? PayItemId);

/// <summary>
/// What a run works with: its period and schedule, the people a correction is for (null: a regular
/// run), the rule pack in force on the pay date, line names, and the company's items.
/// </summary>
public sealed record RunSetup(
    RunPeriod Period, Guid PayScheduleId, IReadOnlyCollection<Guid>? Only, RulePack Pack, IReadOnlyDictionary<string, LineName> Names,
    IReadOnlyList<ItemDefinition> Items, IReadOnlyList<ItemAssignment> Assignments);

/// <summary>One line of a person's pay. Rates are fractions (0.06 is 6%).</summary>
public sealed record WorkedLine(
    string Kind, string Code, string Name, Guid? PayItemId, string? StatutoryKey, bool Taxable, bool BeforeTax, decimal? Base, decimal? Rate, decimal Amount);

public sealed record PersonTotals(
    decimal BasicPay, decimal GrossPay, decimal TaxablePay, decimal TaxBeforeRelief, decimal PersonalRelief, decimal Paye, decimal TotalDeductions,
    decimal NetPay, decimal EmployerCosts);

/// <summary>A person as the run worked them out, or why they were left out (no lines, no totals).</summary>
public sealed record WorkedPerson(
    Guid EmployeeId, string StaffNo, string FullName, string? ExcludedReason, IReadOnlyList<string> Notes, Guid? EmploymentId, Guid? CompensationId,
    IReadOnlyList<WorkedLine> Lines, PersonTotals? Totals)
{
    public bool Included => ExcludedReason is null;
}

public sealed record RunTotals(
    int People, decimal GrossPay, decimal Paye, decimal NssfEmployee, decimal Shif, decimal AhlEmployee, decimal OtherDeductions, decimal NetPay,
    decimal NssfEmployer, decimal AhlEmployer, decimal Nita, decimal OtherEmployerCosts);

/// <summary>
/// Works out each person's pay in a run (PAY-03): a pure function of the run's setup and its people,
/// like the engine it uses. Who is in: for a regular run, the company's people whose job on the
/// month's last day (or their last day, if they left) pays on the run's schedule and who were
/// employed in the month; for a correction, the people chosen (anyone it can't pay is listed with
/// <c>not_in_period</c>). Their basic pay is the compensation in force on that day, monthly only;
/// joiners and leavers are paid the whole month and flagged until proration (PAY-04).
/// Taxable earnings join basic pay as the cash pay the statutory lines are worked out on;
/// before-tax deductions go to the engine as pension contributions; non-taxable earnings and the
/// other deductions only change net pay; employer costs add to what the employer pays.
/// </summary>
public static class PayRunCalculator
{
    public static IReadOnlyList<WorkedPerson> Work(RunSetup setup, IEnumerable<Candidate> candidates)
    {
        var people = new List<WorkedPerson>();
        foreach (var person in candidates)
        {
            var chosen = setup.Only is null || setup.Only.Contains(person.EmployeeId);
            if (!chosen) continue;
            var on = PaidOn(person, setup.Period);
            var job = InForce(person.Jobs, on, j => j.From, j => j.To);
            var employed = person.HireDate <= setup.Period.End && (person.ExitDate is null || person.ExitDate >= setup.Period.Start);
            if (!employed || job?.PayScheduleId != setup.PayScheduleId)
            {
                if (setup.Only is not null) people.Add(Left(person, "not_in_period", job?.Id, null));
                continue;
            }

            var pay = InForce(person.Pay, on, p => p.From, p => p.To);
            people.Add(pay switch
            {
                null => Left(person, "no_pay", job.Id, null),
                { Basis: not "monthly" } => Left(person, "not_monthly", job.Id, pay.Id),
                _ when pay.Currency != setup.Pack.Currency => Left(person, "other_currency", job.Id, pay.Id),
                _ => Pay(setup, person, job, pay),
            });
        }
        return people;
    }

    public static RunTotals Totals(IEnumerable<WorkedPerson> people)
    {
        var paid = people.Where(p => p.Included).ToList();
        var lines = paid.SelectMany(p => p.Lines).ToList();
        decimal Statutory(string key) => lines.Where(l => l.StatutoryKey == key).Sum(l => l.Amount);
        return new RunTotals(
            paid.Count, paid.Sum(p => p.Totals!.GrossPay), Statutory("paye"), Statutory("nssf_employee"), Statutory("shif"), Statutory("ahl_employee"),
            lines.Where(l => l.Kind == "deduction" && l.StatutoryKey is null).Sum(l => l.Amount), paid.Sum(p => p.Totals!.NetPay),
            Statutory("nssf_employer"), Statutory("ahl_employer"), Statutory("nita"),
            lines.Where(l => l.Kind == "employer_cost" && l.StatutoryKey is null).Sum(l => l.Amount));
    }

    /// <summary>The day the run reads someone's job and pay: the month's last day, or their last day if they left in it.</summary>
    private static DateOnly PaidOn(Candidate person, RunPeriod period) =>
        person.ExitDate is { } exit && exit < period.End ? exit : period.End;

    private static T? InForce<T>(IEnumerable<T> rows, DateOnly on, Func<T, DateOnly> from, Func<T, DateOnly?> to) where T : class =>
        rows.Where(r => from(r) <= on && (to(r) is null || to(r) >= on)).OrderByDescending(from).FirstOrDefault();

    private static WorkedPerson Left(Candidate person, string reason, Guid? job, Guid? pay) =>
        new(person.EmployeeId, person.StaffNo, person.FullName, reason, [], job, pay, [], null);

    private static WorkedPerson Pay(RunSetup setup, Candidate person, JobRow job, BasicPayRow basicPay)
    {
        var (period, pack) = (setup.Period, setup.Pack);
        var basic = basicPay.Amount;
        var own = new List<WorkedLine>();
        foreach (var item in setup.Items.Where(i => i.Status == "active" && i.CalcMethod is "fixed" or "percent_of_basic").OrderBy(i => i.Code, StringComparer.Ordinal))
        {
            var applies = setup.Assignments.Where(a => a.PayItemId == item.Id && a.StartsOn <= period.End && (a.EndsOn is null || a.EndsOn >= period.Start)).ToList();
            var row = applies.FirstOrDefault(a => a.EmployeeId == person.EmployeeId) ?? applies.FirstOrDefault(a => a.EmployeeId is null);
            if (row is null) continue;
            var (amount, @base, rate) = item.CalcMethod == "fixed"
                ? (row.Amount ?? item.DefaultAmount ?? 0, (decimal?)null, (decimal?)null)
                : Percent(basic, (row.Rate ?? item.Rate!.Value) / 100, pack);
            if (amount == 0) continue;
            own.Add(new WorkedLine(item.Kind, item.Code, item.Name, item.Id, null, item.Taxable, item.BeforeTax, @base, rate, amount));
        }

        var earnings = own.Where(l => l.Kind == "earning").ToList();
        var deductions = own.Where(l => l.Kind == "deduction").ToList();
        var employerCosts = own.Where(l => l.Kind == "employer_cost").ToList();
        var result = PayCalculator.Calculate(new PayInputs
        {
            GrossCashPay = basic + earnings.Where(l => l.Taxable).Sum(l => l.Amount),
            PensionContributions = deductions.Where(l => l.BeforeTax).Sum(l => l.Amount),
        }, pack);

        var name = setup.Names.GetValueOrDefault("basic") ?? new LineName("BASIC", "Basic pay", null);
        var lines = new List<WorkedLine> { new("earning", name.Code, name.Name, name.PayItemId, null, true, false, null, null, basic) };
        lines.AddRange(earnings);
        lines.AddRange(result.Deductions.Select(l => Statutory(setup, "deduction", l)));
        lines.AddRange(deductions);
        lines.AddRange(result.EmployerCosts.Select(l => Statutory(setup, "employer_cost", l)));
        lines.AddRange(employerCosts);

        var gross = basic + earnings.Sum(l => l.Amount);
        var totalDeductions = result.Deductions.Sum(l => l.Amount) + deductions.Sum(l => l.Amount);
        var net = gross - totalDeductions;
        var notes = new List<string>();
        if (person.HireDate >= period.Start) notes.Add("joined");
        if (person.ExitDate is { } exit && exit <= period.End) notes.Add("left");
        if (net < 0) notes.Add("negative_net");

        return new WorkedPerson(person.EmployeeId, person.StaffNo, person.FullName, null, notes, job.Id, basicPay.Id, lines,
            new PersonTotals(basic, gross, result.TaxablePay, result.TaxBeforeRelief, result.PersonalRelief, result.Amount("paye"), totalDeductions, net,
                result.EmployerCosts.Sum(l => l.Amount) + employerCosts.Sum(l => l.Amount)));
    }

    private static (decimal Amount, decimal? Base, decimal? Rate) Percent(decimal basic, decimal rate, RulePack pack) =>
        (pack.Round(basic * rate), basic, rate);

    private static WorkedLine Statutory(RunSetup setup, string kind, StatutoryLine line)
    {
        var name = setup.Names.GetValueOrDefault(line.Key) ?? new LineName(line.Key.ToUpperInvariant(), line.Key, null);
        return new WorkedLine(kind, name.Code, name.Name, name.PayItemId, line.Key, false, false, line.Base, line.Rate, line.Amount);
    }
}
