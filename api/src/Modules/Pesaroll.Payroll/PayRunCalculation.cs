using Hangfire;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Organizations;
using Pesaroll.Payroll.Engine;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Payroll;

/// <summary>
/// Works out a pay run (PAY-03), as a job in the worker: pay runs are long work (backend.md). It
/// runs inside the run's organization and company under row-level security, loads the company's
/// people and items, works out each person (<see cref="PayRunCalculator"/>) with the rule pack in
/// force on the pay date, and replaces the run's people, lines and totals in one transaction.
/// Weekly, fortnightly and daily runs (PAY-10) make the statutory deductions chosen for the schedule
/// or the person on the pay date, and take what each person's approved runs earlier in the tax month
/// didn't: for each schedule and period, the run approved last (a correction replaces what it
/// corrects), leaving out this run's own period, which this run replaces. Daily and hourly rates are
/// paid for the days and hours entered for the run (PAY-11), and each rate is checked against the
/// minimum wage of the wage order in force on the pay date, for the wage area of the person's place
/// of work (their job's location, else the company's main location). Amounts entered for the run and
/// the accountant's edits in review (PAY-04) go in as they were entered; a person's statutory
/// deductions chosen for the run replace theirs and the schedule's. A regular run follows its
/// schedule's cut-off (PAY-04, <see cref="RunCutoffs"/>), recorded on the run, and pays what the
/// schedule's last approved run held back by its own, unless a correction of that run has paid the
/// person since; leavers it paid to its cut-off are paid the rest.
/// Running it twice gives the same result; a run that is no longer calculating (cancelled since) is
/// left alone. No rule pack for the pay date leaves the run a draft with PAY-2010 on it.
/// </summary>
public sealed class PayRunCalculation(PayrollDbContext db, OrganizationsDbContext org, RulePacks packs, TenantContext tenant, TimeProvider clock)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(Guid payRunId, Guid organizationId, Guid companyId, Guid userId, CancellationToken ct)
    {
        tenant.SetUser(userId);
        tenant.Set(organizationId, [companyId]);
        tenant.SetReference($"pay-run-calculation:{payRunId}");
        var run = await db.PayRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == payRunId, ct);
        if (run is not { Status: "calculating" }) return;

        var company = await org.Companies.AsNoTracking().SingleAsync(c => c.Id == run.CompanyId, ct);
        RulePack pack;
        try
        {
            pack = await packs.InForceAsync(company.CountryCode, run.PayDate, ct);
        }
        catch (AppException error)
        {
            await db.PayRuns.Where(r => r.Id == run.Id && r.Status == "calculating")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "draft").SetProperty(r => r.CalculationError, error.Code), ct);
            return;
        }

        var frequency = await org.PaySchedules.AsNoTracking().Where(s => s.Id == run.PayScheduleId).Select(s => s.Frequency).SingleAsync(ct);
        var cutoff = await RunCutoffs.ForAsync(org, run, ct);
        var candidates = await CandidatesAsync(run, company, ct);
        var choices = await ChoicesAsync(run, frequency, candidates.Select(c => c.EmployeeId).ToArray(), ct);
        var earlier = await EarlierAsync(run, candidates.Select(c => c.EmployeeId).ToArray(), ct);
        var forRun = (await db.RunDeductions.AsNoTracking().Where(d => d.PayRunId == run.Id).ToListAsync(ct))
            .ToDictionary(d => d.EmployeeId, d => new Chosen(new StatutoryChoice(d.Deductions), d.SetBy, d.SetAt));
        candidates = candidates.Select(c => c with
        {
            Choice = choices.People.GetValueOrDefault(c.EmployeeId)?.Choice, Earlier = earlier.GetValueOrDefault(c.EmployeeId),
            RunChoice = forRun.GetValueOrDefault(c.EmployeeId)?.Choice,
        }).ToList();
        var setup = await SetupAsync(run, company.CountryCode, pack, ct) with
        {
            Frequency = frequency, Choice = choices.Schedule?.Choice ?? StatutoryChoice.All, Wages = await WageOrderAsync(company, run.PayDate, ct),
            Cutoff = cutoff,
        };
        if (run.EmployeeIds is null && await RunCutoffs.LastAsync(db, run, ct) is { CutoffOn: { } on } last)
        {
            setup = setup with { Last = new LastRun(new RunPeriod(last.PeriodStart, last.PeriodEnd), new RunCutoff(on, last.CutoffAppliesTo ?? [])) };
            var held = await HeldBackAsync(last, setup.Names.GetValueOrDefault("basic")?.PayItemId, ct);
            candidates = candidates.Select(c => c with { Last = held.GetValueOrDefault(c.EmployeeId) }).ToList();
        }
        var people = PayRunCalculator.Work(setup, candidates);
        await SaveAsync(run.Id, people, id => forRun.GetValueOrDefault(id) ?? choices.People.GetValueOrDefault(id) ?? choices.Schedule, pack.Version, cutoff,
            userId, ct);
    }

    /// <summary>
    /// How the last run left each person it held something back for (PAY-04): left out after its cut-off,
    /// or with a cut-off note. Someone an approved correction of it has paid since is left alone.
    /// </summary>
    private async Task<Dictionary<Guid, LastPaid>> HeldBackAsync(PayRun last, Guid? basicItem, CancellationToken ct)
    {
        string[] held = ["paid_to_cutoff", "pay_change_next_run", "next_run"];
        var people = await db.People.AsNoTracking().Include(p => p.Lines)
            .Where(p => p.PayRunId == last.Id && (p.ExcludedReason == "after_cutoff" || p.Notes.Any(n => held.Contains(n)))).ToListAsync(ct);
        var corrected = await (from p in db.People.AsNoTracking()
                               join r in db.PayRuns.AsNoTracking() on p.PayRunId equals r.Id
                               where r.CorrectsPayRunId == last.Id && r.Status == "approved" && p.Included
                               select p.EmployeeId).ToListAsync(ct);
        var inputs = (await db.Inputs.AsNoTracking().Where(i => i.PayRunId == last.Id).ToListAsync(ct)).ToLookup(i => i.EmployeeId);
        return people.Where(p => !corrected.Contains(p.EmployeeId)).ToDictionary(p => p.EmployeeId, p =>
        {
            var basic = p.Lines.FirstOrDefault(l => l.StatutoryKey is null && l.OwedFrom is null && (basicItem is { } id ? l.PayItemId == id : l.Code == "BASIC"));
            return new LastPaid(p.ExcludedReason, p.Notes, basic?.Amount, basic?.Quantity, basic?.UnitRate, basic?.Reason is not null,
                inputs[p.EmployeeId].Where(i => i.Quantity is not null).Select(i => new TimeEntry(i.PayItemId, i.Quantity!.Value, RunCutoffs.Day(i.EnteredAt))).ToList(),
                inputs[p.EmployeeId].Where(i => i.Amount is not null).Select(i => new RunAmount(i.PayItemId, i.Amount!.Value, i.Reason, RunCutoffs.Day(i.EnteredAt))).ToList());
        });
    }

    /// <summary>A choice of statutory deductions and who made it, when.</summary>
    private sealed record Chosen(StatutoryChoice Choice, Guid By, DateTimeOffset At);

    /// <summary>
    /// The statutory deductions in force on the pay date: the schedule's for everyone (null: every one,
    /// as monthly payroll makes) and each person's own, or theirs as one of the schedule's chosen people.
    /// A weekly, fortnightly or daily schedule nobody has set makes none.
    /// </summary>
    private async Task<(Chosen? Schedule, Dictionary<Guid, Chosen> People)> ChoicesAsync(PayRun run, string frequency, Guid[] employees, CancellationToken ct)
    {
        var people = new Dictionary<Guid, Chosen>();
        if (frequency == "monthly") return (null, people);
        var none = new StatutoryChoice([]);
        var row = await db.ScheduleDeductions.AsNoTracking().Where(d => d.PayScheduleId == run.PayScheduleId && d.StartsOn <= run.PayDate)
            .OrderByDescending(d => d.StartsOn).ThenByDescending(d => d.SetAt).FirstOrDefaultAsync(ct);
        Chosen? schedule = row is null ? new Chosen(none, Guid.Empty, default)
            : new Chosen(row.MakesDeductions && row.AppliesTo == "everyone" ? new StatutoryChoice(row.Items) : none, row.SetBy, row.SetAt);
        if (row is { AppliesTo: "chosen" })
            foreach (var id in row.EmployeeIds!.Intersect(employees))
                people[id] = new Chosen(new StatutoryChoice(row.Items), row.SetBy, row.SetAt);

        var own = await db.EmployeeDeductions.AsNoTracking().Where(d => employees.Contains(d.EmployeeId) && d.StartsOn <= run.PayDate).ToListAsync(ct);
        foreach (var latest in own.GroupBy(d => d.EmployeeId).Select(g => g.OrderByDescending(d => d.StartsOn).ThenByDescending(d => d.SetAt).First()))
        {
            if (latest.Items is null) people.Remove(latest.EmployeeId);   // back to the schedule's
            else people[latest.EmployeeId] = new Chosen(new StatutoryChoice(latest.Items), latest.SetBy, latest.SetAt);
        }
        return (row is null ? schedule with { By = Guid.Empty } : schedule, people);
    }

    /// <summary>What each person's approved runs paid earlier in the run's tax month (the last approved run for each schedule and period).</summary>
    private async Task<Dictionary<Guid, MonthToDate>> EarlierAsync(PayRun run, Guid[] employees, CancellationToken ct)
    {
        var (start, end) = RunPeriods.TaxMonth(run.PayDate);
        var rows = await (from p in db.People.AsNoTracking()
                          join r in db.PayRuns.AsNoTracking() on p.PayRunId equals r.Id
                          where r.CompanyId == run.CompanyId && r.Status == "approved" && r.PayDate >= start && r.PayDate <= end && r.Id != run.Id
                                && !(r.PayScheduleId == run.PayScheduleId && r.PeriodStart == run.PeriodStart) && p.Included && employees.Contains(p.EmployeeId)
                          select new { p.Id, p.EmployeeId, r.PayScheduleId, r.PeriodStart, r.ApprovedAt }).ToListAsync(ct);
        var counted = rows.GroupBy(r => (r.EmployeeId, r.PayScheduleId, r.PeriodStart)).Select(g => g.OrderByDescending(r => r.ApprovedAt).First()).ToList();
        var ids = counted.Select(r => r.Id).ToArray();
        var lines = await db.Lines.AsNoTracking().Where(l => ids.Contains(l.PersonId)).ToListAsync(ct);
        return counted.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g =>
        {
            var mine = g.Select(r => r.Id).ToHashSet();
            return MonthToDate.From(lines.Where(l => mine.Contains(l.PersonId)).Select(l =>
                new WorkedLine(l.Kind, l.Code, l.Name, l.PayItemId, l.StatutoryKey, l.Taxable, l.BeforeTax, l.Base, l.Rate, l.Amount)));
        });
    }

    private async Task<RunSetup> SetupAsync(PayRun run, string country, RulePack pack, CancellationToken ct)
    {
        var items = await org.PayItems.AsNoTracking().ToListAsync(ct);
        var names = items.Where(i => i.LockedKey is not null && (i.CountryCode is null || i.CountryCode == country))
            .ToDictionary(i => i.LockedKey!, i => new LineName(i.Code, i.Name, i.Id));
        var own = items.Where(i => i.LockedKey is null)
            .Select(i => new ItemDefinition(i.Id, i.Code, i.Name, i.Kind, i.Taxable, i.BeforeTax, i.CalcMethod, i.DefaultAmount, i.Rate, i.Status, i.Unit, i.Multiplier))
            .ToList();
        var assignments = await org.EmployeePayItems.AsNoTracking().Where(a => a.CompanyId == run.CompanyId)
            .Select(a => new ItemAssignment(a.PayItemId, a.EmployeeId, a.Amount, a.Rate, a.StartsOn, a.EndsOn, a.Multiplier)).ToListAsync(ct);
        return new RunSetup(new RunPeriod(run.PeriodStart, run.PeriodEnd), run.PayScheduleId, run.EmployeeIds, pack, names, own, assignments);
    }

    /// <summary>
    /// The wage order in force for the company's country on a pay date, with its minimum wages and the
    /// company's own working hours where it set them (null: no order for the country).
    /// </summary>
    private async Task<WageOrder?> WageOrderAsync(Company company, DateOnly payDate, CancellationToken ct)
    {
        var order = await WorkingHoursEndpoints.OrderInForceAsync(db, company.CountryCode, payDate, ct);
        if (order is null) return null;
        var minimums = await db.MinimumWages.AsNoTracking().Where(m => m.CountryCode == company.CountryCode && m.EffectiveFrom == order.EffectiveFrom)
            .ToDictionaryAsync(m => m.WageAreaCode, m => new MinimumWage(m.Monthly, m.Daily, m.Hourly), ct);
        return new WageOrder(company.HoursPerDay ?? order.HoursPerDay, company.HoursPerMonth ?? order.HoursPerMonth, minimums);
    }

    private async Task<List<Candidate>> CandidatesAsync(PayRun run, Company company, CancellationToken ct)
    {
        var query = org.Employees.AsNoTracking().Include(e => e.Employments).Include(e => e.Compensations).AsSplitQuery()
            .Where(e => e.CompanyId == run.CompanyId);
        // A correction lists everyone chosen, saying who it can't pay; a regular run only those employed in the period.
        var leavers = await RunCutoffs.LeaversOwedAsync(db, run, ct);
        query = run.EmployeeIds is { } chosen
            ? query.Where(e => chosen.Contains(e.Id))
            : query.Where(e => (e.HireDate <= run.PeriodEnd && (e.ExitDate == null || e.ExitDate >= run.PeriodStart)) || leavers.Contains(e.Id));
        var employees = await query.OrderBy(e => e.StaffNo).ToListAsync(ct);
        var inputs = await db.Inputs.AsNoTracking().Where(i => i.PayRunId == run.Id).ToListAsync(ct);
        var time = inputs.Where(i => i.Quantity is not null)
            .ToLookup(i => i.EmployeeId, i => new TimeEntry(i.PayItemId, i.Quantity!.Value, RunCutoffs.Day(i.EnteredAt)));
        var amounts = inputs.Where(i => i.Amount is not null)
            .ToLookup(i => i.EmployeeId, i => new RunAmount(i.PayItemId, i.Amount!.Value, i.Reason, RunCutoffs.Day(i.EnteredAt)));
        var areas = await org.Locations.AsNoTracking().Where(l => l.WageAreaCode != null).ToDictionaryAsync(l => l.Id, l => l.WageAreaCode!, ct);

        // Their place of work: the location of the job they hold at the period's end (their last, if none), else the company's main one.
        string? WageArea(Employee e)
        {
            var job = e.Employments.Where(j => j.ValidFrom <= run.PeriodEnd).OrderByDescending(j => j.ValidFrom).FirstOrDefault();
            var location = job?.LocationId ?? company.MainLocationId;
            return location is { } id ? areas.GetValueOrDefault(id) : null;
        }

        return employees.Select(e => new Candidate(e.Id, e.StaffNo, FullName(e), e.HireDate, e.ExitDate,
            e.Employments.Select(j => new JobRow(j.Id, j.ValidFrom, j.ValidTo, j.PayScheduleId)).ToList(),
            e.Compensations.Select(c => new BasicPayRow(c.Id, c.ValidFrom, c.ValidTo, c.BasicAmount, c.PayBasis, c.Currency, c.UsualQuantity)).ToList(),
            Time: time[e.Id].ToList(), WageArea: WageArea(e), Amounts: amounts[e.Id].ToList())).ToList();
    }

    private static string FullName(Employee e) => string.Join(' ', new[] { e.FirstName, e.MiddleName, e.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));

    private async Task SaveAsync(Guid payRunId, IReadOnlyList<WorkedPerson> people, Func<Guid, Chosen?> chosen, string packVersion, RunCutoff? cutoff,
        Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var run = await db.PayRuns.FromSql($"select * from pay_runs where id = {payRunId} for update").SingleAsync(ct);
        if (run.Status != "calculating") return;   // cancelled while it was worked out

        await db.People.Where(p => p.PayRunId == run.Id).ExecuteDeleteAsync(ct);
        foreach (var worked in people)
        {
            var person = new PayRunPerson
            {
                Id = Guid.CreateVersion7(), OrganizationId = run.OrganizationId, CompanyId = run.CompanyId, PayRunId = run.Id, EmployeeId = worked.EmployeeId,
                StaffNo = worked.StaffNo, FullName = worked.FullName, Included = worked.Included, ExcludedReason = worked.ExcludedReason,
                Notes = [.. worked.Notes], EmploymentId = worked.EmploymentId, CompensationId = worked.CompensationId,
                BasicPay = worked.Totals?.BasicPay, GrossPay = worked.Totals?.GrossPay, TaxablePay = worked.Totals?.TaxablePay,
                TaxBeforeRelief = worked.Totals?.TaxBeforeRelief, PersonalRelief = worked.Totals?.PersonalRelief, Paye = worked.Totals?.Paye,
                TotalDeductions = worked.Totals?.TotalDeductions, NetPay = worked.Totals?.NetPay, EmployerCosts = worked.Totals?.EmployerCosts,
                DeductionsMade = worked.Deductions is { } made ? StatutoryChoice.Groups.Where(made.Contains).ToArray() : null,
            };
            if (worked.Included && chosen(worked.EmployeeId) is { By: var by, At: var at } && by != Guid.Empty)
                (person.DeductionsChosenBy, person.DeductionsChosenAt) = (by, at);
            person.Lines = worked.Lines.Select((l, i) => new PayRunLine
            {
                Id = Guid.CreateVersion7(), OrganizationId = run.OrganizationId, CompanyId = run.CompanyId, PayRunId = run.Id, PersonId = person.Id,
                Kind = l.Kind, Code = l.Code, Name = l.Name, PayItemId = l.PayItemId, StatutoryKey = l.StatutoryKey, Taxable = l.Taxable,
                BeforeTax = l.BeforeTax, Base = l.Base, Rate = l.Rate, Amount = l.Amount, MonthToDate = l.MonthToDate, Earlier = l.Earlier,
                Quantity = l.Quantity, UnitRate = l.UnitRate, Reason = l.Reason, OwedFrom = l.Owed?.Start, OwedTo = l.Owed?.End, Position = i + 1,
            }).ToList();
            db.People.Add(person);
        }

        var totals = PayRunCalculator.Totals(people);
        run.People = totals.People;
        run.GrossPay = totals.GrossPay;
        run.Paye = totals.Paye;
        run.NssfEmployee = totals.NssfEmployee;
        run.Shif = totals.Shif;
        run.AhlEmployee = totals.AhlEmployee;
        run.OtherDeductions = totals.OtherDeductions;
        run.NetPay = totals.NetPay;
        run.NssfEmployer = totals.NssfEmployer;
        run.AhlEmployer = totals.AhlEmployer;
        run.Nita = totals.Nita;
        run.OtherEmployerCosts = totals.OtherEmployerCosts;
        run.RulePackVersion = packVersion;
        run.CutoffOn = cutoff?.On;
        run.CutoffAppliesTo = cutoff?.AppliesTo.ToArray();
        run.CalculationError = null;
        run.Status = "calculated";
        run.CalculatedBy = userId;
        run.CalculatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
