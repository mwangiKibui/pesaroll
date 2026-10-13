using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Shared.Errors;

namespace Pesaroll.Organizations;

/// <summary>
/// ONB-07 #196, uploading earnings and deductions with the shared spreadsheet import. Each row is
/// checked by the same rules as adding one by hand (<see cref="PayItemsEndpoints.ValidateAsync"/>),
/// plus the file's own: codes unique among the organization's items and in the file, never the name
/// or code of an item set by law or Basic pay, and payees by name (they're added first: they need
/// payment details). Apply to (the companies) is chosen in the review.
/// </summary>
public static class PayItemsImport
{
    private const string Name = "name", Code = "code", Kind = "kind", Calc = "calcMethod", Amount = "defaultAmount", Rate = "rate",
        Taxed = "taxable", BeforeTax = "beforeTax", Payee = "payee";

    private static readonly Dictionary<string, string> KindLabels = new()
    {
        ["earning"] = "Earning", ["deduction"] = "Deduction", ["employer_cost"] = "Employer cost",
    };

    private static readonly Dictionary<string, string> CalcLabels = new()
    {
        ["fixed"] = "Fixed amount", ["percent_of_basic"] = "Percentage of basic pay", ["each_run"] = "Entered each payroll",
    };

    private sealed record ValidRow(int Line, PayItemsEndpoints.ValidItem Item);

    public static IEndpointRouteBuilder MapPayItemsImportEndpoints(this IEndpointRouteBuilder app)
    {
        SpreadsheetImport.MapImport(app.MapGroup("/api/pay-items").WithTags("Earnings and deductions"), "", new ImportSpec<ValidRow>
        {
            Things = "earnings and deductions",
            Columns =
            [
                new(Name, "Name"), new(Code, "Code"), new(Kind, "Type"), new(Calc, "How it's worked out"), new(Amount, "Default amount"),
                new(Rate, "Rate (%)"), new(Taxed, "Taxed"), new(BeforeTax, "Before tax"), new(Payee, "Paid to"),
            ],
            RequiredKeys = [Name, Kind],
            MaxRows = 1000,
            MaxMegabytes = 2,
            Choices = async (db, _, ct) => new Dictionary<string, List<string>>
            {
                [Kind] = [.. KindLabels.Values],
                [Calc] = [.. CalcLabels.Values],
                [Taxed] = ["Yes", "No"],
                [BeforeTax] = ["Yes", "No"],
                [Payee] = await db.Payees.AsNoTracking().OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(ct),
            },
            Check = (rows, db, _, ct) => CheckAsync(rows, db, ct),
            Save = (rows, request, org, db, now, _, ct) => SaveAsync(rows, request, org, db, now, ct),
        });
        return app;
    }

