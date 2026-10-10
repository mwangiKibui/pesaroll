using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// Adding or editing a payroll schedule. <see cref="PayDay"/> is only for a fixed day. It serves all
/// companies (<see cref="AllCompanies"/>) or <see cref="CompanyIds"/>, and is the default of <see cref="DefaultFor"/>.
/// </summary>
public sealed record PayScheduleRequest(
    string? Name, string? Frequency, string? PayDayRule, int? PayDay, int? CutoffDay,
    bool AllCompanies, List<Guid>? CompanyIds, List<Guid>? DefaultFor);

public sealed record PayScheduleResponse(
    Guid Id, string Name, string Frequency, string PayDayRule, int? PayDay, int CutoffDay,
    bool AllCompanies, IReadOnlyList<Guid> CompanyIds, IReadOnlyList<Guid> DefaultFor, IReadOnlyList<DateOnly> NextPayDates);

/// <summary>
/// ONB-06, the Payroll schedules step: how often and when people are paid. Each company has one
/// default schedule (the suggested "Monthly", last working day, cut-off on the 25th, made with the
/// first company for all companies); a schedule serves chosen companies or all of them. Pay runs
/// (PAY-03) build their periods and pay dates from these.
/// </summary>
public static class PaySchedulesEndpoints
{
    /// <summary>How often a schedule pays; weekly and daily come with shift work in Release 3.</summary>
    public static readonly string[] Frequencies = ["monthly"];

    public static IEndpointRouteBuilder MapPaySchedulesEndpoints(this IEndpointRouteBuilder app)
    {
        var schedules = app.MapGroup("/api/pay-schedules").WithTags("Payroll schedules");

        schedules.MapGet("/", async (Guid? companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var query = db.PaySchedules.AsNoTracking().Include(s => s.Companies).Where(s => s.Status == "active");
            if (companyId is Guid id)
            {
                query = query.Where(s => s.AllCompanies || s.Companies.Any(c => c.CompanyId == id));
            }

            var list = await query.OrderBy(s => s.CreatedAt).ThenBy(s => s.Name).ToListAsync(ct);
            var defaults = await DefaultsAsync(db, ct);
            var today = PayDates.TodayInKenya(clock.GetUtcNow());
            return Results.Ok(list.Select(s => ToResponse(s, defaults, today)).ToList());
        }).Produces<List<PayScheduleResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        // The next pay dates for a pay day, shown while someone fills in a schedule.
        schedules.MapGet("/pay-dates", (string? payDayRule, int? payDay, TenantContext tenant, HttpContext http, TimeProvider clock) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var errors = new Dictionary<string, string>();
            var (rule, day) = PayDay(payDayRule, payDay, errors);
            if (errors.Count > 0)
            {
                throw OnboardingErrors.ScheduleDetailsInvalid(errors);
            }

            return Results.Ok(PayDates.Next(PayDates.TodayInKenya(clock.GetUtcNow()), rule, day));
        }).Produces<List<DateOnly>>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        schedules.MapPost("/", async (PayScheduleRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var valid = await ValidateAsync(request, db, ct);

            var now = clock.GetUtcNow();
            var schedule = new PaySchedule { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = valid.Name, CreatedAt = now };
            Apply(schedule, valid, now);
            db.PaySchedules.Add(schedule);
            await SetDefaultsAsync(db, schedule, valid.DefaultFor, ct);
            await SaveAsync(db, ct);
            return Results.Created($"/api/pay-schedules/{schedule.Id}",
                ToResponse(schedule, await DefaultsAsync(db, ct), PayDates.TodayInKenya(now)));
        }).Produces<PayScheduleResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        schedules.MapPatch("/{id:guid}", async (Guid id, PayScheduleRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var schedule = await db.PaySchedules.Include(s => s.Companies).SingleOrDefaultAsync(s => s.Id == id && s.Status == "active", ct)
                ?? throw OnboardingErrors.ScheduleNotFound();
            var valid = await ValidateAsync(request, db, ct);

            // Every company keeps a default schedule: this one stays its default until another takes over.
            var keeping = await db.Companies.Where(c => c.DefaultPayScheduleId == schedule.Id && !valid.DefaultFor.Contains(c.Id))
                .Select(c => c.LegalName).ToListAsync(ct);
            if (keeping.Count > 0)
            {
                throw OnboardingErrors.DefaultScheduleNeeded(string.Join(", ", keeping));
            }

            var now = clock.GetUtcNow();
            schedule.Name = valid.Name;
            var dropped = schedule.Companies.Where(c => !valid.CompanyIds.Contains(c.CompanyId)).ToList();
            Apply(schedule, valid, now);
            await SetDefaultsAsync(db, schedule, valid.DefaultFor, ct);

            // New links before old ones go: a schedule is visible (row-level security) through its links,
            // so it must never be without one mid-save.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SaveAsync(db, ct);
            db.PayScheduleCompanies.RemoveRange(dropped);
            await SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
            var saved = await db.PaySchedules.AsNoTracking().Include(s => s.Companies).SingleAsync(s => s.Id == id, ct);
            return Results.Ok(ToResponse(saved, await DefaultsAsync(db, ct), PayDates.TodayInKenya(now)));
        }).Produces<PayScheduleResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        schedules.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var schedule = await db.PaySchedules.SingleOrDefaultAsync(s => s.Id == id && s.Status == "active", ct)
                ?? throw OnboardingErrors.ScheduleNotFound();
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var defaultOf = await db.Companies.Where(c => c.DefaultPayScheduleId == id).Select(c => c.LegalName).ToListAsync(ct);
            if (defaultOf.Count > 0)
            {
                throw OnboardingErrors.DefaultScheduleNeeded(string.Join(", ", defaultOf));
            }

