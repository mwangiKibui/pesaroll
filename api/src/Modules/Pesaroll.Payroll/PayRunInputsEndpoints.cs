using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Organizations;
using Pesaroll.Payroll.Engine;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Payroll;

/// <summary>
/// What is entered for one person in a run, for basic pay (no item) or an item. Days or hours
/// (quantity): basic pay at a daily or hourly rate, or an item paid by the hour or day. An amount
/// (PAY-04): an item entered each run (commission), or an edit in review, which says why: a
/// person's item for the run (0 removes it), an item added for the run, basic pay or a statutory
/// amount. Nothing in either removes what was entered.
/// </summary>
public sealed record TimeEntryRequest(Guid EmployeeId, Guid? PayItemId, decimal? Quantity, decimal? Amount = null, string? Reason = null);

public sealed record RunTimeRequest(IReadOnlyList<TimeEntryRequest>? Entries);

/// <summary>
/// What was entered, where it came from (entered, or copied from the last approved run), who did it and
/// when, and whether it came after the schedule's cut-off and so waits for the next run (PAY-04).
/// </summary>
public sealed record TimeEntryResponse(Guid? PayItemId, decimal? Quantity, string Source, Guid EnteredBy, DateTimeOffset EnteredAt, decimal? Amount = null,
    string? Reason = null, bool AfterCutoff = false);

/// <summary>The statutory deductions chosen for a person in this run alone, why, who and when.</summary>
public sealed record RunDeductionsResponse(IReadOnlyList<string> Deductions, string Reason, Guid SetBy, DateTimeOffset SetAt);

/// <summary>
/// Someone the run pays: Unit is day or hour for a daily or hourly rate (null for a salary or wage,
/// paid as it is), with the days or hours they usually work (paid when nothing is entered), the
/// statutory deductions chosen for them in this run (null: theirs or the schedule's), and their last
/// day if they leave by the period's end or left owed from the last run (their final dues are entered here).
/// </summary>
public sealed record RunTimePerson(Guid EmployeeId, string StaffNo, string Name, string? Unit, decimal? UsualQuantity, IReadOnlyList<TimeEntryResponse> Entries,
    RunDeductionsResponse? Deductions = null, DateOnly? LeftOn = null);

/// <summary>An item paid by the hour or day that can be entered for the run's company.</summary>
public sealed record RunTimeItem(Guid PayItemId, string Code, string Name, string Unit);

/// <summary>
/// An item an amount can be entered for: the organization's own (each_run, fixed, percent_of_basic)
/// at the run's company, or a statutory line (StatutoryKey). Anything but each_run is an edit and says why.
/// </summary>
public sealed record RunAmountItem(Guid PayItemId, string Code, string Name, string Kind, string CalcMethod, string? StatutoryKey);

public sealed record RunTimeResponse(Guid PayRunId, IReadOnlyList<RunTimePerson> People, IReadOnlyList<RunTimeItem> Items,
    IReadOnlyList<RunAmountItem>? AmountItems = null);

/// <summary>Statutory deductions for one person in one run (null: back to theirs or the schedule's), and why.</summary>
public sealed record RunDeductionsRequest(IReadOnlyList<string>? Deductions, string? Reason);

/// <summary>
/// What is entered for a pay run. Days and hours worked (PAY-11): entered by Payroll, or copied from
/// the last approved run on the schedule so a small business doesn't type the same week again.
/// Amounts and the accountant's edits in review (PAY-04): amounts for items entered each run, and a
/// person's item, basic pay, statutory amount or statutory deductions for this run, each saying why.
/// They can change until the run is sent for approval; a change sends a worked-out run back to
/// draft, to be worked out again. Reading needs pay_runs.view, changing pay_runs.manage, at the run's company.
/// </summary>
public static class PayRunInputsEndpoints
{
    private const decimal MostInAPeriod = 744;   // hours in a 31-day month
    private const decimal MostAnAmount = 9_999_999_999_999_999.99m;

