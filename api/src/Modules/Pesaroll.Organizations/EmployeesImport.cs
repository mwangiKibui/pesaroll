using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pesaroll.Notifications;
using Pesaroll.Shared.Security;

namespace Pesaroll.Organizations;

/// <summary>
/// ONB-09, uploading employees with the shared spreadsheet import (up to 5,000 a file). Each row
/// names its company and the rest as text: the structure chain (a division, department or position
/// that doesn't exist yet is noted and created on import, for the row's company), location,
/// payroll schedule and leave plan by name (empty means the company's defaults), the line manager
/// by staff number (someone already there or another row of the file), bank and branch by name or
/// code. The rules are those of adding one by hand, checked for the whole file at once.
/// </summary>
public static class EmployeesImport
{
    private const int MaxRows = 5000;
    private const int MaxMegabytes = 5;

    public static readonly IReadOnlyList<ImportColumn> Columns =
    [
        new("company", "Company"), new("staffNo", "Staff number", "@"), new("firstName", "First name"), new("middleName", "Middle name"),
        new("lastName", "Last name"), new("dateOfBirth", "Date of birth", "yyyy-mm-dd"), new("phone", "Phone", "@"), new("email", "Email"),
        new("nationalId", "ID number", "@"), new("kraPin", "KRA PIN", "@"), new("nssfNo", "NSSF number", "@"), new("shaNo", "SHA number", "@"),
        new("hireDate", "Start date", "yyyy-mm-dd"), new("exitDate", "Date left", "yyyy-mm-dd"), new("employmentType", "Employment type"),
        new("contractEnd", "Contract end", "yyyy-mm-dd"), new("division", "Division"), new("department", "Department"), new("unit", "Unit"),
        new("position", "Position"), new("location", "Location"), new("lineManager", "Line manager (staff number)", "@"),
        new("payrollSchedule", "Payroll schedule"), new("leavePlan", "Leave plan"), new("basicPay", "Basic pay"), new("payBasis", "Paid"),
        new("bank", "Bank"), new("branch", "Branch", "@"), new("accountNumber", "Account number", "@"), new("mpesa", "M-Pesa number", "@"),
    ];

    private static readonly string[] Types = ["Permanent", "Contract", "Casual", "Intern"];
    private static readonly string[] Bases = ["Monthly", "Daily", "Hourly"];

    public static IEndpointRouteBuilder MapEmployeesImportEndpoints(this IEndpointRouteBuilder app)
    {
        SpreadsheetImport.MapImport(app.MapGroup("/api/employees").WithTags("Employees"), "", new ImportSpec<ValidRow>
        {
            Things = "employees",
            Columns = Columns,
            RequiredKeys = ["firstName", "lastName"],
            MaxRows = MaxRows,
            MaxMegabytes = MaxMegabytes,
            Choices = (_, _) => Task.FromResult(new Dictionary<string, List<string>> { ["employmentType"] = [.. Types], ["payBasis"] = [.. Bases] }),
            Check = (rows, db, services, ct) => CheckAsync(rows, db, services.GetRequiredService<FieldEncryptor>(), ct),
            Save = (rows, _, organizationId, db, now, services, ct) => SaveAsync(rows, organizationId, db, now, services.GetRequiredService<FieldEncryptor>(), ct),
        });
        return app;
    }

    /// <summary>
    /// A checked row: the employee as adding one by hand would have it, plus what the file adds: new
    /// structure to create (by name) and a line manager who may be another row (by staff number).
    /// </summary>
    private sealed record ValidRow(int Line, EmployeesEndpoints.ValidEmployee Employee, Guid? DivisionId, string? NewDivision, string? NewDepartment,
        string? NewPosition, string? ManagerStaffNo);

    /// <summary>Something rows refer to: id, code, name, which companies use it, and (departments, units) where it sits.</summary>
    private sealed record Ref(Guid Id, string Code, string Name, bool AllCompanies, List<Guid> CompanyIds, Guid? ParentId = null)
    {
        public bool Serves(Guid companyId) => AllCompanies || CompanyIds.Contains(companyId);
    }

    private enum Found { One, None, Several }

