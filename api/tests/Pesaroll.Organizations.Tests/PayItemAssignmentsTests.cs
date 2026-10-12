using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-07: who gets an earning or deduction. At each company the item serves: everyone there
/// (people hired later too), or chosen people with their own amount or rate and dates. Items set
/// by law apply to everyone and can't be given out.
/// </summary>
public sealed class PayItemAssignmentsTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Item(Guid Id);
    private sealed record Summary(Guid CompanyId, bool Everyone, int People);
    private sealed record PayItem(Guid Id, string Code, bool Locked, List<Summary> Assignments);
    private sealed record Person(Guid EmployeeId, string Name, string StaffNo, decimal? Amount, decimal? Rate, DateOnly StartsOn, DateOnly? EndsOn,
        string? Reference = null, string? ShownReference = null, bool FromProfile = false);
    private sealed record Missing(Guid EmployeeId, string Name, string StaffNo);
    private sealed record Assignment(Guid CompanyId, bool Everyone, decimal? Amount, decimal? Rate, DateOnly? StartsOn, DateOnly? EndsOn, List<Person> People,
        List<Missing>? Missing = null);
    private sealed record Mine(Guid PayItemId, string Name, string Kind, string CalcMethod, decimal? Amount, decimal? Rate, DateOnly StartsOn, DateOnly? EndsOn, bool Everyone,
        string? Reference = null);

    private async Task<(HttpClient Client, Company A, Company B)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email = $"assign-{Guid.NewGuid():N}@example.com", password = "Mombasa-raha-7",
            roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        async Task<Company> Add(string name) =>
            (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" })).Content.ReadFromJsonAsync<Company>())!;
        return (client, await Add("Pesaroll Ltd"), await Add("Centrail Ltd"));
    }

    private static async Task<Guid> Make(HttpClient client, string path, object body)
    {
        var res = await client.PostAsJsonAsync(path, body);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<Item>())!.Id;
    }

    private static Task<Guid> Hire(HttpClient client, Company company, string first, string last, string? left = null) =>
        Make(client, "/api/employees", new { companyId = company.Id, firstName = first, lastName = last, hireDate = "2024-01-15", exitDate = left, job = new { employmentType = "permanent" } });

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    private static Task<HttpResponseMessage> Assign(HttpClient client, Guid item, object body) => client.PutAsJsonAsync($"/api/pay-items/{item}/assignments", body);

    [Fact]
    public async Task Everyone_at_a_company_includes_people_hired_later()
    {
        var (client, a, _) = await Onboarded();
        var house = await Make(client, "/api/pay-items", new { name = "House allowance", kind = "earning", taxable = true, calcMethod = "percent_of_basic", rate = 10m, allCompanies = true });
        var wanjiru = await Hire(client, a, "Wanjiru", "Kamau");

        var res = await Assign(client, house, new { companyId = a.Id, everyone = true, startsOn = "2026-11-01" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var later = await Hire(client, a, "Otieno", "Ouma");

        foreach (var person in new[] { wanjiru, later })
        {
            var mine = Assert.Single((await client.GetFromJsonAsync<List<Mine>>($"/api/employees/{person}/pay-items"))!);
            Assert.Equal(("House allowance", 10m, true, DateOnly.Parse("2026-11-01")), (mine.Name, mine.Rate, mine.Everyone, mine.StartsOn));   // the item's rate
        }
        var summary = (await client.GetFromJsonAsync<List<PayItem>>("/api/pay-items"))!.Single(i => i.Id == house).Assignments;
        Assert.Equal([new Summary(a.Id, true, 0)], summary);
    }

    [Fact]
    public async Task Chosen_people_get_their_own_amount_and_dates_and_a_change_replaces_the_list()
    {
        var (client, a, b) = await Onboarded();
        var sacco = await Make(client, "/api/pay-items", new { name = "SACCO shares", kind = "deduction", calcMethod = "fixed", defaultAmount = 1500m, allCompanies = true });
        var wanjiru = await Hire(client, a, "Wanjiru", "Kamau");
        var otieno = await Hire(client, a, "Otieno", "Ouma");
        var okello = await Hire(client, b, "Okello", "Odongo");

        var res = await Assign(client, sacco, new
        {
            companyId = a.Id, everyone = false, people = new object[]
            {
                new { employeeId = wanjiru, amount = 2000m, startsOn = "2026-11-01" },
                new { employeeId = otieno, startsOn = "2026-11-01", endsOn = "2027-03-31" },   // the item's default amount
            },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        await Assign(client, sacco, new { companyId = b.Id, everyone = true, amount = 1000m, startsOn = "2026-12-01" });

        var all = (await client.GetFromJsonAsync<List<Assignment>>($"/api/pay-items/{sacco}/assignments"))!;
        var atA = all.Single(x => x.CompanyId == a.Id);
        Assert.False(atA.Everyone);
        Assert.Equal([("Otieno Ouma", (decimal?)null, (DateOnly?)DateOnly.Parse("2027-03-31")), ("Wanjiru Kamau", 2000m, null)],
            atA.People.Select(p => (p.Name, p.Amount, p.EndsOn)));
        Assert.Equal((true, 1000m), (all.Single(x => x.CompanyId == b.Id).Everyone, all.Single(x => x.CompanyId == b.Id).Amount));
        Assert.Equal(1500m, Assert.Single((await client.GetFromJsonAsync<List<Mine>>($"/api/employees/{otieno}/pay-items"))!).Amount);   // the default
        Assert.Equal(1000m, Assert.Single((await client.GetFromJsonAsync<List<Mine>>($"/api/employees/{okello}/pay-items"))!).Amount);

        // The list is replaced: now only Otieno at A; then nobody at B.
        await Assign(client, sacco, new { companyId = a.Id, everyone = false, people = new object[] { new { employeeId = otieno, amount = 500m, startsOn = "2026-11-01" } } });
        await Assign(client, sacco, new { companyId = b.Id, everyone = false, people = Array.Empty<object>() });
        Assert.Empty((await client.GetFromJsonAsync<List<Mine>>($"/api/employees/{wanjiru}/pay-items"))!);
        Assert.Equal([new Summary(a.Id, false, 1)], (await client.GetFromJsonAsync<List<PayItem>>("/api/pay-items"))!.Single(i => i.Id == sacco).Assignments);
    }

    [Fact]
    public async Task What_cant_be_given_out_is_refused_with_the_reason()
    {
        var (client, a, b) = await Onboarded();
        var shif = (await client.GetFromJsonAsync<List<PayItem>>("/api/pay-items"))!.Single(i => i.Code == "SHIF").Id;
        var lunch = await Make(client, "/api/pay-items", new { name = "Lunch", kind = "earning", taxable = true, calcMethod = "fixed", defaultAmount = 3000m, allCompanies = false, companyIds = new[] { a.Id } });
        var overtime = await Make(client, "/api/pay-items", new { name = "Overtime", kind = "earning", taxable = true, calcMethod = "each_run", allCompanies = true });
        var wanjiru = await Hire(client, a, "Wanjiru", "Kamau");
        var okello = await Hire(client, b, "Okello", "Odongo");
        var left = await Hire(client, a, "Akinyi", "Odera", left: "2025-06-30");

        Assert.Equal("ONB-3545", (await ErrorFrom(await Assign(client, shif, new { companyId = a.Id, everyone = true }))).Code);
        Assert.Equal("ONB-3546", (await ErrorFrom(await Assign(client, lunch, new { companyId = b.Id, everyone = true }))).Code);
        var bad = await ErrorFrom(await Assign(client, overtime, new
        {
            companyId = a.Id, everyone = false, people = new object[]
            {
                new { employeeId = wanjiru, amount = 500m },                                     // entered each payroll: no amount
                new { employeeId = okello },                                                     // works at B
                new { employeeId = left },                                                       // has left
                new { employeeId = wanjiru },                                                    // twice
            },
        }));
        Assert.Equal("ONB-3547", bad.Code);
        Assert.Equal(new Dictionary<string, string>
        {
            ["people[0].amount"] = "Overtime is entered each payroll, so it has no amount here.",
            ["people[1].employeeId"] = "Okello Odongo works at Centrail Ltd.",
            ["people[2].employeeId"] = "Akinyi Odera has left Pesaroll Ltd.",
            ["people[3].employeeId"] = "Wanjiru Kamau is on the list twice.",
        }, bad.Fields);
        var dates = await ErrorFrom(await Assign(client, lunch, new { companyId = a.Id, everyone = true, startsOn = "2026-11-01", endsOn = "2026-10-01" }));
        Assert.Equal("The end can't be before the start.", dates.Fields!["endsOn"]);
    }

    [Fact]
    public async Task Editing_or_deleting_the_item_keeps_who_gets_it_consistent()
    {
        var (client, a, b) = await Onboarded();
        var welfare = await Make(client, "/api/pay-items", new { name = "Welfare", kind = "deduction", calcMethod = "fixed", defaultAmount = 200m, allCompanies = true });
        var wanjiru = await Hire(client, a, "Wanjiru", "Kamau");
        await Assign(client, welfare, new { companyId = a.Id, everyone = false, people = new object[] { new { employeeId = wanjiru, amount = 300m } } });
        await Assign(client, welfare, new { companyId = b.Id, everyone = true });

        // Only for A now, and a percentage: B's go, Wanjiru's amount gives way to the item's rate.
        var edit = await client.PatchAsJsonAsync($"/api/pay-items/{welfare}", new { name = "Welfare", kind = "deduction", calcMethod = "percent_of_basic", rate = 1m, allCompanies = false, companyIds = new[] { a.Id } });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var all = (await client.GetFromJsonAsync<List<Assignment>>($"/api/pay-items/{welfare}/assignments"))!;
        var only = Assert.Single(all);
        Assert.Equal((a.Id, (decimal?)null, (decimal?)null), (only.CompanyId, only.People.Single().Amount, only.People.Single().Rate));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pay-items/{welfare}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<Mine>>($"/api/employees/{wanjiru}/pay-items"))!);
    }

    [Fact]
    public async Task Account_numbers_can_come_from_peoples_profiles_masked_with_who_lacks_one()
    {
        var (client, a, _) = await Onboarded();
        // HELB identifies borrowers by ID number: set once on the payee, its items follow.
        var helbPayee = await Make(client, "/api/payees", new { name = "HELB", method = "mpesa_paybill", paybill = "200800", referenceSource = "national_id" });
        var helb = await Make(client, "/api/pay-items", new { name = "HELB loan", kind = "deduction", calcMethod = "fixed", defaultAmount = 2000m, allCompanies = true, payeeId = helbPayee });
        var wanjiru = await Make(client, "/api/employees", new { companyId = a.Id, firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", nationalId = "12345678",
            job = new { employmentType = "permanent" }, account = new { method = "bank", bankCode = "68", branchCode = "047", accountNumber = "0123456789012" } });
        var otieno = await Hire(client, a, "Otieno", "Ouma");   // no ID number yet

        // Everyone at A, from their ID numbers: no typing; who has none is listed.
        await Assign(client, helb, new { companyId = a.Id, everyone = true });
        var everyone = Assert.Single((await client.GetFromJsonAsync<List<Assignment>>($"/api/pay-items/{helb}/assignments"))!);
        Assert.Equal(["Otieno Ouma"], everyone.Missing!.Select(m => m.Name));
        Assert.Equal("•••• 5678", Assert.Single((await client.GetFromJsonAsync<List<Mine>>($"/api/employees/{wanjiru}/pay-items"))!).Reference);   // masked

        // A profile fix flows through: Otieno's ID is added, and he's no longer missing.
        var fix = await client.PatchAsJsonAsync($"/api/employees/{otieno}", new { companyId = a.Id, firstName = "Otieno", lastName = "Ouma", hireDate = "2024-01-15",
            nationalId = "87654321", job = new { employmentType = "permanent" } });
        Assert.Equal(HttpStatusCode.OK, fix.StatusCode);
        Assert.Empty(Assert.Single((await client.GetFromJsonAsync<List<Assignment>>($"/api/pay-items/{helb}/assignments"))!).Missing!);

        // From the bank account they're paid into, for chosen people; a typed number wins.
        var bank = await Make(client, "/api/payees", new { name = "Equity loans", method = "bank", institution = "bank", bankCode = "68", branchCode = "047", accountNumber = "0123456789012", referenceSource = "bank_account" });
        var loan = await Make(client, "/api/pay-items", new { name = "Bank loan", kind = "deduction", calcMethod = "fixed", defaultAmount = 5000m, allCompanies = true, payeeId = bank });
        await Assign(client, loan, new
        {
            companyId = a.Id, everyone = false, people = new object[] { new { employeeId = wanjiru }, new { employeeId = otieno, reference = "LN-77" } },
        });
        var chosen = Assert.Single((await client.GetFromJsonAsync<List<Assignment>>($"/api/pay-items/{loan}/assignments"))!);
        Assert.Equal([("Otieno Ouma", "LN-77", false), ("Wanjiru Kamau", "•••• 9012", true)], chosen.People.Select(p => (p.Name, p.ShownReference!, p.FromProfile)));
        Assert.Empty(chosen.Missing!);   // Otieno's is typed

        var odd = await ErrorFrom(await client.PostAsJsonAsync("/api/payees", new { name = "Odd", method = "cash", referenceSource = "shoe_size" }));
        Assert.Equal("Choose how they identify each person.", odd.Fields!["referenceSource"]);

        // An item with no payee: numbers are typed.
        var noPayee = await Make(client, "/api/pay-items", new { name = "Welfare", kind = "deduction", calcMethod = "fixed", defaultAmount = 100m, allCompanies = true });
        await Assign(client, noPayee, new { companyId = a.Id, everyone = true });
        Assert.Empty(Assert.Single((await client.GetFromJsonAsync<List<Assignment>>($"/api/pay-items/{noPayee}/assignments"))!).Missing!);
    }
}
