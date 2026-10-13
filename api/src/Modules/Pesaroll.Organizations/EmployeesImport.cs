using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pesaroll.Notifications;
using Pesaroll.Shared.Security;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// ONB-09, uploading employees with the shared spreadsheet import (up to 5,000 a file), for one
/// company chosen first (the app puts it in each row's company cell; the template is made for it,
/// its lists as dropdowns). Rows name the rest as text: the structure chain (a division, department or position
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
        new("company", "Company", InTemplate: false), new("staffNo", "Staff number", "@"), new("firstName", "First name"), new("middleName", "Middle name"),
        new("lastName", "Last name"), ImportColumn.Date("dateOfBirth", "Date of birth"), new("phone", "Phone", "@"), new("email", "Email"), new("address", "Address"),
        new("nationalId", "ID number", "@"), new("kraPin", "KRA PIN", "@"), new("nssfNo", "NSSF number", "@"), new("shaNo", "SHA number", "@"),
        ImportColumn.Date("hireDate", "Start date"), new("stillHere", "Still works here"), ImportColumn.Date("exitDate", "Date left"),
        new("employmentType", "Employment type"),
        ImportColumn.Date("contractEnd", "Contract end"), new("division", "Division"), new("department", "Department"), new("unit", "Unit"),
        new("position", "Position"), new("lineManager", "Line manager (staff number)", "@"),
        new("payrollSchedule", "Payroll schedule"), new("leavePlan", "Leave plan"), new("basicPay", "Basic pay"),
        new("paidInto", "Paid into"), new("bank", "Bank"), new("branch", "Branch", "@", DependsOn: "bank"), new("accountNumber", "Account number", "@"), new("mpesa", "M-Pesa number", "@"),
    ];

    private static readonly string[] Types = ["Permanent", "Contract", "Casual", "Intern"];

    public static IEndpointRouteBuilder MapEmployeesImportEndpoints(this IEndpointRouteBuilder app)
    {
        SpreadsheetImport.MapImport(app.MapGroup("/api/employees").WithTags("Employees"), "", new ImportSpec<ValidRow>
        {
            Things = "employees",
            Permission = Permissions.Employees,
            PerCompany = true,
            Columns = Columns,
            RequiredKeys = ["firstName", "lastName"],
            MaxRows = MaxRows,
            MaxMegabytes = MaxMegabytes,
            Choices = ChoicesAsync,
            Check = (rows, db, services, ct) => CheckAsync(rows, db, services.GetRequiredService<FieldEncryptor>(), ct),
            Save = (rows, _, organizationId, db, now, services, ct) => SaveAsync(rows, organizationId, db, now, services.GetRequiredService<FieldEncryptor>(), ct),
        });
        return app;
    }

    /// <summary>The template's dropdowns: Still works here, employment type, banks and branches, and the company's own lists.</summary>
    private static async Task<Dictionary<string, List<string>>> ChoicesAsync(OrganizationsDbContext db, Guid? companyId, CancellationToken ct)
    {
        var company = companyId is Guid id ? await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) : null;
        var country = company?.CountryCode ?? "KE";
        var choices = new Dictionary<string, List<string>>
        {
            ["stillHere"] = ["Yes", "No"],
            ["paidInto"] = ["Bank", "M-Pesa"],
            ["employmentType"] = [.. Types],
            ["bank"] = await db.Banks.AsNoTracking().Where(b => b.CountryCode == country && b.Active).OrderBy(b => b.SortOrder).Select(b => b.Name).ToListAsync(ct),
        };
        // Branch offers the branches of the row's bank.
        var banks = await db.Banks.AsNoTracking().Where(b => b.CountryCode == country && b.Active).OrderBy(b => b.SortOrder).ToListAsync(ct);
        var branches = await db.BankBranches.AsNoTracking().Where(b => b.CountryCode == country && b.Active).OrderBy(b => b.Name).ToListAsync(ct);
        foreach (var bank in banks)
        {
            choices[$"branch:{bank.Name}"] = branches.Where(b => b.BankCode == bank.Code).Select(b => b.Name).ToList();
        }

        if (company is null)
        {
            return choices;
        }

        // What the company uses: for all companies or linked to it.
        var cid = company.Id;
        choices["division"] = await db.Divisions.Where(x => x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == cid))).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
        choices["department"] = await db.Departments.Where(x => x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == cid))).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
        choices["position"] = await db.Positions.Where(x => x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == cid))).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
        choices["unit"] = await db.Units.Where(x => x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == cid))).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
        choices["payrollSchedule"] = await db.PaySchedules.Where(x => x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == cid))).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
        choices["leavePlan"] = await db.LeavePlans.Where(x => x.Status == "active" && (x.AllCompanies || x.Companies.Any(c => c.CompanyId == cid))).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
        return choices;
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

    /// <summary>Someone already employed, with what no other current employee may share.</summary>
    private sealed record Existing(Guid Id, Guid CompanyId, string StaffNo, string FirstName, string LastName, string? KraPinLookup, string? NationalIdLookup,
        string? NssfNo, string? ShaNo, string? Phone, string? Email, DateOnly? ExitDate);

    private enum Found { One, None, Several }

    private static (Found Found, Ref? Item) Find(List<Ref> items, string text)
    {
        var byCode = items.Where(i => string.Equals(i.Code, text, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = byCode.Count > 0 ? byCode : items.Where(i => string.Equals(i.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch { 1 => (Found.One, matches[0]), 0 => (Found.None, null), _ => (Found.Several, null) };
    }

    /// <summary>A date as written: 2024-01-15, 15/01/2024 (day first, as in Kenya), 15 Jan 2024; null if it isn't one.</summary>
    internal static DateOnly? ParseDate(string text) => SpreadsheetImport.ParseDate(text);

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
        var schedules = await db.PaySchedules.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct);
        var plans = (await db.LeavePlans.AsNoTracking().Include(x => x.Companies).Where(x => x.Status == "active").ToListAsync(ct))
            .Select(x => new Ref(x.Id, x.Name, x.Name, x.AllCompanies, x.Companies.Select(c => c.CompanyId).ToList())).ToList();
        var banks = await db.Banks.AsNoTracking().Where(b => b.Active).ToListAsync(ct);
        var branches = await db.BankBranches.AsNoTracking().Where(b => b.Active).ToListAsync(ct);

        // "City Centre, Nairobi is a branch of NCBA Bank. Choose a branch of Equity Bank: Kisumu (029) or …": whose branch it
        // is, and the bank's own (named when there are a few; codes keep names with commas apart).
        string BranchProblem(Bank bank, string text)
        {
            var other = branches.Where(b => b.CountryCode == bank.CountryCode && b.BankCode != bank.Code
                    && (string.Equals(b.Name, text, StringComparison.OrdinalIgnoreCase) || string.Equals(b.Code, text, StringComparison.OrdinalIgnoreCase)))
                .Select(b => banks.FirstOrDefault(x => x.CountryCode == b.CountryCode && x.Code == b.BankCode)?.Name).FirstOrDefault(n => n is not null);
            var own = branches.Where(b => b.CountryCode == bank.CountryCode && b.BankCode == bank.Code).OrderBy(b => b.Name).Select(b => $"{b.Name} ({b.Code})").ToList();
            var choose = own.Count is > 0 and <= 6 ? $"Choose a branch of {bank.Name}: {string.Join(", ", own[..^1])}{(own.Count > 1 ? " or " : "")}{own[^1]}."
                : $"Choose a branch of {bank.Name} from the list, by name or code.";
            return text.Length == 0 ? choose
                : other is not null ? $"{text} is a branch of {other}. {choose}"
                : $"{bank.Name} has no branch called {text}. {choose}";
        }
        var payMethods = (await db.CompanyPaymentAccounts.AsNoTracking().Where(a => a.ClosedAt == null).Select(a => new { a.CompanyId, a.Method }).ToListAsync(ct))
            .GroupBy(a => a.CompanyId).ToDictionary(g => g.Key, g => g.Select(a => a.Method).Distinct().ToList());
        var existing = await db.Employees.AsNoTracking()
            .Select(e => new Existing(e.Id, e.CompanyId, e.StaffNo, e.FirstName, e.LastName, e.KraPinLookup, e.NationalIdLookup, e.NssfNo, e.ShaNo, e.Phone, e.Email, e.ExitDate))
            .ToListAsync(ct);
        // What current employees already have, by column: value (or keyed lookup) to "Wanjiru Kamau (EMP-01)".
        var current = existing.Where(e => e.ExitDate == null).ToList();
        Dictionary<string, string> Held(Func<Existing, string?> value) => current
            .Select(e => (Value: value(e), Holder: $"{e.FirstName} {e.LastName} ({e.StaffNo})")).Where(x => x.Value is not null)
            .DistinctBy(x => x.Value).ToDictionary(x => x.Value!, x => x.Holder);
        var taken = new Dictionary<string, Dictionary<string, string>>
        {
            ["phone"] = Held(e => e.Phone), ["email"] = Held(e => e.Email), ["nationalId"] = Held(e => e.NationalIdLookup),
            ["kraPin"] = Held(e => e.KraPinLookup), ["nssfNo"] = Held(e => e.NssfNo), ["shaNo"] = Held(e => e.ShaNo),
        };
        var organizationId = companies.FirstOrDefault()?.OrganizationId ?? Guid.Empty;

        var problems = new List<ImportProblem>();
        var notes = new List<ImportProblem>();
        var valid = new List<ValidRow>();
        // What the file itself takes: staff numbers per company, numbers of current staff.
        var fileStaff = new Dictionary<(Guid, string), int>();
        var fileNumbers = new Dictionary<string, int>();
        // A value two rows share is marked on both, once on the earlier row, so the pair shows together.
        var markedEarlier = new HashSet<(int, string)>();
        void Earlier(int other, int line, string key, string label)
        {
            if (markedEarlier.Add((other, key)))
            {
                problems.Add(new ImportProblem(other, key, $"Row {line} has this {label} too."));
            }
        }

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
                    Earlier(other, line, "staffNo", "staff number");
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

            // Still works here (empty means yes unless there's a date they left) and Date left agree.
            var stillText = Cell("stillHere");
            var stillHere = stillText.Length == 0 ? Cell("exitDate").Length == 0
                : stillText.ToUpperInvariant() is "YES" or "Y" or "TRUE" ? true
                : stillText.ToUpperInvariant() is "NO" or "N" or "FALSE" ? false : (bool?)null;
            if (stillHere is null)
            {
                Problem("stillHere", "Enter Yes or No.");
            }
            else if (stillHere == false && Cell("exitDate").Length == 0)
            {
                Problem("exitDate", "Enter the date they left, or set Still works here to Yes.");
            }
            else if (stillHere == true && Cell("exitDate").Length > 0)
            {
                Problem("exitDate", "Leave Date left empty for someone who still works here.");
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
            var address = Cell("address");
            if (address.Length > 200)
            {
                Problem("address", "Keep the address to 200 characters.");
            }
            var nationalId = Number("nationalId", EmployeeNumbers.NationalId, "Enter the ID number: 7 or 8 digits, or 9 for a Maisha Namba.");
            var kraPin = Number("kraPin", EmployeeNumbers.KraPin, "Enter the person's KRA PIN: A, 9 digits and a letter, like A012345678Z.");
            var nssf = Number("nssfNo", EmployeeNumbers.Nssf, "Enter the NSSF number: 7 to 10 digits.");
            var sha = Number("shaNo", EmployeeNumbers.Sha, "Enter the SHA number: 6 to 20 letters, numbers or dashes.");

            // No two current employees share a statutory number, phone or email: those already there
            // (a re-import), then other rows. Encrypted numbers are compared by their keyed lookup; M-Pesa
            // numbers and bank accounts (stored encrypted, without one) only between rows.
            void Unique(string? value, string key, string label, string? kind = null)
            {
                if (string.IsNullOrEmpty(value) || exit is not null) return;
                var compared = kind is null ? value : encryptor.Lookup(value, $"{kind}:{organizationId}");
                if (taken.GetValueOrDefault(key)?.TryGetValue(compared, out var holder) == true)
                {
                    Problem(key, $"{holder} already has this {label}.");
                }
                else if (fileNumbers.TryGetValue(key + compared, out var other))
                {
                    Problem(key, $"Row {other} has this {label} too.");
                    Earlier(other, line, key, label);
                }
                else
                {
                    fileNumbers[key + compared] = line;
                }
            }

            Unique(phone, "phone", "phone number");
            Unique(email, "email", "email address");
            Unique(nationalId, "nationalId", "ID number", "national_id");
            Unique(kraPin, "kraPin", "KRA PIN", "kra_pin");
            Unique(nssf, "nssfNo", "NSSF number");
            Unique(sha, "shaNo", "SHA number");

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
            // Where they work is the company's main location (their own address is a column).
            var locationId = company.MainLocationId;

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

            // Paid as the schedule pays: a monthly salary, a weekly wage, a daily rate.
            (decimal, string)? pay = null;
            var payText = Cell("basicPay");
            if (payText.Length > 0)
            {
                if (ParseAmount(payText) is decimal amount && amount > 0)
                {
                    pay = (amount, schedule?.Frequency ?? "monthly");
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
                    Problem("branch", BranchProblem(bank, branchText));
                }

                if (accountText.Length == 0)
                {
                    Problem("accountNumber", "Enter the account number.");
                }
                else if (bank is not null && EmployeesEndpoints.AccountNumberProblem(bank, accountText) is { } accountProblem)
                {
                    Problem("accountNumber", accountProblem);
                }

                if (mpesaText.Length > 0)
                {
                    Problem("mpesa", "Give a bank account or an M-Pesa number, not both.");
                }

                account = new EmployeesEndpoints.ValidAccount("bank", bank?.Code, branch?.Code, accountText, null);
            }
            else if (mpesaText.Length > 0)
            {
                var mpesa = EmployeeNumbers.Mpesa(mpesaText);
                if (mpesa is null)
                {
                    Problem("mpesa", EmployeesEndpoints.MpesaMessage);
                }

                account = new EmployeesEndpoints.ValidAccount("mpesa", null, null, null, mpesa);
            }

            if (account is { Method: "bank", BankCode: { } accountBank } && accountText.Length > 0)
            {
                Unique($"{accountBank}:{accountText}", "accountNumber", "bank account");
            }
            else if (account is { Method: "mpesa", MpesaPhone: { } wallet })
            {
                Unique(wallet, "mpesa", "M-Pesa number");
            }

            // Paid into a bank or M-Pesa: as Paid into says, and for current staff one the company pays from.
            var paidText = Regex.Replace(Cell("paidInto"), @"[\s-]", "").ToLowerInvariant();
            string? paidInto = paidText switch { "" => null, "bank" => "bank", "mpesa" => "mpesa", _ => "?" };
            var methods = payMethods.GetValueOrDefault(company.Id) ?? [];
            if (paidInto == "?")
            {
                Problem("paidInto", "Choose Bank or M-Pesa.");
            }
            else if (paidInto is not null && account?.Method != paidInto)
            {
                Problem(paidInto == "bank" ? "bank" : "mpesa", paidInto == "bank" ? "Paid into a bank: add their bank details." : "Paid into M-Pesa: add their M-Pesa number.");
            }
            else if (exit is null && methods.Count > 0 && (account is null || !methods.Contains(account.Method)))
            {
                var column = account is not null ? (account.Method == "bank" ? "bank" : "mpesa") : methods.Count > 1 ? "paidInto" : methods[0] == "bank" ? "bank" : "mpesa";
                Problem(column, EmployeesEndpoints.PaysByMessage(company.LegalName, methods));
            }

            if (problems.Count > before || hire is null || type is null || schedule is null || plan is null)
            {
                continue;
            }

            var job = new EmployeesEndpoints.ValidJob(positionId, departmentId, unitId, locationId, null, schedule.Id, plan.Id, type, contractEnd);
            valid.Add(new ValidRow(line, new EmployeesEndpoints.ValidEmployee(company, staffNo.Length == 0 ? null : staffNo, first, middle.Length == 0 ? null : middle,
                last, born, phone, email, address.Length == 0 ? null : address, nationalId, kraPin, nssf, sha, hire.Value, exit, job, pay, account),
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
