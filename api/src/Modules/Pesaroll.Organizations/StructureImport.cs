using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Pesaroll.Organizations;

/// <summary>
/// ONB-05 part 2, uploading divisions, departments, positions and units with the shared spreadsheet
/// import, up to 5,000 rows a file. Rows name what they belong to by name or code: a division, a
/// department that exists, or (for departments) one further up or down the same file. The rules are
/// those of adding one by hand, plus the file's own: codes unique in the file, no loops.
/// </summary>
public static class StructureImport
{
    private const int MaxRows = 5000;
    private const int MaxMegabytes = 5;
    private const string Name = "name", Code = "code", Division = "division", Parent = "parent", Department = "department";

    /// <summary>A checked row: its references resolved to existing ids or, for departments, to rows of the file.</summary>
    private sealed record ValidRow(int Line, string Name, string? Code, Guid? DivisionId, Guid? DepartmentId, int? ParentLine);

    public static IEndpointRouteBuilder MapStructureImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/structure").WithTags("Org structure");

        SpreadsheetImport.MapImport(group, "/divisions", Spec("divisions", "division", [new(Name, "Division name"), new(Code, "Code")],
            choices: (_, _) => Task.FromResult(new Dictionary<string, List<string>>()),
            codes: db => db.Divisions.Where(d => d.Status == "active").Select(d => d.Code),
            save: (rows, request, org, db, now, ct) => SaveAsync(rows, request, org, db, now, db.Divisions, db.Divisions.Select(d => d.Code),
                (org, item, company, at) => new DivisionCompany { OrganizationId = org, DivisionId = item, CompanyId = company, CreatedAt = at }, null, ct)));

        SpreadsheetImport.MapImport(group, "/departments", Spec("departments", "department",
            [new(Name, "Department name"), new(Code, "Code"), new(Division, "Division"), new(Parent, "Part of")],
            choices: async (db, ct) => new Dictionary<string, List<string>>
            {
                [Division] = await db.Divisions.Where(d => d.Status == "active").OrderBy(d => d.Name).Select(d => d.Name).ToListAsync(ct),
                [Parent] = await db.Departments.Where(d => d.Status == "active").OrderBy(d => d.Name).Select(d => d.Name).ToListAsync(ct),
            },
            codes: db => db.Departments.Where(d => d.Status == "active").Select(d => d.Code),
            save: (rows, request, org, db, now, ct) => SaveAsync(rows, request, org, db, now, db.Departments, db.Departments.Select(d => d.Code),
                (org, item, company, at) => new DepartmentCompany { OrganizationId = org, DepartmentId = item, CompanyId = company, CreatedAt = at },
                (department, row, byLine) =>
                {
                    department.DivisionId = row.DivisionId;
                    department.ParentId = row.ParentLine is int line ? byLine[line].Id : row.DepartmentId;
                }, ct)));

        SpreadsheetImport.MapImport(group, "/positions", Spec("positions", "position",
            [new(Name, "Position title"), new(Code, "Code"), new(Department, "Department")],
            choices: async (db, ct) => new Dictionary<string, List<string>> { [Department] = await DepartmentNamesAsync(db, ct) },
            codes: db => db.Positions.Where(p => p.Status == "active").Select(p => p.Code),
            save: (rows, request, org, db, now, ct) => SaveAsync(rows, request, org, db, now, db.Positions, db.Positions.Select(p => p.Code),
                (org, item, company, at) => new PositionCompany { OrganizationId = org, PositionId = item, CompanyId = company, CreatedAt = at },
                (position, row, _) => position.DepartmentId = row.DepartmentId, ct)));

        SpreadsheetImport.MapImport(group, "/units", Spec("units", "unit",
            [new(Name, "Unit name"), new(Code, "Code"), new(Department, "Department")],
            choices: async (db, ct) => new Dictionary<string, List<string>> { [Department] = await DepartmentNamesAsync(db, ct) },
            codes: db => db.Units.Where(u => u.Status == "active").Select(u => u.Code),
            save: (rows, request, org, db, now, ct) => SaveAsync(rows, request, org, db, now, db.Units, db.Units.Select(u => u.Code),
                (org, item, company, at) => new UnitCompany { OrganizationId = org, UnitId = item, CompanyId = company, CreatedAt = at },
                (unit, row, _) => unit.DepartmentId = row.DepartmentId!.Value, ct),
            departmentRequired: true));

