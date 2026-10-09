using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Pesaroll.Organizations;

/// <summary>
/// ONB-04 part 2, uploading locations with the shared spreadsheet import (template, read, check,
/// import). The rules are the same as adding a location by hand, plus the file's own: codes unique
/// in the file and at most one main location row.
/// </summary>
public static class LocationsImport
{
    private const string NameColumn = "name", TypeColumn = "kind", CodeColumn = "code", CountyColumn = "county",
        TownColumn = "town", AddressColumn = "address", MainColumn = "main";

    private static readonly Dictionary<string, string> KindLabels = new()
    {
        ["head_office"] = "Head office", ["branch"] = "Branch", ["office"] = "Office", ["warehouse"] = "Warehouse", ["site"] = "Site",
    };

    private sealed record ValidRow(int Line, string Name, string Kind, string? Code, string RegionCode, string? Town, string? AddressLine, bool Main);

    public static IEndpointRouteBuilder MapLocationsImportEndpoints(this IEndpointRouteBuilder app)
    {
        SpreadsheetImport.MapImport(app.MapGroup("/api/locations").WithTags("Locations"), "", new ImportSpec<ValidRow>
        {
            Things = "locations",
            Columns =
            [
                new(NameColumn, "Location name"), new(TypeColumn, "Type"), new(CodeColumn, "Code"), new(CountyColumn, "County"),
                new(TownColumn, "Town"), new(AddressColumn, "Address"), new(MainColumn, "Main location"),
            ],
            RequiredKeys = [NameColumn, CountyColumn],
            MaxRows = 1000,
            MaxMegabytes = 2,
            Choices = async (db, ct) => new Dictionary<string, List<string>>
            {
                [TypeColumn] = [.. KindLabels.Values],
                [CountyColumn] = (await RegionsAsync(db, ct)).Select(r => r.Name).ToList(),
                [MainColumn] = ["Yes", "No"],
            },
            Check = CheckAsync,
            Save = SaveAsync,
        });
        return app;
    }

    private static async Task<List<CountryRegion>> RegionsAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        var country = await db.Companies.AsNoTracking().Where(c => c.Status == "active").Select(c => c.CountryCode).FirstOrDefaultAsync(ct) ?? "KE";
        return await db.CountryRegions.AsNoTracking().Where(r => r.CountryCode == country).OrderBy(r => r.Name).ToListAsync(ct);
    }

    private static async Task<(List<ValidRow> Rows, List<ImportProblem> Problems, List<ImportProblem> Notes)> CheckAsync(List<ImportRow> raw, OrganizationsDbContext db, CancellationToken ct)
    {
        var regions = await RegionsAsync(db, ct);
        var taken = (await db.Locations.Where(l => l.Status == "active").Select(l => l.Code).ToListAsync(ct)).ToHashSet();
        var seenCodes = new HashSet<string>();
        var mainSeen = false;
        var rows = new List<ValidRow>();
        var problems = new List<ImportProblem>();

        foreach (var row in raw)
        {
            var before = problems.Count;
            void Problem(string column, string message) => problems.Add(new ImportProblem(row.Line, column, message));

            var name = row.Cells[NameColumn];
            if (name.Length is < 2 or > 120) Problem(NameColumn, "Enter the location's name (2 to 120 characters).");

            var typed = row.Cells[TypeColumn];
            var kind = typed.Length == 0 ? "branch"
                : KindLabels.FirstOrDefault(k => string.Equals(k.Value, typed, StringComparison.OrdinalIgnoreCase) || k.Key == typed).Key;
            if (kind is null) Problem(TypeColumn, $"\"{typed}\" isn't a type we know. Use Head office, Branch, Office, Warehouse or Site.");

            var code = row.Cells[CodeColumn].Length == 0 ? null : row.Cells[CodeColumn].ToUpperInvariant();
            if (code is not null)
            {
                if (!Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9-]{1,19}$")) Problem(CodeColumn, "Use 2 to 20 letters, numbers or dashes, or leave it empty for us to make one.");
                else if (taken.Contains(code)) Problem(CodeColumn, $"A location with the code {code} already exists.");
                else if (!seenCodes.Add(code)) Problem(CodeColumn, $"Another row in the file has the code {code}.");
            }

            var county = row.Cells[CountyColumn];
            var region = regions.FirstOrDefault(r => string.Equals(r.Name, county, StringComparison.OrdinalIgnoreCase) || r.Code == county);
            if (region is null) Problem(CountyColumn, county.Length == 0 ? "Choose a county from the list." : $"\"{county}\" isn't a county we know. Choose one from the list.");

            var town = row.Cells[TownColumn];
            if (town.Length > 100) Problem(TownColumn, "Use up to 100 characters.");
            var address = row.Cells[AddressColumn];
            if (address.Length > 200) Problem(AddressColumn, "Use up to 200 characters.");

            var mainText = row.Cells[MainColumn].ToLowerInvariant();
            bool? main = mainText is "" or "no" or "n" or "false" ? false : mainText is "yes" or "y" or "true" ? true : null;
            if (main is null) Problem(MainColumn, "Write Yes or No.");
            else if (main == true && mainSeen) Problem(MainColumn, "Only one row can be the main location.");
            else if (main == true) mainSeen = true;

            if (problems.Count == before)
            {
                rows.Add(new ValidRow(row.Line, name, kind!, code, region!.Code, town.Length == 0 ? null : town,
                    address.Length == 0 ? null : address, main == true));
            }
        }

        return (rows, problems, []);   // locations name nothing that could be created
    }

    private static async Task<int> SaveAsync(List<ValidRow> valid, ImportRequest request, Guid organizationId, OrganizationsDbContext db,
        DateTimeOffset now, CancellationToken ct)
    {
        var companies = await SpreadsheetImport.ChosenCompaniesAsync(request, db, OnboardingErrors.LocationDetailsInvalid, ct, sameCountry: true);
        var countryCode = companies[0].CountryCode;
        var codes = new SpreadsheetImport.CodeMaker(await db.Locations.Select(l => l.Code).ToListAsync(ct));
        foreach (var row in valid)
        {
            var location = new Location
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = organizationId,
                Code = row.Code ?? codes.Next(LocationsEndpoints.Prefix(row.Town ?? row.Name)),
                Name = row.Name,
                Kind = row.Kind,
                CountryCode = countryCode,
                RegionCode = row.RegionCode,
                Town = row.Town,
                AddressLine = row.AddressLine,
                WageAreaCode = await LocationsEndpoints.SuggestWageAreaAsync(db, countryCode, row.Town, ct),
                AllCompanies = request.AllCompanies,
                CreatedAt = now,
                UpdatedAt = now,
            };
            if (row.Code is not null)
            {
                codes.Take(row.Code);
            }

            if (!request.AllCompanies)
            {
                location.Companies.AddRange(companies.Select(c => new LocationCompany
                {
                    OrganizationId = organizationId, LocationId = location.Id, CompanyId = c.Id, CreatedAt = now,
                }));
            }

            db.Locations.Add(location);
            if (row.Main)
            {
                foreach (var company in companies)
                {
                    db.Companies.Attach(company);
                    company.MainLocationId = location.Id;
                }
            }
        }

        await db.SaveChangesAsync(ct);   // one save: every row or none
        return valid.Count;
    }
}
