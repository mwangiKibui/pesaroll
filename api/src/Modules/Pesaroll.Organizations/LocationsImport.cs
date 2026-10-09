using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>A checked row: <see cref="Line"/> is its row number in the file (the header is row 1).</summary>
public sealed record LocationImportRow(int Line, string Name, string Kind, string? Code, string RegionCode, string? Town, string? AddressLine, bool Main);

/// <summary>A problem in the file: the row, the template column it is in (when it is about one) and what to fix.</summary>
public sealed record LocationImportProblem(int Line, string? Column, string Message);

public sealed record LocationImportCheck(IReadOnlyList<LocationImportRow> Rows, IReadOnlyList<LocationImportProblem> Problems);

public sealed record LocationImportRequest(List<LocationImportRow>? Rows, bool AllCompanies, List<Guid>? CompanyIds);

public sealed record LocationImportResult(int Imported);

/// <summary>
/// ONB-04 part 2, uploading locations (the first user of the BulkUpload pattern): download the
/// template, upload a filled copy to check it, then import the checked rows for the chosen
/// companies in one go. The rules are the same as adding a location by hand; the import checks
/// the rows again, so nothing the browser sends is trusted.
/// </summary>
public static class LocationsImport
{
    public const int MaxRows = 1000;
    public const long MaxBytes = 2 * 1024 * 1024;

    // The template's columns, in order; files are read by these names, so columns can move.
    private const string NameColumn = "Location name", TypeColumn = "Type", CodeColumn = "Code", CountyColumn = "County",
        TownColumn = "Town", AddressColumn = "Address", MainColumn = "Main location";
    private static readonly string[] Columns = [NameColumn, TypeColumn, CodeColumn, CountyColumn, TownColumn, AddressColumn, MainColumn];

    private static readonly Dictionary<string, string> KindLabels = new()
    {
        ["head_office"] = "Head office", ["branch"] = "Branch", ["office"] = "Office", ["warehouse"] = "Warehouse", ["site"] = "Site",
    };

    private sealed record RawRow(int Line, Dictionary<string, string> Cells);

    public static IEndpointRouteBuilder MapLocationsImportEndpoints(this IEndpointRouteBuilder app)
    {
        var import = app.MapGroup("/api/locations").WithTags("Locations");

        import.MapGet("/template", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var regions = await RegionsAsync(db, ct);
            return Results.File(Template(regions.Select(r => r.Name).ToList()), Xlsx, "pesaroll-locations-template.xlsx");
        }).Produces(StatusCodes.Status200OK, contentType: Xlsx).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        import.MapPost("/import/check", async (IFormFile file, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            if (file.Length > MaxBytes)
            {
                throw TooBig();
            }

            var raw = await ReadAsync(file, ct);
            return Results.Ok(await CheckAsync(raw, db, ct));
        }).DisableAntiforgery()   // the origin check (OriginCheckMiddleware) protects it, as for JSON requests
          .Produces<LocationImportCheck>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        import.MapPost("/import", async (LocationImportRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var rows = request.Rows ?? [];
            if (rows.Count > MaxRows)
            {
                throw TooBig();
            }

            var companies = await ChosenCompaniesAsync(request, db, ct);
            var raw = rows.Select(r => new RawRow(r.Line, new Dictionary<string, string>
            {
                [NameColumn] = r.Name, [TypeColumn] = r.Kind, [CodeColumn] = r.Code ?? "", [CountyColumn] = r.RegionCode,
                [TownColumn] = r.Town ?? "", [AddressColumn] = r.AddressLine ?? "", [MainColumn] = r.Main ? "Yes" : "No",
            })).ToList();
            var check = await CheckAsync(raw, db, ct);
            if (check.Problems.Count > 0)
            {
                throw new AppException("ONB-3518", "Some rows need fixing. Upload the file again to see which.");
            }

            var now = clock.GetUtcNow();
            var countryCode = companies[0].CountryCode;
            foreach (var row in check.Rows)
            {
                var location = new Location
                {
                    Id = Guid.CreateVersion7(),
                    OrganizationId = organizationId,
                    Code = row.Code ?? await LocationsEndpoints.NextCodeAsync(db, LocationsEndpoints.Prefix(row.Town ?? row.Name), ct),
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
            return Results.Ok(new LocationImportResult(check.Rows.Count));
        }).Produces<LocationImportResult>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        return app;
    }

    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static AppException Unreadable() =>
        new("ONB-3516", "We couldn't read this file. Use the template (.xlsx) or a .csv file with the same columns.");

    private static AppException TooBig() =>
        new("ONB-3517", $"The file is too big. Upload up to {MaxRows} locations and 2 MB at a time.");

    /// <summary>The template: the columns, with dropdowns for type, county and main location from a Lists sheet.</summary>
    private static byte[] Template(List<string> counties)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Locations");
        var lists = book.AddWorksheet("Lists");
        for (var c = 0; c < Columns.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Columns[c];
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        var kinds = KindLabels.Values.ToList();
        for (var i = 0; i < kinds.Count; i++) lists.Cell(i + 1, 1).Value = kinds[i];
        for (var i = 0; i < counties.Count; i++) lists.Cell(i + 1, 2).Value = counties[i];
        lists.Cell(1, 3).Value = "Yes";
        lists.Cell(2, 3).Value = "No";
        lists.Hide();

        var last = MaxRows + 1;
        sheet.Range(2, 2, last, 2).CreateDataValidation().List(lists.Range(1, 1, kinds.Count, 1));
        sheet.Range(2, 4, last, 4).CreateDataValidation().List(lists.Range(1, 2, counties.Count, 2));
        sheet.Range(2, 7, last, 7).CreateDataValidation().List(lists.Range(1, 3, 2, 3));
        sheet.Columns(1, Columns.Length).Width = 22;

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>The file's rows by column name: the first sheet of an .xlsx, or a .csv. Empty rows are skipped.</summary>
    private static async Task<List<RawRow>> ReadAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        copy.Position = 0;
        var isCsv = file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || file.ContentType.Contains("csv", StringComparison.OrdinalIgnoreCase);
        var table = isCsv ? ReadCsv(Encoding.UTF8.GetString(copy.ToArray())) : ReadXlsx(copy);
        if (table.Count == 0)
        {
            throw Unreadable();
        }

        // The header finds the columns; a file without the two that can't be guessed isn't ours.
        var header = table[0].Select(h => h.Trim()).ToList();
        var index = Columns.ToDictionary(c => c, c => header.FindIndex(h => string.Equals(h, c, StringComparison.OrdinalIgnoreCase)));
        if (index[NameColumn] < 0 || index[CountyColumn] < 0)
        {
            throw Unreadable();
        }

        var rows = new List<RawRow>();
        for (var r = 1; r < table.Count; r++)
        {
            var cells = Columns.ToDictionary(c => c, c => index[c] >= 0 && index[c] < table[r].Count ? table[r][index[c]].Trim() : "");
            if (cells.Values.All(string.IsNullOrEmpty))
            {
                continue;
            }

            rows.Add(new RawRow(r + 1, cells));
            if (rows.Count > MaxRows)
            {
                throw TooBig();
            }
        }

        return rows;
    }

    private static List<List<string>> ReadXlsx(Stream stream)
    {
        try
        {
            using var book = new XLWorkbook(stream);
            var sheet = book.Worksheets.First();
            var used = sheet.RangeUsed();
            if (used is null)
            {
                return [];
            }

            var lastColumn = used.LastColumn().ColumnNumber();
            return sheet.Rows(1, used.LastRow().RowNumber())
                .Select(row => Enumerable.Range(1, lastColumn).Select(c => row.Cell(c).GetFormattedString()).ToList())
                .ToList();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw Unreadable();
        }
    }

    /// <summary>A small RFC 4180 reader: commas, quoted fields with commas, quotes ("") and line breaks.</summary>
    private static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else if (ch != '﻿') field.Append(ch);   // skip a byte-order mark
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static async Task<List<CountryRegion>> RegionsAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        var country = await db.Companies.AsNoTracking().Where(c => c.Status == "active").Select(c => c.CountryCode).FirstOrDefaultAsync(ct) ?? "KE";
        return await db.CountryRegions.AsNoTracking().Where(r => r.CountryCode == country).OrderBy(r => r.Name).ToListAsync(ct);
    }

