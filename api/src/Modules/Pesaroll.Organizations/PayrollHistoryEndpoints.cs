using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Security;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>An upload of payroll history: its company, file, rows, months and totals, and when.</summary>
/// <summary>A row of an import: whose month (staff number and name as in Pesaroll) and its figures.</summary>
public sealed record PayrollHistoryLineResponse(Guid Id, Guid EmployeeId, string StaffNo, string Name, DateOnly Period, DateOnly? PayDate, decimal BasicPay,
    decimal? CashAllowances, decimal? NonCashBenefits, decimal? ValueOfQuarters, decimal GrossPay, decimal? Pension, decimal? NssfTier1, decimal? NssfTier2,
    decimal NssfEmployee, decimal? NssfEmployer, decimal Shif, decimal? HousingLevyEmployee, decimal? HousingLevyEmployer, decimal? PostRetirementMedical,
    decimal? MortgageInterest, decimal? TaxablePay, decimal? PersonalRelief, decimal? InsuranceRelief, decimal Paye, decimal? OtherDeductions, decimal? NetPay,
    decimal? Nita, string? Notes);

public sealed record PayrollHistoryBatchResponse(Guid Id, Guid CompanyId, string FileName, int RowCount, DateOnly FirstPeriod, DateOnly LastPeriod, decimal GrossTotal,
    decimal PayeTotal, DateTimeOffset CreatedAt);

/// <summary>
/// ONB-10, payroll history from the previous system (the shared spreadsheet import): one row per
/// employee per month, for one company chosen first, from January two years back to last month
/// (Pesaroll runs payroll from this month). People are found by staff number, then ID number, then
/// KRA PIN; someone not in Pesaroll is added as a former employee (from their first month to the
/// end of their last). Import saves the rows that pass and returns the others with their problems;
/// warnings (net pay that doesn't add up, a KRA PIN unlike their profile) don't block. Each upload is a batch: listed with its totals (all companies, or those chosen), and deleted with its rows.
/// </summary>
public static class PayrollHistoryEndpoints
{
    private const int MaxRows = 20000;
    private const int MaxMegabytes = 10;
    /// <summary>The Housing Levy was deducted from March 2024.</summary>
    private static readonly DateOnly HousingLevyFrom = new(2024, 3, 1);

    public static readonly IReadOnlyList<ImportColumn> Columns =
    [
        new("company", "Company", InTemplate: false), new("staffNo", "Employee number", "@"), new("kraPin", "KRA PIN", "@"),
        new("nationalId", "ID or passport number", "@"), new("name", "Employee name"), new("month", "Pay month (YYYY-MM)", "@"),
        ImportColumn.Date("payDate", "Pay date"), new("basic", "Basic pay amount"), new("allowances", "Cash allowances amount"), new("benefits", "Non-cash benefits amount"),
        new("quarters", "Value of quarters amount"), new("gross", "Gross pay amount"), new("pension", "Pension (employee) amount"), new("nssf1", "NSSF employee Tier I amount"),
        new("nssf2", "NSSF employee Tier II amount"), new("nssf", "NSSF employee total amount"), new("nssfEr", "NSSF employer total amount"), new("shif", "SHIF / NHIF amount"),
        new("ahl", "Housing Levy (employee) amount"), new("ahlEr", "Housing Levy (employer) amount"), new("prmf", "Post-retirement medical fund amount"),
        new("mortgage", "Mortgage interest amount"), new("taxable", "Taxable pay amount"), new("relief", "Personal relief amount"), new("insurance", "Insurance relief amount"),
        new("paye", "PAYE amount"), new("other", "Other deductions amount"), new("net", "Net pay amount"), new("nita", "NITA amount"), new("notes", "Notes"),
    ];

