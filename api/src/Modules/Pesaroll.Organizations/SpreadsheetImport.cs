using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// A row as written in an uploaded file, for the person to review and edit: <see cref="Line"/> is its
/// row number in the file (the header is row 1); <see cref="Cells"/> holds the text by column key.
/// </summary>
public sealed record ImportRow(int Line, Dictionary<string, string> Cells);

/// <summary>A problem: the row (0 for the whole file), the column key it is in (when it is about one) and what to fix.</summary>
public sealed record ImportProblem(int Line, string? Column, string Message);

/// <summary>
/// A file read: every row (problems and all, to be fixed in the review), its problems, and notes:
/// things the rows name that don't exist yet and will be created on import (a new division).
/// </summary>
public sealed record ImportRead(IReadOnlyList<ImportRow> Rows, IReadOnlyList<ImportProblem> Problems, IReadOnlyList<ImportProblem> Notes);

public sealed record ImportRowsRequest(List<ImportRow>? Rows);

public sealed record ImportCheck(IReadOnlyList<ImportProblem> Problems, IReadOnlyList<ImportProblem> Notes);

public sealed record ImportRequest(List<ImportRow>? Rows, bool AllCompanies, List<Guid>? CompanyIds);

public sealed record ImportResult(int Imported);

/// <summary>A template column: its key in a row's cells and its header in the file.</summary>
public sealed record ImportColumn(string Key, string Header);

/// <summary>
/// What one kind of record (locations, departments…) tells the shared import: its columns, its
/// limits, the dropdown choices for its template, how to check rows and how to save them.
/// </summary>
public sealed class ImportSpec<TValid>
{
    /// <summary>"locations": used in the file name and messages.</summary>
    public required string Things { get; init; }
    public required IReadOnlyList<ImportColumn> Columns { get; init; }
    /// <summary>Headers a file must have to be recognised (the first column at least).</summary>
    public required IReadOnlyList<string> RequiredKeys { get; init; }
    public required int MaxRows { get; init; }
    public required int MaxMegabytes { get; init; }
    /// <summary>The template's dropdowns: choices by column key.</summary>
    public required Func<OrganizationsDbContext, CancellationToken, Task<Dictionary<string, List<string>>>> Choices { get; init; }
    /// <summary>Checks the rows (the same rules as adding one by hand): the valid rows, problems, and notes of what will be created.</summary>
    public required Func<List<ImportRow>, OrganizationsDbContext, CancellationToken, Task<(List<TValid> Rows, List<ImportProblem> Problems, List<ImportProblem> Notes)>> Check { get; init; }
    /// <summary>Saves the checked rows for the chosen companies; returns how many were saved.</summary>
    public required Func<List<TValid>, ImportRequest, Guid, OrganizationsDbContext, DateTimeOffset, CancellationToken, Task<int>> Save { get; init; }
}

