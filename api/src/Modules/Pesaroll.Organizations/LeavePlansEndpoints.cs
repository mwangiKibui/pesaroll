using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>Adding or editing a leave type. <see cref="Code"/> is made up when empty.</summary>
public sealed record LeaveTypeRequest(string? Code, string? Name, string? Counts, bool? Paid, bool? NeedsProof, string? WhenUsedUp);

public sealed record LeaveTypeResponse(
    Guid Id, string Code, string Name, string Counts, bool Paid, bool NeedsProof, string WhenUsedUp, bool Locked, string? LockedKey,
    decimal? MinFullPayDays, decimal? MinHalfPayDays, int? MinWaitingMonths);

/// <summary>How a plan gives one leave type (see <see cref="LeavePlanRule"/>).</summary>
public sealed record LeaveRuleRequest(
    Guid LeaveTypeId, string? EarnMethod, decimal? Amount, string? Unit, decimal? HalfPay, bool? Prorate, decimal? CarryOverMax, int? WaitingMonths);

/// <summary>
/// Adding or editing a leave plan: its rules (every statutory leave type must be in it), the
/// companies it serves, and the companies and pay schedules it is the default of.
/// </summary>
public sealed record LeavePlanRequest(
    string? Name, string? LeaveYear, decimal? HoursPerDay, bool AllCompanies, List<Guid>? CompanyIds, List<Guid>? DefaultFor,
    List<Guid>? DefaultForSchedules, List<LeaveRuleRequest>? Rules);

public sealed record LeaveRuleResponse(
    Guid Id, Guid LeaveTypeId, string EarnMethod, decimal? Amount, string Unit, decimal HalfPay, bool Prorate, decimal CarryOverMax,
    int WaitingMonths, decimal? DaysAYear);

public sealed record LeavePlanResponse(
    Guid Id, string Name, string LeaveYear, decimal HoursPerDay, bool AllCompanies, IReadOnlyList<Guid> CompanyIds,
    IReadOnlyList<Guid> DefaultFor, IReadOnlyList<Guid> DefaultForSchedules, IReadOnlyList<LeaveRuleResponse> Rules);

/// <summary>
/// ONB-08, the Leave plans step. The law's leave types (statutory_leave) and a Standard plan at
/// the legal minimums come with the first company. Plans give leave however people are paid
/// (<see cref="LeavePlanRule"/>), never below the law (<see cref="LeaveMinimums"/>). Each person
/// follows their pay schedule's default plan, else their company's (ONB-09).
/// </summary>
public static class LeavePlansEndpoints
{
    public static readonly string[] EarnMethods = ["yearly", "monthly", "weekly", "per_day_worked", "per_hour_worked", "each_time", "unlimited"];

