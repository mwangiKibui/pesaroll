using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-09: uploading employees through the shared review. Rows are held to the same rules as adding
/// one by hand; departments and positions that don't exist yet are created; line managers can be
/// in the same file; people who have left come in too.
/// </summary>
public sealed class EmployeesImportTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Problem(int Line, string? Column, string Message);
    private sealed record Check(List<Problem> Problems, List<Problem> Notes);
    private sealed record Result(int Imported);
    private sealed record Job(Guid? DepartmentId, Guid? PositionId, Guid? LineManagerId, string EmploymentType);
    private sealed record Pay(decimal BasicAmount, string PayBasis);
    private sealed record Account(string Method, string? BankCode, string? AccountMasked, string? MpesaMasked);
    private sealed record Employee(Guid Id, string StaffNo, string FirstName, string? KraPinMasked, DateOnly HireDate, DateOnly? ExitDate, Job Job, Pay? Pay, Account? Account);
    private sealed record Row(Guid Id, string StaffNo, string Name, string? DepartmentName, string? PositionTitle);
    private sealed record Page(List<Row> Items, int Total);
    private sealed record Item(Guid Id, string Name, bool AllCompanies, List<Guid> CompanyIds);

    private async Task<(HttpClient Client, Company Company)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email = $"empimp-{Guid.NewGuid():N}@example.com", password = "Mombasa-raha-7",
            roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        var company = (await (await client.PostAsJsonAsync("/api/companies", new { legalName = "Mama Oliech Restaurants Ltd", countryCode = "KE", town = "Nairobi", regionCode = "047" }))
            .Content.ReadFromJsonAsync<Company>())!;
        return (client, company);
    }

    private static Dictionary<string, string> Cells(params (string Key, string Value)[] cells) =>
        cells.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.Last().Value);   // a later value wins

    private static object RowsBody(params Dictionary<string, string>[] rows) =>
        new { rows = rows.Select((cells, i) => new { line = i + 2, cells }), allCompanies = false, companyIds = Array.Empty<Guid>() };

    private static Dictionary<string, string> Person(string first, string last, params (string Key, string Value)[] more) =>
        Cells([("firstName", first), ("lastName", last), ("hireDate", "2024-01-15"), .. more]);

    [Fact]
    public async Task The_template_has_every_column_with_the_structure_chain_as_text()
    {
        var (client, _) = await Onboarded();

        var bytes = await client.GetByteArrayAsync("/api/employees/template");

        using var book = new XLWorkbook(new MemoryStream(bytes));
        var headers = book.Worksheet(1).Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(["Company", "Staff number", "First name", "Middle name", "Last name", "Date of birth", "Phone", "Email", "ID number", "KRA PIN",
            "NSSF number", "SHA number", "Start date", "Date left", "Employment type", "Contract end", "Division", "Department", "Unit", "Position",
            "Location", "Line manager (staff number)", "Payroll schedule", "Leave plan", "Basic pay", "Paid", "Bank", "Branch", "Account number", "M-Pesa number"], headers);
    }

    [Fact]
    public async Task Problems_show_in_their_columns_and_new_structure_is_noted()
    {
        var (client, _) = await Onboarded();

        var res = await client.PostAsJsonAsync("/api/employees/import/check", RowsBody(
            Person("Wanjiru", "Kamau", ("staffNo", "K-1"), ("kraPin", "P051234567X"), ("department", "Kitchen"), ("position", "Head cook"), ("division", "Restaurants")),
            Person("Otieno", "Ouma", ("staffNo", "k-1"), ("payrollSchedule", "Weekly casuals"), ("unit", "Night shift"), ("basicPay", "900"), ("payBasis", "Weekly")),
            Person("Akinyi", "Odera", ("lineManager", "K-404"), ("accountNumber", "0123456789"))));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var check = (await res.Content.ReadFromJsonAsync<Check>())!;
        Assert.Equal([(2, "kraPin"), (3, "staffNo"), (3, "unit"), (3, "payrollSchedule"), (3, "payBasis"), (4, "lineManager"), (4, "bank")],
            check.Problems.Select(p => (p.Line, p.Column)));
        Assert.Contains("Kitchen", check.Notes.Single(n => n.Column == "department").Message);
        Assert.Contains("Head cook", check.Notes.Single(n => n.Column == "position").Message);
        Assert.Contains("Restaurants", check.Notes.Single(n => n.Column == "division").Message);
    }

    [Fact]
    public async Task A_clean_file_imports_with_new_departments_managers_in_the_file_and_people_who_left()
    {
        var (client, company) = await Onboarded();

        var res = await client.PostAsJsonAsync("/api/employees/import", RowsBody(
            Person("Wanjiru", "Kamau", ("staffNo", "K-1"), ("kraPin", "A012345678Z"), ("nationalId", "12345678"), ("department", "Kitchen"),
                ("position", "Head cook"), ("basicPay", "85,000"), ("payBasis", "Monthly"), ("bank", "Equity Bank"), ("branch", "047"), ("accountNumber", "0123456789012")),
            Person("Otieno", "Ouma", ("staffNo", "K-2"), ("lineManager", "K-1"), ("department", "kitchen"), ("employmentType", "Casual"),
                ("basicPay", "900"), ("payBasis", "Daily"), ("mpesa", "0722 000 111")),
            Person("Akinyi", "Odera", ("hireDate", "15/01/2023"), ("exitDate", "30/06/2024"), ("kraPin", "A099999999B"))));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3, (await res.Content.ReadFromJsonAsync<Result>())!.Imported);

        var page = (await client.GetFromJsonAsync<Page>("/api/employees?status=all"))!;
        Assert.Equal(3, page.Total);
        var wanjiru = (await client.GetFromJsonAsync<Employee>($"/api/employees/{page.Items.Single(r => r.StaffNo == "K-1").Id}"))!;
        var otieno = (await client.GetFromJsonAsync<Employee>($"/api/employees/{page.Items.Single(r => r.StaffNo == "K-2").Id}"))!;
        var akinyi = (await client.GetFromJsonAsync<Employee>($"/api/employees/{page.Items.Single(r => r.Name == "Akinyi Odera").Id}"))!;
        Assert.Equal(("•••• 678Z", 85000m, "bank", "68", "•••• 9012"), (wanjiru.KraPinMasked, wanjiru.Pay!.BasicAmount, wanjiru.Account!.Method, wanjiru.Account.BankCode, wanjiru.Account.AccountMasked));
        Assert.Equal((wanjiru.Id, wanjiru.Job.DepartmentId, "casual", "daily", "•••• 0111"),
            (otieno.Job.LineManagerId!.Value, otieno.Job.DepartmentId, otieno.Job.EmploymentType, otieno.Pay!.PayBasis, otieno.Account!.MpesaMasked));
        Assert.Equal((DateOnly.Parse("2023-01-15"), DateOnly.Parse("2024-06-30"), "EMP-01"), (akinyi.HireDate, akinyi.ExitDate!.Value, akinyi.StaffNo));
        Assert.Equal(1, (await client.GetFromJsonAsync<Page>("/api/employees"))!.Total - 1);   // two current, one former

        var kitchen = Assert.Single((await client.GetFromJsonAsync<List<Item>>("/api/structure/departments"))!);   // "Kitchen" and "kitchen" are one
        Assert.Equal(("Kitchen", false, company.Id), (kitchen.Name, kitchen.AllCompanies, Assert.Single(kitchen.CompanyIds)));
        Assert.Equal("Head cook", Assert.Single((await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/structure/positions"))!)["title"].ToString());
    }

    [Fact]
    public async Task Numbers_already_used_by_current_staff_are_refused()
    {
        var (client, company) = await Onboarded();
        await client.PostAsJsonAsync("/api/employees", new
        {
            companyId = company.Id, staffNo = "K-1", firstName = "Wanjiru", lastName = "Kamau", kraPin = "A012345678Z", hireDate = "2024-01-15", job = new { employmentType = "permanent" },
        });

        var check = (await (await client.PostAsJsonAsync("/api/employees/import/check", RowsBody(
            Person("Twin", "Kamau", ("staffNo", "K-1"), ("kraPin", "a012345678z")),
            Person("Other", "Kamau", ("nationalId", "87654321")),
            Person("Same", "Id", ("nationalId", "87654321"))))).Content.ReadFromJsonAsync<Check>())!;

        Assert.Equal([(2, "staffNo"), (2, "kraPin"), (4, "nationalId")], check.Problems.Select(p => (p.Line, p.Column)));
        Assert.Contains("K-1", check.Problems[1].Message);
    }

    [Fact]
    public async Task A_thousand_rows_import_in_one_go()
    {
        var (client, _) = await Onboarded();
        var rows = Enumerable.Range(1, 1000).Select(i => Person($"Person{i}", "Kamau", ("staffNo", $"S-{i:0000}"), ("department", $"Department {i % 20}"),
            ("lineManager", i == 1 ? "" : "S-0001"), ("kraPin", $"A{i:000000000}Z"), ("basicPay", "30000"), ("mpesa", $"07{i:00000000}"),
            ("exitDate", i % 50 == 0 ? "2025-01-31" : ""))).ToArray();

        var res = await client.PostAsJsonAsync("/api/employees/import", RowsBody(rows));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(1000, (await res.Content.ReadFromJsonAsync<Result>())!.Imported);
        Assert.Equal(980, (await client.GetFromJsonAsync<Page>("/api/employees?pageSize=1"))!.Total);
        Assert.Equal(20, (await client.GetFromJsonAsync<List<Item>>("/api/structure/departments"))!.Count);
    }

    [Fact]
    public async Task A_file_with_problems_imports_nothing()
    {
        var (client, _) = await Onboarded();

        var res = await client.PostAsJsonAsync("/api/employees/import", RowsBody(
            Person("Wanjiru", "Kamau", ("department", "Kitchen")),
            Person("", "Ouma")));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("ONB-3518", (await res.Content.ReadFromJsonAsync<Error>())!.Code);
        Assert.Equal(0, (await client.GetFromJsonAsync<Page>("/api/employees?status=all"))!.Total);
        Assert.Empty((await client.GetFromJsonAsync<List<Item>>("/api/structure/departments"))!);
    }
}