    private static async Task<(List<ValidRow> Rows, List<ImportProblem> Problems, List<ImportProblem> Notes)> CheckAsync(List<ImportRow> raw,
        OrganizationsDbContext db, CancellationToken ct)
    {
        var items = await db.PayItems.AsNoTracking().Where(i => i.Status == "active").Select(i => new { i.Code, i.Name, i.LockedKey }).ToListAsync(ct);
        var locked = items.Where(i => i.LockedKey is not null).ToList();
        var taken = items.Select(i => i.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var payees = await db.Payees.AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ValidRow>();
        var problems = new List<ImportProblem>();

        foreach (var row in raw)
        {
            var before = problems.Count;
            string Cell(string key) => row.Cells.GetValueOrDefault(key)?.Trim() ?? "";
            void Problem(string column, string message) => problems.Add(new ImportProblem(row.Line, column, message));
            static string? Key(Dictionary<string, string> labels, string typed) =>
                labels.FirstOrDefault(l => string.Equals(l.Value, typed, StringComparison.OrdinalIgnoreCase) || l.Key == typed).Key;

            var name = Cell(Name);
            var code = Cell(Code).ToUpperInvariant();
            var lawName = locked.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            var lawCode = locked.FirstOrDefault(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
            string Already(string what, string? key) => key == "basic" ? $"{what} is always included and is already there." : $"{what} is set by law and is already there.";
            if (lawName is not null)
            {
                Problem(Name, Already(lawName.Name, lawName.LockedKey));
            }

            if (code.Length > 0 && lawCode is not null)
            {
                Problem(Code, Already(lawCode.Name, lawCode.LockedKey));
            }
            else if (code.Length > 0 && taken.Contains(code))
            {
                Problem(Code, $"An earning or deduction with the code {code} already exists.");
            }
            else if (code.Length > 0 && !seenCodes.Add(code))
            {
                Problem(Code, $"Another row in the file has the code {code}.");
            }

            var kind = Key(KindLabels, Cell(Kind));
            if (kind is null)
            {
                Problem(Kind, $"\"{Cell(Kind)}\" isn't a type we know. Use Earning, Deduction or Employer cost.");
            }

            var calcText = Cell(Calc);
            var calc = calcText.Length == 0 ? "fixed" : Key(CalcLabels, calcText);
            if (calc is null)
            {
                Problem(Calc, "Choose Fixed amount, Percentage of basic pay or Entered each payroll.");
            }

            decimal? Number(string key, string label)
            {
                var text = Cell(key);
                if (text.Length == 0) return null;
                var value = EmployeesImport.ParseAmount(text);
                if (value is null) Problem(key, $"Enter the {label} as a number.");
                return value;
            }

            var amount = Number(Amount, "default amount");
            var rate = Number(Rate, "rate");
            bool? YesNo(string key)
            {
                var text = Cell(key).ToLowerInvariant();
                bool? value = text is "" ? null : text is "yes" or "y" or "true" ? true : text is "no" or "n" or "false" ? false : null;
                if (text.Length > 0 && value is null) Problem(key, "Write Yes or No.");
                return value;
            }

            var taxed = YesNo(Taxed);
            var beforeTax = YesNo(BeforeTax);
            var payeeText = Cell(Payee);
            var payee = payeeText.Length == 0 ? null : payees.FirstOrDefault(p => string.Equals(p.Name, payeeText, StringComparison.OrdinalIgnoreCase));
            if (payeeText.Length > 0 && payee is null)
            {
                Problem(Payee, $"No payee is called {payeeText}. Add it as a payee first, then upload again.");
            }

            if (problems.Count > before || kind is null || calc is null)
            {
                continue;
            }

            // The rest as adding one by hand: name, code format, rate, amount, payee only for deductions and employer costs.
            try
            {
                var valid = await PayItemsEndpoints.ValidateAsync(new PayItemRequest(code.Length == 0 ? null : code, name, kind,
                    Taxable: taxed ?? kind == "earning", BeforeTax: beforeTax ?? false, calc, amount, rate, AllCompanies: true, CompanyIds: [], PayeeId: payee?.Id), db, ct);
                rows.Add(new ValidRow(row.Line, valid));
            }
            catch (AppException error) when (error.Fields is { } fields)
            {
                foreach (var (field, message) in fields)
                {
                    Problem(field switch { "payeeId" => Payee, "kind" => Kind, "calcMethod" => Calc, _ => field }, message);
                }
            }
        }

        return (rows, problems, []);
    }

    private static async Task<int> SaveAsync(List<ValidRow> valid, ImportRequest request, Guid organizationId, OrganizationsDbContext db,
        DateTimeOffset now, CancellationToken ct)
    {
        var companies = await SpreadsheetImport.ChosenCompaniesAsync(request, db, OnboardingErrors.PayItemDetailsInvalid, ct);
        var codes = new SpreadsheetImport.CodeMaker(await db.PayItems.Select(i => i.Code).ToListAsync(ct));
        foreach (var row in valid.Where(r => r.Item.Code is not null))
        {
            codes.Take(row.Item.Code!);
        }

        foreach (var row in valid)
        {
            var item = new PayItem
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = row.Item.Name, Kind = row.Item.Kind, CalcMethod = row.Item.CalcMethod,
                Code = row.Item.Code ?? codes.Next(LocationsEndpoints.Prefix(row.Item.Name)), CreatedAt = now,
            };
            PayItemsEndpoints.Apply(item, row.Item with { AllCompanies = request.AllCompanies, CompanyIds = request.AllCompanies ? [] : companies.Select(c => c.Id).ToList() }, now);
            db.PayItems.Add(item);
        }

        await db.SaveChangesAsync(ct);   // one save: every row or none
        return valid.Count;
    }
}
