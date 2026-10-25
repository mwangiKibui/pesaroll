using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-07, the Earnings and deductions step. Basic pay and the law's items are made with the first
/// company and locked; the organization adds its own for all or chosen companies.
/// </summary>
public sealed class PayItemsTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record PayItem(Guid Id, string Code, string Name, string Kind, bool Taxable, bool BeforeTax, string CalcMethod,
        decimal? DefaultAmount, decimal? Rate, bool Locked, string? LockedKey, bool AllCompanies, List<Guid> CompanyIds);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email, Guid OrganizationId)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"items-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        return (client, email, created.Organization.Id);
    }

    private static async Task<Company> AddCompany(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE" })).Content.ReadFromJsonAsync<Company>())!;

    private static async Task<List<PayItem>> Items(HttpClient client, Guid? companyId = null) =>
        (await client.GetFromJsonAsync<List<PayItem>>(companyId is null ? "/api/pay-items" : $"/api/pay-items?companyId={companyId}"))!;

    private static object House(Guid[] companyIds, string? code = null) => new
    {
        code, name = "House allowance", kind = "earning", taxable = true, calcMethod = "fixed", defaultAmount = 15000m, allCompanies = false, companyIds,
    };

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task The_first_company_brings_basic_pay_and_the_laws_items_locked()
    {
        var (client, _, _) = await Onboarded();

        await AddCompany(client, "Mama Oliech Restaurants Ltd");
        await AddCompany(client, "Mama Oliech Catering Ltd");   // nothing twice

        var all = await Items(client);
        Assert.Equal(["BASIC", "PAYE", "SHIF", "NSSF", "AHL", "NSSF-ER", "AHL-ER", "NITA", "OVERTIME", "REST-DAY"], all.Select(i => i.Code));
        // PAY-11: with them, the usual hourly rates: the organization's own to change, not locked.
        Assert.All(all.Skip(8), i => Assert.True(!i.Locked && i.AllCompanies && i.CalcMethod == "per_unit"));
        var items = all.Take(8).ToList();
        Assert.All(items, i => Assert.True(i.Locked && i.AllCompanies));
        var basic = items[0];
        Assert.Equal(("Basic pay", "earning", true, "fixed"), (basic.Name, basic.Kind, basic.Taxable, basic.CalcMethod));
        Assert.Equal(["shif", "nssf_employee", "ahl_employee"], items.Where(i => i.BeforeTax).Select(i => i.LockedKey));
        Assert.Equal(["nssf_employer", "ahl_employer", "nita"], items.Where(i => i.Kind == "employer_cost").Select(i => i.LockedKey));
        Assert.All(items.Skip(1), i => Assert.Equal("by_law", i.CalcMethod));
    }

    [Fact]
    public async Task An_item_can_serve_chosen_companies_with_a_code_made_from_its_name()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");

        var res = await client.PostAsJsonAsync("/api/pay-items", House([a.Id]));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var house = (await res.Content.ReadFromJsonAsync<PayItem>())!;
        Assert.Equal(("HOU-01", "earning", true, false, 15000m, false), (house.Code, house.Kind, house.Taxable, house.BeforeTax, house.DefaultAmount, house.Locked));
        Assert.Equal([a.Id], house.CompanyIds);
        Assert.Contains(await Items(client, a.Id), i => i.Id == house.Id);
        Assert.DoesNotContain(await Items(client, b.Id), i => i.Id == house.Id);
    }

    [Fact]
    public async Task Each_kind_and_way_of_working_out_keeps_only_its_own_fields()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");
        async Task<PayItem> Add(object body) => (await (await client.PostAsJsonAsync("/api/pay-items", body)).Content.ReadFromJsonAsync<PayItem>())!;

        var pension = await Add(new { name = "Pension", kind = "deduction", taxable = true, beforeTax = true, calcMethod = "percent_of_basic", rate = 5m, defaultAmount = 100m, allCompanies = true });
        var overtime = await Add(new { name = "Overtime", kind = "earning", taxable = true, beforeTax = true, calcMethod = "each_run", defaultAmount = 500m, allCompanies = true });
        var medical = await Add(new { name = "Medical cover", kind = "employer_cost", taxable = true, calcMethod = "fixed", defaultAmount = 3000m, allCompanies = true });

        Assert.Equal((false, true, 5m, (decimal?)null), (pension.Taxable, pension.BeforeTax, pension.Rate!.Value, pension.DefaultAmount));
        Assert.Equal((true, false, (decimal?)null), (overtime.Taxable, overtime.BeforeTax, overtime.DefaultAmount));   // entered each payroll: no default
        Assert.Equal((false, false, 3000m), (medical.Taxable, medical.BeforeTax, medical.DefaultAmount!.Value));
    }

    [Fact]
    public async Task Details_are_checked_and_problems_named_by_field()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");

        var res = await client.PostAsJsonAsync("/api/pay-items", new
        {
            name = "X", kind = "perk", calcMethod = "by_law", code = "bad code", defaultAmount = -1m, allCompanies = false, companyIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await ErrorFrom(res);
        Assert.Equal("ONB-3528", error.Code);
        Assert.Equal(["calcMethod", "code", "companyIds", "kind", "name"], error.Fields!.Keys.Order());

        var noRate = await client.PostAsJsonAsync("/api/pay-items", new { name = "Pension", kind = "deduction", calcMethod = "percent_of_basic", allCompanies = true });
        Assert.Equal("rate", Assert.Single((await ErrorFrom(noRate)).Fields!.Keys));
        var negative = await client.PostAsJsonAsync("/api/pay-items", new { name = "Lunch", kind = "earning", calcMethod = "fixed", defaultAmount = -1m, allCompanies = true });
        Assert.Equal("defaultAmount", Assert.Single((await ErrorFrom(negative)).Fields!.Keys));

        await client.PostAsJsonAsync("/api/pay-items", new { name = "House", kind = "earning", calcMethod = "fixed", code = "house", allCompanies = true });
        var taken = await client.PostAsJsonAsync("/api/pay-items", new { name = "Housing", kind = "earning", calcMethod = "fixed", code = "HOUSE", allCompanies = true });
        Assert.Equal((HttpStatusCode.Conflict, "ONB-3529"), (taken.StatusCode, (await ErrorFrom(taken)).Code));
    }

    [Fact]
    public async Task Locked_items_cant_be_changed_or_deleted_but_own_ones_can()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var items = await Items(client);
        var shif = items.Single(i => i.LockedKey == "shif");

        var edit = await client.PatchAsJsonAsync($"/api/pay-items/{shif.Id}", new { name = "Health", kind = "deduction", calcMethod = "fixed", allCompanies = true });
        Assert.Equal((HttpStatusCode.Conflict, "ONB-3531"), (edit.StatusCode, (await ErrorFrom(edit)).Code));
        var delete = await client.DeleteAsync($"/api/pay-items/{items[0].Id}");
        Assert.Equal((HttpStatusCode.Conflict, "ONB-3531"), (delete.StatusCode, (await ErrorFrom(delete)).Code));

        var house = (await (await client.PostAsJsonAsync("/api/pay-items", House([a.Id]))).Content.ReadFromJsonAsync<PayItem>())!;
        var renamed = await client.PatchAsJsonAsync($"/api/pay-items/{house.Id}", new
        {
            code = house.Code, name = "Housing allowance", kind = "earning", taxable = true, calcMethod = "fixed", defaultAmount = 20000m, allCompanies = true,
        });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var saved = (await renamed.Content.ReadFromJsonAsync<PayItem>())!;
        Assert.Equal(("Housing allowance", (decimal?)20000m, true), (saved.Name, saved.DefaultAmount, saved.AllCompanies));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pay-items/{house.Id}")).StatusCode);
        Assert.Equal(10, (await Items(client)).Count);   // the locked items and the usual hourly rates
    }

    [Fact]
    public async Task Deleting_a_company_removes_the_items_only_it_used()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        await client.PostAsJsonAsync("/api/pay-items", House([a.Id]));
        var shared = (await (await client.PostAsJsonAsync("/api/pay-items", new
        {
            name = "Airtime", kind = "earning", taxable = true, calcMethod = "fixed", allCompanies = false, companyIds = new[] { a.Id, b.Id },
        })).Content.ReadFromJsonAsync<PayItem>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/companies/{a.Id}")).StatusCode);

        var left = await Items(client);
        Assert.DoesNotContain(left, i => i.Name == "House allowance");
        Assert.Equal([b.Id], left.Single(i => i.Id == shared.Id).CompanyIds);
    }

    [Fact]
    public async Task Another_organization_and_plain_members_get_nothing()
    {
        var (a, _, orgId) = await Onboarded();
        var company = await AddCompany(a, "Oliech Restaurants Ltd");
        var basic = (await Items(a))[0];
        var (b, bEmail, _) = await Onboarded();

        Assert.DoesNotContain(await Items(b), i => i.Id == basic.Id);
        Assert.Equal("ONB-3530", (await ErrorFrom(await b.DeleteAsync($"/api/pay-items/{basic.Id}"))).Code);

        await using (var connection = new NpgsqlConnection(Db))
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

        var res = await b.PostAsJsonAsync("/api/pay-items", House([company.Id]));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("ACC-8001", (await ErrorFrom(res)).Code);
    }
}