    /// <summary>The amount columns: key, how a message names it, and whether every row needs it.</summary>
    private static readonly (string Key, string Label, bool Required)[] Amounts =
    [
        ("basic", "basic pay", true), ("allowances", "cash allowances", false), ("benefits", "non-cash benefits", false), ("quarters", "value of quarters", false),
        ("gross", "gross pay", true), ("pension", "pension", false), ("nssf1", "NSSF Tier I", false), ("nssf2", "NSSF Tier II", false),
        ("nssf", "NSSF employee total", false), ("nssfEr", "NSSF employer total", false), ("shif", "SHIF / NHIF", true), ("ahl", "Housing Levy", false),
        ("ahlEr", "employer's Housing Levy", false), ("prmf", "post-retirement medical fund", false), ("mortgage", "mortgage interest", false),
        ("taxable", "taxable pay", false), ("relief", "personal relief", false), ("insurance", "insurance relief", false), ("paye", "PAYE", true),
        ("other", "other deductions", false), ("net", "net pay", true), ("nita", "NITA", false),
    ];

    public static IEndpointRouteBuilder MapPayrollHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var history = app.MapGroup("/api/payroll-history").WithTags("Payroll history");
        SpreadsheetImport.MapImport(history, "", new ImportSpec<ValidLine>
        {
            Things = "payroll history",
            Permission = Permissions.PayrollHistory,
            PerCompany = true,
            Columns = Columns,
            RequiredKeys = ["kraPin", "month"],
            MaxRows = MaxRows,
            MaxMegabytes = MaxMegabytes,
            Choices = (_, _, _) => Task.FromResult(new Dictionary<string, List<string>>()),
            Check = (rows, db, services, ct) => CheckAsync(rows, db, services, ct),
            Save = (rows, request, organizationId, db, now, services, ct) => SaveAsync(rows, request, organizationId, db, now, services, ct),
            // A person's month stands alone: the good rows are saved, the others come back to fix.
            SavesWhatPasses = true,
        });

        // Every company's imports, or the chosen companies', newest first.
        history.MapGet("/batches", async (Guid[]? companyIds, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var query = db.PayrollHistoryBatches.AsNoTracking();
            if (companyIds is { Length: > 0 })
            {
                query = query.Where(b => companyIds.Contains(b.CompanyId));
            }

            var batches = await query.OrderByDescending(b => b.CreatedAt)
                .Select(b => new PayrollHistoryBatchResponse(b.Id, b.CompanyId, b.FileName, b.RowCount, b.FirstPeriod, b.LastPeriod, b.GrossTotal, b.PayeTotal, b.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(batches);
        }).Produces<List<PayrollHistoryBatchResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        // An import's rows, by month then person, with whose they are.
        history.MapGet("/batches/{id:guid}/lines", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            if (!await db.PayrollHistoryBatches.AnyAsync(b => b.Id == id, ct))
            {
                throw OnboardingErrors.HistoryBatchNotFound();
            }

            var lines = await (from l in db.PayrollHistoryLines.AsNoTracking()
                               join e in db.Employees.AsNoTracking() on l.EmployeeId equals e.Id
                               where l.BatchId == id
                               orderby l.Period, e.StaffNo
                               select new PayrollHistoryLineResponse(l.Id, e.Id, e.StaffNo, e.FirstName + " " + e.LastName, l.Period, l.PayDate, l.BasicPay,
                                   l.CashAllowances, l.NonCashBenefits, l.ValueOfQuarters, l.GrossPay, l.Pension, l.NssfTier1, l.NssfTier2, l.NssfEmployee,
                                   l.NssfEmployer, l.Shif, l.HousingLevyEmployee, l.HousingLevyEmployer, l.PostRetirementMedical, l.MortgageInterest,
                                   l.TaxablePay, l.PersonalRelief, l.InsuranceRelief, l.Paye, l.OtherDeductions, l.NetPay, l.Nita, l.Notes)).ToListAsync(ct);
            return Results.Ok(lines);
        }).Produces<List<PayrollHistoryLineResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        // Takes the batch's months (the people it added stay, as former employees).
        history.MapDelete("/batches/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var batch = await db.PayrollHistoryBatches.SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw OnboardingErrors.HistoryBatchNotFound();
            Access.Require(tenant, Permissions.PayrollHistory, batch.CompanyId);
            db.PayrollHistoryBatches.Remove(batch);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// A checked row: whose month (someone there, or someone to add as a former employee, keyed by KRA
    /// PIN, as checked), and its figures.
    /// </summary>
    private sealed record ValidLine(int Line, Company Company, Guid? EmployeeId, string? NewPerson, PayrollHistoryLine Figures,
        EmployeesEndpoints.ValidEmployee? NewEmployee = null);

    /// <summary>Someone there, with what finds them.</summary>
    private sealed record Existing(Guid Id, string StaffNo, string Name, string? KraPinLookup, string? NationalIdLookup);

    /// <summary>"2026-01", "2026-1", "01/2026", "Jan 2026", "January 2026", or a date in the month: its first day, or null.</summary>
    internal static DateOnly? ParseMonth(string text)
    {
        string[] formats = ["yyyy-MM", "yyyy-M", "MM/yyyy", "M/yyyy", "MMM yyyy", "MMMM yyyy", "MMM-yyyy", "MMM-yy", "yyyy/MM"];
        var trimmed = text.Trim();
        if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var month))
        {
            return new DateOnly(month.Year, month.Month, 1);
        }

        return SpreadsheetImport.ParseDate(trimmed) is DateOnly date ? new DateOnly(date.Year, date.Month, 1) : null;
    }

