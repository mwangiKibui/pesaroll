using System.Globalization;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Identity;
using Pesaroll.Organizations;
using Pesaroll.Shared.Audit;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;
using Pesaroll.Workflows;

namespace Pesaroll.Payroll;

/// <summary>Monthly schedules are run by month (2026-10); weekly, fortnightly and daily ones by pay date.</summary>
public sealed record CreatePayRunRequest(Guid CompanyId, Guid PayScheduleId, string? Month, DateOnly? PayDate = null);

public sealed record CorrectPayRunRequest(Guid[]? EmployeeIds);

/// <summary>A run in the company's list.</summary>
public sealed record PayRunSummary(
    Guid Id, Guid CompanyId, Guid PayScheduleId, string ScheduleName, string Kind, Guid? CorrectsPayRunId, DateOnly PeriodStart, DateOnly PeriodEnd,
    DateOnly PayDate, string Status, int People, decimal GrossPay, decimal NetPay, DateTimeOffset CreatedAt);

public sealed record PayRunPage(IReadOnlyList<PayRunSummary> Items, int Total, int Page, int PageSize);

/// <summary>The run's totals. EmployerCost is everything the employer pays on top of gross pay.</summary>
public sealed record PayRunTotals(
    int People, decimal GrossPay, decimal Paye, decimal NssfEmployee, decimal Shif, decimal HousingLevyEmployee, decimal OtherDeductions, decimal NetPay,
    decimal NssfEmployer, decimal HousingLevyEmployer, decimal Nita, decimal OtherEmployerCosts, decimal EmployerCost);

public sealed record PersonRef(Guid EmployeeId, string StaffNo, string Name);

/// <summary>Someone the run didn't pay: no_pay, other_basis, other_currency or not_in_period.</summary>
public sealed record LeftOutPerson(Guid EmployeeId, string StaffNo, string Name, string Reason);

/// <summary>Someone to check before sending: joined, left, negative_net, no_deductions, some_deductions, refund.</summary>
public sealed record FlaggedPerson(Guid EmployeeId, string StaffNo, string Name, IReadOnlyList<string> Notes);

/// <summary>What changed since the company's last approved regular run on the schedule.</summary>
public sealed record PayRunChanges(Guid PreviousPayRunId, DateOnly PreviousPeriodStart, IReadOnlyList<PersonRef> Added, IReadOnlyList<PersonRef> Gone,
    decimal GrossPayChange, decimal NetPayChange);

/// <summary>
/// A run in full, without anyone's own pay: its totals, who was left out and why, who to check, and
/// what changed since the last approved run. CanManage: whether this person may change it.
/// </summary>
public sealed record PayRunDetail(
    Guid Id, Guid CompanyId, Guid PayScheduleId, string ScheduleName, string Kind, Guid? CorrectsPayRunId, IReadOnlyList<Guid>? EmployeeIds,
    DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly PayDate, string Status, string? RulePackVersion, string? CalculationError, string? DeclineReason,
    Guid? ApprovalRequestId, PayRunTotals Totals, IReadOnlyList<LeftOutPerson> LeftOut, IReadOnlyList<FlaggedPerson> Flagged, PayRunChanges? Changes,
    DateTimeOffset CreatedAt, DateTimeOffset? CalculatedAt, DateTimeOffset? SentAt, DateTimeOffset? ApprovedAt, DateTimeOffset? CancelledAt, bool CanManage,
    DateOnly? CutoffOn = null);

/// <summary>
/// A line of someone's pay. Rate is a fraction (0.06 is 6%). Statutory lines also give the tax
/// month's total and what earlier runs in the month took (PAY-10); lines paid by the day or hour
/// their quantity and unit rate (PAY-11: 5 days x 450.00); lines set by hand for the run, why (PAY-04);
/// lines owed from the last period, held back by its cut-off, that period (PAY-04).
/// </summary>
public sealed record PayRunLineResponse(
    string Kind, string Code, string Name, string? StatutoryKey, decimal? Base, decimal? Rate, decimal Amount, decimal? MonthToDate = null, decimal Earlier = 0,
    decimal? Quantity = null, decimal? UnitRate = null, string? Reason = null, DateOnly? OwedFrom = null, DateOnly? OwedTo = null);

