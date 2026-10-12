using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Identity;
using Pesaroll.Notifications;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Security;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>An employee's job. Empty schedule, leave plan and location take the company's (and schedule's) defaults.</summary>
public sealed record JobRequest(
    Guid? PositionId, Guid? DepartmentId, Guid? UnitId, Guid? LocationId, Guid? LineManagerId, Guid? PayScheduleId, Guid? LeavePlanId,
    string? EmploymentType, DateOnly? ContractEnd);

public sealed record PayRequest(decimal? BasicAmount, string? PayBasis);

/// <summary>Where pay goes. On an edit, leaving the number out keeps the current one (same method).</summary>
public sealed record AccountRequest(string? Method, string? BankCode, string? BranchCode, string? AccountNumber, string? MpesaPhone);

/// <summary>
/// Adding or editing an employee. On an edit, an ID number or KRA PIN left out (null) is kept and
/// an empty one is removed; pay and account left out are kept.
/// </summary>
public sealed record EmployeeRequest(
    Guid CompanyId, string? StaffNo, string? FirstName, string? MiddleName, string? LastName, DateOnly? DateOfBirth, string? Phone, string? Email,
    string? NationalId, string? KraPin, string? NssfNo, string? ShaNo, DateOnly? HireDate, DateOnly? ExitDate,
    JobRequest? Job, PayRequest? Pay, AccountRequest? Account, string? Address = null);

public sealed record JobResponse(
    Guid? PositionId, Guid? DepartmentId, Guid? UnitId, Guid? LocationId, Guid? LineManagerId, Guid PayScheduleId, Guid LeavePlanId,
    string EmploymentType, DateOnly? ContractEnd);

public sealed record PayResponse(decimal BasicAmount, string PayBasis, string Currency);

public sealed record AccountResponse(string Method, string? BankCode, string? BranchCode, string? AccountMasked, string? MpesaMasked);

public sealed record EmployeeResponse(
    Guid Id, Guid CompanyId, string StaffNo, string FirstName, string? MiddleName, string LastName, DateOnly? DateOfBirth, string? Phone, string? Email,
    string? Address, string? NationalIdMasked, string? KraPinMasked, string? NssfNo, string? ShaNo, DateOnly HireDate, DateOnly? ExitDate,
    JobResponse Job, PayResponse? Pay, AccountResponse? Account, ReadinessResponse Readiness);

public sealed record EmployeeRow(
    Guid Id, Guid CompanyId, string StaffNo, string Name, string? PositionTitle, string? DepartmentName, Guid? PayScheduleId,
    decimal? BasicAmount, string? PayBasis, string? Currency, DateOnly? ExitDate, ReadinessResponse Readiness, DateOnly HireDate);

/// <summary>Someone to pick (as a line manager): name, staff number and department.</summary>
public sealed record EmployeeChoice(Guid Id, string Name, string StaffNo, Guid? DepartmentId);

public sealed record EmployeePage(IReadOnlyList<EmployeeRow> Items, int Total, int Page, int PageSize);

/// <summary>
/// ONB-09, the Employees step: people with their statutory numbers (encrypted, masked, no
/// duplicates among current staff), job (defaults from the company and its schedule), basic pay
/// (monthly, daily or hourly) and where pay goes, and what each still needs before payroll
/// (PAY-03 enforces it). Lists are searched and paged on the server.
/// </summary>
public static class EmployeesEndpoints
{
    public static readonly string[] EmploymentTypes = ["permanent", "contract", "casual", "intern"];
    /// <summary>How basic pay is given; it follows the payroll schedule's frequency unless said.</summary>
    public static readonly string[] PayBases = ["monthly", "weekly", "fortnightly", "daily", "hourly"];
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapEmployeesEndpoints(this IEndpointRouteBuilder app)
    {
        var employees = app.MapGroup("/api/employees").WithTags("Employees");

        employees.MapGet("/", async (Guid? companyId, Guid[]? companyIds, string? search, string? status, int? page, int? pageSize,
            string? sort, string? dir, Guid[]? departmentIds, Guid[]? positionIds, Guid[]? scheduleIds, decimal? payFrom, decimal? payTo,
            DateOnly? hiredFrom, DateOnly? hiredTo, OrganizationsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock,
            FieldEncryptor encryptor, CancellationToken ct) =>
        {
            var (_, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var today = PayDates.TodayInKenya(clock.GetUtcNow());
            var query = db.Employees.AsNoTracking();
            if (companyId is Guid id)
            {
                query = query.Where(e => e.CompanyId == id);
            }

            // The company selector's choice (several companies; none means all).
            if (companyIds is { Length: > 0 } ids)
            {
                query = query.Where(e => ids.Contains(e.CompanyId));
            }

            query = status switch
            {
                "former" => query.Where(e => e.ExitDate != null && e.ExitDate < today),
                "all" => query,
                _ => query.Where(e => e.ExitDate == null || e.ExitDate >= today),
            };
            // Any of a person's identifiers: name, staff number, phone (any way it's written), email, NSSF or
            // SHA number (part of them), or a whole ID number or KRA PIN (stored one way: matched by lookup).
            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();
                var term = $"%{text.Replace("%", "").Replace("_", "")}%";
                var digits = Regex.Replace(text, @"[\s\-().]", "");
                var phone = Regex.IsMatch(digits, @"^\+?\d{4,}$")
                    ? $"%{(digits.StartsWith('0') ? "+254" + digits[1..] : digits.StartsWith("254") ? "+" + digits : digits)}%"
                    : null;
                var idLookup = EmployeeNumbers.NationalId(text) is { } nationalId ? encryptor.Lookup(nationalId, $"national_id:{organizationId}") : null;
                var pinLookup = EmployeeNumbers.KraPin(text) is { } pin ? encryptor.Lookup(pin, $"kra_pin:{organizationId}") : null;
                query = query.Where(e => EF.Functions.ILike(e.FirstName + " " + e.LastName, term) || EF.Functions.ILike(e.LastName + " " + e.FirstName, term)
                    || EF.Functions.ILike(e.StaffNo, term) || (e.Email != null && EF.Functions.ILike(e.Email, term))
                    || (phone != null && e.Phone != null && EF.Functions.ILike(e.Phone, phone))
                    || (e.NssfNo != null && EF.Functions.ILike(e.NssfNo, term)) || (e.ShaNo != null && EF.Functions.ILike(e.ShaNo, term))
                    || (idLookup != null && e.NationalIdLookup == idLookup) || (pinLookup != null && e.KraPinLookup == pinLookup));
            }

            if (hiredFrom is DateOnly from)
            {
                query = query.Where(e => e.HireDate >= from);
            }

            if (hiredTo is DateOnly to)
            {
                query = query.Where(e => e.HireDate <= to);
            }

            // Each person with their current job and pay, for the column filters and sorts (across every page).
            var shaped = query.Select(e => new
            {
                Employee = e,
                Job = e.Employments.Where(x => x.ValidTo == null).Select(x => new { x.PositionId, x.DepartmentId, x.PayScheduleId }).FirstOrDefault(),
                Pay = e.Compensations.Where(x => x.ValidTo == null).Select(x => (decimal?)x.BasicAmount).FirstOrDefault(),
            });
            if (departmentIds is { Length: > 0 } inDepartments)
            {
                shaped = shaped.Where(x => x.Job != null && x.Job.DepartmentId != null && inDepartments.Contains(x.Job.DepartmentId.Value));
            }

            if (positionIds is { Length: > 0 } inPositions)
            {
                shaped = shaped.Where(x => x.Job != null && x.Job.PositionId != null && inPositions.Contains(x.Job.PositionId.Value));
            }

            if (scheduleIds is { Length: > 0 } onSchedules)
            {
                shaped = shaped.Where(x => x.Job != null && onSchedules.Contains(x.Job.PayScheduleId));
            }

            if (payFrom is decimal least)
            {
                shaped = shaped.Where(x => x.Pay != null && x.Pay >= least);
            }

            if (payTo is decimal most)
            {
                shaped = shaped.Where(x => x.Pay != null && x.Pay <= most);
            }

            // Sorted by what the column shows (company, name, job, schedule, pay, start date), either way; empty cells last; ties by name, so pages stay put.
            var keyed = shaped.Select(x => new
            {
                x.Employee,
                Job = db.Positions.Where(p => x.Job != null && p.Id == x.Job.PositionId).Select(p => p.Name).FirstOrDefault()
                    + " " + db.Departments.Where(d => x.Job != null && d.Id == x.Job.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                HasJob = x.Job != null && (x.Job.PositionId != null || x.Job.DepartmentId != null),
                Schedule = db.PaySchedules.Where(p => x.Job != null && p.Id == x.Job.PayScheduleId).Select(p => p.Name).FirstOrDefault(),
                Company = db.Companies.Where(c => c.Id == x.Employee.CompanyId).Select(c => c.LegalName).FirstOrDefault(),
                x.Pay,
            });
            var down = dir == "desc";
            var ordered = sort switch
            {
                "company" => down ? keyed.OrderByDescending(x => x.Company) : keyed.OrderBy(x => x.Company),
                "staffNo" => down ? keyed.OrderByDescending(x => x.Employee.StaffNo) : keyed.OrderBy(x => x.Employee.StaffNo),
                "job" => down ? keyed.OrderBy(x => !x.HasJob).ThenByDescending(x => x.Job) : keyed.OrderBy(x => !x.HasJob).ThenBy(x => x.Job),
                "schedule" => down ? keyed.OrderBy(x => x.Schedule == null).ThenByDescending(x => x.Schedule) : keyed.OrderBy(x => x.Schedule == null).ThenBy(x => x.Schedule),
                "basicPay" => down ? keyed.OrderBy(x => x.Pay == null).ThenByDescending(x => x.Pay) : keyed.OrderBy(x => x.Pay == null).ThenBy(x => x.Pay),
                "hireDate" => down ? keyed.OrderByDescending(x => x.Employee.HireDate) : keyed.OrderBy(x => x.Employee.HireDate),
                _ => down ? keyed.OrderByDescending(x => x.Employee.FirstName).ThenByDescending(x => x.Employee.LastName)
                    : keyed.OrderBy(x => x.Employee.FirstName).ThenBy(x => x.Employee.LastName),
            };
            ordered = ordered.ThenBy(x => x.Employee.FirstName).ThenBy(x => x.Employee.LastName).ThenBy(x => x.Employee.Id);

            var size = Math.Clamp(pageSize ?? MaxPageSize, 1, MaxPageSize);
            var number = Math.Max(page ?? 1, 1);
            var total = await keyed.CountAsync(ct);
            var pageIds = await ordered.Skip((number - 1) * size).Take(size).Select(x => x.Employee.Id).ToListAsync(ct);
            var loaded = await db.Employees.AsNoTracking().Where(e => pageIds.Contains(e.Id))
                .Include(e => e.Employments.Where(x => x.ValidTo == null))
                .Include(e => e.Compensations.Where(x => x.ValidTo == null))
                .Include(e => e.Accounts.Where(x => x.ValidTo == null))
                .ToListAsync(ct);
            var list = pageIds.Select(id => loaded.Single(e => e.Id == id)).ToList();
            var shownPositions = list.SelectMany(e => e.Employments).Select(x => x.PositionId).OfType<Guid>().ToList();
            var shownDepartments = list.SelectMany(e => e.Employments).Select(x => x.DepartmentId).OfType<Guid>().ToList();
            var positions = await db.Positions.AsNoTracking().Where(p => shownPositions.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var departments = await db.Departments.AsNoTracking().Where(d => shownDepartments.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
            return Results.Ok(new EmployeePage(list.Select(e =>
            {
                var job = e.Employments.FirstOrDefault();
                var pay = e.Compensations.FirstOrDefault();
                return new EmployeeRow(e.Id, e.CompanyId, e.StaffNo, $"{e.FirstName} {e.LastName}",
                    job?.PositionId is Guid p ? positions.GetValueOrDefault(p) : null, job?.DepartmentId is Guid d ? departments.GetValueOrDefault(d) : null,
                    job?.PayScheduleId, pay?.BasicAmount, pay?.PayBasis, pay?.Currency, e.ExitDate, Readiness(e), e.HireDate);
            }).ToList(), total, number, size));
        }).Produces<EmployeePage>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        // Everyone current in a company, briefly, for picking a line manager (however many there are).
        employees.MapGet("/choices", async (Guid companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var today = PayDates.TodayInKenya(clock.GetUtcNow());
            return Results.Ok(await db.Employees.AsNoTracking()
                .Where(e => e.CompanyId == companyId && (e.ExitDate == null || e.ExitDate >= today))
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                .Select(e => new EmployeeChoice(e.Id, e.FirstName + " " + e.LastName, e.StaffNo,
                    e.Employments.Where(x => x.ValidTo == null).Select(x => x.DepartmentId).FirstOrDefault()))
                .ToListAsync(ct));
        }).Produces<List<EmployeeChoice>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        employees.MapGet("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            return Results.Ok(ToResponse(await LoadAsync(db, id, ct, tracked: false)));
        }).Produces<EmployeeResponse>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        employees.MapPost("/", async (EmployeeRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            FieldEncryptor encryptor, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var valid = await ValidateAsync(request, null, db, ct);
            var now = clock.GetUtcNow();
            var employee = new Employee
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = valid.Company.Id, FirstName = valid.FirstName, LastName = valid.LastName,
                StaffNo = valid.StaffNo ?? await NextStaffNoAsync(db, valid.Company.Id, ct), CreatedAt = now,
            };
            var added = Apply(employee, valid, encryptor, now, today: PayDates.TodayInKenya(now));
            employee.Employments.AddRange(added.OfType<Employment>());
            employee.Compensations.AddRange(added.OfType<Compensation>());
            employee.Accounts.AddRange(added.OfType<EmployeePaymentAccount>());
            await RequireUniqueNumbersAsync(db, employee, ct);
            db.Employees.Add(employee);
            await SaveAsync(db, ct);
            return Results.Created($"/api/employees/{employee.Id}", ToResponse(employee));
        }).Produces<EmployeeResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        employees.MapPatch("/{id:guid}", async (Guid id, EmployeeRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            FieldEncryptor encryptor, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var employee = await LoadAsync(db, id, ct, tracked: true);
            var valid = await ValidateAsync(request with { CompanyId = employee.CompanyId }, employee, db, ct);
            employee.FirstName = valid.FirstName;
            employee.LastName = valid.LastName;
            employee.StaffNo = valid.StaffNo ?? employee.StaffNo;
            var now = clock.GetUtcNow();
            var added = Apply(employee, valid, encryptor, now, today: PayDates.TodayInKenya(now));
            await RequireUniqueNumbersAsync(db, employee, ct);
            // Old rows are closed before new ones go in: one current job, pay and account at a time.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SaveAsync(db, ct);
            db.AddRange(added);
            await SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(ToResponse(await LoadAsync(db, id, ct, tracked: false)));
        }).Produces<EmployeeResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        employees.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw OnboardingErrors.EmployeeNotFound();
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            // People who reported to them lose their line manager (on delete set null); their job, pay
            // and accounts go with them. Once payroll has run for someone (PAY-03), they're ended, not deleted.
            db.Employees.Remove(employee);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Refuses to delete something employees' jobs use (a department, location, schedule, leave plan).</summary>
    internal static async Task RequireUnusedAsync(OrganizationsDbContext db, string name, Expression<Func<Employment, bool>> uses, CancellationToken ct)
    {
        var count = await db.Employments.Where(uses).Select(e => e.EmployeeId).Distinct().CountAsync(ct);
        if (count > 0)
        {
            throw OnboardingErrors.UsedByEmployees(name, count);
        }
    }

    internal sealed record ValidJob(Guid? PositionId, Guid? DepartmentId, Guid? UnitId, Guid? LocationId, Guid? LineManagerId, Guid PayScheduleId,
        Guid LeavePlanId, string EmploymentType, DateOnly? ContractEnd);

    internal sealed record ValidAccount(string Method, string? BankCode, string? BranchCode, string? AccountNumber, string? MpesaPhone);

    /// <summary>A checked employee: numbers as stored (null kept, "" removed on an edit), job with defaults filled in.</summary>
    internal sealed record ValidEmployee(Company Company, string? StaffNo, string FirstName, string? MiddleName, string LastName, DateOnly? DateOfBirth,
        string? Phone, string? Email, string? Address, string? NationalId, string? KraPin, string? NssfNo, string? ShaNo, DateOnly HireDate, DateOnly? ExitDate,
        ValidJob Job, (decimal Amount, string Basis)? Pay, ValidAccount? Account);

    internal static async Task<ValidEmployee> ValidateAsync(EmployeeRequest request, Employee? existing, OrganizationsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CompanyId && c.Status == "active", ct);
        if (company is null)
        {
            throw OnboardingErrors.EmployeeDetailsInvalid(new Dictionary<string, string> { ["companyId"] = "Choose a company from your organization." });
        }

        string Name(string? value, string field, string label, bool required)
        {
            var v = value?.Trim() ?? "";
            if ((required && v.Length == 0) || v.Length > 60)
            {
                errors[field] = $"Enter the {label} (up to 60 characters).";
            }

            return v;
        }

        var first = Name(request.FirstName, "firstName", "first name", required: true);
        var middle = Name(request.MiddleName, "middleName", "middle name", required: false);
        var last = Name(request.LastName, "lastName", "last name", required: true);

        var staffNo = string.IsNullOrWhiteSpace(request.StaffNo) ? null : request.StaffNo.Trim().ToUpperInvariant();
        if (staffNo is not null && !Regex.IsMatch(staffNo, "^[A-Z0-9][A-Z0-9/-]{0,19}$"))
        {
            errors["staffNo"] = "Use up to 20 letters, numbers, dashes or slashes, or leave it empty for us to make one.";
        }

        string? Optional(string? typed, Func<string?, string?> check, string field, string message)
        {
            if (typed is null)
            {
                return null;   // kept on an edit
            }

            if (typed.Trim().Length == 0)
            {
                return "";     // removed on an edit
            }

            var stored = check(typed);
            if (stored is null)
            {
                errors[field] = message;
            }

            return stored;
        }

        var phone = Optional(request.Phone, Recipients.KenyanMobile, "phone", "Enter a Kenyan mobile number, like 0712 345 678.");
        var email = Optional(request.Email, Recipients.Email, "email", "Enter a valid email address.");
        var address = request.Address?.Trim();
        if (address?.Length > 200)
        {
            errors["address"] = "Keep the address to 200 characters.";
        }

        var nationalId = Optional(request.NationalId, EmployeeNumbers.NationalId, "nationalId", "Enter the ID number: 7 or 8 digits, or 9 for a Maisha Namba.");
        var kraPin = Optional(request.KraPin, EmployeeNumbers.KraPin, "kraPin", "Enter the person's KRA PIN: A, 9 digits and a letter, like A012345678Z.");
        var nssf = Optional(request.NssfNo, EmployeeNumbers.Nssf, "nssfNo", "Enter the NSSF number: 7 to 10 digits.");
        var sha = Optional(request.ShaNo, EmployeeNumbers.Sha, "shaNo", "Enter the SHA number: 6 to 20 letters, numbers or dashes.");

        if (request.HireDate is not DateOnly hire)
        {
            errors["hireDate"] = "Enter the date they started.";
            hire = DateOnly.MinValue;
        }

        if (request.ExitDate is DateOnly exit && exit < hire)
        {
            errors["exitDate"] = "The date they left can't be before they started.";
        }

        if (request.DateOfBirth is DateOnly born && request.HireDate is DateOnly started && born.AddYears(13) > started)
        {
            errors["dateOfBirth"] = "In Kenya, nobody under 13 can be employed.";
        }

        var job = await JobAsync(request.Job, company, existing, db, errors, ct);
        (decimal, string)? pay = null;
        if (request.Pay is { } p)
        {
            if (p.BasicAmount is not > 0)
            {
                errors["pay.basicAmount"] = "Enter the basic pay, more than 0.";
            }

            // Paid as the schedule pays (a monthly salary on a monthly schedule) unless said.
            var basis = p.PayBasis ?? await db.PaySchedules.Where(s => s.Id == job.PayScheduleId).Select(s => s.Frequency).FirstOrDefaultAsync(ct) ?? "monthly";
            if (!PayBases.Contains(basis))
            {
                errors["pay.payBasis"] = "Choose monthly, weekly, every two weeks, daily or hourly.";
            }

            pay = (p.BasicAmount ?? 0, basis);
        }

        var account = await AccountAsync(request.Account, company, existing, db, errors, ct);
        // Current staff are paid into a bank or M-Pesa, one the company pays from (once it has a paying account).
        if (request.ExitDate is null && await PayMethodsAsync(db, company.Id, ct) is { Count: > 0 } methods && !errors.ContainsKey("account.method"))
        {
            var method = account?.Method ?? existing?.Accounts.FirstOrDefault(a => a.ValidTo == null)?.Method;
            if (method is null || !methods.Contains(method))
            {
                errors["account.method"] = PaysByMessage(company.LegalName, methods);
            }
        }
        if (errors.Count > 0)
        {
            throw OnboardingErrors.EmployeeDetailsInvalid(errors);
        }

        return new ValidEmployee(company, staffNo, first, middle.Length == 0 ? null : middle, last, request.DateOfBirth, phone, email, address, nationalId, kraPin,
            nssf, sha, hire, request.ExitDate, job, pay, account);
    }

    /// <summary>The job, its references checked against what the company uses, defaults filled in.</summary>
    private static async Task<ValidJob> JobAsync(JobRequest? request, Company company, Employee? existing, OrganizationsDbContext db,
        Dictionary<string, string> errors, CancellationToken ct)
    {
        var r = request ?? new JobRequest(null, null, null, null, null, null, null, null, null);
        var type = string.IsNullOrWhiteSpace(r.EmploymentType) ? "permanent" : r.EmploymentType.Trim();
        if (!EmploymentTypes.Contains(type))
        {
            errors["job.employmentType"] = "Choose permanent, contract, casual or intern.";
        }

        var id = company.Id;
        if (r.PositionId is Guid position && !await db.Positions.AnyAsync(x => x.Id == position && x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == id)), ct))
        {
            errors["job.positionId"] = "Choose a position this company uses.";
        }

        if (r.DepartmentId is Guid department && !await db.Departments.AnyAsync(x => x.Id == department && x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == id)), ct))
        {
            errors["job.departmentId"] = "Choose a department this company uses.";
        }

        if (r.UnitId is Guid unit && !await db.Units.AnyAsync(x => x.Id == unit && x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == id)), ct))
        {
            errors["job.unitId"] = "Choose a unit this company uses.";
        }

        var location = r.LocationId ?? company.MainLocationId;
        if (r.LocationId is Guid l && !await db.Locations.AnyAsync(x => x.Id == l && x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == id)), ct))
        {
            errors["job.locationId"] = "Choose a location this company uses.";
        }

        if (r.LineManagerId is Guid manager && (manager == existing?.Id || !await db.Employees.AnyAsync(e => e.Id == manager, ct)))
        {
            errors["job.lineManagerId"] = "Choose another employee in your organization.";
        }

        var scheduleId = r.PayScheduleId ?? company.DefaultPayScheduleId ?? Guid.Empty;
        var schedule = await db.PaySchedules.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == scheduleId && s.Status == "active" && (s.AllCompanies || s.Companies.Any(c => c.CompanyId == id)), ct);
        if (schedule is null)
        {
            errors["job.payScheduleId"] = "Choose a payroll schedule this company uses.";
        }

        // The schedule's leave plan comes first (daily-paid, shift), then the company's.
        var planId = r.LeavePlanId ?? schedule?.DefaultLeavePlanId ?? company.DefaultLeavePlanId ?? Guid.Empty;
        if (!await db.LeavePlans.AnyAsync(p => p.Id == planId && p.Status == "active" && (p.AllCompanies || p.Companies.Any(c => c.CompanyId == id)), ct))
        {
            errors["job.leavePlanId"] = "Choose a leave plan this company uses.";
        }

        if (r.ContractEnd is not null && type != "contract")
        {
            errors["job.contractEnd"] = "Only fixed-term contracts have an end date.";
        }

        return new ValidJob(r.PositionId, r.DepartmentId, r.UnitId, location, r.LineManagerId, scheduleId, planId, type, r.ContractEnd);
    }

    private static async Task<ValidAccount?> AccountAsync(AccountRequest? request, Company company, Employee? existing, OrganizationsDbContext db,
        Dictionary<string, string> errors, CancellationToken ct)
    {
        if (request is null)
        {
            return null;   // kept on an edit
        }

        var current = existing?.Accounts.FirstOrDefault(a => a.ValidTo == null);
        switch (request.Method)
        {
            case "bank":
                var bank = await db.Banks.AsNoTracking().SingleOrDefaultAsync(b => b.CountryCode == company.CountryCode && b.Code == request.BankCode, ct);
                if (bank is null)
                {
                    errors["account.bankCode"] = "Choose the bank from the list.";
                }
                else if (string.IsNullOrWhiteSpace(request.BranchCode)
                    || !await db.BankBranches.AnyAsync(b => b.CountryCode == company.CountryCode && b.BankCode == bank.Code && b.Code == request.BranchCode, ct))
                {
                    errors["account.branchCode"] = $"Choose the {bank.Name} branch from the list.";
                }

                var number = request.AccountNumber is null ? null : Regex.Replace(request.AccountNumber, @"[\s-]", "");
                if (number is null && current?.Method != "bank")
                {
                    errors["account.accountNumber"] = "Enter the account number.";
                }
                else if (number is not null && bank is not null && AccountNumberProblem(bank, number) is { } problem)
                {
                    errors["account.accountNumber"] = problem;
                }

                return new ValidAccount("bank", request.BankCode, request.BranchCode, number, null);
            case "mpesa":
                var phone = request.MpesaPhone is null ? null : EmployeeNumbers.Mpesa(request.MpesaPhone);
                if ((request.MpesaPhone is null && current?.Method != "mpesa") || (request.MpesaPhone is not null && phone is null))
                {
                    errors["account.mpesaPhone"] = MpesaMessage;
                }

                return new ValidAccount("mpesa", null, null, null, phone);
            default:
                errors["account.method"] = "Choose bank or M-Pesa.";
                return null;
        }
    }

    /// <summary>bank or mpesa: how the company pays (its default open paying account), or null with none.</summary>
    /// <summary>How the company pays people: the methods of its open paying accounts ("bank", "mpesa").</summary>
    internal static async Task<List<string>> PayMethodsAsync(OrganizationsDbContext db, Guid companyId, CancellationToken ct) =>
        await db.CompanyPaymentAccounts.Where(a => a.CompanyId == companyId && a.ClosedAt == null).Select(a => a.Method).Distinct().ToListAsync(ct);

    internal static string PaysByMessage(string company, IReadOnlyCollection<string> methods) =>
        methods.Count > 1 ? "Add their bank details or M-Pesa number."
        : methods.Contains("bank") ? $"{company} pays by bank: add their bank details." : $"{company} pays by M-Pesa: add their M-Pesa number.";

    /// <summary>"Equity Bank account numbers are 13 digits." or "… are 10 to 16 digits.", when the number doesn't fit.</summary>
    internal static string? AccountNumberProblem(Bank bank, string number) =>
        number.All(char.IsAsciiDigit) && number.Length >= bank.AccountDigitsMin && number.Length <= bank.AccountDigitsMax ? null
        : bank.AccountDigitsMin == bank.AccountDigitsMax ? $"{bank.Name} account numbers are {bank.AccountDigitsMin} digits."
        : $"{bank.Name} account numbers are {bank.AccountDigitsMin} to {bank.AccountDigitsMax} digits.";

    public const string MpesaMessage = "Enter a Safaricom number for M-Pesa, like 0712 345 678.";

    /// <summary>
    /// Applies a checked employee. A job, pay or account change closes the current row (or drops
    /// one that hasn't started) and returns the new one, to add once the old is saved.
    /// </summary>
    internal static List<object> Apply(Employee e, ValidEmployee v, FieldEncryptor encryptor, DateTimeOffset now, DateOnly today)
    {
        var added = new List<object>();
        e.MiddleName = v.MiddleName;
        e.DateOfBirth = v.DateOfBirth;
        e.Phone = v.Phone is null ? e.Phone : v.Phone.Length == 0 ? null : v.Phone;
        e.Email = v.Email is null ? e.Email : v.Email.Length == 0 ? null : v.Email;
        e.Address = v.Address is null ? e.Address : v.Address.Length == 0 ? null : v.Address;
        e.NssfNo = v.NssfNo is null ? e.NssfNo : v.NssfNo.Length == 0 ? null : v.NssfNo;
        e.ShaNo = v.ShaNo is null ? e.ShaNo : v.ShaNo.Length == 0 ? null : v.ShaNo;
        if (v.NationalId is not null)
        {
            (e.NationalIdEnc, e.NationalIdLookup, e.NationalIdLast4) = v.NationalId.Length == 0 ? (null, null, null)
                : (encryptor.Encrypt(v.NationalId, $"employee:{e.Id}:national_id"), encryptor.Lookup(v.NationalId, $"national_id:{e.OrganizationId}"), v.NationalId[^4..]);
        }

        if (v.KraPin is not null)
        {
            (e.KraPinEnc, e.KraPinLookup, e.KraPinLast4) = v.KraPin.Length == 0 ? (null, null, null)
                : (encryptor.Encrypt(v.KraPin, $"employee:{e.Id}:kra_pin"), encryptor.Lookup(v.KraPin, $"kra_pin:{e.OrganizationId}"), v.KraPin[^4..]);
        }

        e.HireDate = v.HireDate;
        e.ExitDate = v.ExitDate;
        e.UpdatedAt = now;
        // Changes start today, or on the start date for someone who hasn't started yet.
        var from = today > v.HireDate ? today : v.HireDate;

        var job = e.Employments.FirstOrDefault(x => x.ValidTo == null);
        var j = v.Job;
        if (job is null || (job.PositionId, job.DepartmentId, job.UnitId, job.LocationId, job.LineManagerId, job.PayScheduleId, job.LeavePlanId, job.EmploymentType, job.ContractEnd)
            != (j.PositionId, j.DepartmentId, j.UnitId, j.LocationId, j.LineManagerId, j.PayScheduleId, j.LeavePlanId, j.EmploymentType, j.ContractEnd))
        {
            var start = Close(job, from, x => x.ValidFrom, (x, to) => x.ValidTo = to) ?? v.HireDate;
            if (job is not null && job.ValidFrom >= start)
            {
                e.Employments.Remove(job);
            }

            added.Add(new Employment
            {
                Id = Guid.CreateVersion7(), OrganizationId = e.OrganizationId, CompanyId = e.CompanyId, EmployeeId = e.Id, ValidFrom = start,
                PositionId = j.PositionId, DepartmentId = j.DepartmentId, UnitId = j.UnitId, LocationId = j.LocationId, LineManagerId = j.LineManagerId,
                PayScheduleId = j.PayScheduleId, LeavePlanId = j.LeavePlanId, EmploymentType = j.EmploymentType, ContractEnd = j.ContractEnd, CreatedAt = now,
            });
        }

        var pay = e.Compensations.FirstOrDefault(x => x.ValidTo == null);
        if (v.Pay is var (amount, basis) && (pay is null || pay.BasicAmount != amount || pay.PayBasis != basis))
        {
            var start = Close(pay, from, x => x.ValidFrom, (x, to) => x.ValidTo = to) ?? v.HireDate;
            if (pay is not null && pay.ValidFrom >= start)
            {
                e.Compensations.Remove(pay);
            }

            added.Add(new Compensation
            {
                Id = Guid.CreateVersion7(), OrganizationId = e.OrganizationId, CompanyId = e.CompanyId, EmployeeId = e.Id, ValidFrom = start,
                BasicAmount = amount, PayBasis = basis, Currency = v.Company.Currency, CreatedAt = now,
            });
        }

        var account = e.Accounts.FirstOrDefault(x => x.ValidTo == null);
        if (v.Account is { } a)
        {
            var same = account is not null && account.Method == a.Method && account.BankCode == a.BankCode && account.BranchCode == a.BranchCode
                && a.AccountNumber is null && a.MpesaPhone is null;
            if (!same)
            {
                var start = Close(account, from, x => x.ValidFrom, (x, to) => x.ValidTo = to) ?? v.HireDate;
                var id = Guid.CreateVersion7();
                // A number left out keeps the current one (same method, moved to the new branch).
                var accountEnc = a.AccountNumber is null ? account?.AccountNoEnc : encryptor.Encrypt(a.AccountNumber, $"employee:{e.Id}:account");
                var accountLast4 = a.AccountNumber is null ? account?.AccountLast4 : a.AccountNumber[^4..];
                var phoneEnc = a.MpesaPhone is null ? account?.MpesaPhoneEnc : encryptor.Encrypt(a.MpesaPhone, $"employee:{e.Id}:mpesa");
                var phoneLast4 = a.MpesaPhone is null ? account?.MpesaLast4 : a.MpesaPhone[^4..];
                if (account is not null && account.ValidFrom >= start)
                {
                    e.Accounts.Remove(account);
                }

                added.Add(new EmployeePaymentAccount
                {
                    Id = id, OrganizationId = e.OrganizationId, CompanyId = e.CompanyId, EmployeeId = e.Id, Method = a.Method, ValidFrom = start, CreatedAt = now,
                    CountryCode = a.Method == "bank" ? v.Company.CountryCode : null, BankCode = a.BankCode, BranchCode = a.BranchCode,
                    AccountNoEnc = a.Method == "bank" ? accountEnc : null, AccountLast4 = a.Method == "bank" ? accountLast4 : null,
                    MpesaPhoneEnc = a.Method == "mpesa" ? phoneEnc : null, MpesaLast4 = a.Method == "mpesa" ? phoneLast4 : null,
                });
            }
        }

        return added;
    }

    /// <summary>Ends the current row the day before <paramref name="from"/> and returns when the new one starts (null with none).</summary>
    private static DateOnly? Close<T>(T? current, DateOnly from, Func<T, DateOnly> validFrom, Action<T, DateOnly> end) where T : class
    {
        if (current is null)
        {
            return null;
        }

        if (validFrom(current) >= from)
        {
            return validFrom(current);   // not started yet: replaced, not ended
        }

        end(current, from.AddDays(-1));
        return from;
    }

    private static readonly MissingItem KraPinMissing = new("employee", "kra_pin", new LocalizedText("KRA PIN", "PIN ya KRA"));
    private static readonly MissingItem NationalIdMissing = new("employee", "national_id", new LocalizedText("ID number", "Nambari ya kitambulisho"));
    private static readonly MissingItem PayMissing = new("employee", "basic_pay", new LocalizedText("Basic pay", "Mshahara wa msingi"));
    private static readonly MissingItem AccountMissing = new("employee", "payment_account", new LocalizedText("Where to pay them", "Mahali pa kuwalipa"));

    /// <summary>What payroll still needs for this person (PAY-03 enforces it).</summary>
    internal static ReadinessResponse Readiness(Employee e)
    {
        var missing = new List<MissingItem>();
        if (e.KraPinEnc is null) missing.Add(KraPinMissing);
        if (e.NationalIdEnc is null) missing.Add(NationalIdMissing);
        if (!e.Compensations.Any(c => c.ValidTo == null)) missing.Add(PayMissing);
        if (!e.Accounts.Any(a => a.ValidTo == null)) missing.Add(AccountMissing);
        return new ReadinessResponse(missing.Count == 0, missing);
    }

    private static async Task<Employee> LoadAsync(OrganizationsDbContext db, Guid id, CancellationToken ct, bool tracked)
    {
        var query = db.Employees.Include(e => e.Employments).Include(e => e.Compensations).Include(e => e.Accounts).AsSplitQuery();
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw OnboardingErrors.EmployeeNotFound();
    }

    /// <summary>The first free EMP-01, EMP-02… in the company.</summary>
    internal static async Task<string> NextStaffNoAsync(OrganizationsDbContext db, Guid companyId, CancellationToken ct) =>
        new SpreadsheetImport.CodeMaker(await db.Employees.Where(e => e.CompanyId == companyId && e.StaffNo.StartsWith("EMP-")).Select(e => e.StaffNo).ToListAsync(ct))
            .Next("EMP");

    /// <summary>No two current employees in the organization share an ID number or KRA PIN; says who has it.</summary>
    internal static async Task RequireUniqueNumbersAsync(OrganizationsDbContext db, Employee e, CancellationToken ct)
    {
        if (e.ExitDate is not null)
        {
            return;
        }

        // Statutory numbers, then contacts: someone else who works here already has it (a re-import, or a typo).
        var current = db.Employees.AsNoTracking().Where(x => x.ExitDate == null && x.Id != e.Id);
        (string Field, string Label, string? Value, Expression<Func<Employee, bool>> Same)[] checks =
        [
            ("kraPin", "KRA PIN", e.KraPinLookup, x => x.KraPinLookup == e.KraPinLookup),
            ("nationalId", "ID number", e.NationalIdLookup, x => x.NationalIdLookup == e.NationalIdLookup),
            ("nssfNo", "NSSF number", e.NssfNo, x => x.NssfNo == e.NssfNo),
            ("shaNo", "SHA number", e.ShaNo, x => x.ShaNo == e.ShaNo),
            ("phone", "phone number", e.Phone, x => x.Phone == e.Phone),
            ("email", "email address", e.Email, x => x.Email == e.Email),
        ];
        foreach (var (field, label, value, same) in checks)
        {
            if (value is not null && await current.Where(same).Select(x => new { x.FirstName, x.LastName, x.StaffNo }).FirstOrDefaultAsync(ct) is { } holder)
            {
                throw OnboardingErrors.EmployeeNumberTaken(field, label, $"{holder.FirstName} {holder.LastName} ({holder.StaffNo})");
            }
        }
    }

    internal static async Task SaveAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } p)
        {
            // Someone this person can't see (another company) may hold the number.
            throw p.ConstraintName switch
            {
                "employees_one_kra_pin" => OnboardingErrors.EmployeeNumberTaken("kraPin", "KRA PIN", "Another current employee"),
                "employees_one_national_id" => OnboardingErrors.EmployeeNumberTaken("nationalId", "ID number", "Another current employee"),
                "employees_one_nssf" => OnboardingErrors.EmployeeNumberTaken("nssfNo", "NSSF number", "Another current employee"),
                "employees_one_sha" => OnboardingErrors.EmployeeNumberTaken("shaNo", "SHA number", "Another current employee"),
                "employees_one_phone" => OnboardingErrors.EmployeeNumberTaken("phone", "phone number", "Another current employee"),
                "employees_one_email" => OnboardingErrors.EmployeeNumberTaken("email", "email address", "Another current employee"),
                _ => OnboardingErrors.StaffNoTaken(),
            };
        }
    }

    private static EmployeeResponse ToResponse(Employee e)
    {
        var job = e.Employments.Single(x => x.ValidTo == null);
        var pay = e.Compensations.FirstOrDefault(x => x.ValidTo == null);
        var account = e.Accounts.FirstOrDefault(x => x.ValidTo == null);
        return new EmployeeResponse(e.Id, e.CompanyId, e.StaffNo, e.FirstName, e.MiddleName, e.LastName, e.DateOfBirth, e.Phone, e.Email, e.Address,
            e.NationalIdLast4 is null ? null : $"•••• {e.NationalIdLast4}", e.KraPinLast4 is null ? null : $"•••• {e.KraPinLast4}", e.NssfNo, e.ShaNo,
            e.HireDate, e.ExitDate,
            new JobResponse(job.PositionId, job.DepartmentId, job.UnitId, job.LocationId, job.LineManagerId, job.PayScheduleId, job.LeavePlanId, job.EmploymentType, job.ContractEnd),
            pay is null ? null : new PayResponse(pay.BasicAmount, pay.PayBasis, pay.Currency),
            account is null ? null : new AccountResponse(account.Method, account.BankCode, account.BranchCode,
                account.AccountLast4 is null ? null : $"•••• {account.AccountLast4}", account.MpesaLast4 is null ? null : $"•••• {account.MpesaLast4}"),
            Readiness(e));
    }
}
