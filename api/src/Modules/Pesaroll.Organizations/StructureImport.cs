using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// ONB-05 part 2, uploading divisions, departments, positions and units with the shared spreadsheet
/// import, up to 5,000 rows a file. Rows name what they belong to (a division, a department) by code
/// or name: an existing one, another row of the same file (departments), or one that doesn't exist
/// yet, which is shown as a note in the review and created on import, so a business needn't add the
/// higher levels first. The rules are those of adding one by hand, plus the file's own: codes unique
/// in the file, no loops.
/// </summary>
public static class StructureImport
{
    private const int MaxRows = 5000;
    private const int MaxMegabytes = 5;
    private const string Name = "name", Code = "code", Division = "division", Parent = "parent", Department = "department";

    /// <summary>
    /// A checked row with its references: an existing id, a row of the file (ParentLine), or a name to
    /// create on import (New…). For departments, DepartmentId is an existing parent.
    /// </summary>
    private sealed record ValidRow(int Line, string Name, string? Code, Guid? DivisionId, string? NewDivision,
        Guid? DepartmentId, string? NewDepartment, int? ParentLine);

    public static IEndpointRouteBuilder MapStructureImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/structure").WithTags("Org structure");

        SpreadsheetImport.MapImport(group, "/divisions", Spec("divisions", "division", [new(Name, "Division name"), new(Code, "Code")],
            choices: NoChoices,
            codes: db => db.Divisions.Where(d => d.Status == "active").Select(d => d.Code),
            save: (rows, request, org, db, now, ct) => SaveAsync(rows, request, org, db, now, db.Divisions, db.Divisions.Select(d => d.Code),
                (org, item, company, at) => new DivisionCompany { OrganizationId = org, DivisionId = item, CompanyId = company, CreatedAt = at }, null, ct)));

        SpreadsheetImport.MapImport(group, "/departments", Spec("departments", "department",
            [new(Name, "Department name"), new(Code, "Code"), new(Division, "Division"), new(Parent, "Part of")],
            choices: NoChoices,
            codes: db => db.Departments.Where(d => d.Status == "active").Select(d => d.Code),
            save: (rows, request, org, db, now, ct) => SaveAsync(rows, request, org, db, now, db.Departments, db.Departments.Select(d => d.Code),
                (org, item, company, at) => new DepartmentCompany { OrganizationId = org, DepartmentId = item, CompanyId = company, CreatedAt = at },
                async (department, row, byLine, create) =>
                {
                    department.DivisionId = row.NewDivision is { } division ? await create.DivisionAsync(division) : row.DivisionId;
                    department.ParentId = row.ParentLine is int line ? byLine[line].Id
                        // A new "Part of" department goes in the same division as the row naming it.
                        : row.NewDepartment is { } parent ? await create.DepartmentAsync(parent, department.DivisionId) : row.DepartmentId;
                }, ct)));

        SpreadsheetImport.MapImport(group, "/positions", Spec("positions", "position",
            [new(Name, "Position title"), new(Code, "Code"), new(Department, "Department"), new(Division, "Division")],
            choices: NoChoices,
            codes: db => db.Positions.Where(p => p.Status == "active").Select(p => p.Code),
            save: (rows, request, org, db, now, ct) => SaveAsync(rows, request, org, db, now, db.Positions, db.Positions.Select(p => p.Code),
                (org, item, company, at) => new PositionCompany { OrganizationId = org, PositionId = item, CompanyId = company, CreatedAt = at },
                async (position, row, _, create) => position.DepartmentId = row.NewDepartment is { } name
                    ? await create.DepartmentAsync(name, row.DivisionId, row.NewDivision) : row.DepartmentId, ct)));

        SpreadsheetImport.MapImport(group, "/units", Spec("units", "unit",
            [new(Name, "Unit name"), new(Code, "Code"), new(Department, "Department"), new(Division, "Division")],
            choices: NoChoices,
            codes: db => db.Units.Where(u => u.Status == "active").Select(u => u.Code),
            save: (rows, request, org, db, now, ct) => SaveAsync(rows, request, org, db, now, db.Units, db.Units.Select(u => u.Code),
                (org, item, company, at) => new UnitCompany { OrganizationId = org, UnitId = item, CompanyId = company, CreatedAt = at },
                async (unit, row, _, create) => unit.DepartmentId = row.NewDepartment is { } name
                    ? await create.DepartmentAsync(name, row.DivisionId, row.NewDivision) : row.DepartmentId!.Value, ct),
            departmentRequired: true));