    private static (Found Found, Ref? Item) Find(List<Ref> items, string text)
    {
        var byCode = items.Where(i => string.Equals(i.Code, text, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = byCode.Count > 0 ? byCode : items.Where(i => string.Equals(i.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch { 1 => (Found.One, matches[0]), 0 => (Found.None, null), _ => (Found.Several, null) };
    }

    /// <summary>A date as written: 2024-01-15, 15/01/2024 (day first, as in Kenya), 15 Jan 2024; null if it isn't one.</summary>
    internal static DateOnly? ParseDate(string text)
    {
        string[] formats = ["yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d MMM yyyy", "d-MMM-yyyy", "d MMMM yyyy", "yyyy-MM-dd HH:mm:ss", "d/M/yyyy HH:mm:ss"];
        return DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d) ? DateOnly.FromDateTime(d) : null;
    }

    /// <summary>"85,000", "KES 85000.50": an amount, or null.</summary>
    internal static decimal? ParseAmount(string text)
    {
        var compact = Regex.Replace(text, @"(?i)kes|ksh|[\s,]", "");
        return decimal.TryParse(compact, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    private static async Task<(List<ValidRow> Rows, List<ImportProblem> Problems, List<ImportProblem> Notes)> CheckAsync(
        List<ImportRow> raw, OrganizationsDbContext db, FieldEncryptor encryptor, CancellationToken ct)
    {
        // Everything rows can refer to, once for the whole file.
        var companies = await db.Companies.AsNoTracking().Where(c => c.Status == "active").ToListAsync(ct);
        var divisions = (await db.Divisions.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct))
            .Select(x => new Ref(x.Id, x.Code, x.Name, x.AllCompanies, x.Companies.Select(c => c.CompanyId).ToList())).ToList();
        var departments = (await db.Departments.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct))
            .Select(x => new Ref(x.Id, x.Code, x.Name, x.AllCompanies, x.Companies.Select(c => c.CompanyId).ToList(), x.DivisionId)).ToList();
        var positions = (await db.Positions.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct))
            .Select(x => new Ref(x.Id, x.Code, x.Name, x.AllCompanies, x.Companies.Select(c => c.CompanyId).ToList(), x.DepartmentId)).ToList();
        var units = (await db.Units.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct))
            .Select(x => new Ref(x.Id, x.Code, x.Name, x.AllCompanies, x.Companies.Select(c => c.CompanyId).ToList(), x.DepartmentId)).ToList();
        var locations = (await db.Locations.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct))
            .Select(x => new Ref(x.Id, x.Code, x.Name, x.AllCompanies, x.Companies.Select(c => c.CompanyId).ToList())).ToList();
        var schedules = await db.PaySchedules.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct);
        var plans = (await db.LeavePlans.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct))
            .Select(x => new Ref(x.Id, x.Name, x.Name, x.AllCompanies, x.Companies.Select(c => c.CompanyId).ToList())).ToList();
        var banks = await db.Banks.AsNoTracking().Where(b => b.Active).ToListAsync(ct);
        var branches = await db.BankBranches.AsNoTracking().Where(b => b.Active).ToListAsync(ct);
        var existing = await db.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.CompanyId, e.StaffNo, e.KraPinLookup, e.NationalIdLookup, e.ExitDate }).ToListAsync(ct);
        var organizationId = companies.FirstOrDefault()?.OrganizationId ?? Guid.Empty;

        var problems = new List<ImportProblem>();
        var notes = new List<ImportProblem>();
        var valid = new List<ValidRow>();
        // What the file itself takes: staff numbers per company, numbers of current staff.
        var fileStaff = new Dictionary<(Guid, string), int>();
        var fileNumbers = new Dictionary<string, int>();

        foreach (var row in raw)
        {
            var line = row.Line;
            var before = problems.Count;
            string Cell(string key) => row.Cells.GetValueOrDefault(key)?.Trim() ?? "";
            void Problem(string column, string message) => problems.Add(new ImportProblem(line, column, message));

            var companyText = Cell("company");
            var company = companyText.Length == 0 && companies.Count == 1 ? companies[0]
                : companies.FirstOrDefault(c => string.Equals(c.LegalName, companyText, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.TradingName, companyText, StringComparison.OrdinalIgnoreCase));
            if (company is null)
            {
                Problem("company", $"Choose one of your companies: {string.Join(", ", companies.Select(c => c.LegalName))}.");
                continue;
            }

            string Name(string key, string label, bool required)
            {
                var v = Cell(key);
                if ((required && v.Length == 0) || v.Length > 60)
                {
                    Problem(key, $"Enter the {label} (up to 60 characters).");
                }

                return v;
            }

            var first = Name("firstName", "first name", required: true);
            var middle = Name("middleName", "middle name", required: false);
            var last = Name("lastName", "last name", required: true);

            var staffNo = Cell("staffNo").ToUpperInvariant();
            if (staffNo.Length > 0)
            {
                if (!Regex.IsMatch(staffNo, "^[A-Z0-9][A-Z0-9/-]{0,19}$"))
                {
                    Problem("staffNo", "Use up to 20 letters, numbers, dashes or slashes, or leave it empty for us to make one.");
                }
                else if (existing.Any(e => e.CompanyId == company.Id && e.StaffNo == staffNo))
                {
                    Problem("staffNo", $"{company.LegalName} already has an employee with staff number {staffNo}.");
                }
                else if (fileStaff.TryGetValue((company.Id, staffNo), out var other))
                {
                    Problem("staffNo", $"Row {other} has this staff number too.");
                }
                else
                {
                    fileStaff[(company.Id, staffNo)] = line;
                }
            }

            DateOnly? Date(string key, string label)
            {
                var text = Cell(key);
                if (text.Length == 0) return null;
                var date = ParseDate(text);
                if (date is null) Problem(key, $"Enter the {label} as a date, like 2024-01-15 or 15/01/2024.");
                return date;
            }

            var hire = Date("hireDate", "start date");
            if (hire is null && Cell("hireDate").Length == 0)
            {
                Problem("hireDate", "Enter the date they started.");
            }

            var exit = Date("exitDate", "date they left");
            if (exit < hire)
            {
                Problem("exitDate", "The date they left can't be before they started.");
            }

            var born = Date("dateOfBirth", "date of birth");
            if (born is DateOnly b && hire is DateOnly h && b.AddYears(13) > h)
            {
                Problem("dateOfBirth", "In Kenya, nobody under 13 can be employed.");
            }

            string? Number(string key, Func<string?, string?> check, string message)
            {
                var text = Cell(key);
                if (text.Length == 0) return null;
                var stored = check(text);
                if (stored is null) Problem(key, message);
                return stored;
            }

            var phone = Number("phone", Recipients.KenyanMobile, "Enter a Kenyan mobile number, like 0712 345 678.");
            var email = Number("email", Recipients.Email, "Enter a valid email address.");
            var nationalId = Number("nationalId", EmployeeNumbers.NationalId, "Enter the ID number: 7 or 8 digits.");
            var kraPin = Number("kraPin", EmployeeNumbers.KraPin, "Enter the person's KRA PIN: A, 9 digits and a letter, like A012345678Z.");
            var nssf = Number("nssfNo", EmployeeNumbers.Nssf, "Enter the NSSF number: 6 to 12 digits.");
            var sha = Number("shaNo", EmployeeNumbers.Sha, "Enter the SHA number: 6 to 20 letters, numbers or dashes.");

            // No two current employees share an ID number or KRA PIN: those already there, then earlier rows.
            void Unique(string? value, string key, string label, string kind)
            {
                if (value is null || exit is not null) return;
                var lookup = encryptor.Lookup(value, $"{kind}:{organizationId}");
                if (existing.FirstOrDefault(e => (kind == "kra_pin" ? e.KraPinLookup : e.NationalIdLookup) == lookup && e.ExitDate == null) is { } holder)
                {
                    Problem(key, $"{holder.StaffNo} already has this {label}.");
                }
                else if (fileNumbers.TryGetValue(kind + lookup, out var other))
                {
                    Problem(key, $"Row {other} has this {label} too.");
                }
                else
                {
                    fileNumbers[kind + lookup] = line;
                }
            }

            Unique(kraPin, "kraPin", "KRA PIN", "kra_pin");
            Unique(nationalId, "nationalId", "ID number", "national_id");

            var typeText = Cell("employmentType");
            var type = typeText.Length == 0 ? "permanent" : Types.FirstOrDefault(t => string.Equals(t, typeText, StringComparison.OrdinalIgnoreCase))?.ToLowerInvariant();
            if (type is null)
            {
                Problem("employmentType", "Choose Permanent, Contract, Casual or Intern.");
            }

            var contractEnd = Date("contractEnd", "contract end");
            if (contractEnd is not null && type != "contract")
            {
                Problem("contractEnd", "Only fixed-term contracts have an end date.");
            }

            // The structure chain: existing for this company, or new (noted, created on import).
            string? newDivision = null, newDepartment = null, newPosition = null;
            Ref? Resolve(List<Ref> items, string key, string label, bool canCreate, ref string? created)
            {
                var text = Cell(key);
                if (text.Length == 0) return null;
                var (found, item) = Find(items, text);
                if (found == Found.Several)
                {
                    Problem(key, $"More than one {label} is called {text}. Use its code.");
                }
                else if (found == Found.One && !item!.Serves(company.Id))
                {
                    Problem(key, $"{item.Name} isn't used by {company.LegalName}.");
                }
                else if (found == Found.None && canCreate)
                {
                    created = text;
                    notes.Add(new ImportProblem(line, key, $"{text} will be created as a new {label} for {company.LegalName}."));
                }
                else if (found == Found.None)
                {
                    Problem(key, $"No {label} is called {text}. Add it in Org structure first, or check the spelling.");
                }

                return found == Found.One ? item : null;
            }

            string? none = null;
            var divisionId = Resolve(divisions, "division", "division", canCreate: true, ref newDivision)?.Id;
            var departmentId = Resolve(departments, "department", "department", canCreate: true, ref newDepartment)?.Id;
            var positionId = Resolve(positions, "position", "position", canCreate: true, ref newPosition)?.Id;
            var unitId = Resolve(units, "unit", "unit", canCreate: false, ref none)?.Id;
            var locationId = Cell("location").Length == 0 ? company.MainLocationId : Resolve(locations, "location", "location", canCreate: false, ref none)?.Id;

            var managerText = Cell("lineManager").ToUpperInvariant();
            string? managerStaffNo = null;
            if (managerText.Length > 0)
            {
                if (managerText == staffNo)
                {
                    Problem("lineManager", "Someone can't be their own line manager.");
                }
                else if (existing.Any(e => e.CompanyId == company.Id && e.StaffNo == managerText)
                         || raw.Any(r => (r.Cells.GetValueOrDefault("staffNo") ?? "").Trim().ToUpperInvariant() == managerText
                                         && SameCompany(r, company, companies)))
                {
                    managerStaffNo = managerText;
                }
                else
                {
                    Problem("lineManager", $"No one at {company.LegalName} has staff number {managerText}, in Pesaroll or in this file.");
                }
            }

            var scheduleText = Cell("payrollSchedule");
            var schedule = scheduleText.Length == 0
                ? schedules.FirstOrDefault(s => s.Id == company.DefaultPayScheduleId)
                : schedules.FirstOrDefault(s => string.Equals(s.Name, scheduleText, StringComparison.OrdinalIgnoreCase) && (s.AllCompanies || s.Companies.Any(c => c.CompanyId == company.Id)));
            if (schedule is null)
            {
                Problem("payrollSchedule", $"{company.LegalName} has no payroll schedule called {scheduleText}.");
            }

            var planText = Cell("leavePlan");
            var plan = planText.Length == 0
                ? plans.FirstOrDefault(p => p.Id == (schedule?.DefaultLeavePlanId ?? company.DefaultLeavePlanId))
                : plans.FirstOrDefault(p => string.Equals(p.Name, planText, StringComparison.OrdinalIgnoreCase) && p.Serves(company.Id));
            if (plan is null)
            {
                Problem("leavePlan", $"{company.LegalName} has no leave plan called {planText}.");
            }

            (decimal, string)? pay = null;
            var payText = Cell("basicPay");
            var basisText = Cell("payBasis");
            var basis = basisText.Length == 0 ? "monthly" : Bases.FirstOrDefault(x => string.Equals(x, basisText, StringComparison.OrdinalIgnoreCase))?.ToLowerInvariant();
            if (basis is null)
            {
                Problem("payBasis", "Choose Monthly, Daily or Hourly.");
            }

            if (payText.Length > 0)
            {
                if (ParseAmount(payText) is decimal amount && amount > 0)
                {
                    pay = (amount, basis ?? "monthly");
                }
                else
                {
                    Problem("basicPay", "Enter the basic pay as an amount, more than 0.");
                }
            }

            EmployeesEndpoints.ValidAccount? account = null;
            var bankText = Cell("bank");
            var branchText = Cell("branch");
            var accountText = Regex.Replace(Cell("accountNumber"), @"[\s-]", "");
            var mpesaText = Cell("mpesa");
            if (bankText.Length > 0 || branchText.Length > 0 || accountText.Length > 0)
            {
                var bank = banks.FirstOrDefault(x => x.CountryCode == company.CountryCode
                    && (string.Equals(x.Code, bankText, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, bankText, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.LegalName, bankText, StringComparison.OrdinalIgnoreCase)));
                var branch = bank is null ? null : branches.FirstOrDefault(x => x.CountryCode == bank.CountryCode && x.BankCode == bank.Code
                    && (string.Equals(x.Code, branchText, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, branchText, StringComparison.OrdinalIgnoreCase)));
                if (bank is null)
                {
                    Problem("bank", bankText.Length == 0 ? "Enter the bank for this account number." : $"No bank in Pesaroll is called {bankText}.");
                }
                else if (branch is null)
                {
                    Problem("branch", $"Enter the {bank.Name} branch by name or code.");
                }

                if (!Regex.IsMatch(accountText, "^[0-9]{5,20}$"))
                {
                    Problem("accountNumber", "Enter the account number: 5 to 20 digits.");
                }

                if (mpesaText.Length > 0)
                {
                    Problem("mpesa", "Give a bank account or an M-Pesa number, not both.");
                }

                account = new EmployeesEndpoints.ValidAccount("bank", bank?.Code, branch?.Code, accountText, null);
            }
            else if (mpesaText.Length > 0)
            {
                var mpesa = Recipients.KenyanMobile(mpesaText);
                if (mpesa is null)
                {
                    Problem("mpesa", "Enter a Kenyan mobile number, like 0712 345 678.");
                }

                account = new EmployeesEndpoints.ValidAccount("mpesa", null, null, null, mpesa);
            }

            if (problems.Count > before || hire is null || type is null || schedule is null || plan is null)
            {
                continue;
            }

            var job = new EmployeesEndpoints.ValidJob(positionId, departmentId, unitId, locationId, null, schedule.Id, plan.Id, type, contractEnd);
            valid.Add(new ValidRow(line, new EmployeesEndpoints.ValidEmployee(company, staffNo.Length == 0 ? null : staffNo, first, middle.Length == 0 ? null : middle,
                last, born, phone, email, nationalId, kraPin, nssf, sha, hire.Value, exit, job, pay, account),
                divisionId, newDivision, newDepartment, newPosition, managerStaffNo));
        }

        return (valid, problems, notes);
    }

    private static bool SameCompany(ImportRow row, Company company, List<Company> companies)
    {
        var text = (row.Cells.GetValueOrDefault("company") ?? "").Trim();
        return (text.Length == 0 && companies.Count == 1) || string.Equals(company.LegalName, text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(company.TradingName, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Saves every row or none: structure the rows named but that didn't exist (for the companies
    /// whose rows named it), then the employees with staff numbers made up where empty, then line managers.
    /// </summary>
    private static async Task<int> SaveAsync(List<ValidRow> rows, Guid organizationId, OrganizationsDbContext db, DateTimeOffset now,
        FieldEncryptor encryptor, CancellationToken ct)
    {
        var made = new StructureMaker(db, organizationId, now);
        await made.LoadCodesAsync(ct);
        var today = PayDates.TodayInKenya(now);
        var staffCodes = new Dictionary<Guid, SpreadsheetImport.CodeMaker>();
        var people = new List<(ValidRow Row, Employee Employee)>();
        foreach (var row in rows)
        {
            var v = row.Employee;
            var companyId = v.Company.Id;
            // A new department sits in the row's division, existing or new.
            var division = row.NewDivision is null ? row.DivisionId : made.Division(row.NewDivision, companyId);
            var department = row.NewDepartment is null ? v.Job.DepartmentId : made.Department(row.NewDepartment, division, companyId);
            var position = row.NewPosition is null ? v.Job.PositionId : made.Position(row.NewPosition, department, companyId);
            if (!staffCodes.TryGetValue(companyId, out var codes))
            {
                codes = staffCodes[companyId] = new SpreadsheetImport.CodeMaker(
                    [.. await db.Employees.Where(e => e.CompanyId == companyId).Select(e => e.StaffNo).ToListAsync(ct),
                     .. rows.Where(r => r.Employee.Company.Id == companyId && r.Employee.StaffNo is not null).Select(r => r.Employee.StaffNo!)]);
            }

            var employee = new Employee
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = companyId, StaffNo = v.StaffNo ?? codes.Next("EMP"),
                FirstName = v.FirstName, LastName = v.LastName, CreatedAt = now,
            };
            people.Add((row with { Employee = v with { Job = v.Job with { DepartmentId = department, PositionId = position } } }, employee));
        }

        // Line managers: someone already there, or another row (by staff number, in the same company).
        var managers = (await db.Employees.AsNoTracking().Select(e => new { e.CompanyId, e.StaffNo, e.Id }).ToListAsync(ct))
            .Select(e => (e.CompanyId, e.StaffNo, e.Id)).Concat(people.Select(p => (p.Employee.CompanyId, p.Employee.StaffNo, p.Employee.Id)))
            .ToDictionary(e => (e.CompanyId, e.StaffNo), e => e.Id);
        foreach (var (row, employee) in people)
        {
            var v = row.Employee;
            var manager = row.ManagerStaffNo is null ? (Guid?)null : managers[(employee.CompanyId, row.ManagerStaffNo)];
            var added = EmployeesEndpoints.Apply(employee, v with { Job = v.Job with { LineManagerId = manager } }, encryptor, now, today);
            employee.Employments.AddRange(added.OfType<Employment>());
            employee.Compensations.AddRange(added.OfType<Compensation>());
            employee.Accounts.AddRange(added.OfType<EmployeePaymentAccount>());
            db.Employees.Add(employee);
        }

        await EmployeesEndpoints.SaveAsync(db, ct);   // one save: every row (and what they needed created) or none
        return people.Count;
    }

    /// <summary>
    /// Makes the divisions, departments and positions an upload names but that don't exist yet, once
    /// per name (ignoring case), for the companies whose rows name them, with made-up codes.
    /// </summary>
    private sealed class StructureMaker(OrganizationsDbContext db, Guid organizationId, DateTimeOffset now)
    {
        private readonly Dictionary<string, Division> divisions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Department> departments = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Position> positions = new(StringComparer.OrdinalIgnoreCase);
        private SpreadsheetImport.CodeMaker divisionCodes = new([]);
        private SpreadsheetImport.CodeMaker departmentCodes = new([]);
        private SpreadsheetImport.CodeMaker positionCodes = new([]);

        public async Task LoadCodesAsync(CancellationToken ct)
        {
            divisionCodes = new(await db.Divisions.Select(d => d.Code).ToListAsync(ct));
            departmentCodes = new(await db.Departments.Select(d => d.Code).ToListAsync(ct));
            positionCodes = new(await db.Positions.Select(p => p.Code).ToListAsync(ct));
        }

        public Guid Division(string name, Guid companyId)
        {
            if (!divisions.TryGetValue(name, out var division))
            {
                division = divisions[name] = new Division { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = divisionCodes.Next(LocationsEndpoints.Prefix(name)), Name = name, CreatedAt = now, UpdatedAt = now };
                db.Divisions.Add(division);
            }

            if (!division.Companies.Any(c => c.CompanyId == companyId))
            {
                division.Companies.Add(new DivisionCompany { OrganizationId = organizationId, DivisionId = division.Id, CompanyId = companyId, CreatedAt = now });
            }

            return division.Id;
        }

        public Guid Department(string name, Guid? divisionId, Guid companyId)
        {
            if (!departments.TryGetValue(name, out var department))
            {
                department = departments[name] = new Department
                {
                    Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = departmentCodes.Next(LocationsEndpoints.Prefix(name)), Name = name,
                    DivisionId = divisionId, CreatedAt = now, UpdatedAt = now,
                };
                db.Departments.Add(department);
            }

            if (!department.Companies.Any(c => c.CompanyId == companyId))
            {
                department.Companies.Add(new DepartmentCompany { OrganizationId = organizationId, DepartmentId = department.Id, CompanyId = companyId, CreatedAt = now });
            }

            return department.Id;
        }

        public Guid Position(string name, Guid? departmentId, Guid companyId)
        {
            if (!positions.TryGetValue(name, out var position))
            {
                position = positions[name] = new Position
                {
                    Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = positionCodes.Next(LocationsEndpoints.Prefix(name)), Name = name,
                    DepartmentId = departmentId, CreatedAt = now, UpdatedAt = now,
                };
                db.Positions.Add(position);
            }

            if (!position.Companies.Any(c => c.CompanyId == companyId))
            {
                position.Companies.Add(new PositionCompany { OrganizationId = organizationId, PositionId = position.Id, CompanyId = companyId, CreatedAt = now });
            }

            return position.Id;
        }
    }
}
