using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// Adding or editing a payroll schedule. Only the fields of its <see cref="Frequency"/> are kept:
/// monthly <see cref="PayDayRule"/>, <see cref="PayDay"/> (a fixed day) and <see cref="CutoffDay"/>;
/// weekly <see cref="PayWeekday"/> and <see cref="CutoffDaysBefore"/>; fortnightly those and
/// <see cref="FirstPayDate"/>; daily <see cref="PayTime"/>. The cut-off is optional (<see cref="UsesCutoff"/>,
/// on unless said) and covers <see cref="CutoffAppliesTo"/> (everything unless said). It serves all companies
/// (<see cref="AllCompanies"/>) or <see cref="CompanyIds"/>, and is the default of <see cref="DefaultFor"/>.
/// </summary>
public sealed record PayScheduleRequest(
    string? Name, string? Frequency, string? PayDayRule, int? PayDay, int? CutoffDay,
    int? PayWeekday, DateOnly? FirstPayDate, int? CutoffDaysBefore, TimeOnly? PayTime,
    bool AllCompanies, List<Guid>? CompanyIds, List<Guid>? DefaultFor, bool? UsesCutoff = null, List<string>? CutoffAppliesTo = null,
    List<ReminderRequest>? Reminders = null);

/// <summary>A reminder to run payroll: <see cref="Amount"/> days before the pay day (hours before the pay time for daily pay). <see cref="Id"/> keeps an existing one.</summary>
public sealed record ReminderRequest(Guid? Id, int Amount, string? Channel, string? Recipient);

public sealed record ReminderResponse(Guid Id, int Amount, string Unit, string Channel, string Recipient);

public sealed record PayScheduleResponse(
    Guid Id, string Name, string Frequency, string? PayDayRule, int? PayDay, int? CutoffDay,
    int? PayWeekday, DateOnly? FirstPayDate, int? CutoffDaysBefore, TimeOnly? PayTime, bool UsesCutoff, IReadOnlyList<string> CutoffAppliesTo,
    bool AllCompanies, IReadOnlyList<Guid> CompanyIds, IReadOnlyList<Guid> DefaultFor, IReadOnlyList<DateOnly> NextPayDates,
    IReadOnlyList<ReminderResponse> Reminders);

/// <summary>
/// ONB-06, the Payroll schedules step: how often (monthly, weekly, every two weeks or daily) and when people are paid. Each company has one
/// default schedule (the suggested "Monthly", last working day, cut-off on the 25th, made with the
/// first company for all companies); a schedule serves chosen companies or all of them. Pay runs
/// (PAY-03) build their periods and pay dates from these.
/// </summary>
public static class PaySchedulesEndpoints
{
    /// <summary>How often a schedule pays.</summary>
    public static readonly string[] Frequencies = ["monthly", "weekly", "fortnightly", "daily"];

    /// <summary>What a cut-off can cover; an organization can add its own items (any other text).</summary>
    public static readonly string[] CutoffItems = ["overtime", "new_employees", "leavers", "unpaid_leave", "pay_changes", "one_off"];

