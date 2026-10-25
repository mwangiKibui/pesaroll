using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// Adding or editing an earning, deduction or employer cost. <see cref="Code"/> is made up when
/// empty. Only what fits its kind and way of working out is kept: taxable for earnings, before tax
/// for deductions, a default amount for fixed items, a rate for a percentage of basic pay. An earning
/// paid by the hour or day (per_unit, PAY-11) has its unit and a multiplier of the person's own rate
/// (1.5: time and a half) or a default amount a unit.
/// </summary>
public sealed record PayItemRequest(
    string? Code, string? Name, string? Kind, bool Taxable, bool BeforeTax, string? CalcMethod, decimal? DefaultAmount, decimal? Rate,
    bool AllCompanies, List<Guid>? CompanyIds, Guid? PayeeId = null, string? Unit = null, decimal? Multiplier = null);

public sealed record PayItemResponse(
    Guid Id, string Code, string Name, string Kind, bool Taxable, bool BeforeTax, string CalcMethod, decimal? DefaultAmount, decimal? Rate,
    bool Locked, string? LockedKey, bool AllCompanies, IReadOnlyList<Guid> CompanyIds, IReadOnlyList<AssignmentSummary> Assignments, Guid? PayeeId,
    string? Unit = null, decimal? Multiplier = null);

/// <summary>Who gets an item at a company, briefly: everyone there, or how many chosen people.</summary>
public sealed record AssignmentSummary(Guid CompanyId, bool Everyone, int People);

/// <summary>
/// Who gets an item at one company, replacing what was there: everyone (people hired later too), or
/// the people listed (none: nobody). Amount for fixed items, rate for a percentage of basic pay
/// (empty: the item's); an amount a unit or a multiplier for items paid by the hour or day; nothing
/// for items entered each payroll. Starts today unless said.
/// </summary>
public sealed record AssignmentRequest(Guid CompanyId, bool Everyone, decimal? Amount, decimal? Rate, DateOnly? StartsOn, DateOnly? EndsOn,
    List<AssignmentPersonRequest>? People, decimal? Multiplier = null);

public sealed record AssignmentPersonRequest(Guid EmployeeId, decimal? Amount, decimal? Rate, DateOnly? StartsOn, DateOnly? EndsOn, string? Reference = null,
    decimal? Multiplier = null);

/// <summary>Who gets an item at a company; Missing: people whose profile lacks the account number it takes from there.</summary>
public sealed record CompanyAssignmentResponse(Guid CompanyId, bool Everyone, decimal? Amount, decimal? Rate, DateOnly? StartsOn, DateOnly? EndsOn,
    IReadOnlyList<AssignedPersonResponse> People, IReadOnlyList<MissingPersonResponse> Missing, decimal? Multiplier = null);

/// <summary>Reference: as typed for them; ShownReference: what applies (typed, else from their profile, masked where sensitive).</summary>
public sealed record AssignedPersonResponse(Guid EmployeeId, string Name, string StaffNo, decimal? Amount, decimal? Rate, DateOnly StartsOn, DateOnly? EndsOn,
    string? Reference, string? ShownReference, bool FromProfile, decimal? Multiplier = null);

public sealed record MissingPersonResponse(Guid EmployeeId, string Name, string StaffNo);

/// <summary>An item someone gets, with what applies to them (their own amount, rate or multiplier, else the item's).</summary>
public sealed record EmployeePayItemResponse(Guid PayItemId, string Name, string Code, string Kind, string CalcMethod, decimal? Amount, decimal? Rate,
    DateOnly StartsOn, DateOnly? EndsOn, bool Everyone, string? Reference, string? PayeeName, string? Unit = null, decimal? Multiplier = null);