    private static string MonthName(DateOnly period) => period.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    private static string Kes(decimal amount) => $"KES {amount.ToString("#,##0.##", CultureInfo.InvariantCulture)}";

    private static async Task<(List<ValidLine> Rows, List<ImportProblem> Problems, List<ImportProblem> Notes)> CheckAsync(
        List<ImportRow> raw, OrganizationsDbContext db, IServiceProvider services, CancellationToken ct)
    {
        var encryptor = services.GetRequiredService<FieldEncryptor>();
        var today = PayDates.TodayInKenya(services.GetRequiredService<TimeProvider>().GetUtcNow());
        // Last month at the latest (Pesaroll runs this month's payroll), from January two years back.
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var earliest = new DateOnly(today.Year - 2, 1, 1);

        var companies = await db.Companies.AsNoTracking().Where(c => c.Status == "active").ToListAsync(ct);
        var organizationId = companies.FirstOrDefault()?.OrganizationId ?? Guid.Empty;
        var people = new Dictionary<Guid, List<Existing>>();
        var saved = new Dictionary<Guid, HashSet<(Guid, DateOnly)>>();

        var problems = new List<ImportProblem>();
        var notes = new List<ImportProblem>();
        var valid = new List<ValidLine>();
        // Each person's months in the file: (company, person) → month → row; new people are keyed by KRA PIN.
        var fileMonths = new Dictionary<(Guid, string), Dictionary<DateOnly, int>>();
        var newPeople = new Dictionary<(Guid, string), (int Line, string Name, string? StaffNo, string? NationalId, string KraPin, Company Company, List<DateOnly> Months)>();

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

            if (!people.TryGetValue(company.Id, out var staff))
            {
                staff = people[company.Id] = await db.Employees.AsNoTracking().Where(e => e.CompanyId == company.Id)
                    .Select(e => new Existing(e.Id, e.StaffNo, e.FirstName + " " + e.LastName, e.KraPinLookup, e.NationalIdLookup)).ToListAsync(ct);
                saved[company.Id] = (await db.PayrollHistoryLines.AsNoTracking().Where(l => l.CompanyId == company.Id)
                    .Select(l => new { l.EmployeeId, l.Period }).ToListAsync(ct)).Select(l => (l.EmployeeId, l.Period)).ToHashSet();
            }

            // Who: by staff number, then ID number, then KRA PIN.
            var staffNo = Cell("staffNo").ToUpperInvariant();
            var kraText = Cell("kraPin");
            var kraPin = EmployeeNumbers.KraPin(kraText);
            if (kraPin is null)
            {
                Problem("kraPin", "Enter their KRA PIN: A, 9 digits and a letter, like A012345678Z.");
            }

            var idText = Cell("nationalId");
            var nationalId = idText.Length == 0 ? null : EmployeeNumbers.NationalId(idText);
            if (idText.Length > 0 && nationalId is null)
            {
                Problem("nationalId", "Enter the ID number: 7 or 8 digits, or 9 for a Maisha Namba.");
            }

            var name = Regex.Replace(Cell("name"), @"\s+", " ");
            if (name.Length is 0 or > 120)
            {
                Problem("name", "Enter their name as on payroll (up to 120 characters).");
            }

            var idLookup = nationalId is null ? null : encryptor.Lookup(nationalId, $"national_id:{organizationId}");
            var pinLookup = kraPin is null ? null : encryptor.Lookup(kraPin, $"kra_pin:{organizationId}");
            var person = (staffNo.Length > 0 ? staff.FirstOrDefault(e => e.StaffNo == staffNo) : null)
                ?? (idLookup is null ? null : staff.FirstOrDefault(e => e.NationalIdLookup == idLookup))
                ?? (pinLookup is null ? null : staff.FirstOrDefault(e => e.KraPinLookup == pinLookup));

            // Which month: a real one, in the range.
            var monthText = Cell("month");
            var period = ParseMonth(monthText);
            if (period is null)
            {
                Problem("month", monthText.Length == 0 ? "Enter the month this pay is for, like 2026-01." : "Enter the month as YYYY-MM, like 2026-01.");
            }
            else if (period >= thisMonth)
            {
                Problem("month", $"Payroll history goes up to {MonthName(thisMonth.AddMonths(-1))}: Pesaroll runs payroll from {MonthName(thisMonth)}.");
            }
            else if (period < earliest)
            {
                Problem("month", $"Payroll history starts from {MonthName(earliest)}. Leave out earlier months.");
            }

            var payDateText = Cell("payDate");
            var payDate = payDateText.Length == 0 ? null : SpreadsheetImport.ParseDate(payDateText);
            if (payDateText.Length > 0 && payDate is null)
            {
                Problem("payDate", "Enter the pay date as DD/MM/YYYY, or leave it empty.");
            }

            // The figures: amounts of 0 or more; the required ones there.
            var amounts = new Dictionary<string, decimal?>();
            foreach (var (key, label, required) in Amounts)
            {
                var text = Cell(key);
                if (text.Length == 0)
                {
                    amounts[key] = null;
                    if (required)
                    {
                        Problem(key, $"Enter the {label}, or 0 if there was none.");
                    }

                    continue;
                }

                amounts[key] = EmployeesImport.ParseAmount(text);
                if (amounts[key] is null)
                {
                    Problem(key, $"Enter the {label} as an amount of 0 or more, like 85,000.");
                }
            }

            var nssf = amounts["nssf"] ?? (amounts["nssf1"] is null && amounts["nssf2"] is null ? null : (amounts["nssf1"] ?? 0) + (amounts["nssf2"] ?? 0));
            if (nssf is null && Cell("nssf").Length == 0 && Cell("nssf1").Length == 0 && Cell("nssf2").Length == 0)
            {
                Problem("nssf", "Enter the employee's NSSF total, or Tier I and Tier II.");
            }

            if (period >= HousingLevyFrom && Cell("ahl").Length == 0)
            {
                Problem("ahl", "Enter the Housing Levy deducted: it applies from March 2024.");
            }

            var notesText = Cell("notes");
            if (notesText.Length > 200)
            {
                Problem("notes", "Keep notes to 200 characters.");
            }

            // Once a month per person: not twice in the file, nor already imported.
            var who = person is not null ? (company.Id, person.Id.ToString()) : (company.Id, kraPin ?? $"line:{line}");
            var whoName = person?.Name ?? name;
            if (period is DateOnly month && problems.Count == before)
            {
                if (person is not null && saved[company.Id].Contains((person.Id, month)))
                {
                    Problem("month", $"{whoName}'s {MonthName(month)} is already imported.");
                }
                else if (fileMonths.TryGetValue(who, out var months) && months.TryGetValue(month, out var other))
                {
                    Problem("month", $"Row {other} has {whoName}'s {MonthName(month)} too.");
                }
                else
                {
                    (fileMonths.TryGetValue(who, out var m) ? m : fileMonths[who] = [])[month] = line;
                }
            }

            if (problems.Count > before)
            {
                continue;
            }

            // Someone not in Pesaroll: added as a former employee, once (noted on their first row).
            if (person is null)
            {
                if (newPeople.TryGetValue(who, out var known))
                {
                    known.Months.Add(period!.Value);
                }
                else
                {
                    newPeople[who] = (line, name, staffNo.Length > 0 ? staffNo : null, nationalId, kraPin!, company, [period!.Value]);
                    notes.Add(new ImportProblem(line, "name",
                        $"{name} isn't in Pesaroll: they'll be added as someone who has left. If they still work here, add them under Employees first."));
                }
            }

            // Warnings: what to check, without blocking.
            decimal Of(string key) => amounts[key] ?? 0;
            var gross = Of("gross");
            if (amounts["net"] is decimal net)
            {
                var expected = gross - Of("benefits") - Of("quarters") - (nssf ?? 0) - Of("shif") - Of("ahl") - Of("paye") - Of("pension") - Of("prmf") - Of("other");
                if (Math.Abs(net - expected) > Math.Max(5m, gross * 0.005m))
                {
                    notes.Add(new ImportProblem(line, "net",
                        $"Net pay is {Kes(net)}, but gross less deductions is {Kes(expected)}. Check the row, or carry on if their old payroll had other items.", "warning"));
                }
            }

            if (person?.KraPinLookup is { } profilePin && pinLookup is not null && profilePin != pinLookup)
            {
                notes.Add(new ImportProblem(line, "kraPin", $"This KRA PIN isn't the one on {person.Name}'s profile. Check which is right.", "warning"));
            }

            valid.Add(new ValidLine(line, company, person?.Id, person is null ? who.Item2 : null, new PayrollHistoryLine
            {
                Period = period!.Value, PayDate = payDate, BasicPay = Of("basic"), CashAllowances = amounts["allowances"], NonCashBenefits = amounts["benefits"],
                ValueOfQuarters = amounts["quarters"], GrossPay = gross, Pension = amounts["pension"], NssfTier1 = amounts["nssf1"], NssfTier2 = amounts["nssf2"],
                NssfEmployee = nssf ?? 0, NssfEmployer = amounts["nssfEr"], Shif = Of("shif"), HousingLevyEmployee = amounts["ahl"], HousingLevyEmployer = amounts["ahlEr"],
                PostRetirementMedical = amounts["prmf"], MortgageInterest = amounts["mortgage"], TaxablePay = amounts["taxable"], PersonalRelief = amounts["relief"],
                InsuranceRelief = amounts["insurance"], Paye = Of("paye"), OtherDeductions = amounts["other"], NetPay = amounts["net"], Nita = amounts["nita"],
                Notes = notesText.Length == 0 ? null : notesText,
            }));
        }