            db.PaySchedules.Remove(schedule);   // its company links go with it (on delete cascade)
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// A new company's default schedule: the oldest one for all companies, or else the suggested
    /// "Monthly" (last working day, cut-off on the 25th) for all companies, which the caller adds.
    /// Kept even if the Payroll schedules step is skipped.
    /// </summary>
    internal static async Task<(PaySchedule Schedule, bool IsNew)> DefaultForNewCompanyAsync(
        OrganizationsDbContext db, Guid organizationId, DateTimeOffset now, CancellationToken ct)
    {
        var forAll = await db.PaySchedules.Where(s => s.AllCompanies && s.Status == "active").OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (forAll is not null)
        {
            return (forAll, false);
        }

        var names = (await db.PaySchedules.Where(s => s.Status == "active").Select(s => s.Name).ToListAsync(ct))
            .Select(n => n.Trim().ToLowerInvariant()).ToHashSet();
        var name = Enumerable.Range(1, 99).Select(n => n == 1 ? "Monthly" : $"Monthly {n}").First(n => !names.Contains(n.ToLowerInvariant()));
        return (new PaySchedule { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = name, AllCompanies = true, CreatedAt = now, UpdatedAt = now }, true);
    }

    private sealed record ValidSchedule(
        string Name, string Frequency, string PayDayRule, int? PayDay, int CutoffDay, bool AllCompanies, List<Guid> CompanyIds, List<Guid> DefaultFor);

    private static async Task<ValidSchedule> ValidateAsync(PayScheduleRequest request, OrganizationsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 60)
        {
            errors["name"] = "Enter the schedule's name (2 to 60 characters).";
        }

        var frequency = string.IsNullOrWhiteSpace(request.Frequency) ? "monthly" : request.Frequency.Trim();
        if (!Frequencies.Contains(frequency))
        {
            errors["frequency"] = "Choose how often from the list.";
        }

        var (rule, day) = PayDay(request.PayDayRule, request.PayDay, errors);
        var cutoff = request.CutoffDay ?? 25;
        if (cutoff is < 1 or > 28)
        {
            errors["cutoffDay"] = "Choose a cut-off day from 1 to 28.";
        }

