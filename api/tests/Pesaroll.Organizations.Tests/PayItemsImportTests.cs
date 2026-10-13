using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-07 #196: uploading earnings and deductions through the shared template and review. The same
/// rules as adding one by hand, codes unique among items and in the file, never over an item set by
/// law or Basic pay, payees by name (they're added first), all or nothing.
/// </summary>
public sealed class PayItemsImportTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Item(Guid Id);
    private sealed record Problem(int Line, string? Column, string Message);
    private sealed record Check(List<Problem> Problems, List<Problem> Notes);
    private sealed record Result(int Imported);
    private sealed record PayItem(Guid Id, string Code, string Name, string Kind, bool Taxable, bool BeforeTax, string CalcMethod, decimal? DefaultAmount,
        decimal? Rate, bool Locked, bool AllCompanies, List<Guid> CompanyIds, Guid? PayeeId);

    private async Task<(HttpClient Client, Company A, Company B, Guid Stima)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email = $"itemsimp-{Guid.NewGuid():N}@example.com", password = "Mombasa-raha-7",
            roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        async Task<Company> Add(string name) =>
            (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" })).Content.ReadFromJsonAsync<Company>())!;
        var a = await Add("Pesaroll Ltd");
        var b = await Add("Centrail Ltd");
        var stima = (await (await client.PostAsJsonAsync("/api/payees", new { name = "Stima SACCO", method = "mpesa_paybill", paybill = "510800" })).Content.ReadFromJsonAsync<Item>())!.Id;
        return (client, a, b, stima);
    }

    private static Dictionary<string, string> Row(string name, params (string Key, string Value)[] more)
    {
        var cells = new Dictionary<string, string> { ["name"] = name, ["code"] = "", ["kind"] = "Earning", ["calcMethod"] = "Fixed amount", ["defaultAmount"] = "",
            ["rate"] = "", ["taxable"] = "", ["beforeTax"] = "", ["payee"] = "" };
        foreach (var (key, value) in more) cells[key] = value;
        return cells;
    }

    private static object Body(Guid[] companies, params Dictionary<string, string>[] rows) =>
        new { rows = rows.Select((cells, i) => new { line = i + 2, cells }), allCompanies = companies.Length == 0, companyIds = companies };

    [Fact]
    public async Task The_template_lists_types_ways_and_your_payees()
    {
        var (client, _, _, _) = await Onboarded();

        using var book = new XLWorkbook(new MemoryStream(await client.GetByteArrayAsync("/api/pay-items/template")));
        var headers = book.Worksheet(1).Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(["Name", "Code", "Type", "How it's worked out", "Default amount", "Rate (%)", "Taxed", "Before tax", "Paid to"], headers);
        var lists = book.Worksheet("Lists").CellsUsed().Select(c => c.GetString()).ToList();
        foreach (var expected in new[] { "Earning", "Deduction", "Employer cost", "Fixed amount", "Percentage of basic pay", "Entered each payroll", "Yes", "Stima SACCO" })
        {
            Assert.Contains(expected, lists);
        }
    }

    [Fact]
    public async Task Problems_show_in_their_columns()
    {
        var (client, _, _, _) = await Onboarded();
        await client.PostAsJsonAsync("/api/pay-items", new { code = "HOUSE", name = "House allowance", kind = "earning", taxable = true, calcMethod = "fixed", allCompanies = true });

        var check = (await (await client.PostAsJsonAsync("/api/pay-items/import/check", Body([],
            Row("Commuter", ("kind", "Bonus")),                                                                    // 2: unknown type
            Row("Pension", ("kind", "Deduction"), ("calcMethod", "Percentage of basic pay")),                      // 3: no rate
            Row("Airtime", ("defaultAmount", "-50")),                                                              // 4: amount below 0
            Row("Housing", ("code", "house")),                                                                     // 5: code taken
            Row("Meals", ("code", "MEAL")), Row("Meals two", ("code", "meal")),                                    // 6, 7: same code in the file
            Row("PAYE", ("kind", "Deduction")), Row("Basic", ("code", "BASIC")),                                   // 8, 9: the law's / Basic pay
            Row("Union dues", ("kind", "Deduction"), ("payee", "Kenya Union")),                                    // 10: no such payee
            Row("Overtime", ("calcMethod", "Entered each payroll"), ("payee", "Stima SACCO")),                     // 11: earnings have no payee
            Row("Loan", ("kind", "Deduction"), ("taxable", "Maybe"))))).Content.ReadFromJsonAsync<Check>())!;     // 12: not Yes or No

        Assert.Equal([(2, "kind"), (3, "rate"), (4, "defaultAmount"), (5, "code"), (7, "code"), (8, "name"), (9, "code"), (10, "payee"), (11, "payee"), (12, "taxable")],
            check.Problems.Select(p => (p.Line, p.Column!)));
        Assert.Equal("No payee is called Kenya Union. Add it as a payee first, then upload again.", check.Problems.Single(p => p.Line == 10).Message);
        Assert.Equal("PAYE is set by law and is already there.", check.Problems.Single(p => p.Line == 8).Message);
    }

    [Fact]
    public async Task A_clean_file_imports_for_the_chosen_companies_and_a_bad_one_imports_nothing()
    {
        var (client, a, _, stima) = await Onboarded();

        var bad = await client.PostAsJsonAsync("/api/pay-items/import", Body([a.Id], Row("Commuter"), Row("Pension", ("kind", "Deduction"), ("calcMethod", "Percentage of basic pay"))));
        Assert.Equal(("ONB-3518", HttpStatusCode.BadRequest), ((await bad.Content.ReadFromJsonAsync<Error>())!.Code, bad.StatusCode));
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<PayItem>>("/api/pay-items"))!, i => i.Name == "Commuter");   // all or nothing

        var res = await client.PostAsJsonAsync("/api/pay-items/import", Body([a.Id],
            Row("Commuter allowance", ("defaultAmount", "5,000"), ("taxable", "Yes")),
            Row("Lunch", ("taxable", "No")),
            Row("Pension", ("code", "PEN"), ("kind", "Deduction"), ("calcMethod", "Percentage of basic pay"), ("rate", "5"), ("beforeTax", "Yes")),
            Row("SACCO shares", ("kind", "Deduction"), ("defaultAmount", "1500"), ("payee", "stima sacco")),
            Row("Medical cover", ("kind", "Employer cost"), ("calcMethod", "Entered each payroll"))));
        Assert.Equal(5, (await res.Content.ReadFromJsonAsync<Result>())!.Imported);

        var items = (await client.GetFromJsonAsync<List<PayItem>>("/api/pay-items"))!.Where(i => !i.Locked).ToDictionary(i => i.Name);
        Assert.Equal(("earning", true, 5000m, false), (items["Commuter allowance"].Kind, items["Commuter allowance"].Taxable, items["Commuter allowance"].DefaultAmount, items["Commuter allowance"].AllCompanies));
        Assert.Equal([a.Id], items["Commuter allowance"].CompanyIds);
        Assert.False(items["Lunch"].Taxable);
        Assert.Equal(("PEN", true, 5m), (items["Pension"].Code, items["Pension"].BeforeTax, items["Pension"].Rate));
        Assert.Equal(stima, items["SACCO shares"].PayeeId);
        Assert.Equal(("employer_cost", "each_run"), (items["Medical cover"].Kind, items["Medical cover"].CalcMethod));
        Assert.Matches("^COM", items["Commuter allowance"].Code);   // made from the name
    }
}
