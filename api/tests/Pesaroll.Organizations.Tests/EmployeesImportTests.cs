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
    private sealed record Employee(Guid Id, string StaffNo, string FirstName, string? KraPinMasked, DateOnly HireDate, DateOnly? ExitDate, Job Job, Pay? Pay, Account? Account,
        string? Address = null);
    private sealed record Row(Guid Id, string StaffNo, string Name, string? DepartmentName, string? PositionTitle);
    private sealed record Page(List<Row> Items, int Total);
    private sealed record Item(Guid Id, string Name, bool AllCompanies, List<Guid> CompanyIds);

    private async Task<(HttpClient Client, Company Company)> Onboarded()
    {
        var (client, company) = await OnboardedWithoutDaily();
        await client.PostAsJsonAsync("/api/pay-schedules", new { name = "Daily", frequency = "daily", allCompanies = true });
        return (client, company);
    }

    private async Task<(HttpClient Client, Company Company)> OnboardedWithoutDaily()
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
    public async Task The_template_is_for_one_company_with_its_lists_as_dropdowns()
    {
        var (client, company) = await Onboarded();
        await client.PostAsJsonAsync("/api/structure/departments", new { name = "Kitchen", allCompanies = true });

        var bytes = await client.GetByteArrayAsync($"/api/employees/template?companyId={company.Id}");

        using var book = new XLWorkbook(new MemoryStream(bytes));
        var sheet = book.Worksheet(1);
        var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(["Staff number", "First name", "Middle name", "Last name", "Date of birth (DD/MM/YYYY)", "Phone", "Email", "Address", "ID number", "KRA PIN",
            "NSSF number", "SHA number", "Start date (DD/MM/YYYY)", "Still works here", "Date left (DD/MM/YYYY)", "Employment type", "Contract end (DD/MM/YYYY)",
            "Division", "Department", "Unit",
            "Position", "Line manager (staff number)", "Payroll schedule", "Leave plan", "Basic pay", "Paid into", "Bank", "Branch", "Account number",
            "M-Pesa number"], headers);   // no Company (chosen first), no Paid (the schedule says), no Location (the company's main one)
        var lists = book.Worksheet("Lists").CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Kitchen", lists);
        Assert.Contains("Monthly", lists);      // the company's schedule
        Assert.Contains("Standard", lists);     // its leave plan
        Assert.Contains("Equity Bank", lists);
        Assert.Contains("Yes", lists);
        Assert.Contains("M-Pesa", lists);       // Paid into

        // Branch offers only the branches of the row's bank (a dependent dropdown, as Excel does them).
        var branchColumn = headers.IndexOf("Branch") + 1;
        var bankColumn = sheet.Cell(1, headers.IndexOf("Bank") + 1).Address.ColumnLetter;
        var rule = sheet.Cell(2, branchColumn).GetDataValidation();
        Assert.Contains("INDIRECT", rule.Value);
        Assert.Contains($"VLOOKUP({bankColumn}2,", rule.Value);   // the row's own bank…
        Assert.Matches(@"^INDIRECT\(VLOOKUP\([A-Z]+2,Lists!\$[A-Z]+\$1:\$[A-Z]+\$\d+,2,FALSE\)\)$", rule.Value);   // …in a lookup table that stays put
        var listName = book.Worksheet("Lists").CellsUsed().Where(c => c.GetString() == "Equity Bank")
            .Select(c => c.CellRight().GetString()).Single(v => v.StartsWith("branch_"));   // the bank's row of the lookup table
        var equity = book.DefinedNames.Single(n => n.Name == listName);
        Assert.Equal(["Kisumu", "Moi Avenue, Nairobi", "Mombasa"], equity.Ranges.Single().Cells().Select(c => c.GetString()).Order().ToList());
    }

    [Fact]
    public async Task People_who_work_here_are_paid_into_an_account_the_company_pays_from()
    {
        var (client, company) = await Onboarded();
        var check = async (params Dictionary<string, string>[] rows) =>
            (await (await client.PostAsJsonAsync("/api/employees/import/check", RowsBody(rows))).Content.ReadFromJsonAsync<Check>())!.Problems
                .Select(p => (p.Line, p.Column, p.Message)).ToList();

        // No paying account yet: nothing is required.
        Assert.Empty(await check(Person("Wanjiru", "Kamau")));

        await client.PostAsJsonAsync($"/api/companies/{company.Id}/payment-accounts", new
        {
            method = "bank", bankCode = "68", branchCode = "047", accountName = "Mama Oliech Restaurants Ltd", accountNumber = "0123456789012",
        });
        Assert.Equal([(2, "bank", "Mama Oliech Restaurants Ltd pays by bank: add their bank details."),
                      (3, "mpesa", "Mama Oliech Restaurants Ltd pays by bank: add their bank details."),
                      (4, "mpesa", "Give a bank account or an M-Pesa number, not both.")],
            await check(
                Person("Wanjiru", "Kamau"),
                Person("Otieno", "Ouma", ("mpesa", "0791 569 999")),
                Person("Akinyi", "Odera", ("bank", "Equity Bank"), ("branch", "047"), ("accountNumber", "0123456789012"), ("mpesa", "0791569999"))));

        // With an M-Pesa account too, either will do; Paid into says which, and then its details are needed.
        await client.PostAsJsonAsync($"/api/companies/{company.Id}/payment-accounts", new { method = "mpesa", mpesaShortcode = "123456", accountName = "Mama Oliech" });
        Assert.Equal([(2, "paidInto", "Add their bank details or M-Pesa number."),
                      (4, "mpesa", "Paid into M-Pesa: add their M-Pesa number."),
                      (5, "paidInto", "Choose Bank or M-Pesa.")],
            await check(
                Person("Wanjiru", "Kamau"),
                Person("Otieno", "Ouma", ("paidInto", "M-Pesa"), ("mpesa", "0791569999")),
                Person("Akinyi", "Odera", ("paidInto", "M-Pesa"), ("bank", ""), ("mpesa", "")),
                Person("Baraka", "Mwangi", ("paidInto", "Cheque"))));
    }

    private sealed record Read(List<ImportRowDto> Rows, List<Problem> Problems);
    private sealed record ImportRowDto(int Line, Dictionary<string, string> Cells);

    [Fact]
    public async Task A_file_is_read_for_the_company_chosen_first_with_its_dates_as_written_in_Kenya()
    {
        var (client, company) = await Onboarded();
        await client.PostAsJsonAsync("/api/companies", new { legalName = "Centrail Ltd", countryCode = "KE", town = "Nairobi", regionCode = "047" });
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Employees");
        string[] headers = ["First name", "Last name", "Date of birth", "Start date (DD/MM/YYYY)", "Still works here"];   // an older header still reads
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }
        sheet.Cell(2, 1).Value = "Maggie";
        sheet.Cell(2, 2).Value = "Kibui";
        sheet.Cell(2, 3).Value = "17/03/1998";                     // typed as text
        sheet.Cell(2, 4).Value = new DateTime(2026, 1, 1);          // a real date cell
        sheet.Cell(2, 4).Style.NumberFormat.Format = "dd/mm/yyyy";
        sheet.Cell(2, 5).Value = "Yes";
        sheet.Cell(3, 1).Value = "Otieno";
        sheet.Cell(3, 2).Value = "Ouma";
        sheet.Cell(3, 4).Value = "31/02/2024";                     // no such day
        using var stream = new MemoryStream();
        book.SaveAs(stream);

        var read = async (string query) =>
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent(stream.ToArray()), "file", "people.xlsx" } };
            return (await (await client.PostAsync($"/api/employees/import/read{query}", form)).Content.ReadFromJsonAsync<Read>())!;
        };
        var withCompany = await read($"?companyId={company.Id}");
        var without = await read("");

        Assert.Equal(("1998-03-17", "2026-01-01"), (withCompany.Rows[0].Cells["dateOfBirth"], withCompany.Rows[0].Cells["hireDate"]));
        Assert.Equal("31/02/2024", withCompany.Rows[1].Cells["hireDate"]);   // not a date: as typed, to fix
        Assert.Equal([(3, "hireDate")], withCompany.Problems.Select(p => (p.Line, p.Column)));
        Assert.Contains(without.Problems, p => p.Column == "company");   // two companies, none chosen
    }

    [Fact]
    public async Task A_branch_of_another_bank_says_whose_it_is_and_which_to_choose()
    {
        var (client, _) = await Onboarded();

        var check = (await (await client.PostAsJsonAsync("/api/employees/import/check", RowsBody(
            Person("Maggie", "Kibui", ("paidInto", "Bank"), ("bank", "Equity Bank"), ("branch", "City Centre, Nairobi"), ("accountNumber", "0340169211195"))))).Content
            .ReadFromJsonAsync<Check>())!;

        Assert.Equal((2, "branch", "City Centre, Nairobi is a branch of NCBA Bank. Choose a branch of Equity Bank: Kisumu (029), Moi Avenue, Nairobi (047) or Mombasa (025)."),
            check.Problems.Select(p => (p.Line, p.Column!, p.Message)).Single());
    }

    [Fact]
    public async Task Problems_show_in_their_columns_and_new_structure_is_noted()
    {
        var (client, _) = await Onboarded();

        var res = await client.PostAsJsonAsync("/api/employees/import/check", RowsBody(
            Person("Wanjiru", "Kamau", ("staffNo", "K-1"), ("kraPin", "P051234567X"), ("department", "Kitchen"), ("position", "Head cook"), ("division", "Restaurants")),
            Person("Otieno", "Ouma", ("staffNo", "k-1"), ("payrollSchedule", "Weekly casuals"), ("unit", "Night shift"), ("basicPay", "900")),
            Person("Akinyi", "Odera", ("lineManager", "K-404"), ("accountNumber", "0123456789"), ("stillHere", "No"))));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var check = (await res.Content.ReadFromJsonAsync<Check>())!;
        Assert.Equal([(2, "staffNo"), (2, "kraPin"), (3, "staffNo"), (3, "unit"), (3, "payrollSchedule"), (4, "exitDate"), (4, "lineManager"), (4, "bank")],
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
            Person("Wanjiru", "Kamau", ("staffNo", "K-1"), ("kraPin", "A012345678Z"), ("nationalId", "12345678"), ("department", "Kitchen"), ("address", "Kilimani, Nairobi"),
                ("position", "Head cook"), ("basicPay", "85,000"), ("bank", "Equity Bank"), ("branch", "047"), ("accountNumber", "0123456789012")),
            Person("Otieno", "Ouma", ("staffNo", "K-2"), ("lineManager", "K-1"), ("department", "kitchen"), ("employmentType", "Casual"),
                ("payrollSchedule", "Daily"), ("basicPay", "900"), ("mpesa", "0722 000 111")),
            Person("Akinyi", "Odera", ("hireDate", "15/01/2023"), ("stillHere", "No"), ("exitDate", "30/06/2024"), ("kraPin", "A099999999B"))));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3, (await res.Content.ReadFromJsonAsync<Result>())!.Imported);

        var page = (await client.GetFromJsonAsync<Page>("/api/employees?status=all"))!;
        Assert.Equal(3, page.Total);
        var wanjiru = (await client.GetFromJsonAsync<Employee>($"/api/employees/{page.Items.Single(r => r.StaffNo == "K-1").Id}"))!;
        var otieno = (await client.GetFromJsonAsync<Employee>($"/api/employees/{page.Items.Single(r => r.StaffNo == "K-2").Id}"))!;
        var akinyi = (await client.GetFromJsonAsync<Employee>($"/api/employees/{page.Items.Single(r => r.Name == "Akinyi Odera").Id}"))!;
        Assert.Equal(("•••• 678Z", 85000m, "bank", "68", "•••• 9012"), (wanjiru.KraPinMasked, wanjiru.Pay!.BasicAmount, wanjiru.Account!.Method, wanjiru.Account.BankCode, wanjiru.Account.AccountMasked));
        Assert.Equal("monthly", wanjiru.Pay.PayBasis);   // from the Monthly schedule
        Assert.Equal("Kilimani, Nairobi", wanjiru.Address);
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

        Assert.Equal([(2, "staffNo"), (2, "kraPin"), (3, "nationalId"), (4, "nationalId")], check.Problems.Select(p => (p.Line, p.Column)));
        Assert.Equal("Wanjiru Kamau (K-1) already has this KRA PIN.", check.Problems[1].Message);
    }

    [Fact]
    public async Task A_re_import_is_caught_by_email_phone_NSSF_and_SHA_numbers_too()
    {
        var (client, company) = await Onboarded();
        await client.PostAsJsonAsync("/api/employees", new
        {
            companyId = company.Id, staffNo = "K-1", firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", email = "Wanjiru@Example.com",
            phone = "0712 345 678", nssfNo = "201234567", shaNo = "CR1234567-8", job = new { employmentType = "permanent" },
        });

        var check = (await (await client.PostAsJsonAsync("/api/employees/import/check", RowsBody(
            Person("Wanjiru", "Kamau", ("email", "wanjiru@example.com"), ("phone", "+254712345678"), ("nssfNo", "201234567"), ("shaNo", "cr1234567-8")),
            Person("Otieno", "Ouma", ("email", "otieno@example.com"), ("phone", "0722 000 111")),
            Person("Akinyi", "Odera", ("email", "OTIENO@example.com"), ("phone", "0722000111")),
            Person("Baraka", "Mwangi", ("phone", "0712345678"), ("stillHere", "No"), ("exitDate", "2024-06-30"))))).Content.ReadFromJsonAsync<Check>())!;   // someone who left

        Assert.Equal([(2, "phone"), (2, "email"), (2, "nssfNo"), (2, "shaNo"), (3, "phone"), (3, "email"), (4, "phone"), (4, "email")],
            check.Problems.Select(p => (p.Line, p.Column)));
        Assert.Equal("Wanjiru Kamau (K-1) already has this email address.", check.Problems.Single(p => p is { Line: 2, Column: "email" }).Message);
        Assert.Equal("Row 3 has this phone number too.", check.Problems.Single(p => p is { Line: 4, Column: "phone" }).Message);
        Assert.Equal("Row 4 has this phone number too.", check.Problems.Single(p => p is { Line: 3, Column: "phone" }).Message);   // both rows, to see the pair
    }

    [Fact]
    public async Task Rows_of_a_file_cant_share_an_M_Pesa_number_or_bank_account()
    {
        var (client, _) = await Onboarded();
        Dictionary<string, string> Bank(string first, string account) =>
            Person(first, "Kamau", ("paidInto", "Bank"), ("bank", "Equity Bank"), ("branch", "047"), ("accountNumber", account));

        var check = (await (await client.PostAsJsonAsync("/api/employees/import/check", RowsBody(
            Person("Wanjiru", "Kamau", ("paidInto", "M-Pesa"), ("mpesa", "0712 345 678")),
            Person("Otieno", "Ouma", ("paidInto", "M-Pesa"), ("mpesa", "+254712345678")),
            Bank("Akinyi", "0123456789012"),
            Bank("Baraka", "0123-4567-89012"),
            Bank("Chebet", "0123456789099"),
            Person("Njeri", "Kamau", ("paidInto", "M-Pesa"), ("mpesa", "0712345678"), ("stillHere", "No"), ("exitDate", "2024-06-30"))))).Content   // left: not counted
            .ReadFromJsonAsync<Check>())!;

        Assert.Equal([(2, "mpesa", "Row 3 has this M-Pesa number too."), (3, "mpesa", "Row 2 has this M-Pesa number too."),
                      (4, "accountNumber", "Row 5 has this bank account too."), (5, "accountNumber", "Row 4 has this bank account too.")],
            check.Problems.Select(p => (p.Line, p.Column!, p.Message)));
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