    public static IEndpointRouteBuilder MapPayRunInputsEndpoints(this IEndpointRouteBuilder app)
    {
        var runs = app.MapGroup("/api/pay-runs").WithTags("Pay runs");

        runs.MapGet("/{id:guid}/inputs", async (Guid id, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            CancellationToken ct) =>
        {
            var run = await PayRunsEndpoints.ReadableAsync(db, tenant, http, id, ct);
            return Results.Ok(await DescribeAsync(db, org, run, ct));
        }).Produces<RunTimeResponse>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .SignedIn();   // pay_runs.view, or an approver of the run (ReadableAsync)

        runs.MapPut("/{id:guid}/inputs", async (Guid id, RunTimeRequest request, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = PayRunsEndpoints.Who(tenant, http);
            var run = await ChangeableAsync(db, tenant, id, ct);
            var people = (await PeopleAsync(db, org, run, ct)).ToDictionary(p => p.Id);
            var items = (await ItemsAsync(org, run, ct)).ToDictionary(i => i.Id);
            var amountItems = (await AmountItemsAsync(org, run, ct)).ToDictionary(i => i.PayItemId);
            var entries = request.Entries ?? [];
            var errors = new Dictionary<string, string>();
            var seen = new HashSet<(Guid, Guid?)>();
            for (var i = 0; i < entries.Count; i++)
            {
                var (employee, item, quantity, amount, reason) = entries[i];
                var at = $"entries[{i}].";
                var timed = item is { } t ? items.ContainsKey(t) : people.GetValueOrDefault(employee)?.Unit is not null;
                if (!people.TryGetValue(employee, out var person))
                    errors[$"{at}employeeId"] = "Choose someone this pay run pays.";
                else if (!seen.Add((employee, item)))
                    errors[$"{at}employeeId"] = $"{person.Name} is on the list twice for the same pay.";
                else if (item is null && quantity is not null && person.Unit is null)
                    errors[$"{at}quantity"] = $"{person.Name} is paid a salary or wage, not by the day or hour.";
                if (item is { } itemId && !items.ContainsKey(itemId) && !amountItems.ContainsKey(itemId))
                    errors[$"{at}payItemId"] = "Choose an item at this company.";
                else if (quantity is not null && item is not null && !timed)
                    errors[$"{at}quantity"] = "This item is an amount, not days or hours.";
                else if (quantity is not null and (< 0 or > MostInAPeriod) || quantity is { } q && decimal.Round(q, 2) != q)
                    errors[$"{at}quantity"] = "Enter the days or hours worked, from 0 to 744, with up to two decimals.";
                if (amount is not null && item is { } paidByTime && items.ContainsKey(paidByTime))
                    errors[$"{at}amount"] = "This item is paid by the hour or day: enter the hours or days.";
                else if (amount is not null and (< 0 or > MostAnAmount) || amount is { } a && decimal.Round(a, 2) != a)
                    errors[$"{at}amount"] = "Enter an amount of 0 or more, with up to two decimals.";
                // An edit beyond an item entered each run says why.
                var routine = item is { } r && amountItems.GetValueOrDefault(r)?.CalcMethod == "each_run";
                if (reason is { } why && why.Trim().Length > 500)
                    errors[$"{at}reason"] = "Say why in up to 500 characters.";
                else if (amount is not null && !routine && string.IsNullOrWhiteSpace(reason))
                    errors[$"{at}reason"] = "Say why this is changed for this pay run.";
            }
            if (errors.Count > 0) throw PayRunErrors.InputsInvalid(errors);

            var now = clock.GetUtcNow();
            var existing = await db.Inputs.Where(x => x.PayRunId == run.Id).ToListAsync(ct);
            foreach (var (employee, item, quantity, amount, reason) in entries)
            {
                var row = existing.FirstOrDefault(x => x.EmployeeId == employee && x.PayItemId == item);
                if (quantity is null && amount is null)
                {
                    if (row is not null) db.Inputs.Remove(row);
                    continue;
                }
                if (row is null)
                {
                    row = new PayRunInput
                    {
                        Id = Guid.CreateVersion7(), OrganizationId = run.OrganizationId, CompanyId = run.CompanyId, PayRunId = run.Id, EmployeeId = employee,
                        PayItemId = item,
                    };
                    db.Inputs.Add(row);
                }
                (row.Quantity, row.Amount, row.Reason, row.Source, row.EnteredBy, row.EnteredAt) =
                    (quantity, amount, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), "entered", userId, now);
            }
            if (entries.Count > 0) BackToDraft(run);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await DescribeAsync(db, org, run, ct));
        }).Produces<RunTimeResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayRuns);

        // A person's statutory deductions for this run alone, with why; none goes back to theirs or the schedule's.
        runs.MapPut("/{id:guid}/people/{employeeId:guid}/deductions", async (Guid id, Guid employeeId, RunDeductionsRequest request, PayrollDbContext db,
            OrganizationsDbContext org, TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = PayRunsEndpoints.Who(tenant, http);
            var run = await ChangeableAsync(db, tenant, id, ct);
            if (!(await PeopleAsync(db, org, run, ct)).Any(p => p.Id == employeeId)) throw PayRunErrors.EmployeeNotFound();
            var row = await db.RunDeductions.SingleOrDefaultAsync(d => d.PayRunId == run.Id && d.EmployeeId == employeeId, ct);
            if (request.Deductions is not { } chosen)
            {
                if (row is not null) db.RunDeductions.Remove(row);
            }
            else
            {
                var fields = new Dictionary<string, string>();
                if (chosen.Any(g => !StatutoryChoice.Groups.Contains(g)))
                    fields["deductions"] = $"Choose from {string.Join(", ", StatutoryChoice.Groups)}.";
                if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
                    fields["reason"] = "Say why, in up to 500 characters, these deductions are made for this pay run.";
                if (fields.Count > 0) throw PayRunErrors.DeductionsInvalid(fields);
                if (row is null)
                {
                    row = new PayRunDeductionChoice
                    {
                        Id = Guid.CreateVersion7(), OrganizationId = run.OrganizationId, CompanyId = run.CompanyId, PayRunId = run.Id, EmployeeId = employeeId,
                        Reason = "",
                    };
                    db.RunDeductions.Add(row);
                }
                (row.Deductions, row.Reason, row.SetBy, row.SetAt) =
                    (StatutoryChoice.Groups.Where(chosen.Contains).ToArray(), request.Reason!.Trim(), userId, clock.GetUtcNow());
            }
            BackToDraft(run);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await DescribeAsync(db, org, run, ct));
        }).Produces<RunTimeResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayRuns);

        // Last time's days and hours, for whoever has nothing entered for that pay yet; what was entered stays (amounts are this run's alone).
        runs.MapPost("/{id:guid}/inputs/copy-previous", async (Guid id, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = PayRunsEndpoints.Who(tenant, http);
            var run = await ChangeableAsync(db, tenant, id, ct);
            var previous = await db.PayRuns.AsNoTracking()
                .Where(r => r.CompanyId == run.CompanyId && r.PayScheduleId == run.PayScheduleId && r.Status == "approved" && r.PayDate < run.PayDate)
                .OrderByDescending(r => r.PayDate).ThenByDescending(r => r.ApprovedAt).FirstOrDefaultAsync(ct)
                ?? throw PayRunErrors.NothingToCopy();

            var people = (await PeopleAsync(db, org, run, ct)).ToDictionary(p => p.Id);
            var items = (await ItemsAsync(org, run, ct)).Select(i => i.Id).ToHashSet();
            var have = (await db.Inputs.AsNoTracking().Where(x => x.PayRunId == run.Id).Select(x => new { x.EmployeeId, x.PayItemId }).ToListAsync(ct))
                .Select(x => (x.EmployeeId, x.PayItemId)).ToHashSet();
            var now = clock.GetUtcNow();
            var copied = 0;
            foreach (var old in await db.Inputs.AsNoTracking().Where(x => x.PayRunId == previous.Id && x.Quantity != null).ToListAsync(ct))
            {
                // Only what still fits: someone this run pays, by rate for basic pay, and an item still paid by the hour or day.
                if (!people.TryGetValue(old.EmployeeId, out var person) || have.Contains((old.EmployeeId, old.PayItemId))) continue;
                if (old.PayItemId is null ? person.Unit is null : !items.Contains(old.PayItemId.Value)) continue;
                db.Inputs.Add(new PayRunInput
                {
                    Id = Guid.CreateVersion7(), OrganizationId = run.OrganizationId, CompanyId = run.CompanyId, PayRunId = run.Id, EmployeeId = old.EmployeeId,
                    PayItemId = old.PayItemId, Quantity = old.Quantity, Source = "copied", EnteredBy = userId, EnteredAt = now,
                });
                copied++;
            }
            if (copied > 0) BackToDraft(run);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await DescribeAsync(db, org, run, ct));
        }).Produces<RunTimeResponse>().Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayRuns);

        return app;
    }

    /// <summary>A run whose days and hours can still change: one this person manages that isn't sent, approved or cancelled.</summary>
    private static async Task<PayRun> ChangeableAsync(PayrollDbContext db, TenantContext tenant, Guid id, CancellationToken ct)
    {
        var run = await PayRunsEndpoints.ManageableAsync(db, tenant, id, ct);
        if (run.Status == "awaiting_approval") throw PayRunErrors.WaitingForApproval();
        if (run.Status is "approved" or "cancelled") throw PayRunErrors.Final(run.Status);
        return run;
    }

    /// <summary>Its figures no longer match what was entered: it is worked out again before it can be sent.</summary>
    private static void BackToDraft(PayRun run)
    {
        if (run.Status is "calculated" or "calculating") run.Status = "draft";
    }

    private sealed record Person(Guid Id, string StaffNo, string Name, string? Unit, decimal? Usual, DateOnly? LeftOn);

    private sealed record Item(Guid Id, string Code, string Name, string Unit);

    /// <summary>
    /// Who the run pays, as the calculation finds them: for a regular run, the company's people
    /// employed in the period whose job at its end (their last day, if they left) is on the run's
    /// schedule, and leavers the last run paid to its cut-off (PAY-04: their final dues can be entered);
    /// for a correction, the people chosen. Unit: day or hour for a daily or hourly rate.
    /// </summary>
    private static async Task<List<Person>> PeopleAsync(PayrollDbContext db, OrganizationsDbContext org, PayRun run, CancellationToken ct)
    {
        var leavers = await RunCutoffs.LeaversOwedAsync(db, run, ct);
        var query = org.Employees.AsNoTracking().Include(e => e.Employments).Include(e => e.Compensations).AsSplitQuery()
            .Where(e => e.CompanyId == run.CompanyId
                && ((e.HireDate <= run.PeriodEnd && (e.ExitDate == null || e.ExitDate >= run.PeriodStart)) || leavers.Contains(e.Id)));
        if (run.EmployeeIds is { } chosen) query = query.Where(e => chosen.Contains(e.Id));
        var people = new List<Person>();
        foreach (var e in await query.OrderBy(e => e.StaffNo).ToListAsync(ct))
        {
            var on = e.ExitDate is { } exit && exit < run.PeriodEnd ? exit : run.PeriodEnd;
            var job = e.Employments.Where(j => j.ValidFrom <= on && (j.ValidTo == null || j.ValidTo >= on)).OrderByDescending(j => j.ValidFrom).FirstOrDefault();
            if (job?.PayScheduleId != run.PayScheduleId) continue;
            var pay = e.Compensations.Where(c => c.ValidFrom <= on && (c.ValidTo == null || c.ValidTo >= on)).OrderByDescending(c => c.ValidFrom).FirstOrDefault();
            var unit = pay?.PayBasis switch { "daily" => "day", "hourly" => "hour", _ => null };
            people.Add(new Person(e.Id, e.StaffNo, $"{e.FirstName} {e.LastName}", unit, unit is null ? null : pay!.UsualQuantity,
                e.ExitDate is { } left && left <= run.PeriodEnd ? left : null));
        }
        return people;
    }

    /// <summary>The organization's active items paid by the hour or day that serve the run's company.</summary>
    private static async Task<List<Item>> ItemsAsync(OrganizationsDbContext org, PayRun run, CancellationToken ct) =>
        await org.PayItems.AsNoTracking()
            .Where(i => i.Status == "active" && i.CalcMethod == "per_unit" && (i.AllCompanies || i.Companies.Any(c => c.CompanyId == run.CompanyId)))
            .OrderBy(i => i.Code).Select(i => new Item(i.Id, i.Code, i.Name, i.Unit!)).ToListAsync(ct);

    /// <summary>
    /// What an amount can be entered for at the run's company: the organization's own active items
    /// (each_run, fixed, percent_of_basic) and the statutory lines (their locked items) of its country.
    /// </summary>
    private static async Task<List<RunAmountItem>> AmountItemsAsync(OrganizationsDbContext org, PayRun run, CancellationToken ct)
    {
        var country = await org.Companies.AsNoTracking().Where(c => c.Id == run.CompanyId).Select(c => c.CountryCode).SingleAsync(ct);
        return await org.PayItems.AsNoTracking()
            .Where(i => i.Status == "active" && (i.AllCompanies || i.Companies.Any(c => c.CompanyId == run.CompanyId))
                && (i.LockedKey == null ? i.CalcMethod == "each_run" || i.CalcMethod == "fixed" || i.CalcMethod == "percent_of_basic"
                    : i.CalcMethod == "by_law" && (i.CountryCode == null || i.CountryCode == country)))
            .OrderBy(i => i.LockedKey != null).ThenBy(i => i.Code)
            .Select(i => new RunAmountItem(i.Id, i.Code, i.Name, i.Kind, i.CalcMethod, i.LockedKey)).ToListAsync(ct);
    }

    private static async Task<RunTimeResponse> DescribeAsync(PayrollDbContext db, OrganizationsDbContext org, PayRun run, CancellationToken ct)
    {
        var entries = (await db.Inputs.AsNoTracking().Where(x => x.PayRunId == run.Id).ToListAsync(ct)).ToLookup(x => x.EmployeeId);
        var chosen = await db.RunDeductions.AsNoTracking().Where(d => d.PayRunId == run.Id).ToDictionaryAsync(d => d.EmployeeId, ct);
        var people = await PeopleAsync(db, org, run, ct);
        var items = await ItemsAsync(org, run, ct);
        var amountItems = await AmountItemsAsync(org, run, ct);
        // Overtime and amounts for items entered each run wait for the next run when entered after the cut-off it applies to.
        var cutoff = await RunCutoffs.ForAsync(org, run, ct);
        bool Late(PayRunInput x) => cutoff is not null && cutoff.After(RunCutoffs.Day(x.EnteredAt))
            && ((cutoff.Covers("overtime") && items.Any(i => i.Id == x.PayItemId))
                || (cutoff.Covers("one_off") && amountItems.Any(i => i.PayItemId == x.PayItemId && i.CalcMethod == "each_run")));
        return new RunTimeResponse(run.Id,
            people.Select(p => new RunTimePerson(p.Id, p.StaffNo, p.Name, p.Unit, p.Usual, entries[p.Id]
                .OrderBy(x => x.PayItemId is not null).ThenBy(x => x.PayItemId)
                .Select(x => new TimeEntryResponse(x.PayItemId, x.Quantity, x.Source, x.EnteredBy, x.EnteredAt, x.Amount, x.Reason, Late(x))).ToList(),
                chosen.GetValueOrDefault(p.Id) is { } d ? new RunDeductionsResponse(d.Deductions, d.Reason, d.SetBy, d.SetAt) : null, p.LeftOn)).ToList(),
            items.Select(i => new RunTimeItem(i.Id, i.Code, i.Name, i.Unit)).ToList(), amountItems);
    }
}
