using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Organizations.Tests;

/// <summary>ONB-03, the Companies step: countries as data, companies, statutory numbers and payroll readiness.</summary>
public sealed class CompaniesTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Text(string En, string Sw);
    private sealed record Field(string Key, Text Label, Text? Hint, string? Example, string? Pattern, bool RequiredForPayroll);
    private sealed record Region(string Code, string Name);
    private sealed record Country(string Code, Text Name, string Currency, Text RegionLabel, List<Field> Fields, List<Region> Regions);
    private sealed record Industry(string Code, Text Name);
    private sealed record Missing(string Kind, string Key, Text Label);
    private sealed record Readiness(bool Ready, List<Missing> Missing);
    private sealed record Company(Guid Id, string LegalName, string? TradingName, string CountryCode, string Currency, string? IndustryCode,
        string? AddressLine, string? Town, string? RegionCode, string? PostalAddress, string? PostalCode, string? Phone, string? Email,
        Dictionary<string, string> Identifiers, Readiness Readiness);

    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Password = "Mombasa-raha-7";

    /// <summary>A fresh person with a fresh organization; the client sends their token.</summary>
    private async Task<(HttpClient Client, string Email, Guid OrganizationId)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"co-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse(Ceo) });
        var token = (await signup.Content.ReadFromJsonAsync<Token>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        return (client, email, created.Organization.Id);
    }

    private static object Kenyan(string name = "Mama Oliech Restaurants Ltd", Dictionary<string, string?>? identifiers = null) => new
    {
        legalName = name,
        tradingName = "Mama Oliech",
        countryCode = "KE",
        industryCode = "restaurants_bars",
        addressLine = "Westlands Road",
        town = "Nairobi",
        regionCode = "047",
        identifiers = identifiers ?? new Dictionary<string, string?> { ["kra_pin"] = " p051234567x ", ["nssf_employer_no"] = "NS12345", ["sha_employer_no"] = "SHA9876" },
    };

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task Only_Kenya_is_offered_with_its_fields_counties_and_format_checks()
    {
        var (client, _, _) = await Onboarded();

        var countries = (await client.GetFromJsonAsync<List<Country>>("/api/countries"))!;

        var kenya = Assert.Single(countries);
        Assert.Equal(("KE", "KES", "County"), (kenya.Code, kenya.Currency, kenya.RegionLabel.En));
        Assert.Equal(47, kenya.Regions.Count);
        var pin = Assert.Single(kenya.Fields, f => f.Key == "kra_pin");
        Assert.True(pin.RequiredForPayroll);
        Assert.Equal("P051234567X", pin.Example);
        Assert.Equal(["kra_pin", "nssf_employer_no", "sha_employer_no"], kenya.Fields.Where(f => f.RequiredForPayroll).Select(f => f.Key));
        var industries = (await client.GetFromJsonAsync<List<Industry>>("/api/industries"))!;
        Assert.Contains(industries, i => i.Code == "restaurants_bars");
    }

    [Fact]
    public async Task Adding_a_company_saves_its_details_and_cleaned_numbers_and_says_it_is_ready()
    {
        var (client, _, _) = await Onboarded();

        var res = await client.PostAsJsonAsync("/api/companies", Kenyan());

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var company = (await res.Content.ReadFromJsonAsync<Company>())!;
        Assert.Equal(("Mama Oliech Restaurants Ltd", "KES", "047"), (company.LegalName, company.Currency, company.RegionCode));
        Assert.Equal("P051234567X", company.Identifiers["kra_pin"]);
        Assert.True(company.Readiness.Ready);
        var list = (await client.GetFromJsonAsync<List<Company>>("/api/companies"))!;
        Assert.Equal(company.Id, Assert.Single(list).Id);
    }

    [Fact]
    public async Task Missing_statutory_numbers_are_allowed_but_listed_as_blocking_payroll()
    {
        var (client, _, _) = await Onboarded();

        var res = await client.PostAsJsonAsync("/api/companies", Kenyan(identifiers: new() { ["kra_pin"] = "P051234567X" }));

        var company = (await res.Content.ReadFromJsonAsync<Company>())!;
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.False(company.Readiness.Ready);
        Assert.Equal(["nssf_employer_no", "sha_employer_no"], company.Readiness.Missing.Select(m => m.Key));
        Assert.Equal("NSSF employer number", company.Readiness.Missing[0].Label.En);
    }

    [Fact]
    public async Task Numbers_in_the_wrong_format_are_refused_field_by_field_and_nothing_is_saved()
    {
        var (client, _, _) = await Onboarded();

        var res = await client.PostAsJsonAsync("/api/companies", new
        {
            legalName = "Bad Numbers Ltd", countryCode = "KE", regionCode = "999", email = "not-an-email",
            identifiers = new Dictionary<string, string?> { ["kra_pin"] = "P05123", ["made_up"] = "1" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await ErrorFrom(res);
        Assert.Equal("ONB-3507", error.Code);
        Assert.Equal(["email", "identifiers.kra_pin", "identifiers.made_up", "regionCode"], error.Fields!.Keys.Order());
        Assert.Contains("P051234567X", error.Fields["identifiers.kra_pin"]);
        Assert.Empty((await client.GetFromJsonAsync<List<Company>>("/api/companies"))!);
    }

    [Fact]
    public async Task Only_countries_we_support_and_a_proper_legal_name_are_accepted()
    {
        var (client, _, _) = await Onboarded();

        var uganda = await client.PostAsJsonAsync("/api/companies", new { legalName = "Kampala Ltd", countryCode = "UG" });
        var noName = await client.PostAsJsonAsync("/api/companies", new { legalName = " A ", countryCode = "KE" });

        Assert.Equal("ONB-3506", (await ErrorFrom(uganda)).Code);
        Assert.Equal("ONB-3505", (await ErrorFrom(noName)).Code);
    }

    [Fact]
    public async Task Two_companies_in_one_organization_cannot_share_a_name()
    {
        var (client, _, _) = await Onboarded();
        await client.PostAsJsonAsync("/api/companies", Kenyan("Kampuni Ltd"));

        var again = await client.PostAsJsonAsync("/api/companies", Kenyan(" kampuni ltd "));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("ONB-3508", (await ErrorFrom(again)).Code);
    }

    [Fact]
    public async Task Editing_changes_details_and_clearing_a_number_puts_it_back_on_the_missing_list()
    {
        var (client, _, _) = await Onboarded();
        var company = (await (await client.PostAsJsonAsync("/api/companies", Kenyan())).Content.ReadFromJsonAsync<Company>())!;

        var res = await client.PatchAsJsonAsync($"/api/companies/{company.Id}", new
        {
            legalName = "Mama Oliech Restaurants Limited", countryCode = "KE", tradingName = (string?)null, regionCode = "001",
            identifiers = new Dictionary<string, string?> { ["kra_pin"] = "P051234567X", ["nssf_employer_no"] = "NS12345", ["sha_employer_no"] = "" },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var updated = (await res.Content.ReadFromJsonAsync<Company>())!;
        Assert.Equal(("Mama Oliech Restaurants Limited", (string?)null, "001"), (updated.LegalName, updated.TradingName, updated.RegionCode));
        Assert.False(updated.Identifiers.ContainsKey("sha_employer_no"));
        Assert.Equal("sha_employer_no", Assert.Single(updated.Readiness.Missing).Key);
    }

    [Fact]
    public async Task Deleting_removes_the_company_and_its_numbers()
    {
        var (client, _, _) = await Onboarded();
        var company = (await (await client.PostAsJsonAsync("/api/companies", Kenyan())).Content.ReadFromJsonAsync<Company>())!;

        var res = await client.DeleteAsync($"/api/companies/{company.Id}");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<Company>>("/api/companies"))!);
    }

    [Fact]
    public async Task Companies_stay_inside_their_organization()
    {
        var (a, _, _) = await Onboarded();
        var (b, _, _) = await Onboarded();
        var company = (await (await a.PostAsJsonAsync("/api/companies", Kenyan())).Content.ReadFromJsonAsync<Company>())!;

        var list = (await b.GetFromJsonAsync<List<Company>>("/api/companies"))!;
        var edit = await b.PatchAsJsonAsync($"/api/companies/{company.Id}", Kenyan("Taken Ltd"));
        var delete = await b.DeleteAsync($"/api/companies/{company.Id}");

        Assert.Empty(list);
        Assert.Equal("ONB-3509", (await ErrorFrom(edit)).Code);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Single((await a.GetFromJsonAsync<List<Company>>("/api/companies"))!);
    }

    [Fact]
    public async Task Only_the_person_who_onboarded_the_organization_changes_companies_for_now()
    {
        var (a, _, orgId) = await Onboarded();
        var (b, bEmail, _) = await Onboarded();
        await using (var connection = new Npgsql.NpgsqlConnection(System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")))
        {
            await connection.OpenAsync();
            // B joins A's organization as a plain member and makes it their current one.
            await using var command = new Npgsql.NpgsqlCommand("""
                insert into memberships (id, organization_id, user_id) select uuidv7(), @org, id from users where email = @email;
                update memberships set status = 'removed' where user_id = (select id from users where email = @email) and organization_id <> @org;
                """, connection);
            command.Parameters.AddWithValue("org", orgId);
            command.Parameters.AddWithValue("email", bEmail);
            await command.ExecuteNonQueryAsync();
        }
        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = bEmail, password = Password });
        b.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);

        var res = await b.PostAsJsonAsync("/api/companies", Kenyan("Member Ltd"));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("ACC-8001", (await ErrorFrom(res)).Code);
    }

    [Fact]
    public async Task Signed_out_people_and_people_without_an_organization_get_nothing()
    {
        var signedOut = await api.CreateClient().GetAsync("/api/companies");
        Assert.Equal("AUTH-1007", (await ErrorFrom(signedOut)).Code);

        var client = api.CreateClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Bea", lastName = "Njeri", email = $"co-{Guid.NewGuid():N}@example.com", password = Password, roleId = Guid.Parse(Ceo) });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var noOrg = await client.GetAsync("/api/companies");
        Assert.Equal("ONB-3503", (await ErrorFrom(noOrg)).Code);
    }
}
