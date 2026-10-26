using Pesaroll.Payroll.Engine;

namespace Pesaroll.Payroll;

/// <summary>A pay run's period: a calendar month, or the week, fortnight or day up to the pay date (PAY-10).</summary>
public sealed record RunPeriod(DateOnly Start, DateOnly End);

/// <summary>An employment row: when it applied and the schedule it pays on.</summary>
public sealed record JobRow(Guid Id, DateOnly From, DateOnly? To, Guid PayScheduleId);

/// <summary>
/// A compensation row: basic pay, how often it is paid, and when it applied. A daily or hourly rate
/// may say the days or hours usually worked in a pay period (PAY-11).
/// </summary>
public sealed record BasicPayRow(Guid Id, DateOnly From, DateOnly? To, decimal Amount, string Basis, string Currency, decimal? UsualQuantity = null);

/// <summary>
/// Days or hours worked in a run (PAY-11): for basic pay (no item) or an item paid by the hour or day,
/// and the day they were entered (PAY-04: overtime entered after a cut-off waits for the next run).
/// </summary>
public sealed record TimeEntry(Guid? PayItemId, decimal Quantity, DateOnly? EnteredOn = null);

/// <summary>
/// An amount entered for one run (PAY-04): for an item entered each run (commission), a person's item
/// changed for the run (0 removes it), an item added for the run, basic pay (no item) or a statutory
/// line (its locked item). Edits beyond items entered each run say why. The day it was entered: an
/// amount for an item entered each run that comes after a cut-off waits for the next run.
/// </summary>
public sealed record RunAmount(Guid? PayItemId, decimal Amount, string? Reason = null, DateOnly? EnteredOn = null);

/// <summary>The lowest minimum wage in a wage area: a month (without housing allowance), a day and an hour.</summary>
public sealed record MinimumWage(decimal Monthly, decimal Daily, decimal Hourly);

/// <summary>
/// The wage order in force on the pay date (PAY-11): the hours that turn a daily rate or a monthly
/// salary into an hourly one, and the minimum wage in each wage area.
/// </summary>
public sealed record WageOrder(decimal HoursPerDay, decimal HoursPerMonth, IReadOnlyDictionary<string, MinimumWage> Minimums);

/// <summary>
/// An employee of the run's company, with their jobs and pay over time, their own choice of statutory
/// deductions if they have one (it replaces the schedule's), what their approved runs paid earlier
/// in the run's tax month, the days and hours entered for them in the run, the wage area of
/// their place of work (null: none known), the amounts entered for them in the run and the
/// statutory deductions chosen for them in this run alone (it replaces theirs and the schedule's), and
/// how the schedule's last run left them when it held something back for this one (PAY-04).
/// </summary>
public sealed record Candidate(
    Guid EmployeeId, string StaffNo, string FullName, DateOnly HireDate, DateOnly? ExitDate, IReadOnlyList<JobRow> Jobs, IReadOnlyList<BasicPayRow> Pay,
    StatutoryChoice? Choice = null, MonthToDate? Earlier = null, IReadOnlyList<TimeEntry>? Time = null, string? WageArea = null,
    IReadOnlyList<RunAmount>? Amounts = null, StatutoryChoice? RunChoice = null, LastPaid? Last = null);

/// <summary>
/// A schedule's cut-off for a run (PAY-04): its day, and what it applies to (new_employees, leavers,
/// pay_changes, overtime, one_off; unpaid_leave and the organization's own words are kept but do nothing yet).
/// </summary>
public sealed record RunCutoff(DateOnly On, IReadOnlyCollection<string> AppliesTo)
{
    public bool Covers(string what) => AppliesTo.Contains(what);

    /// <summary>Entered after the cut-off day; an entry with no day is the cut-off's.</summary>
    public bool After(DateOnly? entered) => entered > On;
}

/// <summary>The schedule's last approved run before this one: its period and the cut-off it used (null: none).</summary>
public sealed record LastRun(RunPeriod Period, RunCutoff? Cutoff);

/// <summary>
/// How the last run left a person (PAY-04): why it left them out and its notes, its basic pay line
/// (days or hours and the rate for a daily or hourly rate; set says it was set by hand) and what was
/// entered for them in it.
/// </summary>
public sealed record LastPaid(string? ExcludedReason, IReadOnlyList<string> Notes, decimal? Basic = null, decimal? Quantity = null, decimal? UnitRate = null,
    bool BasicSet = false, IReadOnlyList<TimeEntry>? Time = null, IReadOnlyList<RunAmount>? Amounts = null);