    public static IEndpointRouteBuilder MapLeavePlansEndpoints(this IEndpointRouteBuilder app)
    {
        var types = app.MapGroup("/api/leave-types").WithTags("Leave plans");

        types.MapGet("/", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var list = await db.LeaveTypes.AsNoTracking().Where(t => t.Status == "active").ToListAsync(ct);
            var law = await LawAsync(db, ct);
            return Results.Ok(list
                .OrderBy(t => t.LockedKey is null ? 1 : 0)
                .ThenBy(t => t.LockedKey is null ? 0 : law.GetValueOrDefault((t.CountryCode!, t.LockedKey))?.SortOrder ?? 0)
                .ThenBy(t => t.CreatedAt).ThenBy(t => t.Code)
                .Select(t => ToResponse(t, law)).ToList());
        }).Produces<List<LeaveTypeResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        types.MapPost("/", async (LeaveTypeRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var valid = ValidateType(request);
            var now = clock.GetUtcNow();
            var type = new LeaveType
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = valid.Name, CreatedAt = now,
                Code = valid.Code ?? await NextTypeCodeAsync(db, LocationsEndpoints.Prefix(valid.Name), ct),
            };
            ApplyType(type, valid, now);
            db.LeaveTypes.Add(type);
            await SaveAsync(db, ct);
            return Results.Created($"/api/leave-types/{type.Id}", ToResponse(type, await LawAsync(db, ct)));
        }).Produces<LeaveTypeResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        types.MapPatch("/{id:guid}", async (Guid id, LeaveTypeRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var type = await db.LeaveTypes.SingleOrDefaultAsync(t => t.Id == id && t.Status == "active", ct) ?? throw OnboardingErrors.LeaveNotFound();
            var valid = ValidateType(request);
            if (type.LockedKey is not null)
            {
                // The law's leave keeps its name, code and how it counts; how it's handled can change.
                (type.NeedsProof, type.WhenUsedUp, type.UpdatedAt) = (valid.NeedsProof, valid.WhenUsedUp, clock.GetUtcNow());
            }
            else
            {
                type.Name = valid.Name;
                type.Code = valid.Code ?? type.Code;
                ApplyType(type, valid, clock.GetUtcNow());
            }

            await SaveAsync(db, ct);
            return Results.Ok(ToResponse(type, await LawAsync(db, ct)));
        }).Produces<LeaveTypeResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        types.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var type = await db.LeaveTypes.SingleOrDefaultAsync(t => t.Id == id && t.Status == "active", ct) ?? throw OnboardingErrors.LeaveNotFound();
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            if (type.LockedKey is not null)
            {
                throw OnboardingErrors.LeaveTypeLocked();
            }

            var plans = await db.LeavePlans.Where(p => p.Rules.Any(r => r.LeaveTypeId == id)).Select(p => p.Name).ToListAsync(ct);
            if (plans.Count > 0)
            {
                throw OnboardingErrors.LeaveTypeInUse(string.Join(", ", plans));
            }

            db.LeaveTypes.Remove(type);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        var plans = app.MapGroup("/api/leave-plans").WithTags("Leave plans");

        plans.MapGet("/", async (Guid? companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var query = db.LeavePlans.AsNoTracking().Include(p => p.Companies).Include(p => p.Rules).Where(p => p.Status == "active");
            if (companyId is Guid id)
            {
                query = query.Where(p => p.AllCompanies || p.Companies.Any(c => c.CompanyId == id));
            }

            var list = await query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Name).ToListAsync(ct);
            var defaults = await DefaultsAsync(db, ct);
            return Results.Ok(list.Select(p => ToResponse(p, defaults)).ToList());
        }).Produces<List<LeavePlanResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        plans.MapPost("/", async (LeavePlanRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var valid = await ValidatePlanAsync(request, db, ct);
            var now = clock.GetUtcNow();
            var plan = new LeavePlan { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = valid.Name, CreatedAt = now };
            ApplyPlan(plan, valid, now);
            db.LeavePlans.Add(plan);
            await SetDefaultsAsync(db, plan, valid, ct);
            await SaveAsync(db, ct);
            return Results.Created($"/api/leave-plans/{plan.Id}", ToResponse(plan, await DefaultsAsync(db, ct)));
        }).Produces<LeavePlanResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        plans.MapPatch("/{id:guid}", async (Guid id, LeavePlanRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var plan = await db.LeavePlans.Include(p => p.Companies).Include(p => p.Rules).SingleOrDefaultAsync(p => p.Id == id && p.Status == "active", ct)
                ?? throw OnboardingErrors.LeaveNotFound();
            var valid = await ValidatePlanAsync(request, db, ct);

            // Every company keeps a default plan: this one stays its default until another takes over.
            var keeping = await db.Companies.Where(c => c.DefaultLeavePlanId == plan.Id && !valid.DefaultFor.Contains(c.Id)).Select(c => c.LegalName).ToListAsync(ct);
            if (keeping.Count > 0)
            {
                throw OnboardingErrors.DefaultLeavePlanNeeded(string.Join(", ", keeping));
            }

            plan.Name = valid.Name;
            var droppedCompanies = plan.Companies.Where(c => !valid.CompanyIds.Contains(c.CompanyId)).ToList();
            db.LeavePlanRules.RemoveRange(plan.Rules.Where(r => !valid.Rules.Any(v => v.LeaveTypeId == r.LeaveTypeId)));
            ApplyPlan(plan, valid, clock.GetUtcNow());
            await SetDefaultsAsync(db, plan, valid, ct);

            // New links before old ones go: a plan is visible (row-level security) through its links.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SaveAsync(db, ct);
            db.LeavePlanCompanies.RemoveRange(droppedCompanies);
            await SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
            var saved = await db.LeavePlans.AsNoTracking().Include(p => p.Companies).Include(p => p.Rules).SingleAsync(p => p.Id == id, ct);
            return Results.Ok(ToResponse(saved, await DefaultsAsync(db, ct)));
        }).Produces<LeavePlanResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        plans.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var plan = await db.LeavePlans.SingleOrDefaultAsync(p => p.Id == id && p.Status == "active", ct) ?? throw OnboardingErrors.LeaveNotFound();
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            await EmployeesEndpoints.RequireUnusedAsync(db, plan.Name, e => e.LeavePlanId == id, ct);
            var defaultOf = await db.Companies.Where(c => c.DefaultLeavePlanId == id).Select(c => c.LegalName).ToListAsync(ct);
            if (defaultOf.Count > 0)
            {
                throw OnboardingErrors.DefaultLeavePlanNeeded(string.Join(", ", defaultOf));
            }

            // Schedules that named it fall back to their companies' defaults.
            foreach (var schedule in await db.PaySchedules.Where(s => s.DefaultLeavePlanId == id).ToListAsync(ct))
            {
                schedule.DefaultLeavePlanId = null;
            }

            db.LeavePlans.Remove(plan);   // its rules and company links go with it (on delete cascade)
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// The law's leave types for a new company's country and Unpaid leave (unless the organization has
    /// them), and its default plan: the oldest for all companies, or else a Standard plan at the legal
    /// minimums for all companies, which the caller adds. Kept even if the step is skipped.
    /// </summary>
    internal static async Task<(List<LeaveType> Types, LeavePlan Plan, bool IsNew)> ForNewCompanyAsync(
        OrganizationsDbContext db, Guid organizationId, string countryCode, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.LeaveTypes.Where(t => t.Status == "active").ToListAsync(ct);
        var law = await db.StatutoryLeave.AsNoTracking().Where(s => s.CountryCode == countryCode && s.Active).OrderBy(s => s.SortOrder).ToListAsync(ct);
        var made = new List<LeaveType>();
        foreach (var s in law.Where(s => !existing.Any(t => t.LockedKey == s.Key && t.CountryCode == s.CountryCode)))
        {
            made.Add(new LeaveType
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = s.Code, Name = s.NameEn, Counts = s.Counts, NeedsProof = s.NeedsProof,
                LockedKey = s.Key, CountryCode = s.CountryCode, CreatedAt = now, UpdatedAt = now,
            });
        }

        if (!existing.Any(t => t.Code == "UNPAID"))
        {
            made.Add(new LeaveType { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = "UNPAID", Name = "Unpaid leave", Paid = false, CreatedAt = now, UpdatedAt = now });
        }

        var forAll = await db.LeavePlans.Where(p => p.AllCompanies && p.Status == "active").OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        if (forAll is not null)
        {
            return (made, forAll, false);
        }

        var names = (await db.LeavePlans.Where(p => p.Status == "active").Select(p => p.Name).ToListAsync(ct)).Select(n => n.Trim().ToLowerInvariant()).ToHashSet();
        var plan = new LeavePlan
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, AllCompanies = true, CreatedAt = now, UpdatedAt = now,
            Name = Enumerable.Range(1, 99).Select(n => n == 1 ? "Standard" : $"Standard {n}").First(n => !names.Contains(n.ToLowerInvariant())),
        };
        var i = 0;
        foreach (var type in existing.Concat(made).Where(t => t.LockedKey is not null || t.Code == "UNPAID"))
        {
            var s = law.FirstOrDefault(l => l.Key == type.LockedKey && l.CountryCode == type.CountryCode);
            plan.Rules.Add(new LeavePlanRule
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, LeavePlanId = plan.Id, LeaveTypeId = type.Id,
                EarnMethod = s?.EarnMethod ?? "unlimited", Amount = s?.EarnAmount, HalfPay = s?.HalfPayDays ?? 0, WaitingMonths = s?.WaitingMonths ?? 0,
                CreatedAt = now.AddTicks(10 * i++), UpdatedAt = now,
            });
        }

        return (made, plan, true);
    }

    private sealed record ValidType(string? Code, string Name, string Counts, bool Paid, bool NeedsProof, string WhenUsedUp);

    private static ValidType ValidateType(LeaveTypeRequest request)
    {
        var errors = new Dictionary<string, string>();
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 80)
        {
            errors["name"] = "Enter its name (2 to 80 characters).";
        }

        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant();
        if (code is not null && !Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9-]{1,19}$"))
        {
            errors["code"] = "Use 2 to 20 letters, numbers or dashes, or leave it empty for us to make one.";
        }

        var counts = string.IsNullOrWhiteSpace(request.Counts) ? "working_days" : request.Counts.Trim();
        if (counts is not ("working_days" or "calendar_days"))
        {
            errors["counts"] = "Choose working days or calendar days.";
        }

        var usedUp = string.IsNullOrWhiteSpace(request.WhenUsedUp) ? "stop" : request.WhenUsedUp.Trim();
        if (usedUp is not ("stop" or "unpaid"))
        {
            errors["whenUsedUp"] = "Choose what happens when it runs out.";
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.LeaveDetailsInvalid(errors);
        }

        return new ValidType(code, name, counts, request.Paid ?? true, request.NeedsProof ?? false, usedUp);
    }

    private static void ApplyType(LeaveType type, ValidType valid, DateTimeOffset now)
    {
        (type.Counts, type.Paid, type.NeedsProof, type.WhenUsedUp, type.UpdatedAt) = (valid.Counts, valid.Paid, valid.NeedsProof, valid.WhenUsedUp, now);
    }

    private sealed record ValidRule(Guid LeaveTypeId, string EarnMethod, decimal? Amount, string Unit, decimal HalfPay, bool Prorate, decimal CarryOverMax, int WaitingMonths);

    private sealed record ValidPlan(string Name, string LeaveYear, decimal HoursPerDay, bool AllCompanies, List<Guid> CompanyIds, List<Guid> DefaultFor,
        List<Guid> DefaultForSchedules, List<ValidRule> Rules);

    private static async Task<ValidPlan> ValidatePlanAsync(LeavePlanRequest request, OrganizationsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 80)
        {
            errors["name"] = "Enter the plan's name (2 to 80 characters).";
        }

        var leaveYear = string.IsNullOrWhiteSpace(request.LeaveYear) ? "calendar" : request.LeaveYear.Trim();
        if (leaveYear is not ("calendar" or "anniversary"))
        {
            errors["leaveYear"] = "Choose the calendar year or each person's work anniversary.";
        }

        var hours = request.HoursPerDay ?? 8;
        if (hours is < 1 or > 24)
        {
            errors["hoursPerDay"] = "Enter the hours in a working day, from 1 to 24.";
        }

        var companies = await db.Companies.AsNoTracking().Where(c => c.Status == "active").Select(c => c.Id).ToListAsync(ct);
        var chosen = (request.CompanyIds ?? []).Distinct().ToList();
        var serves = request.AllCompanies ? companies : companies.Where(chosen.Contains).ToList();
        if (!request.AllCompanies && chosen.Count == 0)
        {
            errors["companyIds"] = "Choose at least one company, or all companies.";
        }
        else if (!request.AllCompanies && serves.Count != chosen.Count)
        {
            errors["companyIds"] = "Choose companies from your organization.";
        }

        var defaultFor = (request.DefaultFor ?? []).Distinct().ToList();
        if (defaultFor.Any(id => !serves.Contains(id)))
        {
            errors["defaultFor"] = "A plan can only be the default of a company it serves.";
        }

        var scheduleIds = (request.DefaultForSchedules ?? []).Distinct().ToList();
        if (scheduleIds.Count > 0 && await db.PaySchedules.CountAsync(s => scheduleIds.Contains(s.Id) && s.Status == "active", ct) != scheduleIds.Count)
        {
            errors["defaultForSchedules"] = "Choose payroll schedules from your organization.";
        }

        // Every statutory leave type is in every plan, at least at the law's minimum.
        var types = await db.LeaveTypes.AsNoTracking().Where(t => t.Status == "active").ToListAsync(ct);
        var law = await LawAsync(db, ct);
        var rules = new List<ValidRule>();
        var given = request.Rules ?? [];
        for (var i = 0; i < given.Count; i++)
        {
            var r = given[i];
            var type = types.SingleOrDefault(t => t.Id == r.LeaveTypeId);
            if (type is null)
            {
                errors[$"rules[{i}].leaveTypeId"] = "Choose a leave type from your organization.";
                continue;
            }

            if (rules.Any(x => x.LeaveTypeId == type.Id))
            {
                errors[$"rules[{i}].leaveTypeId"] = $"{type.Name} is in the plan twice.";
                continue;
            }

            var earn = r.EarnMethod?.Trim() ?? "";
            if (!EarnMethods.Contains(earn))
            {
                errors[$"rules[{i}].earnMethod"] = "Choose how it's earned from the list.";
                continue;
            }

            var amount = earn == "unlimited" ? null : r.Amount;
            if (earn != "unlimited" && amount is not > 0)
            {
                errors[$"rules[{i}].amount"] = "Enter how much, more than 0.";
            }

            var unit = string.IsNullOrWhiteSpace(r.Unit) ? "days" : r.Unit.Trim();
            if (unit is not ("days" or "hours"))
            {
                errors[$"rules[{i}].unit"] = "Choose days or hours.";
            }

            var halfPay = earn is "yearly" or "each_time" ? r.HalfPay ?? 0 : 0;
            if (halfPay < 0)
            {
                errors[$"rules[{i}].halfPay"] = "Enter half-pay days of 0 or more.";
            }

            var carry = r.CarryOverMax ?? 0;
            if (carry < 0)
            {
                errors[$"rules[{i}].carryOverMax"] = "Enter 0 or more.";
            }

            var waiting = r.WaitingMonths ?? 0;
            if (waiting is < 0 or > 24)
            {
                errors[$"rules[{i}].waitingMonths"] = "Enter from 0 to 24 months.";
            }

            if (type.LockedKey is not null && law.GetValueOrDefault((type.CountryCode!, type.LockedKey)) is { } s)
            {
                var terms = new LeaveRuleTerms(earn, amount, unit, halfPay, waiting);
                foreach (var (field, message) in LeaveMinimums.Check(terms, new LeaveMinimum(s.NameEn, s.FullPayDays, s.HalfPayDays, s.WaitingMonths), hours))
                {
                    errors.TryAdd($"rules[{i}].{field}", message);
                }
            }

            rules.Add(new ValidRule(type.Id, earn, amount, unit, halfPay, r.Prorate ?? true, carry, waiting));
        }

        var missing = types.Where(t => t.LockedKey is not null && !given.Any(r => r.LeaveTypeId == t.Id)).Select(t => t.Name).ToList();
        if (missing.Count > 0)
        {
            errors["rules"] = $"Every plan includes the leave the law gives: add {string.Join(", ", missing)}.";
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.LeaveDetailsInvalid(errors);
        }

        return new ValidPlan(name, leaveYear, hours, request.AllCompanies, request.AllCompanies ? [] : chosen, defaultFor, scheduleIds, rules);
    }

    private static void ApplyPlan(LeavePlan plan, ValidPlan valid, DateTimeOffset now)
    {
        (plan.LeaveYear, plan.HoursPerDay, plan.AllCompanies, plan.UpdatedAt) = (valid.LeaveYear, valid.HoursPerDay, valid.AllCompanies, now);
        var i = 0;
        foreach (var r in valid.Rules)
        {
            var rule = plan.Rules.FirstOrDefault(x => x.LeaveTypeId == r.LeaveTypeId);
            if (rule is null)
            {
                rule = new LeavePlanRule
                {
                    Id = Guid.CreateVersion7(), OrganizationId = plan.OrganizationId, LeavePlanId = plan.Id, LeaveTypeId = r.LeaveTypeId, EarnMethod = r.EarnMethod,
                    CreatedAt = now.AddTicks(10 * i),   // keeps the order given
                };
                plan.Rules.Add(rule);
            }

            i++;
            (rule.EarnMethod, rule.Amount, rule.Unit, rule.HalfPay, rule.Prorate, rule.CarryOverMax, rule.WaitingMonths, rule.UpdatedAt) =
                (r.EarnMethod, r.Amount, r.Unit, r.HalfPay, r.Prorate, r.CarryOverMax, r.WaitingMonths, now);
        }

        foreach (var companyId in valid.CompanyIds.Where(id => !plan.Companies.Any(c => c.CompanyId == id)))
        {
            plan.Companies.Add(new LeavePlanCompany { OrganizationId = plan.OrganizationId, LeavePlanId = plan.Id, CompanyId = companyId, CreatedAt = now });
        }
    }

    // A company or schedule has one default plan, so making this one its default moves it from wherever it was.
    private static async Task SetDefaultsAsync(OrganizationsDbContext db, LeavePlan plan, ValidPlan valid, CancellationToken ct)
    {
        foreach (var company in await db.Companies.Where(c => valid.DefaultFor.Contains(c.Id)).ToListAsync(ct))
        {
            company.DefaultLeavePlanId = plan.Id;
        }

        foreach (var schedule in await db.PaySchedules.Where(s => s.DefaultLeavePlanId == plan.Id || valid.DefaultForSchedules.Contains(s.Id)).ToListAsync(ct))
        {
            schedule.DefaultLeavePlanId = valid.DefaultForSchedules.Contains(schedule.Id) ? plan.Id : null;
        }
    }

    private static async Task<Dictionary<(string, string), StatutoryLeave>> LawAsync(OrganizationsDbContext db, CancellationToken ct) =>
        await db.StatutoryLeave.AsNoTracking().ToDictionaryAsync(s => (s.CountryCode, s.Key), ct);

    private sealed record Defaults(Dictionary<Guid, List<Guid>> Companies, Dictionary<Guid, List<Guid>> Schedules);

    private static async Task<Defaults> DefaultsAsync(OrganizationsDbContext db, CancellationToken ct) => new(
        (await db.Companies.AsNoTracking().Where(c => c.DefaultLeavePlanId != null && c.Status == "active").Select(c => new { c.Id, Plan = c.DefaultLeavePlanId!.Value }).ToListAsync(ct))
            .GroupBy(c => c.Plan).ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList()),
        (await db.PaySchedules.AsNoTracking().Where(s => s.DefaultLeavePlanId != null && s.Status == "active").Select(s => new { s.Id, Plan = s.DefaultLeavePlanId!.Value }).ToListAsync(ct))
            .GroupBy(s => s.Plan).ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList()));

    private static async Task<string> NextTypeCodeAsync(OrganizationsDbContext db, string prefix, CancellationToken ct) =>
        new SpreadsheetImport.CodeMaker(await db.LeaveTypes.Where(t => t.Code.StartsWith(prefix + "-")).Select(t => t.Code).ToListAsync(ct)).Next(prefix);

    private static async Task SaveAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } p)
        {
            throw OnboardingErrors.LeaveNameTaken(p.ConstraintName == "leave_plans_unique_name" ? "a leave plan with this name" : "a leave type with this code");
        }
    }

    private static LeaveTypeResponse ToResponse(LeaveType t, Dictionary<(string, string), StatutoryLeave> law)
    {
        var s = t.LockedKey is null ? null : law.GetValueOrDefault((t.CountryCode!, t.LockedKey));
        return new(t.Id, t.Code, t.Name, t.Counts, t.Paid, t.NeedsProof, t.WhenUsedUp, t.LockedKey is not null, t.LockedKey,
            s?.FullPayDays, s?.HalfPayDays, s?.WaitingMonths);
    }

    private static LeavePlanResponse ToResponse(LeavePlan p, Defaults defaults) => new(
        p.Id, p.Name, p.LeaveYear, p.HoursPerDay, p.AllCompanies, p.Companies.Select(c => c.CompanyId).ToList(),
        defaults.Companies.GetValueOrDefault(p.Id) ?? [], defaults.Schedules.GetValueOrDefault(p.Id) ?? [],
        p.Rules.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => new LeaveRuleResponse(
            r.Id, r.LeaveTypeId, r.EarnMethod, r.Amount, r.Unit, r.HalfPay, r.Prorate, r.CarryOverMax, r.WaitingMonths,
            LeaveMinimums.DaysAYear(r.EarnMethod, r.Amount, r.Unit, p.HoursPerDay))).ToList());
}
