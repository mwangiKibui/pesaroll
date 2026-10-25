using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Organizations;
using Pesaroll.Payroll.Engine;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Payroll;

/// <summary>What a schedule asks for: whether it makes statutory deductions, which, for whom, from when.</summary>
public sealed record ScheduleDeductionsRequest(bool MakesDeductions, string[]? Items, string? AppliesTo, Guid[]? EmployeeIds, DateOnly? StartsOn);

/// <summary>A choice of statutory deductions as recorded: who set it and when (none for the default).</summary>
public sealed record DeductionChoiceResponse(
    bool MakesDeductions, IReadOnlyList<string> Items, string AppliesTo, IReadOnlyList<Guid> EmployeeIds, DateOnly? StartsOn, Guid? SetBy, DateTimeOffset? SetAt);

/// <summary>A schedule's deductions: the choice in force today, and every choice recorded, newest first.</summary>
public sealed record ScheduleDeductionsResponse(Guid PayScheduleId, string Frequency, DeductionChoiceResponse Current, IReadOnlyList<DeductionChoiceResponse> History);

/// <summary>A person's own deductions from a date: the items (none when empty), or their schedule's (null).</summary>
public sealed record EmployeeDeductionsRequest(string[]? Items, DateOnly? StartsOn);

public sealed record EmployeeDeductionChoice(IReadOnlyList<string>? Items, DateOnly StartsOn, Guid SetBy, DateTimeOffset SetAt);

public sealed record EmployeeDeductionsResponse(Guid EmployeeId, IReadOnlyList<EmployeeDeductionChoice> History);

public sealed record DeductionsExampleRequest(string? Frequency, decimal? Wage, string[]? Items, string? CountryCode);

public sealed record ExampleLine(string Key, decimal Amount);

public sealed record ExampleRun(int Number, decimal Pay, IReadOnlyList<ExampleLine> Deductions, decimal TakeHome, IReadOnlyList<ExampleLine> EmployerCosts);

/// <summary>A month of a sample wage: each run, and the month's pay, deductions, take-home pay and employer costs.</summary>
public sealed record DeductionsExample(
    string Frequency, decimal Wage, int Runs, string RulePackVersion, IReadOnlyList<ExampleRun> EachRun, decimal Pay, IReadOnlyList<ExampleLine> Deductions,
    decimal TakeHome, IReadOnlyList<ExampleLine> EmployerCosts);

/// <summary>
/// Which statutory deductions weekly, fortnightly and daily schedules make (PAY-10). Monthly payroll
/// makes every one; other schedules make none until the organization says otherwise (off by default),
/// then the items chosen, for everyone on the schedule or for chosen people, from a date. A person can
/// have their own. Every choice is kept with who made it. The example works out a month of a sample
/// wage with the rule pack in force, so whoever sets it up sees what it means before saving.
/// Schedules need Payroll schedules (Manage, for every company: they serve the organization); a
/// person's own needs Pay (Manage) at their company.
/// </summary>
public static class StatutoryDeductionsEndpoints
{
    private static readonly string[] Frequencies = ["weekly", "fortnightly", "daily"];

    /// <summary>Runs in a month, for the example: four weeks, two fortnights, 22 working days.</summary>
    private static int RunsInMonth(string frequency) => frequency switch { "weekly" => 4, "fortnightly" => 2, _ => 22 };

    public static IEndpointRouteBuilder MapStatutoryDeductionsEndpoints(this IEndpointRouteBuilder app)
    {
        var schedules = app.MapGroup("/api/pay-schedules").WithTags("Payroll schedules");

        schedules.MapGet("/{id:guid}/deductions", async (Guid id, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            PayRunsEndpoints.Who(tenant, http);
            var schedule = await ScheduleAsync(org, id, ct);
            return Results.Ok(await DescribeAsync(db, schedule, PayDates.TodayInKenya(clock.GetUtcNow()), ct));
        }).Produces<ScheduleDeductionsResponse>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).RequirePermission(Permissions.SchedulesView);