/// <summary>For a correction: what was last approved for the person in the period, and how net pay changes.</summary>
public sealed record ApprovedBefore(Guid PayRunId, decimal GrossPay, decimal Paye, decimal TotalDeductions, decimal NetPay, decimal NetPayChange);

public sealed record PayRunPersonResponse(
    Guid EmployeeId, string StaffNo, string Name, IReadOnlyList<string> Notes, decimal BasicPay, decimal GrossPay, decimal TaxablePay, decimal Paye,
    decimal NssfEmployee, decimal Shif, decimal HousingLevyEmployee, decimal TotalDeductions, decimal NetPay, decimal EmployerCosts,
    IReadOnlyList<PayRunLineResponse> Lines, ApprovedBefore? ApprovedBefore, IReadOnlyList<string>? DeductionsMade = null, Guid? DeductionsChosenBy = null);

public sealed record PayRunPeoplePage(IReadOnlyList<PayRunPersonResponse> Items, int Total, int Page, int PageSize);

/// <summary>
/// Pay runs (PAY-03, PAY-10), per company: Payroll starts one for a company, schedule and month (or
/// pay date, for weekly, fortnightly and daily schedules),
/// has it worked out (a job), checks it and sends it for approval through the organization's
/// pay_run workflow (WF-01..03); approved, it never changes, and mistakes are corrected with a new
/// run for the people concerned. Payroll needs pay_runs.manage (pay_runs.view to look); approvers
/// see a run that waits for them, or that they approved, through its request, never a draft.
/// Every look at people's pay is recorded (ACC-05), never the amounts.
/// </summary>
public static class PayRunsEndpoints
{
    public const int MaxPageSize = 100;
    private static readonly string[] Open = ["draft", "calculating", "calculated"];