    public static IEndpointRouteBuilder MapPaySchedulesEndpoints(this IEndpointRouteBuilder app)
    {
        var schedules = app.MapGroup("/api/pay-schedules").WithTags("Payroll schedules");

        schedules.MapGet("/", async (Guid? companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var query = db.PaySchedules.AsNoTracking().Include(s => s.Companies).Include(s => s.Reminders).Where(s => s.Status == "active");
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
        schedules.MapGet("/pay-dates", (string? frequency, string? payDayRule, int? payDay, int? payWeekday, DateOnly? firstPayDate,
            TenantContext tenant, HttpContext http, TimeProvider clock) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var errors = new Dictionary<string, string>();
            var timing = Timing(new PayScheduleRequest(null, frequency, payDayRule, payDay, null, payWeekday, firstPayDate, null, null, true, null, null), errors);
            if (errors.Count > 0)
            {
                throw OnboardingErrors.ScheduleDetailsInvalid(errors);
            }

            return Results.Ok(NextPayDates(timing, PayDates.TodayInKenya(clock.GetUtcNow())));
        }).Produces<List<DateOnly>>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        schedules.MapPost("/", async (PayScheduleRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Schedules);
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
            Access.Require(tenant, Permissions.Schedules);
            var schedule = await db.PaySchedules.Include(s => s.Companies).Include(s => s.Reminders).SingleOrDefaultAsync(s => s.Id == id && s.Status == "active", ct)
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
            db.PayScheduleReminders.RemoveRange(schedule.Reminders.Where(r => !valid.Reminders.Any(v => v.Id == r.Id)));
            Apply(schedule, valid, now);
            await SetDefaultsAsync(db, schedule, valid.DefaultFor, ct);

            // New links before old ones go: a schedule is visible (row-level security) through its links,
            // so it must never be without one mid-save.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SaveAsync(db, ct);
            db.PayScheduleCompanies.RemoveRange(dropped);
            await SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
            var saved = await db.PaySchedules.AsNoTracking().Include(s => s.Companies).Include(s => s.Reminders).SingleAsync(s => s.Id == id, ct);
            return Results.Ok(ToResponse(saved, await DefaultsAsync(db, ct), PayDates.TodayInKenya(now)));
        }).Produces<PayScheduleResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        schedules.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var schedule = await db.PaySchedules.SingleOrDefaultAsync(s => s.Id == id && s.Status == "active", ct)
                ?? throw OnboardingErrors.ScheduleNotFound();
            Access.Require(tenant, Permissions.Schedules);
            await EmployeesEndpoints.RequireUnusedAsync(db, schedule.Name, e => e.PayScheduleId == id, ct);
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

    /// <summary>When a schedule pays: its frequency and that frequency's fields only.</summary>
    private sealed record ValidTiming(
        string Frequency, string? PayDayRule, int? PayDay, int? CutoffDay, int? PayWeekday, DateOnly? FirstPayDate, int? CutoffDaysBefore, TimeOnly? PayTime,
        bool UsesCutoff = false, List<string>? CutoffAppliesTo = null);

    private sealed record ValidReminder(Guid? Id, int Amount, string Unit, string Channel, string Recipient);

    private sealed record ValidSchedule(string Name, ValidTiming Timing, bool AllCompanies, List<Guid> CompanyIds, List<Guid> DefaultFor, List<ValidReminder> Reminders);

    /// <summary>Up to this many reminders per schedule, so a schedule can't send a flood of SMS.</summary>
    public const int MaxReminders = 5;

    /// <summary>
    /// Reminders in the schedule's unit (hours before the pay time for daily pay, days before the pay
    /// day otherwise) within its limit, by email or SMS to a checked address or Kenyan number.
    /// </summary>
    private static List<ValidReminder> Reminders(List<ReminderRequest>? reminders, string frequency, Dictionary<string, string> errors)
    {
        var list = reminders ?? [];
        if (list.Count > MaxReminders)
        {
            errors["reminders"] = $"Add up to {MaxReminders} reminders.";
            return [];
        }

        var (unit, max) = frequency switch { "daily" => ("hours", 12), "weekly" => ("days", 6), "fortnightly" => ("days", 13), _ => ("days", 14) };
        var valid = new List<ValidReminder>();
        for (var i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r.Amount < 1 || r.Amount > max)
            {
                errors[$"reminders[{i}].amount"] = $"Choose from 1 to {max} {unit} before.";
            }

            var channel = r.Channel?.Trim() ?? "";
            string? recipient = null;
            if (channel is not ("email" or "sms"))
            {
                errors[$"reminders[{i}].channel"] = "Choose email or SMS.";
            }
            else
            {
                recipient = channel == "sms" ? Pesaroll.Notifications.Recipients.KenyanMobile(r.Recipient) : Pesaroll.Notifications.Recipients.Email(r.Recipient);
                if (recipient is null)
                {
                    errors[$"reminders[{i}].recipient"] = channel == "sms" ? "Enter a Kenyan mobile number, like 0712 345 678." : "Enter a valid email address.";
                }
            }

            valid.Add(new ValidReminder(r.Id, r.Amount, unit, channel, recipient ?? ""));
        }

        return valid;
    }