        schedules.MapPost("/{id:guid}/deductions", async (Guid id, ScheduleDeductionsRequest request, PayrollDbContext db, OrganizationsDbContext org,
            TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = PayRunsEndpoints.Who(tenant, http);
            if (!tenant.Can(Permissions.Schedules)) throw AccessErrors.NoPermission();
            var schedule = await ScheduleAsync(org, id, ct);
            var fields = new Dictionary<string, string>();
            if (schedule.Frequency == "monthly") fields["payScheduleId"] = "Monthly payroll makes every statutory deduction.";
            var items = Items(request.MakesDeductions ? request.Items : [], fields);
            if (request.MakesDeductions && items.Length == 0 && !fields.ContainsKey("items")) fields["items"] = "Choose at least one deduction.";
            var appliesTo = request.MakesDeductions ? request.AppliesTo ?? "everyone" : "everyone";
            if (appliesTo is not ("everyone" or "chosen")) fields["appliesTo"] = "Choose everyone on the schedule or chosen employees.";
            Guid[]? chosen = null;
            if (appliesTo == "chosen")
            {
                chosen = (request.EmployeeIds ?? []).Distinct().ToArray();
                var found = await org.Employees.AsNoTracking().CountAsync(e => chosen.Contains(e.Id), ct);
                if (chosen.Length == 0 || found != chosen.Length) fields["employeeIds"] = "Choose at least one of your employees.";
            }
            if (request.StartsOn is null) fields["startsOn"] = "Choose the date it applies from.";
            if (fields.Count > 0) throw PayRunErrors.DeductionsInvalid(fields);

            db.ScheduleDeductions.Add(new ScheduleDeductions
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, PayScheduleId = schedule.Id, StartsOn = request.StartsOn!.Value,
                MakesDeductions = request.MakesDeductions, Items = items, AppliesTo = appliesTo, EmployeeIds = chosen, SetBy = userId, SetAt = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await DescribeAsync(db, schedule, PayDates.TodayInKenya(clock.GetUtcNow()), ct));
        }).Produces<ScheduleDeductionsResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .RequirePermission(Permissions.Schedules);

        // A month of a sample wage with the deductions ticked, shown while someone sets a schedule up.
        schedules.MapPost("/deductions-example", async (DeductionsExampleRequest request, RulePacks packs, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            PayRunsEndpoints.Who(tenant, http);
            var fields = new Dictionary<string, string>();
            if (!Frequencies.Contains(request.Frequency)) fields["frequency"] = "Choose weekly, fortnightly or daily.";
            if (request.Wage is not (> 0 and <= 100_000_000)) fields["wage"] = "Enter a wage above 0.";
            var items = Items(request.Items, fields);
            if (fields.Count > 0) throw PayRunErrors.DeductionsInvalid(fields);

            var pack = await packs.InForceAsync(request.CountryCode ?? "KE", PayDates.TodayInKenya(clock.GetUtcNow()), ct);
            return Results.Ok(Example(request.Frequency!, request.Wage!.Value, new StatutoryChoice(items), pack));
        }).Produces<DeductionsExample>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .RequirePermission(Permissions.SchedulesView);

        var people = app.MapGroup("/api/companies/{companyId:guid}/employees/{employeeId:guid}/deductions").WithTags("Employees");