/// <summary>
/// Uploading many records from a spreadsheet, the same for every kind (BulkUpload on the web):
/// GET template (an Excel file with the columns and dropdowns), POST import/read (an .xlsx or .csv
/// read back row by row, with problems), POST import/check (edited rows checked again) and POST
/// import (checked once more, then saved all or none). Only the person who onboarded the
/// organization may upload, for now.
/// </summary>
public static class SpreadsheetImport
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void MapImport<TValid>(RouteGroupBuilder group, string prefix, ImportSpec<TValid> spec)
    {
        group.MapGet($"{prefix}/template", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            return Results.File(Template(spec, await spec.Choices(db, ct)), Xlsx, $"pesaroll-{spec.Things}-template.xlsx");
        }).Produces(StatusCodes.Status200OK, contentType: Xlsx).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost($"{prefix}/import/read", async (IFormFile file, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            if (file.Length > spec.MaxMegabytes * 1024L * 1024L)
            {
                throw TooBig(spec);
            }

            var rows = await ReadAsync(file, spec, ct);
            var (_, problems, notes) = await CheckAsync(spec, rows, db, ct);
            return Results.Ok(new ImportRead(rows, problems, notes));
        }).DisableAntiforgery()   // the origin check (OriginCheckMiddleware) protects it, as for JSON requests
          .Produces<ImportRead>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        group.MapPost($"{prefix}/import/check", async (ImportRowsRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var (_, problems, notes) = await CheckAsync(spec, Clean(request.Rows, spec), db, ct);
            return Results.Ok(new ImportCheck(problems, notes));
        }).Produces<ImportCheck>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

        group.MapPost($"{prefix}/import", async (ImportRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var (valid, problems, _) = await CheckAsync(spec, Clean(request.Rows, spec), db, ct);
            if (problems.Count > 0)
            {
                throw RowsNeedFixing(problems);
            }

            return Results.Ok(new ImportResult(await spec.Save(valid, request, organizationId, db, clock.GetUtcNow(), ct)));
        }).Produces<ImportResult>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden);
    }

    private static async Task<(List<TValid> Rows, List<ImportProblem> Problems, List<ImportProblem> Notes)> CheckAsync<TValid>(
        ImportSpec<TValid> spec, List<ImportRow> rows, OrganizationsDbContext db, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return ([], [new ImportProblem(0, null, $"The file has no {spec.Things}.")], []);
        }

        // In file order: by row, then by column.
        var (valid, problems, notes) = await spec.Check(rows, db, ct);
        var order = spec.Columns.Select((c, i) => (c.Key, i)).ToDictionary(c => c.Key, c => c.i);
        List<ImportProblem> Sorted(List<ImportProblem> list) =>
            list.OrderBy(p => p.Line).ThenBy(p => p.Column is null ? -1 : order.GetValueOrDefault(p.Column, 99)).ToList();
        return (valid, Sorted(problems), Sorted(notes));
    }

    private static AppException Unreadable() =>
        new("ONB-3516", "We couldn't read this file. Use the template (.xlsx) or a .csv file with the same columns.");

    private static AppException TooBig<TValid>(ImportSpec<TValid> spec) =>
        new("ONB-3517", $"The file is too big. Upload up to {spec.MaxRows:N0} {spec.Things} and {spec.MaxMegabytes} MB at a time.");

    private static AppException RowsNeedFixing(List<ImportProblem> problems)
    {
        var lines = problems.Select(p => p.Line).Distinct().ToList();
        var which = lines.Count == 1 ? $"Row {lines[0]} needs" : $"Rows {string.Join(", ", lines.Take(5))}{(lines.Count > 5 ? "…" : "")} need";
        return new AppException("ONB-3518", $"{which} fixing. Check them and try again.");
    }

    /// <summary>The template: the columns, and a dropdown for each column with choices (from a hidden Lists sheet).</summary>
    private static byte[] Template<TValid>(ImportSpec<TValid> spec, Dictionary<string, List<string>> choices)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(char.ToUpperInvariant(spec.Things[0]) + spec.Things[1..]);
        var lists = book.AddWorksheet("Lists");
        var last = spec.MaxRows + 1;
        var listColumn = 0;
        for (var c = 0; c < spec.Columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = spec.Columns[c].Header;
            if (choices.GetValueOrDefault(spec.Columns[c].Key) is not { } options)
            {
                continue;
            }

            // A column of the Lists sheet per dropdown, in column order; an empty list still gets its column.
            listColumn++;
            for (var i = 0; i < options.Count; i++) lists.Cell(i + 1, listColumn).Value = options[i];
            if (options.Count > 0)
            {
                sheet.Range(2, c + 1, last, c + 1).CreateDataValidation().List(lists.Range(1, listColumn, options.Count, listColumn));
            }
        }

        lists.Hide();
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, spec.Columns.Count).Width = 22;
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>The file's rows by column key: the first sheet of an .xlsx, or a .csv. Empty rows are skipped.</summary>
    private static async Task<List<ImportRow>> ReadAsync<TValid>(IFormFile file, ImportSpec<TValid> spec, CancellationToken ct)
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

        // The header finds the columns; a file without the required ones isn't ours.
        var header = table[0].Select(h => h.Trim()).ToList();
        var index = spec.Columns.ToDictionary(c => c.Key, c => header.FindIndex(h => string.Equals(h, c.Header, StringComparison.OrdinalIgnoreCase)));
        if (spec.RequiredKeys.Any(k => index[k] < 0))
        {
            throw Unreadable();
        }

        var rows = new List<ImportRow>();
        for (var r = 1; r < table.Count; r++)
        {
            var cells = spec.Columns.ToDictionary(c => c.Key, c => index[c.Key] >= 0 && index[c.Key] < table[r].Count ? table[r][index[c.Key]].Trim() : "");
            if (cells.Values.All(string.IsNullOrEmpty))
            {
                continue;
            }

            rows.Add(new ImportRow(r + 1, cells));
            if (rows.Count > spec.MaxRows)
            {
                throw TooBig(spec);
            }
        }

        return rows;
    }

    /// <summary>Rows sent back for checking or importing: within the limit, every column present and trimmed.</summary>
    private static List<ImportRow> Clean<TValid>(List<ImportRow>? rows, ImportSpec<TValid> spec)
    {
        if (rows is not null && rows.Count > spec.MaxRows)
        {
            throw TooBig(spec);
        }

        return (rows ?? []).Select(r => new ImportRow(r.Line,
            spec.Columns.ToDictionary(c => c.Key, c => r.Cells?.GetValueOrDefault(c.Key)?.Trim() ?? ""))).ToList();
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

    /// <summary>The companies chosen for an import, or all of them; at least one.</summary>
    internal static async Task<List<Company>> ChosenCompaniesAsync(ImportRequest request, OrganizationsDbContext db, Func<Dictionary<string, string>, AppException> invalid,
        CancellationToken ct, bool sameCountry = false)
    {
        var companies = await db.Companies.AsNoTracking().Where(c => c.Status == "active").ToListAsync(ct);
        var chosen = (request.CompanyIds ?? []).Distinct().ToList();
        var serves = request.AllCompanies ? companies : companies.Where(c => chosen.Contains(c.Id)).ToList();
        if (serves.Count == 0 || (!request.AllCompanies && serves.Count != chosen.Count))
        {
            throw invalid(new Dictionary<string, string> { ["companyIds"] = "Choose at least one company, or all companies." });
        }

        if (sameCountry && serves.Select(c => c.CountryCode).Distinct().Count() > 1)
        {
            throw invalid(new Dictionary<string, string> { ["companyIds"] = "A location serves companies in one country. Choose companies in the same country." });
        }

        return serves;
    }

    /// <summary>Makes codes PREFIX-01, PREFIX-02… for rows without one, avoiding the codes already taken.</summary>
    internal sealed class CodeMaker(IEnumerable<string> taken)
    {
        private readonly HashSet<string> used = [.. taken];

        public string Next(string prefix)
        {
            for (var n = 1; ; n++)
            {
                var code = $"{prefix}-{n:00}";
                if (used.Add(code))
                {
                    return code;
                }
            }
        }

        public void Take(string code) => used.Add(code);
    }
}