        return app;
    }

    private static Task<List<string>> DepartmentNamesAsync(OrganizationsDbContext db, CancellationToken ct) =>
        db.Departments.Where(d => d.Status == "active").OrderBy(d => d.Name).Select(d => d.Name).ToListAsync(ct);

    private static ImportSpec<ValidRow> Spec(string things, string thing, IReadOnlyList<ImportColumn> columns,
        Func<OrganizationsDbContext, CancellationToken, Task<Dictionary<string, List<string>>>> choices,
        Func<OrganizationsDbContext, IQueryable<string>> codes,
        Func<List<ValidRow>, ImportRequest, Guid, OrganizationsDbContext, DateTimeOffset, CancellationToken, Task<int>> save,
        bool departmentRequired = false) => new()
    {
        Things = things,
        Columns = columns,
        RequiredKeys = [Name],
        MaxRows = MaxRows,
        MaxMegabytes = MaxMegabytes,
        Choices = choices,
        Check = (rows, db, ct) => CheckAsync(rows, thing, columns.Select(c => c.Key).ToHashSet(), codes(db), departmentRequired, db, ct),
        Save = save,
    };

    /// <summary>A department or division by code first, then by name; "" when nothing matches, null when several names do.</summary>
    private sealed record Ref(Guid Id, string Code, string Name);

    private static async Task<(List<ValidRow> Rows, List<ImportProblem> Problems)> CheckAsync(List<ImportRow> raw, string thing, HashSet<string> columns,
        IQueryable<string> existingCodes, bool departmentRequired, OrganizationsDbContext db, CancellationToken ct)
    {
        var taken = (await existingCodes.ToListAsync(ct)).ToHashSet();
        var divisions = columns.Contains(Division)
            ? await db.Divisions.Where(d => d.Status == "active").Select(d => new Ref(d.Id, d.Code, d.Name)).ToListAsync(ct) : [];
        var departments = columns.Contains(Parent) || columns.Contains(Department)
            ? await db.Departments.Where(d => d.Status == "active").Select(d => new Ref(d.Id, d.Code, d.Name)).ToListAsync(ct) : [];
        var problems = new List<ImportProblem>();
        var seenCodes = new HashSet<string>();
        var checkedRows = new List<(ImportRow Row, string Name, string? Code, Guid? DivisionId, Guid? DepartmentId, string ParentText, bool Ok)>();

        foreach (var row in raw)
        {
            var before = problems.Count;
            void Problem(string column, string message) => problems.Add(new ImportProblem(row.Line, column, message));

            var name = row.Cells[Name];
            if (name.Length is < 2 or > 120) Problem(Name, $"Enter the {thing}'s {(thing == "position" ? "title" : "name")} (2 to 120 characters).");

            var code = row.Cells[Code].Length == 0 ? null : row.Cells[Code].ToUpperInvariant();
            if (code is not null)
            {
                if (!Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9-]{1,19}$")) Problem(Code, "Use 2 to 20 letters, numbers or dashes, or leave it empty for us to make one.");
                else if (taken.Contains(code)) Problem(Code, $"A {thing} with the code {code} already exists.");
                else if (!seenCodes.Add(code)) Problem(Code, $"Another row in the file has the code {code}.");
            }

            Guid? divisionId = null;
            if (columns.Contains(Division) && row.Cells[Division].Length > 0)
            {
                divisionId = Find(divisions, row.Cells[Division], "division", Division, Problem);
            }

            Guid? departmentId = null;
            if (columns.Contains(Department))
            {
                if (row.Cells[Department].Length > 0) departmentId = Find(departments, row.Cells[Department], "department", Department, Problem);
                else if (departmentRequired) Problem(Department, $"Choose the department this {thing} is in.");
            }

            checkedRows.Add((row, name, code, divisionId, departmentId, columns.Contains(Parent) ? row.Cells[Parent] : "", problems.Count == before));
        }

        // Departments may sit under another row of the file (by its code, then its name) or an existing department.
        var parentLines = new Dictionary<int, int>();
        var parentIds = new Dictionary<int, Guid>();
        if (columns.Contains(Parent))
        {
            foreach (var (row, _, _, _, _, text, _) in checkedRows.Where(r => r.ParentText.Length > 0))
            {
                var inFile = checkedRows.Where(r => string.Equals(r.Code, text, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inFile.Count == 0) inFile = checkedRows.Where(r => string.Equals(r.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inFile.Count == 1)
                {
                    parentLines[row.Line] = inFile[0].Row.Line;
                }
                else if (inFile.Count > 1)
                {
                    problems.Add(new ImportProblem(row.Line, Parent, $"More than one row is called \"{text}\". Use its code."));
                }
                else if (Find(departments, text, "department", Parent, (c, m) => problems.Add(new ImportProblem(row.Line, c, m))) is Guid id)
                {
                    parentIds[row.Line] = id;
                }
            }

            // No loops among the file's rows (an existing department can't be under a new one).
            foreach (var start in parentLines.Keys)
            {
                var seen = new HashSet<int> { start };
                for (var at = start; parentLines.TryGetValue(at, out var next); at = next)
                {
                    if (!seen.Add(next) || next == start)
                    {
                        problems.Add(new ImportProblem(start, Parent, "This would make a loop: a department can't sit under itself or one of its own departments."));
                        break;
                    }
                }
            }
        }

        // For departments, DepartmentId carries an existing parent; ParentLine a parent in the file.
        var bad = problems.Select(p => p.Line).ToHashSet();
        var valid = new List<ValidRow>();
        foreach (var r in checkedRows.Where(r => !bad.Contains(r.Row.Line)))
        {
            Guid? existingParent = parentIds.TryGetValue(r.Row.Line, out var parentId) ? parentId : null;
            int? parentLine = parentLines.TryGetValue(r.Row.Line, out var line) ? line : null;
            valid.Add(new ValidRow(r.Row.Line, r.Name, r.Code, r.DivisionId, columns.Contains(Parent) ? existingParent : r.DepartmentId, parentLine));
        }

        return (valid, problems);
    }

    /// <summary>A division or department by its code, then by its name; a problem when none or several match.</summary>
    private static Guid? Find(List<Ref> items, string text, string what, string column, Action<string, string> problem)
    {
        var byCode = items.Where(i => string.Equals(i.Code, text, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = byCode.Count > 0 ? byCode : items.Where(i => string.Equals(i.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
        {
            return matches[0].Id;
        }

        problem(column, matches.Count == 0
            ? $"There's no {what} called \"{text}\". Add it first, or choose one from the list."
            : $"More than one {what} is called \"{text}\". Use its code.");
        return null;
    }

    /// <summary>Saves the rows in one go: codes made up where empty, linked to the chosen companies, references resolved.</summary>
    private static async Task<int> SaveAsync<TItem, TLink>(List<ValidRow> rows, ImportRequest request, Guid organizationId, OrganizationsDbContext db,
        DateTimeOffset now, DbSet<TItem> items, IQueryable<string> allCodes, Func<Guid, Guid, Guid, DateTimeOffset, TLink> link,
        Action<TItem, ValidRow, Dictionary<int, TItem>>? apply, CancellationToken ct)
        where TItem : class, IStructureItem<TLink>, new()
        where TLink : class, IStructureLink
    {
        var companies = await SpreadsheetImport.ChosenCompaniesAsync(request, db, OnboardingErrors.StructureDetailsInvalid, ct);
        var codes = new SpreadsheetImport.CodeMaker(await allCodes.ToListAsync(ct));
        foreach (var row in rows.Where(r => r.Code is not null))
        {
            codes.Take(row.Code!);
        }

        var byLine = new Dictionary<int, TItem>();
        foreach (var row in rows)
        {
            var item = new TItem
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = organizationId,
                Code = row.Code ?? codes.Next(LocationsEndpoints.Prefix(row.Name)),
                Name = row.Name,
                AllCompanies = request.AllCompanies,
                CreatedAt = now,
                UpdatedAt = now,
            };
            if (!request.AllCompanies)
            {
                item.Companies.AddRange(companies.Select(c => link(organizationId, item.Id, c.Id, now)));
            }

            byLine[row.Line] = item;
        }

        foreach (var row in rows)
        {
            apply?.Invoke(byLine[row.Line], row, byLine);
        }

        items.AddRange(byLine.Values);
        await db.SaveChangesAsync(ct);   // one save: every row or none
        return rows.Count;
    }
}