/// <summary>
/// What a person's approved runs paid earlier in a tax month: taxable cash pay, before-tax deductions
/// (pension) and each statutory line, read back from their lines.
/// </summary>
public sealed record MonthToDate(decimal Pay, decimal Pension, IReadOnlyDictionary<string, decimal> Statutory)
{
    public static readonly MonthToDate None = new(0, 0, new Dictionary<string, decimal>());

    public static MonthToDate From(IEnumerable<WorkedLine> lines)
    {
        var all = lines.ToList();
        return new MonthToDate(
            all.Where(l => l.Kind == "earning" && l.Taxable).Sum(l => l.Amount),
            all.Where(l => l.Kind == "deduction" && l.BeforeTax && l.StatutoryKey is null).Sum(l => l.Amount),
            all.Where(l => l.StatutoryKey is not null).GroupBy(l => l.StatutoryKey!).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount)));
    }

    public decimal Took(string key) => Statutory.GetValueOrDefault(key);
}

/// <summary>An earning, deduction or employer cost the organization set up (pay_items); per_unit items have a unit and a multiplier or amount a unit.</summary>
public sealed record ItemDefinition(
    Guid Id, string Code, string Name, string Kind, bool Taxable, bool BeforeTax, string CalcMethod, decimal? DefaultAmount, decimal? Rate,
    string Status = "active", string? Unit = null, decimal? Multiplier = null);

/// <summary>Who gets an item at the company (employee_pay_items): everyone (no employee) or one person.</summary>
public sealed record ItemAssignment(Guid PayItemId, Guid? EmployeeId, decimal? Amount, decimal? Rate, DateOnly StartsOn, DateOnly? EndsOn,
    decimal? Multiplier = null);

/// <summary>How a line is shown: the organization's locked item for basic pay or a statutory key.</summary>
public sealed record LineName(string Code, string Name, Guid? PayItemId);

/// <summary>
/// What a run works with: its period and schedule, the people a correction is for (null: a regular
/// run), the rule pack in force on the pay date, line names, the company's items, how often the
/// schedule pays, the statutory deductions it makes (every one, unless set otherwise) and the wage
/// order in force (null: none for the country, so nothing is checked against a minimum), the
/// schedule's cut-off for the run and its last approved run (PAY-04; null: none).
/// </summary>
public sealed record RunSetup(
    RunPeriod Period, Guid PayScheduleId, IReadOnlyCollection<Guid>? Only, RulePack Pack, IReadOnlyDictionary<string, LineName> Names,
    IReadOnlyList<ItemDefinition> Items, IReadOnlyList<ItemAssignment> Assignments, string Frequency = "monthly", StatutoryChoice? Choice = null,
    WageOrder? Wages = null, RunCutoff? Cutoff = null, LastRun? Last = null);

/// <summary>
/// One line of a person's pay. Rates are fractions (0.06 is 6%). A statutory line also keeps the
/// month to date it was worked out on and what earlier runs in the month took (its amount is the
/// difference); its base is the month to date's. A line paid by the day or hour keeps its quantity
/// and unit rate (amount = quantity x unit rate). A line set by hand for the run keeps why (PAY-04),
/// and a line owed from the last period (held back by its cut-off) says which period.
/// </summary>
public sealed record WorkedLine(
    string Kind, string Code, string Name, Guid? PayItemId, string? StatutoryKey, bool Taxable, bool BeforeTax, decimal? Base, decimal? Rate, decimal Amount,
    decimal? MonthToDate = null, decimal Earlier = 0, decimal? Quantity = null, decimal? UnitRate = null, string? Reason = null, RunPeriod? Owed = null);

public sealed record PersonTotals(
    decimal BasicPay, decimal GrossPay, decimal TaxablePay, decimal TaxBeforeRelief, decimal PersonalRelief, decimal Paye, decimal TotalDeductions,
    decimal NetPay, decimal EmployerCosts);

/// <summary>
/// A person as the run worked them out, with the statutory items made for them, or why they were
/// left out (no lines, no totals).
/// </summary>
public sealed record WorkedPerson(
    Guid EmployeeId, string StaffNo, string FullName, string? ExcludedReason, IReadOnlyList<string> Notes, Guid? EmploymentId, Guid? CompensationId,
    IReadOnlyList<WorkedLine> Lines, PersonTotals? Totals, IReadOnlyCollection<string>? Deductions = null)
{
    public bool Included => ExcludedReason is null;
}

