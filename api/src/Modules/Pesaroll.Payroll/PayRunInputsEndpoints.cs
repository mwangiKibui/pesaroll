using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Organizations;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Payroll;

/// <summary>
/// Days or hours for one person in a run: for basic pay (no item: a daily or hourly rate) or an item
/// paid by the hour or day. An empty quantity removes what was entered.
/// </summary>
public sealed record TimeEntryRequest(Guid EmployeeId, Guid? PayItemId, decimal? Quantity);

public sealed record RunTimeRequest(IReadOnlyList<TimeEntryRequest>? Entries);

/// <summary>What was entered, where it came from (entered, or copied from the last approved run), who did it and when.</summary>
public sealed record TimeEntryResponse(Guid? PayItemId, decimal Quantity, string Source, Guid EnteredBy, DateTimeOffset EnteredAt);

/// <summary>
/// Someone the run pays: Unit is day or hour for a daily or hourly rate (null for a salary or wage,
/// paid as it is), with the days or hours they usually work (paid when nothing is entered).
/// </summary>
public sealed record RunTimePerson(Guid EmployeeId, string StaffNo, string Name, string? Unit, decimal? UsualQuantity, IReadOnlyList<TimeEntryResponse> Entries);

/// <summary>An item paid by the hour or day that can be entered for the run's company.</summary>
public sealed record RunTimeItem(Guid PayItemId, string Code, string Name, string Unit);

public sealed record RunTimeResponse(Guid PayRunId, IReadOnlyList<RunTimePerson> People, IReadOnlyList<RunTimeItem> Items);

/// <summary>
/// Days and hours worked in a pay run (PAY-11): entered by Payroll, or copied from the last approved
/// run on the schedule so a small business doesn't type the same week again. They can change until
/// the run is sent for approval; a change sends a worked-out run back to draft, to be worked out
/// again. Reading needs pay_runs.view, changing pay_runs.manage, at the run's company.
/// </summary>
public static class PayRunInputsEndpoints
{
    private const decimal MostInAPeriod = 744;   // hours in a 31-day month

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
            var people = (await PeopleAsync(org, run, ct)).ToDictionary(p => p.Id);
            var items = (await ItemsAsync(org, run, ct)).ToDictionary(i => i.Id);
            var entries = request.Entries ?? [];
            var errors = new Dictionary<string, string>();
            var seen = new HashSet<(Guid, Guid?)>();
            for (var i = 0; i < entries.Count; i++)
            {
                var (employee, item, quantity) = entries[i];
                var at = $"entries[{i}].";
                if (!people.TryGetValue(employee, out var person))
                    errors[$"{at}employeeId"] = "Choose someone this pay run pays.";
                else if (item is null && person.Unit is null)
                    errors[$"{at}payItemId"] = $"{person.Name} is paid a salary or wage, not by the day or hour.";
                else if (!seen.Add((employee, item)))
                    errors[$"{at}employeeId"] = $"{person.Name} is on the list twice for the same pay.";
                if (item is { } itemId && !items.ContainsKey(itemId))
                    errors[$"{at}payItemId"] = "Choose an item paid by the hour or day at this company.";
                if (quantity is not null and (< 0 or > MostInAPeriod) || quantity is { } q && decimal.Round(q, 2) != q)
                    errors[$"{at}quantity"] = "Enter the days or hours worked, from 0 to 744, with up to two decimals.";
            }
            if (errors.Count > 0) throw PayRunErrors.InputsInvalid(errors);