    public static IEndpointRouteBuilder MapPayRunsEndpoints(this IEndpointRouteBuilder app)
    {
        var runs = app.MapGroup("/api/pay-runs").WithTags("Pay runs");

        runs.MapPost("/", async (CreatePayRunRequest request, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = Who(tenant, http);
            if (!tenant.Can(Permissions.PayRuns, request.CompanyId)) throw AccessErrors.NoPermission();
            var fields = new Dictionary<string, string>();
            var company = await org.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CompanyId && c.Status == "active", ct);
            if (company is null) fields["companyId"] = "Choose one of your companies.";
            var schedule = await org.PaySchedules.AsNoTracking().Include(s => s.Companies)
                .SingleOrDefaultAsync(s => s.Id == request.PayScheduleId && s.Status == "active", ct);
            if (schedule is null || !(schedule.AllCompanies || schedule.Companies.Any(c => c.CompanyId == request.CompanyId)))
                fields["payScheduleId"] = "Choose a payroll schedule the company uses.";
            var monthly = schedule?.Frequency is null or "monthly";
            var start = default(DateOnly);
            if (monthly && !DateOnly.TryParseExact($"{request.Month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                fields["month"] = "Choose the month, like 2026-10.";
            if (!monthly && request.PayDate is null) fields["payDate"] = "Choose the pay date.";
            if (fields.Count > 0) throw PayRunErrors.Invalid(fields);

            RunPeriod period;
            DateOnly payDate;
            string label;
            if (monthly)
            {
                (period, payDate) = (new RunPeriod(start, start.AddMonths(1).AddDays(-1)), PayDates.In(start.Year, start.Month, schedule!.PayDayRule!, schedule.PayDay));
                label = start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            }
            else
            {
                payDate = request.PayDate!.Value;
                label = payDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
                if (!RunPeriods.IsPayDate(new ScheduleDates(schedule!.Frequency, schedule.PayWeekday, schedule.FirstPayDate), payDate))
                    throw PayRunErrors.NotAPayDate(schedule.Name, label);
                period = RunPeriods.Ending(schedule.Frequency, payDate);
                label = $"the run paid on {label}";
            }

            if (await db.PayRuns.AnyAsync(r => r.CompanyId == company!.Id && r.PayScheduleId == schedule.Id && r.PeriodStart == period.Start
                                               && r.Kind == "regular" && r.Status != "cancelled", ct))
                throw PayRunErrors.AlreadyExists(schedule.Name, label, company!.LegalName);

            var run = new PayRun
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = company!.Id, PayScheduleId = schedule.Id,
                PeriodStart = period.Start, PeriodEnd = period.End, PayDate = payDate, CreatedBy = userId, CreatedAt = clock.GetUtcNow(),
            };
            db.PayRuns.Add(run);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw PayRunErrors.AlreadyExists(schedule.Name, label, company.LegalName);   // started at the same moment
            }

            await RecordAsync(db, org, AuditActions.PayRunStarted, run, ct);
            await transaction.CommitAsync(ct);

            return Results.Created($"/api/pay-runs/{run.Id}", await DescribeAsync(db, org, tenant, run, ct));
        }).Produces<PayRunDetail>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status409Conflict)
          .RequirePermission(Permissions.PayRuns);

        // The company's runs, newest month first.
        runs.MapGet("/", async (Guid companyId, int? page, int? pageSize, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant,
            HttpContext http, CancellationToken ct) =>
        {
            Who(tenant, http);
            if (!tenant.Can(Permissions.PayRunsView, companyId)) throw AccessErrors.NoPermission();
            var size = Math.Clamp(pageSize ?? 24, 1, MaxPageSize);
            var number = Math.Max(page ?? 1, 1);
            var query = db.PayRuns.AsNoTracking().Where(r => r.CompanyId == companyId);
            var total = await query.CountAsync(ct);
            var list = await query.OrderByDescending(r => r.PeriodStart).ThenByDescending(r => r.CreatedAt).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var names = await ScheduleNamesAsync(org, list.Select(r => r.PayScheduleId), ct);
            return Results.Ok(new PayRunPage(list.Select(r => new PayRunSummary(r.Id, r.CompanyId, r.PayScheduleId, names[r.PayScheduleId], r.Kind,
                r.CorrectsPayRunId, r.PeriodStart, r.PeriodEnd, r.PayDate, r.Status, r.People, r.GrossPay, r.NetPay, r.CreatedAt)).ToList(), total, number, size));
        }).Produces<PayRunPage>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .RequirePermission(Permissions.PayRunsView);

        runs.MapGet("/{id:guid}", async (Guid id, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            CancellationToken ct) =>
        {
            var run = await ReadableAsync(db, tenant, http, id, ct);
            return Results.Ok(await DescribeAsync(db, org, tenant, run, ct));
        }).Produces<PayRunDetail>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .SignedIn();   // Payroll by permission; approvers through the run's request

        // Each person paid, with their lines, by name. Shows pay, so every look is recorded.
        runs.MapGet("/{id:guid}/people", async (Guid id, int? page, int? pageSize, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant,
            HttpContext http, CancellationToken ct) =>
        {
            var run = await ReadableAsync(db, tenant, http, id, ct);
            var size = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);
            var number = Math.Max(page ?? 1, 1);
            var query = db.People.AsNoTracking().Where(p => p.PayRunId == run.Id && p.Included);
            var total = await query.CountAsync(ct);
            var people = await query.OrderBy(p => p.FullName).ThenBy(p => p.StaffNo).Skip((number - 1) * size).Take(size)
                .Include(p => p.Lines.OrderBy(l => l.Position)).ToListAsync(ct);
            var before = run.Kind == "correction" ? await ApprovedBeforeAsync(db, run, people.Select(p => p.EmployeeId).ToArray(), ct) : [];

            await SensitiveViews.RecordAsync(org, tenant, http, "employee_pay", people.Select(p => p.EmployeeId).ToList(), ct);
            decimal Statutory(PayRunPerson p, string key) => p.Lines.Where(l => l.StatutoryKey == key).Sum(l => l.Amount);
            return Results.Ok(new PayRunPeoplePage(people.Select(p => new PayRunPersonResponse(p.EmployeeId, p.StaffNo, p.FullName, p.Notes,
                p.BasicPay!.Value, p.GrossPay!.Value, p.TaxablePay!.Value, p.Paye!.Value, Statutory(p, "nssf_employee"), Statutory(p, "shif"),
                Statutory(p, "ahl_employee"), p.TotalDeductions!.Value, p.NetPay!.Value, p.EmployerCosts!.Value,
                p.Lines.Select(l => new PayRunLineResponse(l.Kind, l.Code, l.Name, l.StatutoryKey, l.Base, l.Rate, l.Amount, l.MonthToDate, l.Earlier,
                    l.Quantity, l.UnitRate, l.Reason, l.OwedFrom, l.OwedTo)).ToList(),
                before.GetValueOrDefault(p.EmployeeId) is { } b ? b with { NetPayChange = p.NetPay!.Value - b.NetPay } : null,
                p.DeductionsMade, p.DeductionsChosenBy)).ToList(), total, number, size));
        }).Produces<PayRunPeoplePage>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .SignedIn();   // as above

        // Works it out again in the background; the run shows calculating until it is done.
        runs.MapPost("/{id:guid}/calculate", async (Guid id, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            IBackgroundJobClient jobs, CancellationToken ct) =>
        {
            var (userId, organizationId) = Who(tenant, http);
            var run = await ManageableAsync(db, tenant, id, ct);
            if (run.Status == "awaiting_approval") throw PayRunErrors.WaitingForApproval();
            if (!Open.Contains(run.Status)) throw PayRunErrors.Final(run.Status);
            // Each run takes what earlier ones in its tax month didn't, so those come first (PAY-10).
            var (monthStart, _) = RunPeriods.TaxMonth(run.PayDate);
            var waiting = await db.PayRuns.AsNoTracking()
                .Where(r => r.CompanyId == run.CompanyId && r.PayScheduleId == run.PayScheduleId && r.Id != run.Id && r.PayDate >= monthStart
                            && r.PayDate < run.PayDate && r.Status != "approved" && r.Status != "cancelled")
                .OrderBy(r => r.PayDate).FirstOrDefaultAsync(ct);
            if (waiting is not null) throw PayRunErrors.EarlierNotApproved(waiting.PayDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture));
            run.Status = "calculating";
            run.CalculationError = null;
            await db.SaveChangesAsync(ct);
            jobs.Enqueue<PayRunCalculation>(job => job.RunAsync(run.Id, organizationId, run.CompanyId, userId, CancellationToken.None));
            return Results.Accepted($"/api/pay-runs/{run.Id}", await DescribeAsync(db, org, tenant, run, ct));
        }).Produces<PayRunDetail>(StatusCodes.Status202Accepted).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayRuns);

        // Sends a worked-out run for approval (WF-5005 when nobody is set up to approve pay runs at the company).
        runs.MapPost("/{id:guid}/submit", async (Guid id, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            Approvals approvals, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = Who(tenant, http);
            var run = await ManageableAsync(db, tenant, id, ct);
            if (run.Status == "awaiting_approval") throw PayRunErrors.WaitingForApproval();
            if (run.Status is "approved" or "cancelled") throw PayRunErrors.Final(run.Status);
            if (run.Status != "calculated") throw PayRunErrors.NotCalculated();
            if (run.People == 0) throw PayRunErrors.NobodyPaid();
            var belowZero = await db.People.CountAsync(p => p.PayRunId == run.Id && p.Notes.Contains("negative_net"), ct);
            if (belowZero > 0) throw PayRunErrors.BelowZero(belowZero);
            // Nobody is paid below the minimum wage where they work (PAY-11).
            var belowMinimum = await db.People.CountAsync(p => p.PayRunId == run.Id && p.Included && p.Notes.Contains("below_minimum"), ct);
            if (belowMinimum > 0) throw PayRunErrors.BelowMinimumWage(belowMinimum);

            // Marked first, so its figures are fixed before anyone can approve them; put back if sending fails.
            var sent = await db.PayRuns.Where(r => r.Id == run.Id && r.Status == "calculated").ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "awaiting_approval").SetProperty(r => r.SentBy, userId).SetProperty(r => r.SentAt, clock.GetUtcNow())
                .SetProperty(r => r.DeclineReason, (string?)null), ct);
            if (sent == 0) throw PayRunErrors.NotCalculated();   // recalculated or cancelled meanwhile
            try
            {
                await approvals.SubmitAsync("pay_run", run.CompanyId, run.Id, null, ct);
            }
            catch
            {
                await db.PayRuns.Where(r => r.Id == run.Id && r.Status == "awaiting_approval").ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, "calculated").SetProperty(r => r.SentBy, (Guid?)null).SetProperty(r => r.SentAt, (DateTimeOffset?)null), CancellationToken.None);
                throw;
            }

            await RecordAsync(db, org, AuditActions.PayRunSent, run, ct, ("people", run.People));
            return Results.Ok(await DescribeAsync(db, org, tenant, await db.PayRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id, ct), ct));
        }).Produces<PayRunDetail>().Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayRuns);

        // A run that wasn't sent (or came back) can be dropped; its month is free for a new one.
        runs.MapPost("/{id:guid}/cancel", async (Guid id, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = Who(tenant, http);
            var run = await ManageableAsync(db, tenant, id, ct);
            if (run.Status == "awaiting_approval") throw PayRunErrors.WaitingForApproval();
            if (!Open.Contains(run.Status)) throw PayRunErrors.Final(run.Status);
            run.Status = "cancelled";
            run.CancelledBy = userId;
            run.CancelledAt = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await RecordAsync(db, org, AuditActions.PayRunCancelled, run, ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(await DescribeAsync(db, org, tenant, run, ct));
        }).Produces<PayRunDetail>().Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayRuns);

        // A correction of an approved run: a new run for the same month and the people chosen, worked
        // out again with today's details; it is then calculated, sent and approved like any other.
        runs.MapPost("/{id:guid}/corrections", async (Guid id, CorrectPayRunRequest request, PayrollDbContext db, OrganizationsDbContext org,
            TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = Who(tenant, http);
            var original = await ManageableAsync(db, tenant, id, ct);
            if (original.Status != "approved") throw PayRunErrors.OnlyApprovedCorrected();
            var chosen = (request.EmployeeIds ?? []).Distinct().ToArray();
            var found = await org.Employees.AsNoTracking().CountAsync(e => e.CompanyId == original.CompanyId && chosen.Contains(e.Id), ct);
            if (chosen.Length == 0 || found != chosen.Length)
                throw PayRunErrors.Invalid(new Dictionary<string, string> { ["employeeIds"] = "Choose at least one employee of this company." });

            var run = new PayRun
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = original.CompanyId, PayScheduleId = original.PayScheduleId,
                Kind = "correction", CorrectsPayRunId = original.Id, EmployeeIds = chosen, PeriodStart = original.PeriodStart,
                PeriodEnd = original.PeriodEnd, PayDate = original.PayDate, CreatedBy = userId, CreatedAt = clock.GetUtcNow(),
            };
            db.PayRuns.Add(run);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await RecordAsync(db, org, AuditActions.PayRunCorrected, original, ct, ("correctionId", run.Id), ("people", chosen.Length));
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/pay-runs/{run.Id}", await DescribeAsync(db, org, tenant, run, ct));
        }).Produces<PayRunDetail>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayRuns);

        return app;
    }

    /// <summary>Records something done to a run in the audit log (PLAT-03): named by its schedule, with its period, never its amounts.</summary>
    internal static async Task RecordAsync(PayrollDbContext db, OrganizationsDbContext org, string action, PayRun run, CancellationToken ct,
        params (string Key, object? Value)[] more)
    {
        var names = await ScheduleNamesAsync(org, [run.PayScheduleId], ct);
        await Audit.RecordAsync(db, action, run.CompanyId, new AuditSubject("pay_run", run.Id, names[run.PayScheduleId]),
            AuditDetails(run, more), ct);
    }

    /// <summary>A run's details for the audit log: its kind and period.</summary>
    internal static Dictionary<string, object?> AuditDetails(PayRun run, params (string Key, object? Value)[] more)
    {
        var details = new Dictionary<string, object?>
        {
            ["kind"] = run.Kind, ["periodStart"] = run.PeriodStart, ["periodEnd"] = run.PeriodEnd, ["payDate"] = run.PayDate,
        };
        foreach (var (key, value) in more) details[key] = value;
        return details;
    }

    internal static (Guid User, Guid Organization) Who(TenantContext tenant, HttpContext http)
    {
        if (tenant.UserId is not Guid user) throw SessionService.RequireSignedIn(http);
        if (tenant.OrganizationId is not Guid organization) throw AccessErrors.NoPermission();
        return (user, organization);
    }

    /// <summary>A run this person may change: pay_runs.manage at its company (one they can't see is not found).</summary>
    internal static async Task<PayRun> ManageableAsync(PayrollDbContext db, TenantContext tenant, Guid id, CancellationToken ct)
    {
        var run = await db.PayRuns.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (run is null || !tenant.Can(Permissions.PayRunsView, run.CompanyId)) throw PayRunErrors.NotFound();
        if (!tenant.Can(Permissions.PayRuns, run.CompanyId)) throw AccessErrors.NoPermission();
        return run;
    }

    /// <summary>
    /// A run this person may look at: pay_runs.view at its company, or an approver of the request it is
    /// waiting on or was approved with. Anyone else, and approvers of a run back in Payroll's hands,
    /// get not found.
    /// </summary>
    internal static async Task<PayRun> ReadableAsync(PayrollDbContext db, TenantContext tenant, HttpContext http, Guid id, CancellationToken ct)
    {
        var (user, _) = Who(tenant, http);
        var run = await db.PayRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw PayRunErrors.NotFound();
        if (tenant.Can(Permissions.PayRunsView, run.CompanyId)) return run;
        if (run.Status is "awaiting_approval" or "approved" && await db.Database.SqlQuery<bool>($"""
                select exists (
                    select 1 from approval_requests r join approval_request_approvers a on a.request_id = r.id
                    where r.kind = 'pay_run' and r.subject_id = {run.Id} and r.status in ('submitted', 'approved') and a.user_id = {user}) as "Value"
                """).SingleAsync(ct))
            return run;
        throw PayRunErrors.NotFound();
    }

    /// <summary>Schedule names by id; a schedule since taken off the company shows no name.</summary>
    private static async Task<Dictionary<Guid, string>> ScheduleNamesAsync(OrganizationsDbContext org, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToArray();
        var names = await org.PaySchedules.AsNoTracking().Where(s => wanted.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return wanted.ToDictionary(id => id, id => names.GetValueOrDefault(id) ?? "");
    }

    private static async Task<PayRunDetail> DescribeAsync(PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, PayRun run, CancellationToken ct)
    {
        var names = await ScheduleNamesAsync(org, [run.PayScheduleId], ct);
        var people = await db.People.AsNoTracking().Where(p => p.PayRunId == run.Id).OrderBy(p => p.FullName).ThenBy(p => p.StaffNo)
            .Select(p => new { p.EmployeeId, p.StaffNo, p.FullName, p.Included, p.ExcludedReason, p.Notes }).ToListAsync(ct);
        var requestId = await db.Database.SqlQuery<Guid?>($"""
            select id as "Value" from approval_requests where kind = 'pay_run' and subject_id = {run.Id} order by sent_at desc limit 1
            """).FirstOrDefaultAsync(ct);

        PayRunChanges? changes = null;
        if (run.Kind == "regular")
        {
            var previous = await db.PayRuns.AsNoTracking()
                .Where(r => r.CompanyId == run.CompanyId && r.PayScheduleId == run.PayScheduleId && r.Kind == "regular" && r.Status == "approved"
                            && r.PeriodStart < run.PeriodStart)
                .OrderByDescending(r => r.PeriodStart).FirstOrDefaultAsync(ct);
            if (previous is not null)
            {
                var before = await db.People.AsNoTracking().Where(p => p.PayRunId == previous.Id && p.Included)
                    .Select(p => new PersonRef(p.EmployeeId, p.StaffNo, p.FullName)).ToListAsync(ct);
                var now = people.Where(p => p.Included).Select(p => new PersonRef(p.EmployeeId, p.StaffNo, p.FullName)).ToList();
                var beforeIds = before.Select(p => p.EmployeeId).ToHashSet();
                var nowIds = now.Select(p => p.EmployeeId).ToHashSet();
                changes = new PayRunChanges(previous.Id, previous.PeriodStart, now.Where(p => !beforeIds.Contains(p.EmployeeId)).ToList(),
                    before.Where(p => !nowIds.Contains(p.EmployeeId)).OrderBy(p => p.Name).ToList(), run.GrossPay - previous.GrossPay, run.NetPay - previous.NetPay);
            }
        }

        var totals = new PayRunTotals(run.People, run.GrossPay, run.Paye, run.NssfEmployee, run.Shif, run.AhlEmployee, run.OtherDeductions, run.NetPay,
            run.NssfEmployer, run.AhlEmployer, run.Nita, run.OtherEmployerCosts, run.NssfEmployer + run.AhlEmployer + run.Nita + run.OtherEmployerCosts);
        return new PayRunDetail(run.Id, run.CompanyId, run.PayScheduleId, names[run.PayScheduleId], run.Kind, run.CorrectsPayRunId, run.EmployeeIds,
            run.PeriodStart, run.PeriodEnd, run.PayDate, run.Status, run.RulePackVersion, run.CalculationError, run.DeclineReason, requestId, totals,
            people.Where(p => !p.Included).Select(p => new LeftOutPerson(p.EmployeeId, p.StaffNo, p.FullName, p.ExcludedReason!)).ToList(),
            people.Where(p => p.Included && p.Notes.Length > 0).Select(p => new FlaggedPerson(p.EmployeeId, p.StaffNo, p.FullName, p.Notes)).ToList(),
            changes, run.CreatedAt, run.CalculatedAt, run.SentAt, run.ApprovedAt, run.CancelledAt, tenant.Can(Permissions.PayRuns, run.CompanyId),
            run.CutoffOn);
    }

    /// <summary>For a correction: each person's figures in the last run approved for the period before it.</summary>
    private static async Task<Dictionary<Guid, ApprovedBefore>> ApprovedBeforeAsync(PayrollDbContext db, PayRun run, Guid[] employees, CancellationToken ct)
    {
        var rows = await (from p in db.People.AsNoTracking()
                          join r in db.PayRuns.AsNoTracking() on p.PayRunId equals r.Id
                          where r.CompanyId == run.CompanyId && r.PayScheduleId == run.PayScheduleId && r.PeriodStart == run.PeriodStart
                                && r.Status == "approved" && r.Id != run.Id && (run.ApprovedAt == null || r.ApprovedAt < run.ApprovedAt)
                                && p.Included && employees.Contains(p.EmployeeId)
                          select new { p.EmployeeId, r.Id, r.ApprovedAt, p.GrossPay, p.Paye, p.TotalDeductions, p.NetPay }).ToListAsync(ct);
        return rows.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g =>
        {
            var last = g.OrderByDescending(r => r.ApprovedAt).First();
            return new ApprovedBefore(last.Id, last.GrossPay!.Value, last.Paye!.Value, last.TotalDeductions!.Value, last.NetPay!.Value, 0);
        });
    }
}