public sealed record RunTotals(
    int People, decimal GrossPay, decimal Paye, decimal NssfEmployee, decimal Shif, decimal AhlEmployee, decimal OtherDeductions, decimal NetPay,
    decimal NssfEmployer, decimal AhlEmployer, decimal Nita, decimal OtherEmployerCosts);

/// <summary>
/// Works out each person's pay in a run (PAY-03, PAY-10): a pure function of the run's setup and its
/// people, like the engine it uses. Who is in: for a regular run, the company's people whose job on
/// the period's last day (or their last day, if they left) pays on the run's schedule and who were
/// employed in the period; for a correction, the people chosen (anyone it can't pay is listed with
/// <c>not_in_period</c>). Their basic pay is the compensation in force on that day: a salary or wage
/// paid as it is when its basis is the schedule's frequency (<c>other_basis</c> otherwise), a daily
/// or hourly rate on any schedule for the days or hours entered, else those usually worked (flagged
/// usual_time; a daily rate on a daily schedule is a day), else left out (<c>no_time</c>). Items paid
/// by the hour or day pay the hours entered at a multiple of the person's own rate (the wage
/// order's hours turn a daily rate or a salary into an hourly one) or an amount a unit; the
/// person's own terms win. A rate below the minimum wage for their wage area is flagged
/// (below_minimum; no_wage_area when it isn't known). A salary is paid for the calendar days the
/// person was employed in the period, each part of a pay change at its own rate (PAY-04: prorated,
/// pay_changed). Amounts entered for the run (PAY-04) pay items entered each run, and edits made in
/// review set a person's item, basic pay or statutory amount for the run (PAYE follows a statutory
/// amount set) or their statutory deductions; any edit beyond items entered each run flags adjusted.
/// Taxable earnings join basic pay as the cash pay the statutory lines are worked out on;
/// before-tax deductions go to the engine as pension contributions; non-taxable earnings and the
/// other deductions only change net pay; employer costs add to what the employer pays.
/// Statutory lines are worked out on the month to date (this run plus what the person's approved
/// runs paid earlier in the tax month) and each takes what earlier runs didn't, so a month of
/// weekly runs adds up to a monthly payroll. Only the deductions chosen are made (the person's own
/// choice over the schedule's); a line below zero (a pension added later in the month) is flagged.
/// </summary>
public static class PayRunCalculator
{
    public static IReadOnlyList<WorkedPerson> Work(RunSetup setup, IEnumerable<Candidate> candidates)
    {
        var people = new List<WorkedPerson>();
        var cutoff = setup.Only is null ? setup.Cutoff : null;
        foreach (var candidate in candidates)
        {
            var chosen = setup.Only is null || setup.Only.Contains(candidate.EmployeeId);
            if (!chosen) continue;
            var (person, held) = Hold(setup, cutoff, candidate);
            var on = PaidOn(person, setup.Period);
            var job = InForce(person.Jobs, on, j => j.From, j => j.To);
            var employed = person.HireDate <= setup.Period.End && (person.ExitDate is null || person.ExitDate >= setup.Period.Start);
            if (!employed && job?.PayScheduleId == setup.PayScheduleId && LeftAfterLastCutoff(setup, person)
                && InForce(person.Pay, on, p => p.From, p => p.To) is { } last && Owed(setup, person, last) is { Count: > 0 } owed)
            {
                people.Add(Pay(setup, person, job, last, null, held, owed, owedOnly: true));
                continue;
            }
            if (!employed || job?.PayScheduleId != setup.PayScheduleId)
            {
                if (setup.Only is not null) people.Add(Left(person, "not_in_period", job?.Id, null));
                continue;
            }
            if (cutoff?.Covers("new_employees") == true && person.HireDate > cutoff.On)
            {
                people.Add(Left(person, "after_cutoff", job.Id, null));
                continue;
            }

            var pay = InForce(person.Pay, on, p => p.From, p => p.To);
            var units = pay is null ? null : Units(setup, person, pay);
            var basicSet = person.Amounts?.FirstOrDefault(a => a.PayItemId is null);
            people.Add(pay switch
            {
                null => Left(person, "no_pay", job.Id, null),
                _ when !ByRate(pay) && pay.Basis != setup.Frequency => Left(person, "other_basis", job.Id, pay.Id),
                _ when pay.Currency != setup.Pack.Currency => Left(person, "other_currency", job.Id, pay.Id),
                _ when ByRate(pay) && units is not { Quantity: > 0 } && basicSet is null => Left(person, "no_time", job.Id, pay.Id),
                _ => Pay(setup, person, job, pay, units, held, Owed(setup, person, pay)),
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

    /// <summary>A daily or hourly rate, paid for the days or hours worked rather than as it is.</summary>
    private static bool ByRate(BasicPayRow pay) => pay.Basis is "daily" or "hourly";

    /// <summary>
    /// The days or hours a daily or hourly rate is paid for: entered for the run, else usually worked,
    /// else a day on a daily schedule (null: none known). Usual says it wasn't entered.
    /// </summary>
    private static (decimal Quantity, bool Usual)? Units(RunSetup setup, Candidate person, BasicPayRow pay)
    {
        if (!ByRate(pay)) return null;
        if (person.Time?.FirstOrDefault(t => t.PayItemId is null) is { } entered) return (entered.Quantity, false);
        if (pay.UsualQuantity is { } usual) return (usual, true);
        return pay.Basis == "daily" && setup.Frequency == "daily" ? (1m, false) : null;
    }

    /// <summary>
    /// A salary for the calendar days of the period the person was employed (PAY-04): each pay row on
    /// the same basis as the one in force pays its own amount for its own days, so a joiner, a leaver or
    /// a change inside the period is paid each part. Days on another basis are not guessed (basis_changed).
    /// A single row keeps its full amount as the base and the share of the period as the rate.
    /// </summary>
    private static (decimal Amount, decimal? Base, decimal? Rate, List<string> Notes) Salary(RunPeriod period, RulePack pack, Candidate person, BasicPayRow inForce,
        DateOnly? paidTo = null)
    {
        var start = person.HireDate > period.Start ? person.HireDate : period.Start;
        var end = person.ExitDate is { } exit && exit < period.End ? exit : period.End;
        if (paidTo < end) end = paidTo.Value;
        var periodDays = period.End.DayNumber - period.Start.DayNumber + 1;
        var rows = person.Pay.Where(p => p.From <= end && (p.To is null || p.To >= start)).ToList();
        var parts = rows.Where(p => p.Basis == inForce.Basis && p.Currency == inForce.Currency)
            .Select(p => (Row: p, Days: Min(p.To ?? end, end).DayNumber - Max(p.From, start).DayNumber + 1)).ToList();
        var days = parts.Sum(p => p.Days);

        var notes = new List<string>();
        if (days < periodDays) notes.Add("prorated");
        if (parts.Count > 1) notes.Add("pay_changed");
        if (rows.Count > parts.Count) notes.Add("basis_changed");
        if (days == periodDays && parts.Count == 1) return (inForce.Amount, null, null, notes);
        var amount = pack.Round(parts.Sum(p => p.Row.Amount * p.Days / periodDays));
        return parts.Count == 1
            ? (amount, parts[0].Row.Amount, Math.Round((decimal)days / periodDays, 6), notes)
            : (amount, null, null, notes);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    /// <summary>A person's own hourly or daily rate: as it is, or from a daily rate or a salary through the wage order's hours.</summary>
    private static decimal? OwnRate(BasicPayRow pay, string unit, WageOrder? order)
    {
        var monthly = pay.Basis switch
        {
            "monthly" => pay.Amount,
            "weekly" => pay.Amount * 52 / 12,
            "fortnightly" => pay.Amount * 26 / 12,
            _ => (decimal?)null,
        };
        return (unit, pay.Basis) switch
        {
            ("hour", "hourly") or ("day", "daily") => pay.Amount,
            _ when order is null => null,
            ("hour", "daily") => pay.Amount / order.HoursPerDay,
            ("day", "hourly") => pay.Amount * order.HoursPerDay,
            ("hour", _) => monthly / order.HoursPerMonth,
            ("day", _) => monthly / order.HoursPerMonth * order.HoursPerDay,
            _ => null,
        };
    }

    /// <summary>Below the minimum wage for the person's wage area, comparing like with like (a weekly wage as a month's).</summary>
    private static string? MinimumWageNote(RunSetup setup, Candidate person, BasicPayRow pay)
    {
        if (setup.Wages is not { } order) return null;
        if (person.WageArea is null || !order.Minimums.TryGetValue(person.WageArea, out var minimum)) return "no_wage_area";
        var below = pay.Basis switch
        {
            "hourly" => pay.Amount < minimum.Hourly,
            "daily" => pay.Amount < minimum.Daily,
            "weekly" => pay.Amount * 52 / 12 < minimum.Monthly,
            "fortnightly" => pay.Amount * 26 / 12 < minimum.Monthly,
            _ => pay.Amount < minimum.Monthly,
        };
        return below ? "below_minimum" : null;
    }

    /// <summary>The day the run reads someone's job and pay: the period's last day, or their last day if they left in it.</summary>
    private static DateOnly PaidOn(Candidate person, RunPeriod period) =>
        person.ExitDate is { } exit && exit < period.End ? exit : period.End;

    private static T? InForce<T>(IEnumerable<T> rows, DateOnly on, Func<T, DateOnly> from, Func<T, DateOnly?> to) where T : class =>
        rows.Where(r => from(r) <= on && (to(r) is null || to(r) >= on)).OrderByDescending(from).FirstOrDefault();

    private static WorkedPerson Left(Candidate person, string reason, Guid? job, Guid? pay) =>
        new(person.EmployeeId, person.StaffNo, person.FullName, reason, [], job, pay, [], null);

    private static WorkedPerson Pay(RunSetup setup, Candidate person, JobRow job, BasicPayRow basicPay, (decimal Quantity, bool Usual)? units,
        IReadOnlyList<string> held, IReadOnlyList<WorkedLine> owed, bool owedOnly = false)
    {
        var (period, pack) = (setup.Period, setup.Pack);
        // A leaver after the cut-off is paid to it; the next run pays the rest.
        var cutoff = setup.Only is null ? setup.Cutoff : null;
        DateOnly? paidTo = cutoff?.Covers("leavers") == true && person.ExitDate is { } exit && exit > cutoff.On && exit <= period.End ? cutoff.On : null;
        // Basic pay set for the run replaces what it would be, by days or hours or for the days employed.
        var basicSet = owedOnly ? null : person.Amounts?.FirstOrDefault(a => a.PayItemId is null);
        if (basicSet is not null) units = null;
        (decimal Amount, decimal? Base, decimal? Rate, List<string> Notes)? salary = units is null && basicSet is null && !owedOnly
            ? Salary(period, pack, person, basicPay, paidTo) : null;
        var basic = owedOnly ? 0 : basicSet?.Amount ?? (units is var (quantity, _) ? pack.Round(quantity * basicPay.Amount) : salary!.Value.Amount);
        RunAmount? Set(Guid item) => person.Amounts?.FirstOrDefault(a => a.PayItemId == item);
        var own = new List<WorkedLine>();
        foreach (var item in setup.Items.Where(i => i.Status == "active" && i.CalcMethod is "fixed" or "percent_of_basic" or "each_run")
                     .OrderBy(i => i.Code, StringComparer.Ordinal))
        {
            // Set for the run: entered each run, or an edit in review (0 removes it for the run).
            if (Set(item.Id) is { } set)
            {
                if (set.Amount > 0)
                    own.Add(new WorkedLine(item.Kind, item.Code, item.Name, item.Id, null, item.Taxable, item.BeforeTax, null, null, set.Amount, Reason: set.Reason));
                continue;
            }
            // Someone paid only what they are owed (a leaver) gets only what is entered for them, such as final dues.
            if (item.CalcMethod == "each_run" || owedOnly) continue;
            var applies = setup.Assignments.Where(a => a.PayItemId == item.Id && a.StartsOn <= period.End && (a.EndsOn is null || a.EndsOn >= period.Start)).ToList();
            var row = applies.FirstOrDefault(a => a.EmployeeId == person.EmployeeId) ?? applies.FirstOrDefault(a => a.EmployeeId is null);
            if (row is null) continue;
            var (amount, @base, rate) = item.CalcMethod == "fixed"
                ? (row.Amount ?? item.DefaultAmount ?? 0, (decimal?)null, (decimal?)null)
                : Percent(basic, (row.Rate ?? item.Rate!.Value) / 100, pack);
            if (amount == 0) continue;
            own.Add(new WorkedLine(item.Kind, item.Code, item.Name, item.Id, null, item.Taxable, item.BeforeTax, @base, rate, amount));
        }

        // Items paid by the hour or day: the hours entered, at their own amount a unit or multiple of their own rate, else the item's.
        foreach (var item in setup.Items.Where(i => !owedOnly && i.Status == "active" && i.CalcMethod == "per_unit").OrderBy(i => i.Code, StringComparer.Ordinal))
        {
            var hours = person.Time?.FirstOrDefault(t => t.PayItemId == item.Id)?.Quantity ?? 0;
            if (PerUnit(setup, person, basicPay, item, hours) is { } line) own.Add(line);
        }
        own.AddRange(owed);

        var earnings = own.Where(l => l.Kind == "earning").ToList();
        var deductions = own.Where(l => l.Kind == "deduction").ToList();
        var employerCosts = own.Where(l => l.Kind == "employer_cost").ToList();
        var choice = person.RunChoice ?? person.Choice ?? setup.Choice ?? StatutoryChoice.All;
        var earlier = person.Earlier ?? MonthToDate.None;
        // Statutory amounts set for the run (by their locked item): this run's share, on top of what earlier runs in the month took.
        var statutory = (person.Amounts ?? []).Where(a => a.PayItemId is not null)
            .Select(a => (Set: a, Key: setup.Names.FirstOrDefault(n => n.Key != "basic" && n.Value.PayItemId == a.PayItemId).Key))
            .Where(x => x.Key is not null).ToDictionary(x => x.Key!, x => x.Set);
        var result = PayCalculator.Calculate(new PayInputs
        {
            GrossCashPay = earlier.Pay + basic + earnings.Where(l => l.Taxable).Sum(l => l.Amount),
            PensionContributions = earlier.Pension + deductions.Where(l => l.BeforeTax).Sum(l => l.Amount),
        }, pack, choice, statutory.ToDictionary(x => x.Key, x => earlier.Took(x.Key) + x.Value.Amount));
        var statutoryDeductions = Made(setup, "deduction", result.Deductions, choice, earlier, statutory);
        var statutoryCosts = Made(setup, "employer_cost", result.EmployerCosts, choice, earlier, statutory);

        var name = setup.Names.GetValueOrDefault("basic") ?? new LineName("BASIC", "Basic pay", null);
        var lines = new List<WorkedLine>();
        if (!owedOnly)
            lines.Add(new("earning", name.Code, name.Name, name.PayItemId, null, true, false, salary?.Base, salary?.Rate, basic,
                Quantity: units?.Quantity, UnitRate: units is null ? null : basicPay.Amount, Reason: basicSet?.Reason));
        lines.AddRange(earnings);
        lines.AddRange(statutoryDeductions);
        lines.AddRange(deductions);
        lines.AddRange(statutoryCosts);
        lines.AddRange(employerCosts);

        var gross = basic + earnings.Sum(l => l.Amount);
        var totalDeductions = statutoryDeductions.Sum(l => l.Amount) + deductions.Sum(l => l.Amount);
        var net = gross - totalDeductions;
        var notes = new List<string>();
        if (person.HireDate >= period.Start) notes.Add("joined");
        if (person.ExitDate is { } left && left >= period.Start && left <= period.End) notes.Add("left");
        if (salary is { } paid) notes.AddRange(paid.Notes);
        if (paidTo is not null && salary is not null) notes.Add("paid_to_cutoff");
        notes.AddRange(held);
        if (owed.Count > 0) notes.Add("owed");
        if (net < 0) notes.Add("negative_net");
        if (choice.Makes.Count == 0) notes.Add("no_deductions");
        else if (choice.Makes.Count < StatutoryChoice.Groups.Count) notes.Add("some_deductions");
        if (statutoryDeductions.Concat(statutoryCosts).Any(l => l.Amount < 0)) notes.Add("refund");
        if (units is { Usual: true }) notes.Add("usual_time");
        if (!owedOnly && MinimumWageNote(setup, person, basicPay) is { } minimum) notes.Add(minimum);
        if (person.RunChoice is not null
            || (person.Amounts ?? []).Any(a => a.PayItemId is not { } id || setup.Items.FirstOrDefault(i => i.Id == id)?.CalcMethod != "each_run"))
            notes.Add("adjusted");

        // Taxable pay, tax before relief and relief are the month to date's; PAYE is this run's share.
        return new WorkedPerson(person.EmployeeId, person.StaffNo, person.FullName, null, notes, job.Id, basicPay.Id, lines,
            new PersonTotals(basic, gross, result.TaxablePay, result.TaxBeforeRelief, result.PersonalRelief,
                statutoryDeductions.Where(l => l.StatutoryKey == "paye").Sum(l => l.Amount), totalDeductions, net,
                statutoryCosts.Sum(l => l.Amount) + employerCosts.Sum(l => l.Amount)), choice.Makes);
    }

    /// <summary>
    /// An item paid by the hour or day for the hours given: at the person's own amount a unit or multiple of
    /// their own rate, else the item's (null: no hours, or no rate to pay them at).
    /// </summary>
    private static WorkedLine? PerUnit(RunSetup setup, Candidate person, BasicPayRow basicPay, ItemDefinition item, decimal hours, RunPeriod? owed = null)
    {
        if (hours <= 0) return null;
        var period = setup.Period;
        var theirs = setup.Assignments.FirstOrDefault(a => a.PayItemId == item.Id && a.EmployeeId == person.EmployeeId && a.StartsOn <= period.End
            && (a.EndsOn is null || a.EndsOn >= period.Start));
        var everyone = setup.Assignments.FirstOrDefault(a => a.PayItemId == item.Id && a.EmployeeId is null && a.StartsOn <= period.End
            && (a.EndsOn is null || a.EndsOn >= period.Start));
        var terms = (theirs is { Amount: not null } or { Multiplier: not null } ? theirs : null)
            ?? (everyone is { Amount: not null } or { Multiplier: not null } ? everyone : null);
        var unitRate = terms is { Amount: { } flat } ? flat
            : (terms?.Multiplier ?? item.Multiplier) is { } times ? OwnRate(basicPay, item.Unit!, setup.Wages) * times
            : item.DefaultAmount;
        if (unitRate is not { } rate) return null;
        rate = setup.Pack.Round(rate);
        return new WorkedLine("earning", item.Code, item.Name, item.Id, null, item.Taxable, false, null, null, setup.Pack.Round(hours * rate),
            Quantity: hours, UnitRate: rate, Owed: owed);
    }

    /// <summary>
    /// What the cut-off holds back for the next run (PAY-04), as the person this run pays and its notes:
    /// a pay change starting after it keeps the rate before it for the whole period (pay_change_next_run),
    /// and overtime and amounts for items entered each run that were entered after it aren't paid
    /// (next_run). Review edits are never held back.
    /// </summary>
    private static (Candidate Person, List<string> Notes) Hold(RunSetup setup, RunCutoff? cutoff, Candidate person)
    {
        var notes = new List<string>();
        if (cutoff is null) return (person, notes);
        if (cutoff.Covers("pay_changes"))
        {
            var rows = person.Pay.OrderBy(p => p.From).ToList();
            var later = rows.Where((p, i) => i > 0 && p.From > cutoff.On && p.From <= setup.Period.End).ToList();
            if (later.Count > 0)
            {
                // The rate before a change held back runs on until the next change kept (or as long as the changes held back ran).
                var kept = rows.Except(later).ToList();
                person = person with
                {
                    Pay = kept.Select((p, i) => !later.Any(l => p.To?.AddDays(1) == l.From) ? p
                        : p with { To = i + 1 < kept.Count ? kept[i + 1].From.AddDays(-1) : rows[^1].To }).ToList(),
                };
                notes.Add("pay_change_next_run");
            }
        }
        bool Late(Guid? item, string method, string what, DateOnly? entered) =>
            item is { } id && setup.Items.FirstOrDefault(i => i.Id == id)?.CalcMethod == method && cutoff.Covers(what) && cutoff.After(entered);
        var time = person.Time?.Where(t => !Late(t.PayItemId, "per_unit", "overtime", t.EnteredOn)).ToList();
        var amounts = person.Amounts?.Where(a => !Late(a.PayItemId, "each_run", "one_off", a.EnteredOn)).ToList();
        if (time?.Count != person.Time?.Count || amounts?.Count != person.Amounts?.Count)
        {
            person = person with { Time = time, Amounts = amounts };
            notes.Add("next_run");
        }
        return (person, notes);
    }

    /// <summary>Someone the last run paid to its cut-off, who left in its period: this run pays them the rest.</summary>
    private static bool LeftAfterLastCutoff(RunSetup setup, Candidate person) =>
        setup is { Only: null, Last: { Cutoff: not null } last } && person.Last?.Notes.Contains("paid_to_cutoff") == true
        && person.ExitDate >= last.Period.Start && person.ExitDate <= last.Period.End;

    /// <summary>
    /// What the last run held back for this one (PAY-04), as lines owed from its period: basic pay as the
    /// period turned out less what the last run paid (someone it left out, a leaver it paid to its
    /// cut-off, a pay change it didn't use; never basic pay set by hand), allowances on basic pay on that,
    /// and what was entered for the person in it that it didn't pay (everything, for someone it left out;
    /// overtime and amounts for items entered each run entered after its cut-off). Daily and hourly
    /// rates are paid at the rate in force at the period's end for the days or hours it paid.
    /// </summary>
    private static List<WorkedLine> Owed(RunSetup setup, Candidate person, BasicPayRow pay)
    {
        var lines = new List<WorkedLine>();
        if (setup is not { Only: null, Last: { Cutoff: { } cutoff, Period: var period } } || person.Last is not { } was) return lines;
        var pack = setup.Pack;
        var heldOut = was.ExcludedReason == "after_cutoff";
        var name = setup.Names.GetValueOrDefault("basic") ?? new LineName("BASIC", "Basic pay", null);

        decimal owedBasic = 0;
        if (!was.BasicSet && (heldOut || was.Notes.Contains("paid_to_cutoff") || was.Notes.Contains("pay_change_next_run"))
            && InForce(person.Pay, PaidOn(person, period), p => p.From, p => p.To) is { } then)
        {
            if (ByRate(then))
            {
                var quantity = heldOut ? was.Time?.FirstOrDefault(t => t.PayItemId is null)?.Quantity : was.Quantity;
                var unitRate = then.Amount - (heldOut ? 0 : was.UnitRate ?? then.Amount);
                if (quantity is > 0 && unitRate != 0)
                {
                    owedBasic = pack.Round(quantity.Value * unitRate);
                    lines.Add(new WorkedLine("earning", name.Code, name.Name, name.PayItemId, null, true, false, null, null, owedBasic,
                        Quantity: quantity, UnitRate: unitRate, Owed: period));
                }
            }
            else
            {
                owedBasic = Salary(period, pack, person, then).Amount - (heldOut ? 0 : was.Basic ?? 0);
                if (owedBasic != 0)
                    lines.Add(new WorkedLine("earning", name.Code, name.Name, name.PayItemId, null, true, false, null, null, owedBasic, Owed: period));
            }
        }

        if (owedBasic != 0)
            foreach (var item in setup.Items.Where(i => i.CalcMethod == "percent_of_basic").OrderBy(i => i.Code, StringComparer.Ordinal))
            {
                var applies = setup.Assignments.Where(a => a.PayItemId == item.Id && a.StartsOn <= period.End && (a.EndsOn is null || a.EndsOn >= period.Start)).ToList();
                var row = applies.FirstOrDefault(a => a.EmployeeId == person.EmployeeId) ?? applies.FirstOrDefault(a => a.EmployeeId is null);
                if (row is null) continue;
                var (amount, @base, rate) = Percent(owedBasic, (row.Rate ?? item.Rate!.Value) / 100, pack);
                if (amount != 0)
                    lines.Add(new WorkedLine(item.Kind, item.Code, item.Name, item.Id, null, item.Taxable, item.BeforeTax, @base, rate, amount, Owed: period));
            }

        bool HeldBack(string what, DateOnly? entered) => heldOut || (cutoff.Covers(what) && cutoff.After(entered));
        foreach (var entry in was.Time ?? [])
            if (setup.Items.FirstOrDefault(i => i.Id == entry.PayItemId && i.CalcMethod == "per_unit") is { } item && HeldBack("overtime", entry.EnteredOn)
                && PerUnit(setup, person, pay, item, entry.Quantity, period) is { } line)
                lines.Add(line);
        foreach (var entry in was.Amounts ?? [])
            if (setup.Items.FirstOrDefault(i => i.Id == entry.PayItemId && i.CalcMethod == "each_run") is { } item && entry.Amount > 0
                && HeldBack("one_off", entry.EnteredOn))
                lines.Add(new WorkedLine(item.Kind, item.Code, item.Name, item.Id, null, item.Taxable, item.BeforeTax, null, null, entry.Amount,
                    Reason: entry.Reason, Owed: period));
        return lines;
    }

    private static (decimal Amount, decimal? Base, decimal? Rate) Percent(decimal basic, decimal rate, RulePack pack) =>
        (pack.Round(basic * rate), basic, rate);

    /// <summary>
    /// The statutory lines of the groups made and those set for the run, each taking what earlier runs
    /// in the month didn't; a line set keeps why.
    /// </summary>
    private static List<WorkedLine> Made(RunSetup setup, string kind, IEnumerable<StatutoryLine> lines, StatutoryChoice choice, MonthToDate earlier,
        IReadOnlyDictionary<string, RunAmount> set) =>
        lines.Where(l => choice.Made(StatutoryChoice.GroupOf(l.Key)) || set.ContainsKey(l.Key))
            .Select(l => Statutory(setup, kind, l, earlier.Took(l.Key), set.GetValueOrDefault(l.Key)?.Reason)).ToList();

    private static WorkedLine Statutory(RunSetup setup, string kind, StatutoryLine line, decimal earlier, string? reason)
    {
        var name = setup.Names.GetValueOrDefault(line.Key) ?? new LineName(line.Key.ToUpperInvariant(), line.Key, null);
        return new WorkedLine(kind, name.Code, name.Name, name.PayItemId, line.Key, false, false, line.Base, line.Rate, line.Amount - earlier, line.Amount, earlier,
            Reason: reason);
    }
}