        return app;
    }

    // What a row belongs to is typed as a name (or code), existing or new, so the template has no dropdowns.
    private static Task<Dictionary<string, List<string>>> NoChoices(OrganizationsDbContext db, CancellationToken ct) =>
        Task.FromResult(new Dictionary<string, List<string>>());

    private static ImportSpec<ValidRow> Spec(string things, string thing, IReadOnlyList<ImportColumn> columns,
        Func<OrganizationsDbContext, CancellationToken, Task<Dictionary<string, List<string>>>> choices,
        Func<OrganizationsDbContext, IQueryable<string>> codes,
        Func<List<ValidRow>, ImportRequest, Guid, OrganizationsDbContext, DateTimeOffset, CancellationToken, Task<int>> save,
        bool departmentRequired = false) => new()
    {
        Things = things,
        Permission = Permissions.Structure,
        Columns = columns,
        RequiredKeys = [Name],
        MaxRows = MaxRows,
        MaxMegabytes = MaxMegabytes,
        Choices = (db, _, ct) => choices(db, ct),
        Check = (rows, db, _, ct) => CheckAsync(rows, thing, columns.Select(c => c.Key).ToHashSet(), codes(db), departmentRequired, db, ct),
        Save = (rows, request, org, db, now, _, ct) => save(rows, request, org, db, now, ct),
    };

    /// <summary>A division or department; DivisionId is a department's division.</summary>
    private sealed record Ref(Guid Id, string Code, string Name, Guid? DivisionId = null);

    /// <summary>What a reference found: an existing id, nothing (it will be created), or several names (a problem).</summary>
    private enum Found { One, None, Several }

    private static (Found Found, Guid? Id) Find(List<Ref> items, string text)
    {
        var byCode = items.Where(i => string.Equals(i.Code, text, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = byCode.Count > 0 ? byCode : items.Where(i => string.Equals(i.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch { 1 => (Found.One, matches[0].Id), 0 => (Found.None, null), _ => (Found.Several, null) };
    }

    private sealed record Checked(ImportRow Row, string Name, string? Code, Guid? DivisionId, string? NewDivision, Guid? DepartmentId, string? NewDepartment);

    private static async Task<(List<ValidRow> Rows, List<ImportProblem> Problems, List<ImportProblem> Notes)> CheckAsync(List<ImportRow> raw, string thing,
        HashSet<string> columns, IQueryable<string> existingCodes, bool departmentRequired, OrganizationsDbContext db, CancellationToken ct)
    {
        var taken = (await existingCodes.ToListAsync(ct)).ToHashSet();
        var divisions = columns.Contains(Division)
            ? await db.Divisions.Where(d => d.Status == "active").Select(d => new Ref(d.Id, d.Code, d.Name)).ToListAsync(ct) : [];
        var departments = columns.Contains(Parent) || columns.Contains(Department)
            ? await db.Departments.Where(d => d.Status == "active").Select(d => new Ref(d.Id, d.Code, d.Name, d.DivisionId)).ToListAsync(ct) : [];
        var problems = new List<ImportProblem>();
        var notes = new List<ImportProblem>();
        var seenCodes = new HashSet<string>();
        var rows = new List<Checked>();

        // An existing division or department, or a note that it will be created; several matching names is a problem.
        (Guid? Id, string? New) Resolve(List<Ref> items, string text, string what, string column, int line)
        {
            var (found, id) = Find(items, text);
            switch (found)
            {
                case Found.One:
                    return (id, null);
                case Found.None:
                    notes.Add(new ImportProblem(line, column, $"No {what} called \"{text}\" yet: it will be created."));
                    return (null, text);
                default:
                    problems.Add(new ImportProblem(line, column, $"More than one {what} is called \"{text}\". Use its code."));
                    return (null, null);
            }
        }

        foreach (var row in raw)
        {
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

            (Guid? divisionId, string? newDivision) = (null, null);
            (Guid? departmentId, string? newDepartment) = (null, null);
            if (columns.Contains(Department))
            {
                // Positions and units: a department and, optionally, the division it is (or will be) in.
                var departmentText = row.Cells[Department];
                var divisionText = row.Cells[Division];
                if (departmentText.Length == 0)
                {
                    if (departmentRequired) Problem(Department, $"Choose the department this {thing} is in.");
                    if (divisionText.Length > 0) Problem(Division, "Name the department too, or leave Division empty: a division only says where a department sits.");
                }
                else
                {
                    var (found, id) = Find(departments, departmentText);
                    if (found == Found.Several)
                    {
                        Problem(Department, $"More than one department is called \"{departmentText}\". Use its code.");
                    }
                    else if (found == Found.One)
                    {
                        departmentId = id;
                        var department = departments.Single(d => d.Id == id);
                        if (divisionText.Length > 0 && !(Find(divisions, divisionText) is (Found.One, var given) && given == department.DivisionId))
                        {
                            var current = divisions.FirstOrDefault(d => d.Id == department.DivisionId)?.Name;
                            Problem(Division, current is null
                                ? $"{department.Name} isn't in a division. Leave Division empty."
                                : $"{department.Name} is in {current}, not \"{divisionText}\". Leave Division empty or change it to {current}.");
                        }
                    }
                    else
                    {
                        notes.Add(new ImportProblem(row.Line, Department, $"No department called \"{departmentText}\" yet: it will be created."));
                        newDepartment = departmentText;
                        if (divisionText.Length > 0) (divisionId, newDivision) = Resolve(divisions, divisionText, "division", Division, row.Line);
                    }
                }
            }
            else if (columns.Contains(Division) && row.Cells[Division].Length > 0)
            {
                (divisionId, newDivision) = Resolve(divisions, row.Cells[Division], "division", Division, row.Line);
            }

            rows.Add(new Checked(row, name, code, divisionId, newDivision, departmentId, newDepartment));
        }

        // Departments may sit under another row of the file (by its code, then its name), an existing
        // department, or one that will be created.
        var parentLines = new Dictionary<int, int>();
        if (columns.Contains(Parent))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var text = rows[i].Row.Cells[Parent];
                if (text.Length == 0)
                {
                    continue;
                }

                var inFile = rows.Where(r => string.Equals(r.Code, text, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inFile.Count == 0) inFile = rows.Where(r => string.Equals(r.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inFile.Count == 1)
                {
                    parentLines[rows[i].Row.Line] = inFile[0].Row.Line;
                }
                else if (inFile.Count > 1)
                {
                    problems.Add(new ImportProblem(rows[i].Row.Line, Parent, $"More than one row is called \"{text}\". Use its code."));
                }
                else
                {
                    var (id, created) = Resolve(departments, text, "department", Parent, rows[i].Row.Line);
                    rows[i] = rows[i] with { DepartmentId = id, NewDepartment = created };
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

        // A new department is created once, in one division: rows naming it with different divisions are a problem;
        // rows naming it without one take the division another row gives.
        if (columns.Contains(Department))
        {
            foreach (var group in rows.Select((r, i) => (r, i)).Where(x => x.r.NewDepartment is not null)
                .GroupBy(x => x.r.NewDepartment!, StringComparer.OrdinalIgnoreCase))
            {
                var given = group.Where(x => x.r.Row.Cells[Division].Length > 0).ToList();
                if (given.Select(x => x.r.Row.Cells[Division]).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                {
                    foreach (var (r, _) in given)
                    {
                        problems.Add(new ImportProblem(r.Row.Line, Division,
                            $"Rows give the new department \"{group.Key}\" different divisions. Use the same one, or leave it empty on the others."));
                    }
                }
                else if (given.Count > 0)
                {
                    foreach (var (r, i) in group)
                    {
                        rows[i] = r with { DivisionId = given[0].r.DivisionId, NewDivision = given[0].r.NewDivision };
                    }
                }
            }
        }

        // A cell with a problem doesn't also say it will be created.
        notes = notes.Where(n => !problems.Any(p => p.Line == n.Line && p.Column == n.Column)).ToList();

        var bad = problems.Select(p => p.Line).ToHashSet();
        var valid = rows.Where(r => !bad.Contains(r.Row.Line))
            .Select(r => new ValidRow(r.Row.Line, r.Name, r.Code, r.DivisionId, r.NewDivision, r.DepartmentId, r.NewDepartment,
                parentLines.TryGetValue(r.Row.Line, out var line) ? line : null))
            .ToList();
        return (valid, problems, notes);
    }

    /// <summary>
    /// Makes the divisions and departments an import names but that don't exist yet, once per name
    /// (ignoring case), for the same companies as the import, with made-up codes.
    /// </summary>
    private sealed class Creator(OrganizationsDbContext db, Guid organizationId, ImportRequest request, List<Company> companies, DateTimeOffset now, CancellationToken ct)
    {
        private readonly Dictionary<string, Guid> divisions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> departments = new(StringComparer.OrdinalIgnoreCase);
        private SpreadsheetImport.CodeMaker? divisionCodes;
        private SpreadsheetImport.CodeMaker? departmentCodes;

        public async Task<Guid> DivisionAsync(string name)
        {
            if (divisions.TryGetValue(name, out var id)) return id;
            divisionCodes ??= new(await db.Divisions.Select(d => d.Code).ToListAsync(ct));
            var division = New<Division, DivisionCompany>(name, divisionCodes,
                (org, item, company, at) => new DivisionCompany { OrganizationId = org, DivisionId = item, CompanyId = company, CreatedAt = at });
            db.Divisions.Add(division);
            return divisions[name] = division.Id;
        }

        /// <summary>A department, created in its division (an existing one, or a new one by name) the first time.</summary>
        public async Task<Guid> DepartmentAsync(string name, Guid? divisionId = null, string? newDivision = null)
        {
            if (departments.TryGetValue(name, out var id)) return id;
            departmentCodes ??= new([.. await db.Departments.Select(d => d.Code).ToListAsync(ct), .. db.Departments.Local.Select(d => d.Code)]);
            var department = New<Department, DepartmentCompany>(name, departmentCodes,
                (org, item, company, at) => new DepartmentCompany { OrganizationId = org, DepartmentId = item, CompanyId = company, CreatedAt = at });
            department.DivisionId = newDivision is not null ? await DivisionAsync(newDivision) : divisionId;
            db.Departments.Add(department);
            return departments[name] = department.Id;
        }

        private TItem New<TItem, TLink>(string name, SpreadsheetImport.CodeMaker codes, Func<Guid, Guid, Guid, DateTimeOffset, TLink> link)
            where TItem : class, IStructureItem<TLink>, new()
            where TLink : class, IStructureLink
        {
            var item = new TItem
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = codes.Next(LocationsEndpoints.Prefix(name)), Name = name,
                AllCompanies = request.AllCompanies, CreatedAt = now, UpdatedAt = now,
            };
            if (!request.AllCompanies)
            {
                item.Companies.AddRange(companies.Select(c => link(organizationId, item.Id, c.Id, now)));
            }

            return item;
        }
    }

    /// <summary>Saves the rows in one go: codes made up where empty, linked to the chosen companies, references resolved or created.</summary>
    private static async Task<int> SaveAsync<TItem, TLink>(List<ValidRow> rows, ImportRequest request, Guid organizationId, OrganizationsDbContext db,
        DateTimeOffset now, DbSet<TItem> items, IQueryable<string> allCodes, Func<Guid, Guid, Guid, DateTimeOffset, TLink> link,
        Func<TItem, ValidRow, Dictionary<int, TItem>, Creator, Task>? apply, CancellationToken ct)
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

        items.AddRange(byLine.Values);
        var creator = new Creator(db, organizationId, request, companies, now, ct);
        foreach (var row in rows)
        {
            if (apply is not null)
            {
                await apply(byLine[row.Line], row, byLine, creator);
            }
        }

        await db.SaveChangesAsync(ct);   // one save: every row (and what they needed created) or none
        return rows.Count;
    }
}