        var companies = await db.Companies.AsNoTracking().Where(c => c.Status == "active").ToListAsync(ct);
        var chosen = (request.CompanyIds ?? []).Distinct().ToList();
        var serves = request.AllCompanies ? companies : companies.Where(c => chosen.Contains(c.Id)).ToList();
        if (!request.AllCompanies && chosen.Count == 0)
        {
            errors["companyIds"] = "Choose at least one company, or all companies.";
        }
        else if (!request.AllCompanies && serves.Count != chosen.Count)
        {
            errors["companyIds"] = "Choose companies from your organization.";
        }

        var defaultFor = (request.DefaultFor ?? []).Distinct().ToList();
        if (defaultFor.Any(id => !serves.Any(c => c.Id == id)))
        {
            errors["defaultFor"] = "A schedule can only be the default of a company that uses it.";
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.ScheduleDetailsInvalid(errors);
        }

        return new ValidSchedule(name, frequency, rule, day, cutoff, request.AllCompanies, request.AllCompanies ? [] : chosen, defaultFor);
    }

    /// <summary>The pay day rule (last working day unless said) and, only for a fixed day, the day.</summary>
    private static (string Rule, int? Day) PayDay(string? payDayRule, int? payDay, Dictionary<string, string> errors)
    {
        var rule = string.IsNullOrWhiteSpace(payDayRule) ? PayDates.LastWorkingDay : payDayRule.Trim();
        if (!PayDates.Rules.Contains(rule))
        {
            errors["payDayRule"] = "Choose when people are paid from the list.";
            return (PayDates.LastWorkingDay, null);
        }

        if (rule != PayDates.FixedDay)
        {
            return (rule, null);
        }

        if (payDay is not (>= 1 and <= 28))
        {
            errors["payDay"] = "Choose a pay day from 1 to 28.";
        }

        return (rule, payDay);
    }

    private static void Apply(PaySchedule schedule, ValidSchedule valid, DateTimeOffset now)
    {
        schedule.Frequency = valid.Frequency;
        schedule.PayDayRule = valid.PayDayRule;
        schedule.PayDay = valid.PayDay;
        schedule.CutoffDay = valid.CutoffDay;
        schedule.AllCompanies = valid.AllCompanies;
        schedule.UpdatedAt = now;
        foreach (var companyId in valid.CompanyIds.Where(id => !schedule.Companies.Any(c => c.CompanyId == id)))
        {
            schedule.Companies.Add(new PayScheduleCompany { OrganizationId = schedule.OrganizationId, PayScheduleId = schedule.Id, CompanyId = companyId, CreatedAt = now });
        }
    }

    // A company has one default schedule, so making this one its default moves it from wherever it was.
    private static async Task SetDefaultsAsync(OrganizationsDbContext db, PaySchedule schedule, List<Guid> defaultFor, CancellationToken ct)
    {
        foreach (var company in await db.Companies.Where(c => defaultFor.Contains(c.Id)).ToListAsync(ct))
        {
            company.DefaultPayScheduleId = schedule.Id;
        }
    }

    private static async Task SaveAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "pay_schedules_unique_name" })
        {
            throw OnboardingErrors.ScheduleNameTaken();
        }
    }

    private static async Task<Dictionary<Guid, List<Guid>>> DefaultsAsync(OrganizationsDbContext db, CancellationToken ct) =>
        (await db.Companies.AsNoTracking().Where(c => c.DefaultPayScheduleId != null && c.Status == "active")
            .Select(c => new { c.Id, Default = c.DefaultPayScheduleId!.Value }).ToListAsync(ct))
        .GroupBy(c => c.Default).ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

    private static PayScheduleResponse ToResponse(PaySchedule s, Dictionary<Guid, List<Guid>> defaults, DateOnly today) => new(
        s.Id, s.Name, s.Frequency, s.PayDayRule, s.PayDay, s.CutoffDay, s.AllCompanies,
        s.Companies.Select(c => c.CompanyId).ToList(), defaults.GetValueOrDefault(s.Id) ?? [],
        PayDates.Next(today, s.PayDayRule, s.PayDay));
}