    /// <summary>Each row checked with the rules for adding a location by hand, plus the file's own (codes and one main row).</summary>
    private static async Task<LocationImportCheck> CheckAsync(List<RawRow> raw, OrganizationsDbContext db, CancellationToken ct)
    {
        if (raw.Count == 0)
        {
            return new LocationImportCheck([], [new LocationImportProblem(0, null, "The file has no locations.")]);
        }

        var regions = await RegionsAsync(db, ct);
        var taken = (await db.Locations.Where(l => l.Status == "active").Select(l => l.Code).ToListAsync(ct)).ToHashSet();
        var seenCodes = new HashSet<string>();
        var mainSeen = false;
        var rows = new List<LocationImportRow>();
        var problems = new List<LocationImportProblem>();

        foreach (var row in raw)
        {
            var before = problems.Count;
            void Problem(string column, string message) => problems.Add(new LocationImportProblem(row.Line, column, message));

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
                rows.Add(new LocationImportRow(row.Line, name, kind!, code, region!.Code, town.Length == 0 ? null : town,
                    address.Length == 0 ? null : address, main == true));
            }
        }

        return new LocationImportCheck(rows, problems);
    }

    private static async Task<List<Company>> ChosenCompaniesAsync(LocationImportRequest request, OrganizationsDbContext db, CancellationToken ct)
    {
        var companies = await db.Companies.Where(c => c.Status == "active").AsNoTracking().ToListAsync(ct);
        var chosen = (request.CompanyIds ?? []).Distinct().ToList();
        var serves = request.AllCompanies ? companies : companies.Where(c => chosen.Contains(c.Id)).ToList();
        string? problem = null;
        if (serves.Count == 0 || (!request.AllCompanies && serves.Count != chosen.Count))
        {
            problem = "Choose at least one company, or all companies.";
        }
        else if (serves.Select(c => c.CountryCode).Distinct().Count() > 1)
        {
            problem = "A location serves companies in one country. Choose companies in the same country.";
        }

        return problem is null ? serves : throw OnboardingErrors.LocationDetailsInvalid(new Dictionary<string, string> { ["companyIds"] = problem });
    }
}