/// <summary>
/// ONB-07, the Earnings and deductions step ("pay items" in code). Basic pay and the law's items
/// (statutory_items) are made with the first company, locked and for all companies; the rule pack
/// (PAY-01) works the law's items out. The organization adds its own for all or chosen companies.
/// </summary>
public static class PayItemsEndpoints
{
    public static readonly string[] Kinds = ["earning", "deduction", "employer_cost"];
    /// <summary>How an own item is worked out; by_law is only for the law's items.</summary>
    public static readonly string[] CalcMethods = ["fixed", "percent_of_basic", "each_run", "per_unit"];
    /// <summary>The rates paid by the hour every organization starts with (PAY-11); database/post-deploy/0019 adds them to existing ones.</summary>
    internal static readonly (string Code, string Name, decimal Multiplier)[] StandardRates =
        [("OVERTIME", "Overtime", 1.5m), ("REST-DAY", "Rest day and public holiday", 2m)];

    public static IEndpointRouteBuilder MapPayItemsEndpoints(this IEndpointRouteBuilder app)
    {
        var items = app.MapGroup("/api/pay-items").WithTags("Earnings and deductions");

        items.MapGet("/", async (Guid? companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var query = db.PayItems.AsNoTracking().Include(i => i.Companies).Where(i => i.Status == "active");
            if (companyId is Guid id)
            {
                query = query.Where(i => i.AllCompanies || i.Companies.Any(c => c.CompanyId == id));
            }

            var list = await query.ToListAsync(ct);
            var who = await SummariesAsync(db, list.Select(i => i.Id).ToList(), ct);
            var order = await db.StatutoryItems.AsNoTracking().ToDictionaryAsync(s => (s.CountryCode, s.Key), s => s.SortOrder, ct);
            // Basic pay, then the law's items in their order, then the organization's own by kind and age.
            return Results.Ok(list
                .OrderBy(i => i.LockedKey == "basic" ? 0 : i.LockedKey is not null ? 1 : 2)
                .ThenBy(i => i.LockedKey is null ? 0 : order.GetValueOrDefault((i.CountryCode ?? "", i.LockedKey)))
                .ThenBy(i => Array.IndexOf(Kinds, i.Kind)).ThenBy(i => i.CreatedAt).ThenBy(i => i.Code)
                .Select(i => ToResponse(i, who)).ToList());
        }).Produces<List<PayItemResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).RequirePermission(Permissions.PayItemsView);