    private static async Task<ValidSchedule> ValidateAsync(PayScheduleRequest request, OrganizationsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 60)
        {
            errors["name"] = "Enter the schedule's name (2 to 60 characters).";
        }

        var timing = Timing(request, errors);
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

        var reminders = Reminders(request.Reminders, timing.Frequency, errors);
        if (errors.Count > 0)
        {
            throw OnboardingErrors.ScheduleDetailsInvalid(errors);
        }

        return new ValidSchedule(name, timing, request.AllCompanies, request.AllCompanies ? [] : chosen, defaultFor, reminders);
    }

    /// <summary>
    /// The frequency (monthly unless said) and its own fields, with sensible defaults: the last
    /// working day and the 25th; Friday and two days before; 5 pm. Fields of other frequencies are dropped.
    /// </summary>
    private static ValidTiming Timing(PayScheduleRequest request, Dictionary<string, string> errors)
    {
        var timing = When(request, errors);
        var uses = timing.Frequency != "daily" && (request.UsesCutoff ?? true);
        return uses
            ? timing with { UsesCutoff = true, CutoffAppliesTo = CutoffAppliesTo(request.CutoffAppliesTo, errors) }
            : timing with { CutoffDay = null, CutoffDaysBefore = null, UsesCutoff = false, CutoffAppliesTo = [] };
    }

    /// <summary>
    /// What a cut-off covers: everything unless said; known items by their key, the organization's
    /// own items as typed (2 to 60 characters), each once whatever its case.
    /// </summary>
    private static List<string> CutoffAppliesTo(List<string>? items, Dictionary<string, string> errors)
    {
        if (items is null)
        {
            return [.. CutoffItems];
        }

        var chosen = new List<string>();
        foreach (var raw in items)
        {
            var item = raw?.Trim() ?? "";
            item = CutoffItems.FirstOrDefault(k => string.Equals(k, item, StringComparison.OrdinalIgnoreCase)) ?? item;
            if (item.Length is < 2 or > 60)
            {
                errors["cutoffAppliesTo"] = "Name each item in 2 to 60 characters.";
                return [];
            }

            if (!chosen.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                chosen.Add(item);
            }
        }

        if (chosen.Count == 0)
        {
            errors["cutoffAppliesTo"] = "Choose at least one thing the cut-off applies to, or turn the cut-off off.";
        }
        else if (chosen.Count > 20)
        {
            errors["cutoffAppliesTo"] = "Choose up to 20 things.";
        }

        return chosen;
    }

    private static ValidTiming When(PayScheduleRequest request, Dictionary<string, string> errors)
    {
        var frequency = string.IsNullOrWhiteSpace(request.Frequency) ? "monthly" : request.Frequency.Trim();
        switch (frequency)
        {
            case "monthly":
                var rule = string.IsNullOrWhiteSpace(request.PayDayRule) ? PayDates.LastWorkingDay : request.PayDayRule.Trim();
                if (!PayDates.Rules.Contains(rule))
                {
                    errors["payDayRule"] = "Choose when people are paid from the list.";
                    rule = PayDates.LastWorkingDay;
                }

                var day = rule == PayDates.FixedDay ? request.PayDay : null;
                if (rule == PayDates.FixedDay && day is not (>= 1 and <= 28))
                {
                    errors["payDay"] = "Choose a pay day from 1 to 28.";
                }

                var cutoff = request.CutoffDay ?? 25;
                if ((request.UsesCutoff ?? true) && cutoff is < 1 or > 28)
                {
                    errors["cutoffDay"] = "Choose a cut-off day from 1 to 28.";
                }

                return new ValidTiming(frequency, rule, day, cutoff, null, null, null, null);
            case "weekly" or "fortnightly":
                var weekday = request.PayWeekday ?? 5;
                if (weekday is < 1 or > 5)
                {
                    errors["payWeekday"] = "Choose a pay day from Monday to Friday.";
                }

                var weekly = frequency == "weekly";
                var before = request.CutoffDaysBefore ?? 2;
                if ((request.UsesCutoff ?? true) && (before < 0 || before > (weekly ? 6 : 13)))
                {
                    errors["cutoffDaysBefore"] = weekly ? "Choose a cut-off from the pay day to 6 days before." : "Choose a cut-off from the pay day to 13 days before.";
                }

                var first = weekly ? null : request.FirstPayDate;
                if (!weekly && !errors.ContainsKey("payWeekday") && (first is null || IsoWeekday(first.Value) != weekday))
                {
                    errors["firstPayDate"] = "Choose the first pay date, on the pay day.";
                }

                return new ValidTiming(frequency, null, null, null, weekday, first, before, null);
            case "daily":
                return new ValidTiming(frequency, null, null, null, null, null, null, request.PayTime ?? new TimeOnly(17, 0));
            default:
                errors["frequency"] = "Choose how often from the list.";
                return new ValidTiming("monthly", PayDates.LastWorkingDay, null, 25, null, null, null, null);
        }
    }

    private static int IsoWeekday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    private static IReadOnlyList<DateOnly> NextPayDates(ValidTiming t, DateOnly today) =>
        PayDates.For(t.Frequency, t.PayDayRule, t.PayDay, t.PayWeekday, t.FirstPayDate, today);

    private static void Apply(PaySchedule schedule, ValidSchedule valid, DateTimeOffset now)
    {
        schedule.Frequency = valid.Timing.Frequency;
        schedule.PayDayRule = valid.Timing.PayDayRule;
        schedule.PayDay = valid.Timing.PayDay;
        schedule.CutoffDay = valid.Timing.CutoffDay;
        schedule.PayWeekday = valid.Timing.PayWeekday;
        schedule.FirstPayDate = valid.Timing.FirstPayDate;
        schedule.CutoffDaysBefore = valid.Timing.CutoffDaysBefore;
        schedule.PayTime = valid.Timing.PayTime;
        schedule.UsesCutoff = valid.Timing.UsesCutoff;
        schedule.CutoffAppliesTo = valid.Timing.CutoffAppliesTo ?? [];
        schedule.AllCompanies = valid.AllCompanies;
        schedule.UpdatedAt = now;
        var added = 0;
        foreach (var r in valid.Reminders)
        {
            var existing = schedule.Reminders.FirstOrDefault(e => e.Id == r.Id);
            if (existing is null)
            {
                // A microsecond apart, so reminders added together keep the order they were given in
                // (ids made in the same millisecond don't sort in order).
                var created = now.AddTicks(10 * added++);
                schedule.Reminders.Add(new PayScheduleReminder
                {
                    Id = Guid.CreateVersion7(), OrganizationId = schedule.OrganizationId, PayScheduleId = schedule.Id,
                    Amount = r.Amount, Unit = r.Unit, Channel = r.Channel, Recipient = r.Recipient, CreatedAt = created, UpdatedAt = now,
                });
            }
            else
            {
                (existing.Amount, existing.Unit, existing.Channel, existing.Recipient, existing.UpdatedAt) = (r.Amount, r.Unit, r.Channel, r.Recipient, now);
            }
        }

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
        s.Id, s.Name, s.Frequency, s.PayDayRule, s.PayDay, s.CutoffDay, s.PayWeekday, s.FirstPayDate, s.CutoffDaysBefore, s.PayTime,
        s.UsesCutoff, s.CutoffAppliesTo, s.AllCompanies, s.Companies.Select(c => c.CompanyId).ToList(), defaults.GetValueOrDefault(s.Id) ?? [],
        NextPayDates(new ValidTiming(s.Frequency, s.PayDayRule, s.PayDay, s.CutoffDay, s.PayWeekday, s.FirstPayDate, s.CutoffDaysBefore, s.PayTime), today),
        s.Reminders.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => new ReminderResponse(r.Id, r.Amount, r.Unit, r.Channel, r.Recipient)).ToList());
}