            var now = clock.GetUtcNow();
            var existing = await db.Inputs.Where(x => x.PayRunId == run.Id).ToListAsync(ct);
            foreach (var (employee, item, quantity) in entries)
            {
                var row = existing.FirstOrDefault(x => x.EmployeeId == employee && x.PayItemId == item);
                if (quantity is not { } value)
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
                (row.Quantity, row.Source, row.EnteredBy, row.EnteredAt) = (value, "entered", userId, now);
            }
            if (entries.Count > 0) BackToDraft(run);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await DescribeAsync(db, org, run, ct));
        }).Produces<RunTimeResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayRuns);

        // Last time's days and hours, for whoever has nothing entered for that pay yet; what was entered stays.
        runs.MapPost("/{id:guid}/inputs/copy-previous", async (Guid id, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = PayRunsEndpoints.Who(tenant, http);
            var run = await ChangeableAsync(db, tenant, id, ct);
            var previous = await db.PayRuns.AsNoTracking()
                .Where(r => r.CompanyId == run.CompanyId && r.PayScheduleId == run.PayScheduleId && r.Status == "approved" && r.PayDate < run.PayDate)
                .OrderByDescending(r => r.PayDate).ThenByDescending(r => r.ApprovedAt).FirstOrDefaultAsync(ct)
                ?? throw PayRunErrors.NothingToCopy();

            var people = (await PeopleAsync(org, run, ct)).ToDictionary(p => p.Id);
            var items = (await ItemsAsync(org, run, ct)).Select(i => i.Id).ToHashSet();
            var have = (await db.Inputs.AsNoTracking().Where(x => x.PayRunId == run.Id).Select(x => new { x.EmployeeId, x.PayItemId }).ToListAsync(ct))
                .Select(x => (x.EmployeeId, x.PayItemId)).ToHashSet();
            var now = clock.GetUtcNow();
            var copied = 0;
            foreach (var old in await db.Inputs.AsNoTracking().Where(x => x.PayRunId == previous.Id).ToListAsync(ct))
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

    private sealed record Person(Guid Id, string StaffNo, string Name, string? Unit, decimal? Usual);

    private sealed record Item(Guid Id, string Code, string Name, string Unit);

    /// <summary>
    /// Who the run pays, as the calculation finds them: for a regular run, the company's people
    /// employed in the period whose job at its end (their last day, if they left) is on the run's
    /// schedule; for a correction, the people chosen. Unit: day or hour for a daily or hourly rate.
    /// </summary>
    private static async Task<List<Person>> PeopleAsync(OrganizationsDbContext org, PayRun run, CancellationToken ct)
    {
        var query = org.Employees.AsNoTracking().Include(e => e.Employments).Include(e => e.Compensations).AsSplitQuery()
            .Where(e => e.CompanyId == run.CompanyId && e.HireDate <= run.PeriodEnd && (e.ExitDate == null || e.ExitDate >= run.PeriodStart));
        if (run.EmployeeIds is { } chosen) query = query.Where(e => chosen.Contains(e.Id));
        var people = new List<Person>();
        foreach (var e in await query.OrderBy(e => e.StaffNo).ToListAsync(ct))
        {
            var on = e.ExitDate is { } exit && exit < run.PeriodEnd ? exit : run.PeriodEnd;
            var job = e.Employments.Where(j => j.ValidFrom <= on && (j.ValidTo == null || j.ValidTo >= on)).OrderByDescending(j => j.ValidFrom).FirstOrDefault();
            if (job?.PayScheduleId != run.PayScheduleId) continue;
            var pay = e.Compensations.Where(c => c.ValidFrom <= on && (c.ValidTo == null || c.ValidTo >= on)).OrderByDescending(c => c.ValidFrom).FirstOrDefault();
            var unit = pay?.PayBasis switch { "daily" => "day", "hourly" => "hour", _ => null };
            people.Add(new Person(e.Id, e.StaffNo, $"{e.FirstName} {e.LastName}", unit, unit is null ? null : pay!.UsualQuantity));
        }
        return people;
    }

    /// <summary>The organization's active items paid by the hour or day that serve the run's company.</summary>
    private static async Task<List<Item>> ItemsAsync(OrganizationsDbContext org, PayRun run, CancellationToken ct) =>
        await org.PayItems.AsNoTracking()
            .Where(i => i.Status == "active" && i.CalcMethod == "per_unit" && (i.AllCompanies || i.Companies.Any(c => c.CompanyId == run.CompanyId)))
            .OrderBy(i => i.Code).Select(i => new Item(i.Id, i.Code, i.Name, i.Unit!)).ToListAsync(ct);

    private static async Task<RunTimeResponse> DescribeAsync(PayrollDbContext db, OrganizationsDbContext org, PayRun run, CancellationToken ct)
    {
        var entries = (await db.Inputs.AsNoTracking().Where(x => x.PayRunId == run.Id).ToListAsync(ct)).ToLookup(x => x.EmployeeId);
        var people = await PeopleAsync(org, run, ct);
        return new RunTimeResponse(run.Id,
            people.Select(p => new RunTimePerson(p.Id, p.StaffNo, p.Name, p.Unit, p.Usual, entries[p.Id]
                .OrderBy(x => x.PayItemId is not null).ThenBy(x => x.PayItemId)
                .Select(x => new TimeEntryResponse(x.PayItemId, x.Quantity, x.Source, x.EnteredBy, x.EnteredAt)).ToList())).ToList(),
            (await ItemsAsync(org, run, ct)).Select(i => new RunTimeItem(i.Id, i.Code, i.Name, i.Unit)).ToList());
    }
}
