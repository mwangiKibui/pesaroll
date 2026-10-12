using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-04 part 2: uploading locations. Download the template, upload a filled copy (its rows come
/// back as written, with problems by row and column) to review and edit, check the edited rows, then
/// import them for chosen companies, all or nothing.
/// </summary>
public sealed class LocationsImportTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Row(int Line, Dictionary<string, string> Cells);
    private sealed record Problem(int Line, string? Column, string Message);
    private sealed record Checked(List<Row> Rows, List<Problem> Problems);
    private sealed record RowsChecked(List<Problem> Problems);

    private static Row R(int line, string name, string kind = "", string code = "", string county = "Nairobi", string town = "", string address = "", string main = "") =>
        new(line, new() { ["name"] = name, ["kind"] = kind, ["code"] = code, ["county"] = county, ["town"] = town, ["address"] = address, ["main"] = main });
    private sealed record ImportResult(int Imported);
    private sealed record Location(Guid Id, string Code, string Name, string Kind, string? RegionCode, string? Town, string? WageAreaCode,
        bool AllCompanies, List<Guid> CompanyIds, List<Guid> MainFor);

    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Password = "Mombasa-raha-7";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] Header = ["Location name", "Type", "Code", "County", "Town", "Address", "Main location"];

    private async Task<(HttpClient Client, string Email, Guid OrganizationId)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"imp-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse(Ceo) });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        return (client, email, created.Organization.Id);
    }

    private static async Task<Company> AddCompany(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" }))
            .Content.ReadFromJsonAsync<Company>())!;

    /// <summary>A filled-in template: the header, then one row per location.</summary>
    private static byte[] Workbook(params string?[][] rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Locations");
        for (var c = 0; c < Header.Length; c++) sheet.Cell(1, c + 1).Value = Header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = rows[r][c];
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<HttpResponseMessage> Check(HttpClient client, byte[] file, string name = "locations.xlsx", string type = Xlsx)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(content, "file", name);
        return await client.PostAsync("/api/locations/import/read", form);
    }

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task The_template_has_the_columns_and_dropdowns_for_type_county_and_main()
    {
        var (client, _, _) = await Onboarded();

        var res = await client.GetAsync("/api/locations/template");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(Xlsx, res.Content.Headers.ContentType?.MediaType);
        Assert.Contains("locations-template.xlsx", res.Content.Headers.ContentDisposition?.ToString());
        using var book = new XLWorkbook(await res.Content.ReadAsStreamAsync());
        var sheet = book.Worksheet(1);
        Assert.Equal(Header, Enumerable.Range(1, Header.Length).Select(c => sheet.Cell(1, c).GetString()));
        var lists = book.Worksheet("Lists");
        Assert.Contains("Nairobi", lists.Column(2).CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Branch", lists.Column(1).CellsUsed().Select(c => c.GetString()));
        Assert.True(sheet.Cell(2, 4).HasDataValidation);   // County picks from the list
        Assert.True(sheet.Cell(2, 2).HasDataValidation);   // and so does Type
    }

    [Fact]
    public async Task A_good_file_comes_back_row_by_row_as_written()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");

        var res = await Check(client, Workbook(
            ["Westlands branch", "Branch", null, "Nairobi", "Westlands", null, "No"],
            [" Mombasa shop ", "warehouse", "msa-09", "mombasa", "Mombasa", "Digo Road", "Yes"],
            [null, null, null, null, null, null, null]));   // empty rows are skipped

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var check = (await res.Content.ReadFromJsonAsync<Checked>())!;
        Assert.Empty(check.Problems);
        // The rows come back as written (trimmed), for the person to review; the checks read the labels.
        Assert.Equal([2, 3], check.Rows.Select(r => r.Line));
        Assert.Equal(R(2, "Westlands branch", "Branch", "", "Nairobi", "Westlands", "", "No").Cells, check.Rows[0].Cells);
        Assert.Equal(R(3, "Mombasa shop", "warehouse", "msa-09", "mombasa", "Mombasa", "Digo Road", "Yes").Cells, check.Rows[1].Cells);
    }

    [Fact]
    public async Task Problems_are_listed_by_row_and_column()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");
        var taken = (await client.GetFromJsonAsync<List<Location>>("/api/locations"))!.Single().Code;

        var check = (await (await Check(client, Workbook(
            [null, "Branch", null, "Nairobi", null, null, null],
            ["Atlantis office", "Castle", null, "Atlantis", null, null, null],
            ["Shop one", null, "SHOP-1", "Nairobi", null, null, "Yes"],
            ["Shop two", null, "shop-1", "Nairobi", null, null, "Yes"],
            ["Old head office", null, taken, "Nairobi", null, null, "Maybe"]))).Content.ReadFromJsonAsync<Checked>())!;

        Assert.Equal(new (int, string?)[]
        {
            (2, "name"), (3, "kind"), (3, "county"), (5, "code"), (5, "main"), (6, "code"), (6, "main"),
        }, check.Problems.Select(p => (p.Line, p.Column)).ToArray());
        Assert.Contains("Atlantis", check.Problems.Single(p => p.Line == 3 && p.Column == "county").Message);
        Assert.Equal(5, check.Rows.Count);   // every row comes back, problems and all, to be fixed in place
    }

    [Fact]
    public async Task A_csv_file_works_too()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");
        var csv = "Location name,Type,Code,County,Town,Address,Main location\r\n" +
                  "Karen branch,Branch,,Nairobi,Karen,\"Karen Road, 2nd floor\",No\r\n";

        var check = (await (await Check(client, Encoding.UTF8.GetBytes(csv), "locations.csv", "text/csv")).Content.ReadFromJsonAsync<Checked>())!;

        Assert.Empty(check.Problems);
        Assert.Equal("Karen Road, 2nd floor", Assert.Single(check.Rows).Cells["address"]);
    }

    [Fact]
    public async Task Files_that_cannot_be_read_have_no_rows_or_are_too_big_are_refused()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");

        var garbage = await Check(client, [1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        Assert.Equal("ONB-3516", (await ErrorFrom(garbage)).Code);

        // Only .xlsx and .csv, whatever the file says it is.
        var pdf = await Check(client, Encoding.UTF8.GetBytes("Location name,Type\r\n"), "locations.pdf", "text/csv");
        var wrongType = await ErrorFrom(pdf);
        Assert.Equal(("ONB-3543", "Only .xlsx or .csv files can be imported. locations.pdf is a .pdf file."), (wrongType.Code, wrongType.Message));

        // The columns are checked first: a file that isn't the template says why.
        var noHeader = await Check(client, Encoding.UTF8.GetBytes("Name,Place\r\nShop,Nairobi\r\n"), "x.csv", "text/csv");
        var mismatch = await ErrorFrom(noHeader);
        Assert.Equal("ONB-3544", mismatch.Code);
        Assert.Equal("Invalid file: it doesn't match the locations template. Missing columns: Location name, Type, Code, County, Town, Address, Main location. "
            + "Columns the template doesn't have: Name, Place. Download the template and copy your rows into it.", mismatch.Message);
        var oneMissing = await ErrorFrom(await Check(client, Encoding.UTF8.GetBytes("Location name,Type,Code,County,Town,Address\r\nShop,Branch,,Nairobi,,\r\n"), "x.csv", "text/csv"));
        Assert.Equal("Invalid file: it doesn't match the locations template. Missing columns: Main location. Download the template and copy your rows into it.", oneMissing.Message);

        var empty = (await (await Check(client, Workbook())).Content.ReadFromJsonAsync<Checked>())!;
        Assert.Equal("The file has no locations.", Assert.Single(empty.Problems).Message);

        var many = new StringBuilder("Location name,Type,Code,County,Town,Address,Main location\r\n");
        for (var i = 0; i < 1001; i++) many.Append($"Shop {i},Branch,,Nairobi,,,No\r\n");
        var tooMany = await Check(client, Encoding.UTF8.GetBytes(many.ToString()), "x.csv", "text/csv");
        Assert.Equal("ONB-3517", (await ErrorFrom(tooMany)).Code);
    }

    [Fact]
    public async Task Edited_rows_are_checked_again_without_a_file()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");

        var res = await client.PostAsJsonAsync("/api/locations/import/check", new
        {
            rows = new[] { R(2, "Westlands branch", "Branch"), R(3, "Atlantis office", "Castle", county: "Atlantis"), R(4, "Fixed shop", county: "Kisumu") },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var check = (await res.Content.ReadFromJsonAsync<RowsChecked>())!;
        Assert.Equal(new (int, string?)[] { (3, "kind"), (3, "county") }, check.Problems.Select(p => (p.Line, p.Column)).ToArray());
    }

    [Fact]
    public async Task Importing_saves_every_row_for_the_chosen_companies_and_the_main_row_becomes_their_main()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        var check = (await (await Check(client, Workbook(
            ["Westlands branch", "Branch", null, "Nairobi", "Westlands", null, "No"],
            ["Mombasa shop", "Warehouse", null, "Mombasa", "Mombasa", "Digo Road", "Yes"]))).Content.ReadFromJsonAsync<Checked>())!;

        var res = await client.PostAsJsonAsync("/api/locations/import", new { rows = check.Rows, allCompanies = false, companyIds = new[] { a.Id } });   // the rows as reviewed

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, (await res.Content.ReadFromJsonAsync<ImportResult>())!.Imported);
        var all = (await client.GetFromJsonAsync<List<Location>>("/api/locations"))!;
        var westlands = all.Single(l => l.Name == "Westlands branch");
        var shop = all.Single(l => l.Name == "Mombasa shop");
        Assert.Equal(("WES-01", "branch"), (westlands.Code, westlands.Kind));
        Assert.Equal([a.Id], westlands.CompanyIds);
        Assert.Equal(("warehouse", "cities"), (shop.Kind, shop.WageAreaCode));
        Assert.Equal([a.Id], shop.MainFor);
        Assert.Contains(all, l => l.Kind == "head_office" && l.MainFor.SequenceEqual([b.Id]));   // B keeps its own main
    }

    [Fact]
    public async Task Importing_for_all_companies_makes_the_main_row_main_for_each()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        var check = (await (await Check(client, Workbook(["Group office", "Office", "GRP-HQ", "Nairobi", "Nairobi", null, "Yes"])))
            .Content.ReadFromJsonAsync<Checked>())!;

        await client.PostAsJsonAsync("/api/locations/import", new { rows = check.Rows, allCompanies = true, companyIds = Array.Empty<Guid>() });

        var office = (await client.GetFromJsonAsync<List<Location>>("/api/locations"))!.Single(l => l.Code == "GRP-HQ");
        Assert.True(office.AllCompanies);
        Assert.Equal([a.Id, b.Id], office.MainFor.Order());
    }

    [Fact]
    public async Task Import_is_all_or_nothing_and_checks_the_rows_again()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var before = (await client.GetFromJsonAsync<List<Location>>("/api/locations"))!.Count;

        var res = await client.PostAsJsonAsync("/api/locations/import", new
        {
            rows = new[] { R(2, "Westlands branch"), R(3, "Nowhere shop", county: "Atlantis") },
            allCompanies = false, companyIds = new[] { a.Id },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await ErrorFrom(res);
        Assert.Equal("ONB-3518", error.Code);
        Assert.Contains("Row 3", error.Message);
        Assert.Equal(before, (await client.GetFromJsonAsync<List<Location>>("/api/locations"))!.Count);
        var noCompanies = await client.PostAsJsonAsync("/api/locations/import", new
        {
            rows = new[] { R(2, "Westlands branch") }, allCompanies = false, companyIds = Array.Empty<Guid>(),
        });
        Assert.Equal("ONB-3512", (await ErrorFrom(noCompanies)).Code);
    }

    [Fact]
    public async Task Plain_members_cannot_check_or_import()
    {
        var (a, _, orgId) = await Onboarded();
        await AddCompany(a, "Oliech Restaurants Ltd");
        var (b, bEmail, _) = await Onboarded();
        await using (var connection = new NpgsqlConnection(System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "insert into memberships (id, organization_id, user_id) select uuidv7(), @org, id from users where email = @email; " +
                "update memberships set status = 'removed' where user_id = (select id from users where email = @email) and organization_id <> @org;",
                connection);
            command.Parameters.AddWithValue("org", orgId);
            command.Parameters.AddWithValue("email", bEmail);
            await command.ExecuteNonQueryAsync();
        }
        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = bEmail, password = Password });
        b.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);

        Assert.Equal("ACC-8001", (await ErrorFrom(await Check(b, Workbook(["Shop", null, null, "Nairobi", null, null, null])))).Code);
        Assert.Equal("ACC-8001", (await ErrorFrom(await b.PostAsJsonAsync("/api/locations/import",
            new { rows = Array.Empty<Row>(), allCompanies = true, companyIds = Array.Empty<Guid>() }))).Code);
    }
}