        // New people as adding them by hand would check them: from their first month to the end of their last.
        foreach (var (key, p) in newPeople)
        {
            var parts = p.Name.Split(' ', 2);
            var request = new EmployeeRequest(p.Company.Id, p.StaffNo, parts[0], null, parts.Length > 1 ? parts[1] : "", null, null, null, p.NationalId, p.KraPin,
                null, null, p.Months.Min(), p.Months.Max().AddMonths(1).AddDays(-1), new JobRequest(null, null, null, null, null, null, null, "permanent", null), null, null);
            try
            {
                var employee = await EmployeesEndpoints.ValidateAsync(request, null, db, ct);
                for (var i = 0; i < valid.Count; i++)
                {
                    if (valid[i].NewPerson == key.Item2 && valid[i].Company.Id == key.Item1)
                    {
                        valid[i] = valid[i] with { NewEmployee = employee };
                    }
                }
            }
            catch (AppException error) when (error.Fields is { Count: > 0 } fields)
            {
                foreach (var (field, message) in fields)
                {
                    var column = field switch { "staffNo" => "staffNo", "kraPin" => "kraPin", "nationalId" => "nationalId", _ => "name" };
                    problems.Add(new ImportProblem(p.Line, column, field == "lastName" ? "Enter their first and last name." : message));
                }

                notes.RemoveAll(n => n.Line == p.Line && n.Column == "name");
                valid.RemoveAll(v => v.NewPerson == key.Item2 && v.Company.Id == key.Item1);
            }
        }

