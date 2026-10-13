using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-10: payroll history from the previous system, one row per employee per month, for one
/// company chosen first. People are found by staff number, ID number or KRA PIN; someone not in
/// Pesaroll is added as a former employee. Errors block; warnings (net pay that doesn't add up, a
/// KRA PIN unlike their profile) don't. Imported in a batch that can be deleted.
/// </summary>
public sealed class PayrollHistoryTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Item(Guid Id);
    private sealed record Problem(int Line, string? Column, string Message, string? Kind);
    private sealed record Check(List<Problem> Problems, List<Problem> Notes);
    private sealed record Result(int Imported, List<Problem>? Problems);
    private sealed record HistoryLine(string StaffNo, string Name, DateOnly Period, decimal GrossPay, decimal Paye, decimal? NetPay, string? Notes);
    private sealed record Batch(Guid Id, Guid CompanyId, string FileName, int RowCount, DateOnly FirstPeriod, DateOnly LastPeriod, decimal GrossTotal, decimal PayeTotal);
    private sealed record Row(Guid Id, string StaffNo, string Name, DateOnly? ExitDate);
    private sealed record Page(List<Row> Items, int Total);
    private sealed record Employee(DateOnly HireDate, DateOnly? ExitDate, string? KraPinMasked);

    private static readonly DateTime Now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Africa/Nairobi");

    private async Task<(HttpClient Client, Company Company)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email = $"history-{Guid.NewGuid():N}@example.com", password = "Mombasa-raha-7",
            roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        var company = (await (await client.PostAsJsonAsync("/api/companies", new { legalName = "Pesaroll Ltd", countryCode = "KE", town = "Nairobi", regionCode = "047" }))
            .Content.ReadFromJsonAsync<Company>())!;
        var res = await client.PostAsJsonAsync("/api/employees", new { companyId = company.Id, staffNo = "EMP-01", firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15",
            nationalId = "12345678", kraPin = "A012345678Z", job = new { employmentType = "permanent" } });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (client, company);
    }

    /// <summary>A month that adds up: gross 85,000 less NSSF 4,320, SHIF 2,338, Housing Levy 1,275 and PAYE 15,000 is 62,067.</summary>
    private static Dictionary<string, string> Line(string month, params (string Key, string Value)[] more)
    {
        var cells = new Dictionary<string, string>
        {
            ["company"] = "Pesaroll Ltd", ["staffNo"] = "EMP-01", ["kraPin"] = "A012345678Z", ["nationalId"] = "", ["name"] = "Wanjiru Kamau", ["month"] = month,
            ["basic"] = "85000", ["gross"] = "85,000", ["nssf"] = "4320", ["shif"] = "2338", ["ahl"] = "1275", ["paye"] = "15000", ["net"] = "62067",
        };
        foreach (var (key, value) in more) cells[key] = value;
        return cells;
    }

    private static object Body(string? fileName, params Dictionary<string, string>[] rows) =>
        new { rows = rows.Select((cells, i) => new { line = i + 2, cells }), allCompanies = false, companyIds = Array.Empty<Guid>(), fileName };

    [Fact]
    public async Task The_template_has_the_months_columns_and_no_company()
    {
        var (client, company) = await Onboarded();
        using var book = new XLWorkbook(new MemoryStream(await client.GetByteArrayAsync($"/api/payroll-history/template?companyId={company.Id}")));
        var headers = book.Worksheet(1).Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(["Employee number", "KRA PIN", "ID or passport number", "Employee name", "Pay month (YYYY-MM)", "Pay date (DD/MM/YYYY)", "Basic pay amount",
            "Cash allowances amount", "Non-cash benefits amount", "Value of quarters amount", "Gross pay amount", "Pension (employee) amount", "NSSF employee Tier I amount", "NSSF employee Tier II amount",
            "NSSF employee total amount", "NSSF employer total amount", "SHIF / NHIF amount", "Housing Levy (employee) amount", "Housing Levy (employer) amount", "Post-retirement medical fund amount",
            "Mortgage interest amount", "Taxable pay amount", "Personal relief amount", "Insurance relief amount", "PAYE amount", "Other deductions amount", "Net pay amount", "NITA amount", "Notes"], headers);
    }

    [Fact]
    public async Task Errors_block_warnings_dont_and_new_people_are_noted()
    {
        var (client, _) = await Onboarded();
        await client.PostAsJsonAsync("/api/payroll-history/import", Body("first.xlsx", Line("2026-01")));
        var thisMonth = $"{Now:yyyy-MM}";

        var check = (await (await client.PostAsJsonAsync("/api/payroll-history/import/check", Body(null,
            Line("2026-01"),                                                                        // 2: already imported
            Line("2026-02"), Line("February 2026"),                                                 // 3, 4: the same month twice in the file
            Line(thisMonth),                                                                         // 5: not before this month
            Line("2026-13"),                                                                         // 6: not a month
            Line("2026-03", ("paye", "")),                                                           // 7: PAYE missing
            Line("2026-04", ("gross", "-5")),                                                        // 8: below 0
            Line("2024-04", ("ahl", "")), Line("2024-02", ("ahl", ""), ("net", "63342")),            // 9: Housing Levy from March 2024; 10: fine before
            Line("2026-05", ("net", "70000")),                                                       // 11: warning, doesn't add up
            Line("2026-06", ("kraPin", "A099999999B")),                                              // 12: warning, unlike the profile
            Line("2026-03", ("staffNo", ""), ("kraPin", "A111111111C"), ("name", "Otieno Ouma")),    // 13: note, a new former employee
            Line("2026-03", ("staffNo", ""), ("kraPin", ""), ("name", "Nobody")))))                  // 14: can't be found or added
            .Content.ReadFromJsonAsync<Check>())!;

        Assert.Equal([(2, "month"), (4, "month"), (5, "month"), (6, "month"), (7, "paye"), (8, "gross"), (9, "ahl"), (14, "kraPin")],
            check.Problems.Select(p => (p.Line, p.Column!)));
        Assert.Equal("Wanjiru Kamau's January 2026 is already imported.", check.Problems.Single(p => p.Line == 2).Message);
        Assert.Equal("Row 3 has Wanjiru Kamau's February 2026 too.", check.Problems.Single(p => p.Line == 4).Message);
        var notes = check.Notes.Select(n => (n.Line, n.Column!, n.Kind)).ToList();
        Assert.Equal([(11, "net", "warning"), (12, "kraPin", "warning"), (13, "name", null)], notes);
        Assert.StartsWith("Net pay is KES 70,000, but gross less deductions is KES 62,067", check.Notes.Single(n => n.Line == 11).Message);
        Assert.Equal("Otieno Ouma isn't in Pesaroll: they'll be added as someone who has left. If they still work here, add them under Employees first.",
            check.Notes.Single(n => n.Line == 13).Message);
    }

    [Fact]
    public async Task An_import_is_a_batch_with_its_months_new_former_employees_and_can_be_deleted()
    {
        var (client, company) = await Onboarded();

        var res = await client.PostAsJsonAsync("/api/payroll-history/import", Body("old-payroll.xlsx",
            Line("2026-01"), Line("2026-02"),
            Line("2026-01", ("staffNo", ""), ("kraPin", "A111111111C"), ("name", "Otieno Ouma"), ("gross", "50000"), ("basic", "50000"), ("paye", "5000"), ("net", "37067")),
            Line("2026-02", ("staffNo", ""), ("kraPin", "a111111111c"), ("name", "Otieno Ouma"), ("gross", "50000"), ("basic", "50000"), ("paye", "5000"), ("net", "37067")),
            // Another new person: each gets their own staff number.
            Line("2026-01", ("staffNo", ""), ("kraPin", "A222222222D"), ("name", "Akinyi Wekesa"), ("gross", "40000"), ("basic", "40000"), ("paye", "0"), ("net", "32067"))));
        Assert.Equal(5, (await res.Content.ReadFromJsonAsync<Result>())!.Imported);

        var batch = Assert.Single((await client.GetFromJsonAsync<List<Batch>>($"/api/payroll-history/batches?companyIds={company.Id}"))!);
        Assert.Equal((company.Id, "old-payroll.xlsx", 5, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), 310000m, 40000m),
            (batch.CompanyId, batch.FileName, batch.RowCount, batch.FirstPeriod, batch.LastPeriod, batch.GrossTotal, batch.PayeTotal));
        Assert.Single((await client.GetFromJsonAsync<List<Batch>>("/api/payroll-history/batches"))!);   // every company's

        // Otieno is a former employee: from his first month, left at the end of his last.
        var otieno = (await client.GetFromJsonAsync<Page>("/api/employees?status=former&search=otieno"))!.Items.Single();
        var detail = (await client.GetFromJsonAsync<Employee>($"/api/employees/{otieno.Id}"))!;
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28), "•••• 111C"), (detail.HireDate, detail.ExitDate!.Value, detail.KraPinMasked));
        var akinyi = (await client.GetFromJsonAsync<Page>("/api/employees?status=former&search=akinyi"))!.Items.Single();
        Assert.NotEqual(otieno.StaffNo, akinyi.StaffNo);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/payroll-history/batches/{batch.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<Batch>>("/api/payroll-history/batches"))!);
        Assert.Equal("ONB-3552", (await (await client.DeleteAsync($"/api/payroll-history/batches/{batch.Id}")).Content.ReadFromJsonAsync<Error>())!.Code);
    }

    [Fact]
    public async Task An_import_saves_the_good_rows_and_returns_the_rest_with_their_problems()
    {
        var (client, company) = await Onboarded();
        var res = await client.PostAsJsonAsync("/api/payroll-history/import", Body("mixed.xlsx",
            Line("2026-01", ("notes", "January")), Line("2026-02"), Line("2026-02"), Line("2026-03", ("paye", ""))));
        var result = (await res.Content.ReadFromJsonAsync<Result>())!;
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, result.Imported);
        Assert.Equal([(4, "month"), (5, "paye")], result.Problems!.Select(p => (p.Line, p.Column!)));

        // The batch holds what was saved; its lines can be read.
        var batch = Assert.Single((await client.GetFromJsonAsync<List<Batch>>($"/api/payroll-history/batches?companyIds={company.Id}"))!);
        Assert.Equal(2, batch.RowCount);
        var lines = (await client.GetFromJsonAsync<List<HistoryLine>>($"/api/payroll-history/batches/{batch.Id}/lines"))!;
        Assert.Equal([("EMP-01", "Wanjiru Kamau", new DateOnly(2026, 1, 1), 85000m, 15000m, (decimal?)62067m, "January"), ("EMP-01", "Wanjiru Kamau", new DateOnly(2026, 2, 1), 85000m, 15000m, 62067m, null)],
            lines.Select(l => (l.StaffNo, l.Name, l.Period, l.GrossPay, l.Paye, l.NetPay, l.Notes)));

        // Nothing good: nothing saved, no empty batch.
        var none = (await (await client.PostAsJsonAsync("/api/payroll-history/import", Body("bad.xlsx", Line("2026-03", ("paye", ""))))).Content.ReadFromJsonAsync<Result>())!;
        Assert.Equal((0, 1), (none.Imported, none.Problems!.Count));
        Assert.Single((await client.GetFromJsonAsync<List<Batch>>("/api/payroll-history/batches"))!);
        Assert.Equal("ONB-3552", (await (await client.GetAsync($"/api/payroll-history/batches/{Guid.NewGuid()}/lines")).Content.ReadFromJsonAsync<Error>())!.Code);
    }
}