        items.MapPost("/", async (PayItemRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.PayItems);
            var valid = await ValidateAsync(request, db, ct);

            var now = clock.GetUtcNow();
            var item = new PayItem
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = valid.Name, Kind = valid.Kind, CalcMethod = valid.CalcMethod,
                Code = valid.Code ?? await NextCodeAsync(db, LocationsEndpoints.Prefix(valid.Name), ct), CreatedAt = now,
            };
            Apply(item, valid, now);
            db.PayItems.Add(item);
            await SaveAsync(db, ct);
            return Results.Created($"/api/pay-items/{item.Id}", ToResponse(item, []));
        }).Produces<PayItemResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayItems);

        items.MapPatch("/{id:guid}", async (Guid id, PayItemRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.PayItems);
            var item = await db.PayItems.Include(i => i.Companies).SingleOrDefaultAsync(i => i.Id == id && i.Status == "active", ct)
                ?? throw OnboardingErrors.PayItemNotFound();
            if (item.LockedKey is not null)
            {
                throw OnboardingErrors.PayItemLocked();
            }

            var valid = await ValidateAsync(request, db, ct);
            item.Code = valid.Code ?? item.Code;
            item.Name = valid.Name;
            item.Kind = valid.Kind;
            var dropped = item.Companies.Where(c => !valid.CompanyIds.Contains(c.CompanyId)).ToList();
            var methodChanged = item.CalcMethod != valid.CalcMethod;
            item.CalcMethod = valid.CalcMethod;
            Apply(item, valid, clock.GetUtcNow());

            // New links before old ones go: an item is visible (row-level security) through its links.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Who gets it follows: not at companies it no longer serves; a new way of working it out drops their amounts and rates.
            if (!valid.AllCompanies)
            {
                await db.EmployeePayItems.Where(x => x.PayItemId == id && !valid.CompanyIds.Contains(x.CompanyId)).ExecuteDeleteAsync(ct);
            }

            if (methodChanged)
            {
                await db.EmployeePayItems.Where(x => x.PayItemId == id).ExecuteUpdateAsync(x => x.SetProperty(y => y.Amount, (decimal?)null).SetProperty(y => y.Rate, (decimal?)null)
                    .SetProperty(y => y.Multiplier, (decimal?)null), ct);
            }

            await SaveAsync(db, ct);
            db.PayItemCompanies.RemoveRange(dropped);
            await SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(ToResponse(await db.PayItems.AsNoTracking().Include(i => i.Companies).SingleAsync(i => i.Id == id, ct),
                await SummariesAsync(db, [id], ct)));
        }).Produces<PayItemResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayItems);

        items.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var item = await db.PayItems.SingleOrDefaultAsync(i => i.Id == id && i.Status == "active", ct)
                ?? throw OnboardingErrors.PayItemNotFound();
            Access.Require(tenant, Permissions.PayItems);
            if (item.LockedKey is not null)
            {
                throw OnboardingErrors.PayItemLocked();
            }

            db.PayItems.Remove(item);   // its company links and who gets it go with it (on delete cascade)
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayItems);

        items.MapGet("/{id:guid}/assignments", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock,
            CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            _ = await db.PayItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && i.Status == "active", ct) ?? throw OnboardingErrors.PayItemNotFound();
            return Results.Ok(await AssignmentsAsync(db, id, PayDates.TodayInKenya(clock.GetUtcNow()), ct));
        }).Produces<List<CompanyAssignmentResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).RequirePermission(Permissions.PayItemsView);

        items.MapPut("/{id:guid}/assignments", async (Guid id, AssignmentRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.PayItems);
            var item = await db.PayItems.AsNoTracking().Include(i => i.Companies).SingleOrDefaultAsync(i => i.Id == id && i.Status == "active", ct)
                ?? throw OnboardingErrors.PayItemNotFound();
            if (item.LockedKey is not null)
            {
                throw OnboardingErrors.PayItemForEveryone();
            }

            var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CompanyId, ct) ?? throw OnboardingErrors.CompanyNotFound();
            if (!item.AllCompanies && item.Companies.All(c => c.CompanyId != company.Id))
            {
                throw OnboardingErrors.PayItemNotForCompany(company.LegalName);
            }

            var now = clock.GetUtcNow();
            var today = PayDates.TodayInKenya(now);
            var errors = new Dictionary<string, string>();
            var rows = new List<EmployeePayItem>();
            EmployeePayItem Row(Guid? employee, decimal? amount, decimal? rate, decimal? multiplier, DateOnly? startsOn, DateOnly? endsOn, string at,
                string? reference = null)
            {
                reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
                if (reference?.Length > 40)
                {
                    errors[$"{at}reference"] = "Keep the member or account number to 40 characters.";
                }

                if (amount is not null && item.CalcMethod is not ("fixed" or "per_unit"))
                {
                    errors[$"{at}amount"] = item.CalcMethod == "each_run" ? $"{item.Name} is entered each payroll, so it has no amount here."
                        : $"{item.Name} is a percentage of basic pay: give a rate, not an amount.";
                }
                else if (amount < 0)
                {
                    errors[$"{at}amount"] = "Enter an amount of 0 or more.";
                }
                else if (amount is null && item.CalcMethod == "fixed" && item.DefaultAmount is null)
                {
                    errors[$"{at}amount"] = $"Enter the amount: {item.Name} has no default.";
                }

                if (rate is not null && item.CalcMethod != "percent_of_basic")
                {
                    errors[$"{at}rate"] = item.CalcMethod switch
                    {
                        "fixed" => $"{item.Name} is a fixed amount: give an amount, not a rate.",
                        "per_unit" => $"{item.Name} is paid by the {item.Unit}: give an amount a {item.Unit} or a multiplier, not a rate.",
                        _ => $"{item.Name} is entered each payroll, so it has no rate here.",
                    };
                }
                else if (rate is <= 0 or > 100)
                {
                    errors[$"{at}rate"] = "Enter a rate above 0 and up to 100.";
                }

                // Paid by the hour or day: their own amount a unit or multiplier of their own rate, not both.
                if (multiplier is not null && item.CalcMethod != "per_unit")
                {
                    errors[$"{at}multiplier"] = $"Only items paid by the hour or day have a multiplier.";
                }
                else if (multiplier is not null && amount is not null)
                {
                    errors[$"{at}multiplier"] = "Give an amount a unit or a multiplier, not both.";
                }
                else if (multiplier is not (null or (> 0 and <= 10)))
                {
                    errors[$"{at}multiplier"] = "Enter a multiplier above 0 and up to 10 (1.5 is time and a half).";
                }

                var starts = startsOn ?? today;
                if (endsOn < starts)
                {
                    errors[$"{at}endsOn"] = "The end can't be before the start.";
                }

                return new EmployeePayItem
                {
                    Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = company.Id, PayItemId = item.Id, EmployeeId = employee,
                    Amount = amount, Rate = rate, Multiplier = multiplier, StartsOn = starts, EndsOn = endsOn, Reference = reference, CreatedAt = now,
                    UpdatedAt = now,
                };
            }

            if (request.Everyone)
            {
                rows.Add(Row(null, request.Amount, request.Rate, request.Multiplier, request.StartsOn, request.EndsOn, ""));
            }
            else
            {
                var people = request.People ?? [];
                var ids = people.Select(p => p.EmployeeId).Distinct().ToList();
                var found = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
                var companyNames = await db.Companies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.LegalName, ct);
                var seen = new HashSet<Guid>();
                for (var i = 0; i < people.Count; i++)
                {
                    var p = people[i];
                    var at = $"people[{i}].";
                    if (!found.TryGetValue(p.EmployeeId, out var e))
                    {
                        errors[$"{at}employeeId"] = "We couldn't find this person. They may have been deleted.";
                    }
                    else if (!seen.Add(e.Id))
                    {
                        errors[$"{at}employeeId"] = $"{e.FirstName} {e.LastName} is on the list twice.";
                    }
                    else if (e.CompanyId != company.Id)
                    {
                        errors[$"{at}employeeId"] = $"{e.FirstName} {e.LastName} works at {companyNames.GetValueOrDefault(e.CompanyId, "another company")}.";
                    }
                    else if (e.ExitDate is DateOnly left && left < today)
                    {
                        errors[$"{at}employeeId"] = $"{e.FirstName} {e.LastName} has left {company.LegalName}.";
                    }
                    else
                    {
                        rows.Add(Row(e.Id, p.Amount, p.Rate, p.Multiplier, p.StartsOn, p.EndsOn, at, p.Reference));
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw OnboardingErrors.AssignmentInvalid(errors);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.EmployeePayItems.Where(x => x.PayItemId == item.Id && x.CompanyId == company.Id).ExecuteDeleteAsync(ct);
            db.EmployeePayItems.AddRange(rows);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { MessageText: "ONB-3545" or "ONB-3546" or "ONB-3547" } p)
            {
                // Changed meanwhile: the item became one set by law, left the company or changed how it's worked out.
                throw p.MessageText == "ONB-3546" ? OnboardingErrors.PayItemNotForCompany(company.LegalName) : OnboardingErrors.PayItemForEveryone();
            }

            await transaction.CommitAsync(ct);
            return Results.Ok(await AssignmentsAsync(db, item.Id, today, ct));
        }).Produces<List<CompanyAssignmentResponse>>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.PayItems);

        // What one person gets: their own rows and their company's "everyone" rows.
        // ACC-05: amounts and rates go out only when asked for (includePay) by someone with View on pay; who saw whose is recorded.
        app.MapGet("/api/employees/{id:guid}/pay-items", async (Guid id, bool? includePay, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var employee = await db.Employees.AsNoTracking().Include(e => e.Accounts.Where(a => a.ValidTo == null))
                .SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw OnboardingErrors.EmployeeNotFound();
            Access.Require(tenant, Permissions.PayItemsView, employee.CompanyId);
            var rows = await (
                from x in db.EmployeePayItems.AsNoTracking()
                join i in db.PayItems.AsNoTracking() on x.PayItemId equals i.Id
                where i.Status == "active" && (x.EmployeeId == id || (x.EmployeeId == null && x.CompanyId == employee.CompanyId))
                select new
                {
                    x, i,
                    Payee = db.Payees.Where(p => p.Id == i.PayeeId).Select(p => p.Name).FirstOrDefault(),
                    Source = db.Payees.Where(p => p.Id == i.PayeeId).Select(p => p.ReferenceSource).FirstOrDefault() ?? "typed",
                }).ToListAsync(ct);
            var shows = includePay == true && tenant.Can(Permissions.PayView, employee.CompanyId);
            await SensitiveViews.RecordAsync(db, tenant, http, "employee_pay", shows && rows.Count > 0 ? [employee.Id] : [], ct);
            return Results.Ok(rows
                .OrderBy(r => Array.IndexOf(Kinds, r.i.Kind)).ThenBy(r => r.i.Name)
                .Select(r => new EmployeePayItemResponse(r.i.Id, r.i.Name, r.i.Code, r.i.Kind, r.i.CalcMethod,
                    shows ? r.x.Amount ?? (r.i.CalcMethod is "fixed" or "per_unit" && r.x.Multiplier is null ? r.i.DefaultAmount : null) : null,
                    shows ? r.x.Rate ?? (r.i.CalcMethod == "percent_of_basic" ? r.i.Rate : null) : null,
                    r.x.StartsOn, r.x.EndsOn, r.x.EmployeeId is null, r.x.Reference ?? FromProfile(employee, r.Source), r.Payee, r.i.Unit,
                    shows ? r.x.Multiplier ?? (r.x.Amount is null ? r.i.Multiplier : null) : null))
                .ToList());
        }).WithTags("Earnings and deductions").Produces<List<EmployeePayItemResponse>>()
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status404NotFound).RequirePermission(Permissions.PayItemsView);

        return app;
    }

    /// <summary>Who gets each item, by company: everyone, or how many chosen people.</summary>
    private static async Task<Dictionary<Guid, List<AssignmentSummary>>> SummariesAsync(OrganizationsDbContext db, List<Guid> items, CancellationToken ct) =>
        (await db.EmployeePayItems.AsNoTracking().Where(x => items.Contains(x.PayItemId))
            .GroupBy(x => new { x.PayItemId, x.CompanyId })
            .Select(g => new { g.Key.PayItemId, g.Key.CompanyId, Everyone = g.Any(x => x.EmployeeId == null), People = g.Count(x => x.EmployeeId != null) })
            .ToListAsync(ct))
        .GroupBy(x => x.PayItemId)
        .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CompanyId).Select(x => new AssignmentSummary(x.CompanyId, x.Everyone, x.People)).ToList());

    /// <summary>
    /// A person's account number from their profile, as the item's payee identifies people (null: typed, or not in their
    /// profile). Encrypted ones (ID number, KRA PIN, bank account, M-Pesa number) only masked: the
    /// payee's file with the real numbers is PAY-09's.
    /// </summary>
    internal static string? FromProfile(Employee e, string source)
    {
        static string? Masked(string? last4) => last4 is null ? null : $"•••• {last4}";
        var account = e.Accounts.FirstOrDefault(a => a.ValidTo == null);
        return source switch
        {
            "national_id" => Masked(e.NationalIdLast4),
            "kra_pin" => Masked(e.KraPinLast4),
            "staff_no" => e.StaffNo,
            "phone" => e.Phone,
            "bank_account" => account?.Method == "bank" ? Masked(account.AccountLast4) : null,
            "mpesa_number" => account?.Method == "mpesa" ? Masked(account.MpesaLast4) : null,
            _ => null,
        };
    }

    /// <summary>
    /// Who gets an item, company by company, chosen people by name, each with the account number that
    /// applies; and, when it comes from profiles, who lacks one (everyone current at the company, or
    /// the chosen people without a typed one).
    /// </summary>
    private static async Task<List<CompanyAssignmentResponse>> AssignmentsAsync(OrganizationsDbContext db, Guid item, DateOnly today, CancellationToken ct)
    {
        // How the item's payee identifies people (an item without a payee: typed).
        var source = await db.PayItems.AsNoTracking().Where(i => i.Id == item)
            .Select(i => db.Payees.Where(p => p.Id == i.PayeeId).Select(p => p.ReferenceSource).FirstOrDefault()).SingleAsync(ct) ?? "typed";
        var rows = await db.EmployeePayItems.AsNoTracking().Where(x => x.PayItemId == item).ToListAsync(ct);
        var everyoneAt = rows.Where(x => x.EmployeeId is null).Select(x => x.CompanyId).ToList();
        var chosen = rows.Where(x => x.EmployeeId is not null).Select(x => x.EmployeeId!.Value).ToList();
        var people = await db.Employees.AsNoTracking().Include(e => e.Accounts.Where(a => a.ValidTo == null))
            .Where(e => chosen.Contains(e.Id) || (source != "typed" && everyoneAt.Contains(e.CompanyId) && (e.ExitDate == null || e.ExitDate >= today)))
            .ToDictionaryAsync(e => e.Id, ct);
        static string Name(Employee e) => $"{e.FirstName} {e.LastName}";
        MissingPersonResponse Missing(Employee e) => new(e.Id, Name(e), e.StaffNo);

        return rows.GroupBy(r => r.CompanyId).Select(g =>
        {
            var everyone = g.FirstOrDefault(r => r.EmployeeId is null);
            var listed = g.Where(r => r.EmployeeId is not null && people.ContainsKey(r.EmployeeId.Value))
                .Select(r => (Row: r, Person: people[r.EmployeeId!.Value])).OrderBy(x => Name(x.Person)).ToList();
            var missing = source == "typed" ? []
                : everyone is not null
                    ? people.Values.Where(e => e.CompanyId == g.Key && !chosen.Contains(e.Id) && FromProfile(e, source) is null).OrderBy(Name).Select(Missing).ToList()
                    : listed.Where(x => x.Row.Reference is null && FromProfile(x.Person, source) is null).Select(x => Missing(x.Person)).ToList();
            return new CompanyAssignmentResponse(g.Key, everyone is not null, everyone?.Amount, everyone?.Rate, everyone?.StartsOn, everyone?.EndsOn,
                listed.Select(x =>
                {
                    var fromProfile = x.Row.Reference is null ? FromProfile(x.Person, source) : null;
                    return new AssignedPersonResponse(x.Person.Id, Name(x.Person), x.Person.StaffNo, x.Row.Amount, x.Row.Rate, x.Row.StartsOn, x.Row.EndsOn,
                        x.Row.Reference, x.Row.Reference ?? fromProfile, fromProfile is not null, x.Row.Multiplier);
                }).ToList(),
                missing, everyone?.Multiplier);
        }).ToList();
    }

    /// <summary>
    /// Basic pay and the law's items for a new company's country, locked and for all companies,
    /// unless the organization has them already. Kept even if the step is skipped. With them come
    /// the usual rates paid by the hour (PAY-11), for all companies and the organization's to change:
    /// overtime at time and a half, rest days and public holidays at double time.
    /// </summary>
    internal static async Task<List<PayItem>> LockedForNewCompanyAsync(OrganizationsDbContext db, Guid organizationId, string countryCode,
        DateTimeOffset now, CancellationToken ct)
    {
        var have = (await db.PayItems.Where(i => i.LockedKey != null).Select(i => new { i.LockedKey, i.CountryCode }).ToListAsync(ct))
            .Select(i => (i.LockedKey, i.CountryCode)).ToHashSet();
        var made = new List<PayItem>();
        PayItem Locked(string code, string name, string kind, string key, string? country, bool taxable, bool beforeTax, string calc) => new()
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = code, Name = name, Kind = kind, Taxable = taxable, BeforeTax = beforeTax,
            CalcMethod = calc, LockedKey = key, CountryCode = country, AllCompanies = true, CreatedAt = now, UpdatedAt = now,
        };
        if (!have.Contains(("basic", null)))
        {
            made.Add(Locked("BASIC", "Basic pay", "earning", "basic", null, taxable: true, beforeTax: false, "fixed"));
        }

        foreach (var s in await db.StatutoryItems.AsNoTracking().Where(s => s.CountryCode == countryCode && s.Active).OrderBy(s => s.SortOrder).ToListAsync(ct))
        {
            if (!have.Contains((s.Key, s.CountryCode)))
            {
                made.Add(Locked(s.Code, s.NameEn, s.Kind, s.Key, s.CountryCode, taxable: false, beforeTax: s.ReducesTaxablePay, "by_law"));
            }
        }

        var codes = await db.PayItems.Select(i => i.Code).ToListAsync(ct);   // archived too: one the organization removed stays removed
        foreach (var (code, name, multiplier) in StandardRates.Where(r => !codes.Contains(r.Code)))
        {
            made.Add(new PayItem
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = code, Name = name, Kind = "earning", Taxable = true,
                CalcMethod = "per_unit", Unit = "hour", Multiplier = multiplier, AllCompanies = true, CreatedAt = now, UpdatedAt = now,
            });
        }

        return made;
    }

    private sealed record ValidItem(string? Code, string Name, string Kind, bool Taxable, bool BeforeTax, string CalcMethod,
        decimal? DefaultAmount, decimal? Rate, bool AllCompanies, List<Guid> CompanyIds, Guid? PayeeId, string? Unit, decimal? Multiplier);

    private static async Task<ValidItem> ValidateAsync(PayItemRequest request, OrganizationsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 80)
        {
            errors["name"] = "Enter its name (2 to 80 characters).";
        }

        var kind = request.Kind?.Trim() ?? "";
        if (!Kinds.Contains(kind))
        {
            errors["kind"] = "Choose earning, deduction or employer cost.";
        }

        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant();
        if (code is not null && !Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9-]{1,19}$"))
        {
            errors["code"] = "Use 2 to 20 letters, numbers or dashes, or leave it empty for us to make one.";
        }

        var calc = request.CalcMethod?.Trim() ?? "";
        if (!CalcMethods.Contains(calc))
        {
            errors["calcMethod"] = "Choose how it's worked out from the list.";
        }

        var rate = calc == "percent_of_basic" ? request.Rate : null;
        if (calc == "percent_of_basic" && rate is not (> 0 and <= 100))
        {
            errors["rate"] = "Enter a percentage from 0.01 to 100.";
        }

        var amount = calc is "fixed" or "per_unit" ? request.DefaultAmount : null;
        if (amount is < 0)
        {
            errors["defaultAmount"] = "Enter an amount of 0 or more, or leave it empty.";
        }

        // Paid by the hour or day (PAY-11): an earning, its unit, and a multiplier of the person's own rate or an amount a unit.
        var unit = calc == "per_unit" ? request.Unit?.Trim() : null;
        var multiplier = calc == "per_unit" ? request.Multiplier : null;
        if (calc == "per_unit")
        {
            if (Kinds.Contains(kind) && kind != "earning")
            {
                errors["kind"] = "Only earnings are paid by the hour or day.";
            }

            if (unit is not ("hour" or "day"))
            {
                errors["unit"] = "Choose by the hour or by the day.";
            }

            if ((multiplier is null) == (amount is null))
            {
                errors["multiplier"] = "Give a multiplier of their own rate (1.5 is time and a half) or an amount a unit, not both.";
            }
            else if (multiplier is not (null or (> 0 and <= 10)))
            {
                errors["multiplier"] = "Enter a multiplier above 0 and up to 10.";
            }
        }

        var companies = await db.Companies.AsNoTracking().Where(c => c.Status == "active").Select(c => c.Id).ToListAsync(ct);
        var chosen = (request.CompanyIds ?? []).Distinct().ToList();
        if (!request.AllCompanies && chosen.Count == 0)
        {
            errors["companyIds"] = "Choose at least one company, or all companies.";
        }
        else if (!request.AllCompanies && chosen.Any(id => !companies.Contains(id)))
        {
            errors["companyIds"] = "Choose companies from your organization.";
        }

        // Who the money goes to: deductions and employer costs only.
        if (request.PayeeId is Guid payee)
        {
            if (kind == "earning")
            {
                errors["payeeId"] = "Earnings aren't paid to anyone else.";
            }
            else if (!await db.Payees.AnyAsync(p => p.Id == payee, ct))
            {
                errors["payeeId"] = "Choose a payee from the list.";
            }
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.PayItemDetailsInvalid(errors);
        }

        return new ValidItem(code, name, kind, kind == "earning" && request.Taxable, kind == "deduction" && request.BeforeTax, calc,
            amount, rate, request.AllCompanies, request.AllCompanies ? [] : chosen, kind == "earning" ? null : request.PayeeId, unit, multiplier);
    }

    private static void Apply(PayItem item, ValidItem valid, DateTimeOffset now)
    {
        item.Taxable = valid.Taxable;
        item.BeforeTax = valid.BeforeTax;
        item.DefaultAmount = valid.DefaultAmount;
        item.PayeeId = valid.PayeeId;
        item.Rate = valid.Rate;
        item.Unit = valid.Unit;
        item.Multiplier = valid.Multiplier;
        item.AllCompanies = valid.AllCompanies;
        item.UpdatedAt = now;
        foreach (var companyId in valid.CompanyIds.Where(id => !item.Companies.Any(c => c.CompanyId == id)))
        {
            item.Companies.Add(new PayItemCompany { OrganizationId = item.OrganizationId, PayItemId = item.Id, CompanyId = companyId, CreatedAt = now });
        }
    }

    /// <summary>The first free PREFIX-01, PREFIX-02… among the organization's items.</summary>
    private static async Task<string> NextCodeAsync(OrganizationsDbContext db, string prefix, CancellationToken ct)
    {
        var taken = await db.PayItems.Where(i => i.Code.StartsWith(prefix + "-")).Select(i => i.Code).ToListAsync(ct);
        return new SpreadsheetImport.CodeMaker(taken).Next(prefix);
    }

    private static async Task SaveAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "pay_items_unique_code" })
        {
            throw OnboardingErrors.PayItemCodeTaken();
        }
    }

    private static PayItemResponse ToResponse(PayItem i, Dictionary<Guid, List<AssignmentSummary>> who) => new(
        i.Id, i.Code, i.Name, i.Kind, i.Taxable, i.BeforeTax, i.CalcMethod, i.DefaultAmount, i.Rate,
        i.LockedKey is not null, i.LockedKey, i.AllCompanies, i.Companies.Select(c => c.CompanyId).ToList(), who.GetValueOrDefault(i.Id) ?? [], i.PayeeId,
        i.Unit, i.Multiplier);
}