        people.MapGet("/", async (Guid companyId, Guid employeeId, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            CancellationToken ct) =>
        {
            PayRunsEndpoints.Who(tenant, http);
            if (!tenant.Can(Permissions.PayView, companyId)) throw AccessErrors.NoPermission();
            await EmployeeAsync(org, companyId, employeeId, ct);
            return Results.Ok(await EmployeeHistoryAsync(db, employeeId, ct));
        }).Produces<EmployeeDeductionsResponse>().Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).RequirePermission(Permissions.PayView);

        people.MapPost("/", async (Guid companyId, Guid employeeId, EmployeeDeductionsRequest request, PayrollDbContext db, OrganizationsDbContext org,
            TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = PayRunsEndpoints.Who(tenant, http);
            if (!tenant.Can(Permissions.Pay, companyId)) throw AccessErrors.NoPermission();
            await EmployeeAsync(org, companyId, employeeId, ct);
            var fields = new Dictionary<string, string>();
            var items = request.Items is null ? null : Items(request.Items, fields);
            if (request.StartsOn is null) fields["startsOn"] = "Choose the date it applies from.";
            if (fields.Count > 0) throw PayRunErrors.DeductionsInvalid(fields);

            db.EmployeeDeductions.Add(new EmployeeDeductions
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = companyId, EmployeeId = employeeId, StartsOn = request.StartsOn!.Value,
                Items = items, SetBy = userId, SetAt = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await EmployeeHistoryAsync(db, employeeId, ct));
        }).Produces<EmployeeDeductionsResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .RequirePermission(Permissions.Pay);

        return app;
    }

    /// <summary>The statutory items asked for, each known (paye, shif, nssf, housing_levy, nita), in the usual order.</summary>
    private static string[] Items(string[]? asked, Dictionary<string, string> fields)
    {
        var items = (asked ?? []).Distinct().ToArray();
        if (items.Any(i => !StatutoryChoice.Groups.Contains(i))) fields["items"] = "Choose from PAYE, SHIF, NSSF, Housing Levy and NITA.";
        return StatutoryChoice.Groups.Where(items.Contains).ToArray();
    }

    private static async Task<PaySchedule> ScheduleAsync(OrganizationsDbContext org, Guid id, CancellationToken ct) =>
        await org.PaySchedules.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.Status == "active", ct) ?? throw PayRunErrors.ScheduleNotFound();

    private static async Task EmployeeAsync(OrganizationsDbContext org, Guid companyId, Guid employeeId, CancellationToken ct)
    {
        if (!await org.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct)) throw PayRunErrors.EmployeeNotFound();
    }

    private static async Task<ScheduleDeductionsResponse> DescribeAsync(PayrollDbContext db, PaySchedule schedule, DateOnly today, CancellationToken ct)
    {
        var rows = await db.ScheduleDeductions.AsNoTracking().Where(d => d.PayScheduleId == schedule.Id)
            .OrderByDescending(d => d.StartsOn).ThenByDescending(d => d.SetAt).ToListAsync(ct);
        var history = rows.Select(Describe).ToList();
        var current = schedule.Frequency == "monthly"
            ? new DeductionChoiceResponse(true, StatutoryChoice.Groups, "everyone", [], null, null, null)
            : rows.FirstOrDefault(d => d.StartsOn <= today) is { } row ? Describe(row) : new DeductionChoiceResponse(false, [], "everyone", [], null, null, null);
        return new ScheduleDeductionsResponse(schedule.Id, schedule.Frequency, current, history);
    }

    private static DeductionChoiceResponse Describe(ScheduleDeductions d) =>
        new(d.MakesDeductions, d.Items, d.AppliesTo, d.EmployeeIds ?? [], d.StartsOn, d.SetBy, d.SetAt);

    private static async Task<EmployeeDeductionsResponse> EmployeeHistoryAsync(PayrollDbContext db, Guid employeeId, CancellationToken ct)
    {
        var rows = await db.EmployeeDeductions.AsNoTracking().Where(d => d.EmployeeId == employeeId)
            .OrderByDescending(d => d.StartsOn).ThenByDescending(d => d.SetAt).ToListAsync(ct);
        return new EmployeeDeductionsResponse(employeeId, rows.Select(d => new EmployeeDeductionChoice(d.Items, d.StartsOn, d.SetBy, d.SetAt)).ToList());
    }

    /// <summary>A month of runs on the wage, each taking what the ones before it didn't, as real runs do.</summary>
    public static DeductionsExample Example(string frequency, decimal wage, StatutoryChoice choice, RulePack pack)
    {
        var schedule = Guid.Empty;
        var day = new DateOnly(2000, 1, 1);
        var setup = new RunSetup(new RunPeriod(day, day), schedule, null, pack, new Dictionary<string, LineName>(), [], [], frequency, choice);
        var lines = new List<WorkedLine>();
        var runs = new List<ExampleRun>();
        for (var number = 1; number <= RunsInMonth(frequency); number++)
        {
            var person = new Candidate(Guid.Empty, "", "", day, null, [new JobRow(Guid.Empty, day, null, schedule)],
                [new BasicPayRow(Guid.Empty, day, null, wage, frequency, pack.Currency)], Earlier: MonthToDate.From(lines));
            var worked = PayRunCalculator.Work(setup, [person]).Single();
            lines.AddRange(worked.Lines);
            runs.Add(new ExampleRun(number, worked.Totals!.GrossPay, Statutory(worked.Lines, "deduction"), worked.Totals.NetPay,
                Statutory(worked.Lines, "employer_cost")));
        }
        return new DeductionsExample(frequency, wage, runs.Count, pack.Version, runs, runs.Sum(r => r.Pay), Statutory(lines, "deduction"),
            runs.Sum(r => r.TakeHome), Statutory(lines, "employer_cost"));
    }

    private static List<ExampleLine> Statutory(IEnumerable<WorkedLine> lines, string kind) =>
        lines.Where(l => l.Kind == kind && l.StatutoryKey is not null).GroupBy(l => l.StatutoryKey!)
            .Select(g => new ExampleLine(g.Key, g.Sum(l => l.Amount))).ToList();
}
