using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-05 part 2: uploading divisions, departments, positions and units, with the same template,
/// read, check and import steps as locations, up to 5,000 rows. Rows name what they belong to
/// (a division, a department) by name or code, including departments in the same file.
/// </summary>
public sealed class StructureImportTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Row(int Line, Dictionary<string, string> Cells);
    private sealed record Problem(int Line, string? Column, string Message);
    private sealed record Read(List<Row> Rows, List<Problem> Problems, List<Problem> Notes);
    private sealed record ImportResult(int Imported);
    private sealed record Item(Guid Id, string Code, string? Name, string? Title, Guid? DivisionId, Guid? ParentId, Guid? DepartmentId, bool AllCompanies, List<Guid> CompanyIds);

    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Password = "Mombasa-raha-7";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private async Task<(HttpClient Client, Company Company)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email = $"sti-{Guid.NewGuid():N}@example.com", password = Password, roleId = Guid.Parse(Ceo) });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        var company = (await (await client.PostAsJsonAsync("/api/companies", new { legalName = "Oliech Restaurants Ltd", countryCode = "KE" }))
            .Content.ReadFromJsonAsync<Company>())!;
        return (client, company);
    }

    private static byte[] Workbook(string[] header, params string?[][] rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Sheet");
        for (var c = 0; c < header.Length; c++) sheet.Cell(1, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = rows[r][c];
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static readonly string[] Departments = ["Department name", "Code", "Division", "Part of"];
    private static readonly string[] Positions = ["Position title", "Code", "Department"];

    private static async Task<HttpResponseMessage> ReadFile(HttpClient client, string level, byte[] file, string name = "x.xlsx", string type = Xlsx)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(content, "file", name);
        return await client.PostAsync($"/api/structure/{level}/import/read", form);
    }

    private static async Task<Read> ReadOk(HttpClient client, string level, byte[] file) =>
        (await (await ReadFile(client, level, file)).Content.ReadFromJsonAsync<Read>())!;

    private static async Task<HttpResponseMessage> Import(HttpClient client, string level, List<Row> rows, Company company) =>
        await client.PostAsJsonAsync($"/api/structure/{level}/import", new { rows, allCompanies = false, companyIds = new[] { company.Id } });

    private static async Task<T> Post<T>(HttpClient client, string path, object body) =>
        (await (await client.PostAsJsonAsync(path, body)).Content.ReadFromJsonAsync<T>())!;

    [Fact]
    public async Task Each_level_has_a_template_where_what_rows_belong_to_is_typed_as_a_name()
    {
        var (client, _) = await Onboarded();
        await Post<Item>(client, "/api/structure/divisions", new { name = "Restaurants", allCompanies = true, companyIds = Array.Empty<Guid>() });

        var res = await client.GetAsync("/api/structure/departments/template");

        Assert.Equal(Xlsx, res.Content.Headers.ContentType?.MediaType);
        Assert.Contains("departments-template.xlsx", res.Content.Headers.ContentDisposition?.ToString());
        using var book = new XLWorkbook(await res.Content.ReadAsStreamAsync());
        var sheet = book.Worksheet(1);
        Assert.Equal(Departments, Enumerable.Range(1, 4).Select(c => sheet.Cell(1, c).GetString()));
        // Division and Part of are plain cells: a name, existing or new (created on import).
        Assert.All(Enumerable.Range(1, 4), c => Assert.False(sheet.Cell(2, c).HasDataValidation));
        foreach (var level in new[] { "divisions", "positions", "units" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/structure/{level}/template")).StatusCode);
        }
    }

    [Fact]
    public async Task Departments_can_sit_under_departments_in_the_same_file_and_all_import_in_one_go()
    {
        var (client, company) = await Onboarded();
        var division = await Post<Item>(client, "/api/structure/divisions", new { name = "Restaurants", allCompanies = true, companyIds = Array.Empty<Guid>() });
        var ops = await Post<Item>(client, "/api/structure/departments", new { name = "Operations", allCompanies = true, companyIds = Array.Empty<Guid>() });

        var read = await ReadOk(client, "departments", Workbook(Departments,
            ["Grill", null, null, "kitchen"],            // under a department further down the file
            ["Kitchen", "KIT", "restaurants", "Operations"],
            ["Bar", null, "RES-01", "OPE-01"]));        // division and department by code

        Assert.Empty(read.Problems);
        var res = await Import(client, "departments", read.Rows, company);

        Assert.Equal(3, (await res.Content.ReadFromJsonAsync<ImportResult>())!.Imported);
        var all = (await client.GetFromJsonAsync<List<Item>>("/api/structure/departments"))!;
        var kitchen = all.Single(d => d.Name == "Kitchen");
        Assert.Equal(("KIT", division.Id, ops.Id), (kitchen.Code, kitchen.DivisionId!.Value, kitchen.ParentId!.Value));
        Assert.Equal(kitchen.Id, all.Single(d => d.Name == "Grill").ParentId);
        Assert.Equal(("GRI-01", ops.Id), (all.Single(d => d.Name == "Grill").Code, all.Single(d => d.Name == "Bar").ParentId!.Value));
        Assert.Equal([company.Id], kitchen.CompanyIds);
    }

    [Fact]
    public async Task Divisions_and_departments_a_file_names_but_that_dont_exist_yet_are_created_on_import()
    {
        var (client, company) = await Onboarded();

        var read = await ReadOk(client, "departments", Workbook(Departments,
            ["Kitchen", null, "Restaurants", "Operations"],
            ["Bar", null, "restaurants", null],
            ["Laundry", null, "Housekeeping", null]));

        Assert.Empty(read.Problems);
        Assert.Equal(new (int, string?)[] { (2, "division"), (2, "parent"), (3, "division"), (4, "division") },
            read.Notes.Select(n => (n.Line, n.Column)).ToArray());
        Assert.Equal(3, (await (await Import(client, "departments", read.Rows, company)).Content.ReadFromJsonAsync<ImportResult>())!.Imported);

        var divisions = (await client.GetFromJsonAsync<List<Item>>("/api/structure/divisions"))!;
        Assert.Equal(["Housekeeping", "Restaurants"], divisions.Select(d => d.Name).Order());
        var departments = (await client.GetFromJsonAsync<List<Item>>("/api/structure/departments"))!;
        var ops = departments.Single(d => d.Name == "Operations");
        var kitchen = departments.Single(d => d.Name == "Kitchen");
        Assert.Equal((ops.Id, divisions.Single(d => d.Name == "Restaurants").Id), (kitchen.ParentId!.Value, kitchen.DivisionId!.Value));
        Assert.Equal(kitchen.DivisionId, departments.Single(d => d.Name == "Bar").DivisionId);
        Assert.Equal([company.Id], ops.CompanyIds);
    }

    [Fact]
    public async Task Problems_are_listed_by_row_and_column()
    {
        var (client, _) = await Onboarded();
        await Post<Item>(client, "/api/structure/departments", new { code = "ADM", name = "Admin", allCompanies = true, companyIds = Array.Empty<Guid>() });

        var read = await ReadOk(client, "departments", Workbook(Departments,
            [null, null, null, null],                     // skipped: empty
            ["A", null, null, null],
            ["Stores", null, "Atlantis", "Nowhere"],
            ["Loop one", "L1", null, "L2"],
            ["Loop two", "L2", null, "L1"],
            ["Admin two", "adm", null, null],
            ["Twin", "TW", null, null],
            ["Twin again", "tw", null, null]));

        Assert.Equal(new (int, string?)[]
        {
            (3, "name"), (5, "parent"), (6, "parent"), (7, "code"), (9, "code"),
        }, read.Problems.Select(p => (p.Line, p.Column)).ToArray());
        Assert.Contains("loop", read.Problems.Single(p => p.Line == 5).Message);
        // What isn't there yet isn't a problem: it will be created.
        Assert.Equal(new (int, string?)[] { (4, "division"), (4, "parent") }, read.Notes.Select(n => (n.Line, n.Column)).ToArray());
        Assert.Contains("will be created", read.Notes[0].Message);
    }

    [Fact]
    public async Task Positions_name_their_department_or_are_shared_and_units_need_one()
    {
        var (client, company) = await Onboarded();
        var kitchen = await Post<Item>(client, "/api/structure/departments", new { code = "KIT", name = "Kitchen", allCompanies = true, companyIds = Array.Empty<Guid>() });

        var positions = await ReadOk(client, "positions", Workbook(Positions,
            ["Cook", null, "kitchen"], ["Driver", null, null], ["Waiter", null, "Service"], ["Host", null, "service"]));
        Assert.Empty(positions.Problems);
        Assert.Equal([(4, "department"), (5, "department")], positions.Notes.Select(n => (n.Line, n.Column)));
        await Import(client, "positions", positions.Rows, company);
        var saved = (await client.GetFromJsonAsync<List<Item>>("/api/structure/positions"))!;
        Assert.Equal(kitchen.Id, saved.Single(p => p.Title == "Cook").DepartmentId);
        Assert.Null(saved.Single(p => p.Title == "Driver").DepartmentId);
        // "Service" was created once, for the same companies, and both positions sit in it.
        var service = Assert.Single((await client.GetFromJsonAsync<List<Item>>("/api/structure/departments"))!, d => d.Name == "Service");
        Assert.Equal([company.Id], service.CompanyIds);
        Assert.Equal(service.Id, saved.Single(p => p.Title == "Waiter").DepartmentId);
        Assert.Equal(service.Id, saved.Single(p => p.Title == "Host").DepartmentId);

        var units = await ReadOk(client, "units", Workbook(["Unit name", "Code", "Department"], ["Morning shift", null, "KIT"], ["Night shift", null, null]));
        Assert.Equal([(3, "department")], units.Problems.Select(p => (p.Line, p.Column)));
    }

    [Fact]
    public async Task Import_checks_the_rows_again_and_saves_all_or_none()
    {
        var (client, company) = await Onboarded();

        var res = await Import(client, "divisions",
            [new Row(2, new() { ["name"] = "Restaurants", ["code"] = "" }), new Row(3, new() { ["name"] = "X", ["code"] = "" })], company);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("ONB-3518", (await res.Content.ReadFromJsonAsync<Error>())!.Code);
        Assert.Empty((await client.GetFromJsonAsync<List<Item>>("/api/structure/divisions"))!);
        var check = await client.PostAsJsonAsync("/api/structure/divisions/import/check", new { rows = new[] { new Row(3, new() { ["name"] = "X" }) } });
        Assert.Equal([(3, "name")], (await check.Content.ReadFromJsonAsync<Read>())!.Problems.Select(p => (p.Line, p.Column)));
    }

    [Fact]
    public async Task Five_thousand_positions_read_and_import_in_one_go()
    {
        var (client, company) = await Onboarded();
        await Post<Item>(client, "/api/structure/departments", new { code = "KIT", name = "Kitchen", allCompanies = true, companyIds = Array.Empty<Guid>() });
        var csv = new StringBuilder("Position title,Code,Department\r\n");
        for (var i = 1; i <= 5000; i++) csv.Append($"Position {i},,{(i % 2 == 0 ? "KIT" : "")}\r\n");
        var clock = Stopwatch.StartNew();

        var read = (await (await ReadFile(client, "positions", Encoding.UTF8.GetBytes(csv.ToString()), "positions.csv", "text/csv")).Content.ReadFromJsonAsync<Read>())!;
        Assert.Equal(5000, read.Rows.Count);
        Assert.Empty(read.Problems);
        var res = await Import(client, "positions", read.Rows, company);

        Assert.Equal(5000, (await res.Content.ReadFromJsonAsync<ImportResult>())!.Imported);
        Assert.Equal(5000, (await client.GetFromJsonAsync<List<Item>>("/api/structure/positions"))!.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"5,000 positions took {clock.Elapsed}");

        csv.Append("One too many,,\r\n");
        var tooMany = await ReadFile(client, "positions", Encoding.UTF8.GetBytes(csv.ToString()), "positions.csv", "text/csv");
        Assert.Equal("ONB-3517", (await tooMany.Content.ReadFromJsonAsync<Error>())!.Code);
    }
}
