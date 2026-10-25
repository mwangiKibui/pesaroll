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

        var people = PayRunCalculator.Work(await SetupAsync(run, company.CountryCode, pack, ct), await CandidatesAsync(run, ct));
        await SaveAsync(run.Id, people, pack.Version, userId, ct);
    }

    private async Task<RunSetup> SetupAsync(PayRun run, string country, RulePack pack, CancellationToken ct)
    {
        var items = await org.PayItems.AsNoTracking().ToListAsync(ct);
        var names = items.Where(i => i.LockedKey is not null && (i.CountryCode is null || i.CountryCode == country))
            .ToDictionary(i => i.LockedKey!, i => new LineName(i.Code, i.Name, i.Id));
        var own = items.Where(i => i.LockedKey is null)
            .Select(i => new ItemDefinition(i.Id, i.Code, i.Name, i.Kind, i.Taxable, i.BeforeTax, i.CalcMethod, i.DefaultAmount, i.Rate, i.Status)).ToList();
        var assignments = await org.EmployeePayItems.AsNoTracking().Where(a => a.CompanyId == run.CompanyId)
            .Select(a => new ItemAssignment(a.PayItemId, a.EmployeeId, a.Amount, a.Rate, a.StartsOn, a.EndsOn)).ToListAsync(ct);
        return new RunSetup(new RunPeriod(run.PeriodStart, run.PeriodEnd), run.PayScheduleId, run.EmployeeIds, pack, names, own, assignments);
    }

    private async Task<List<Candidate>> CandidatesAsync(PayRun run, CancellationToken ct)
    {
        var query = org.Employees.AsNoTracking().Include(e => e.Employments).Include(e => e.Compensations).AsSplitQuery()
            .Where(e => e.CompanyId == run.CompanyId);
        // A correction lists everyone chosen, saying who it can't pay; a regular run only those employed in the period.
        query = run.EmployeeIds is { } chosen
            ? query.Where(e => chosen.Contains(e.Id))
            : query.Where(e => e.HireDate <= run.PeriodEnd && (e.ExitDate == null || e.ExitDate >= run.PeriodStart));
        var employees = await query.OrderBy(e => e.StaffNo).ToListAsync(ct);
        return employees.Select(e => new Candidate(e.Id, e.StaffNo, FullName(e), e.HireDate, e.ExitDate,
            e.Employments.Select(j => new JobRow(j.Id, j.ValidFrom, j.ValidTo, j.PayScheduleId)).ToList(),
            e.Compensations.Select(c => new BasicPayRow(c.Id, c.ValidFrom, c.ValidTo, c.BasicAmount, c.PayBasis, c.Currency)).ToList())).ToList();
    }

    private static string FullName(Employee e) => string.Join(' ', new[] { e.FirstName, e.MiddleName, e.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));

    private async Task SaveAsync(Guid payRunId, IReadOnlyList<WorkedPerson> people, string packVersion, Guid userId, CancellationToken ct)
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
            };
            person.Lines = worked.Lines.Select((l, i) => new PayRunLine
            {
                Id = Guid.CreateVersion7(), OrganizationId = run.OrganizationId, CompanyId = run.CompanyId, PayRunId = run.Id, PersonId = person.Id,
                Kind = l.Kind, Code = l.Code, Name = l.Name, PayItemId = l.PayItemId, StatutoryKey = l.StatutoryKey, Taxable = l.Taxable,
                BeforeTax = l.BeforeTax, Base = l.Base, Rate = l.Rate, Amount = l.Amount, Position = i + 1,
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
        run.CalculationError = null;
        run.Status = "calculated";
        run.CalculatedBy = userId;
        run.CalculatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