        return (valid, problems, notes);
    }

    /// <summary>Saves every row or none: the new former employees, then the batch and its months.</summary>
    private static async Task<int> SaveAsync(List<ValidLine> rows, ImportRequest request, Guid organizationId, OrganizationsDbContext db, DateTimeOffset now,
        IServiceProvider services, CancellationToken ct)
    {
        var encryptor = services.GetRequiredService<FieldEncryptor>();
        var userId = services.GetRequiredService<TenantContext>().UserId!.Value;
        var today = PayDates.TodayInKenya(now);
        var fileName = string.IsNullOrWhiteSpace(request.FileName) ? "payroll-history.xlsx" : request.FileName.Trim()[..Math.Min(request.FileName.Trim().Length, 255)];

        foreach (var group in rows.GroupBy(r => r.Company.Id))
        {
            var companyId = group.Key;
            var added = new Dictionary<string, Guid>();
            // Staff numbers made up once for the whole upload, so two new people don't get the same one.
            var codes = new SpreadsheetImport.CodeMaker(
                [.. await db.Employees.Where(e => e.CompanyId == companyId).Select(e => e.StaffNo).ToListAsync(ct),
                 .. group.Where(r => r.NewEmployee?.StaffNo is not null).Select(r => r.NewEmployee!.StaffNo!)]);
            foreach (var person in group.Where(r => r.NewPerson is not null).GroupBy(r => r.NewPerson!))
            {
                var check = person.First().NewEmployee!;
                var employee = new Employee
                {
                    Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = companyId,
                    StaffNo = check.StaffNo ?? codes.Next("EMP"),
                    FirstName = check.FirstName, LastName = check.LastName, CreatedAt = now,
                };
                var made = EmployeesEndpoints.Apply(employee, check, encryptor, now, today);
                employee.Employments.AddRange(made.OfType<Employment>());
                employee.Compensations.AddRange(made.OfType<Compensation>());
                employee.Accounts.AddRange(made.OfType<EmployeePaymentAccount>());
                db.Employees.Add(employee);
                added[person.Key] = employee.Id;
            }

            var lines = group.ToList();
            var batch = new PayrollHistoryBatch
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = companyId, FileName = fileName, RowCount = lines.Count,
                FirstPeriod = lines.Min(l => l.Figures.Period), LastPeriod = lines.Max(l => l.Figures.Period),
                GrossTotal = lines.Sum(l => l.Figures.GrossPay), PayeTotal = lines.Sum(l => l.Figures.Paye), ImportedBy = userId, CreatedAt = now,
            };
            db.PayrollHistoryBatches.Add(batch);
            foreach (var l in lines)
            {
                var figures = l.Figures;
                figures.Id = Guid.CreateVersion7();
                figures.OrganizationId = organizationId;
                figures.CompanyId = companyId;
                figures.BatchId = batch.Id;
                figures.EmployeeId = l.EmployeeId ?? added[l.NewPerson!];
                figures.CreatedAt = now;
                db.PayrollHistoryLines.Add(figures);
            }
        }

        await EmployeesEndpoints.SaveAsync(db, ct);   // one save: the people, the batch and its months, or none
        return rows.Count;
    }
}
