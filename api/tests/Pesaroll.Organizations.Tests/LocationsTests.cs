using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-04, the Locations step: where people work. A location serves chosen companies or all of
/// them; each company has one main location, created as "Head office" with the company.
/// </summary>
public sealed class LocationsTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Text(string En, string Sw);
    private sealed record WageArea(string Code, Text Name, Text Description, List<string> Towns);
    private sealed record Country(string Code, List<WageArea> WageAreas);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Location(Guid Id, string Code, string Name, string Kind, string CountryCode, string? RegionCode, string? Town,
        string? AddressLine, string? WageAreaCode, bool AllCompanies, List<Guid> CompanyIds, List<Guid> MainFor);

    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email, Guid OrganizationId)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"loc-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse(Ceo) });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        return (client, email, created.Organization.Id);
    }

    private static async Task<Company> AddCompany(HttpClient client, string name, string? town = "Nairobi", string? regionCode = "047") =>
        (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE", town, regionCode, addressLine = "Moi Avenue" }))
            .Content.ReadFromJsonAsync<Company>())!;

    private static async Task<List<Location>> Locations(HttpClient client, Guid? companyId = null) =>
        (await client.GetFromJsonAsync<List<Location>>(companyId is null ? "/api/locations" : $"/api/locations?companyId={companyId}"))!;

    private static object Shop(Guid[] companyIds, Guid[] mainFor, string? code = null) => new
    {
        code, name = "Mombasa shop", regionCode = "001", town = "Mombasa", addressLine = "Digo Road", wageAreaCode = "cities",
        allCompanies = false, companyIds, mainFor,
    };

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task Kenya_comes_with_its_three_minimum_wage_areas()
    {
        var (client, _, _) = await Onboarded();

        var kenya = Assert.Single((await client.GetFromJsonAsync<List<Country>>("/api/countries"))!);

        Assert.Equal(["cities", "municipalities", "other"], kenya.WageAreas.Select(a => a.Code));
        Assert.Contains("Eldoret", kenya.WageAreas[0].Towns);
    }

    [Fact]
    public async Task Adding_a_company_creates_its_head_office_as_its_main_location()
    {
        var (client, _, _) = await Onboarded();

        var company = await AddCompany(client, "Mama Oliech Restaurants Ltd");

        var hq = Assert.Single(await Locations(client));
        Assert.Equal(("Head office", "head_office", "KE", "047", "Nairobi", "Moi Avenue", "cities"),
            (hq.Name, hq.Kind, hq.CountryCode, hq.RegionCode, hq.Town, hq.AddressLine, hq.WageAreaCode));
        Assert.Matches("^[A-Z0-9][A-Z0-9-]{1,19}$", hq.Code);
        Assert.Equal([company.Id], hq.CompanyIds);
        Assert.Equal([company.Id], hq.MainFor);
    }

    [Fact]
    public async Task A_location_can_serve_chosen_companies_and_be_main_for_one_of_them()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");

        var res = await client.PostAsJsonAsync("/api/locations", Shop([a.Id, b.Id], [b.Id]));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var shop = (await res.Content.ReadFromJsonAsync<Location>())!;
        Assert.Equal(("MOM-01", "cities", "branch"), (shop.Code, shop.WageAreaCode, shop.Kind));   // a branch unless said otherwise
        Assert.Equal([a.Id, b.Id], shop.CompanyIds.Order());
        Assert.Equal([b.Id], shop.MainFor);

        var all = await Locations(client);
        Assert.Empty(all.Single(l => l.Name == "Head office" && l.CompanyIds.Contains(b.Id)).MainFor);   // B's main moved
        Assert.Equal([a.Id], all.Single(l => l.Name == "Head office" && l.CompanyIds.Contains(a.Id)).MainFor);

        // The company filter shows what one company uses.
        var forA = await Locations(client, a.Id);
        Assert.Equal(2, forA.Count);
        Assert.DoesNotContain(forA, l => l.CompanyIds.SequenceEqual([b.Id]));
    }

    [Fact]
    public async Task An_all_companies_location_also_serves_companies_added_later()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");
        var store = (await (await client.PostAsJsonAsync("/api/locations", new
        {
            name = "Central store", kind = "warehouse", regionCode = "022", town = "Ruiru", wageAreaCode = "municipalities", allCompanies = true,
            companyIds = Array.Empty<Guid>(), mainFor = Array.Empty<Guid>(),
        })).Content.ReadFromJsonAsync<Location>())!;
        Assert.True(store.AllCompanies);
        Assert.Equal("warehouse", store.Kind);

        var later = await AddCompany(client, "Oliech Bakery Ltd");

        Assert.Contains(await Locations(client, later.Id), l => l.Id == store.Id);
    }

    [Fact]
    public async Task Details_are_checked_and_problems_named_by_field()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");

        var res = await client.PostAsJsonAsync("/api/locations", new
        {
            name = "A", regionCode = "999", wageAreaCode = "moon", allCompanies = false, companyIds = Array.Empty<Guid>(), mainFor = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await ErrorFrom(res);
        Assert.Equal("ONB-3512", error.Code);
        Assert.Equal(["companyIds", "name", "regionCode", "wageAreaCode"], error.Fields!.Keys.Order());   // an unknown area is still refused

        var kind = await ErrorFrom(await client.PostAsJsonAsync("/api/locations", new
        {
            name = "Castle", kind = "castle", regionCode = "047", allCompanies = false, companyIds = new[] { a.Id }, mainFor = Array.Empty<Guid>(),
        }));
        Assert.Equal(["kind"], kind.Fields!.Keys);
        var code = await ErrorFrom(await client.PostAsJsonAsync("/api/locations", Shop([a.Id], [], code: "bad code!")));
        Assert.Equal(["code"], code.Fields!.Keys);
        var main = await ErrorFrom(await client.PostAsJsonAsync("/api/locations", Shop([a.Id], [b.Id])));
        Assert.Equal(["mainFor"], main.Fields!.Keys);
        var stranger = await ErrorFrom(await client.PostAsJsonAsync("/api/locations", Shop([Guid.NewGuid()], [])));
        Assert.Equal(["companyIds"], stranger.Fields!.Keys);
    }

    [Fact]
    public async Task The_minimum_wage_area_comes_from_the_town_when_not_given()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");

        var city = (await (await client.PostAsJsonAsync("/api/locations", new
        {
            name = "Kisumu shop", regionCode = "042", town = " kisumu ", allCompanies = false, companyIds = new[] { a.Id }, mainFor = Array.Empty<Guid>(),
        })).Content.ReadFromJsonAsync<Location>())!;
        var rural = (await (await client.PostAsJsonAsync("/api/locations", new
        {
            name = "Farm store", regionCode = "032", town = "Njoro", allCompanies = false, companyIds = new[] { a.Id }, mainFor = Array.Empty<Guid>(),
        })).Content.ReadFromJsonAsync<Location>())!;

        Assert.Equal("cities", city.WageAreaCode);
        Assert.Null(rural.WageAreaCode);
    }

    [Fact]
    public async Task Codes_are_upper_cased_and_unique_in_the_organization()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");

        var first = (await (await client.PostAsJsonAsync("/api/locations", Shop([a.Id], [], code: " msa-01 "))).Content.ReadFromJsonAsync<Location>())!;
        var again = await client.PostAsJsonAsync("/api/locations", Shop([a.Id], [], code: "MSA-01"));

        Assert.Equal("MSA-01", first.Code);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("ONB-3513", (await ErrorFrom(again)).Code);
    }

    [Fact]
    public async Task A_main_location_stays_until_another_is_made_main()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var hq = Assert.Single(await Locations(client));

        var delete = await client.DeleteAsync($"/api/locations/{hq.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var error = await ErrorFrom(delete);
        Assert.Equal("ONB-3515", error.Code);
        Assert.Contains("Oliech Restaurants Ltd", error.Message);
        var unmark = await client.PatchAsJsonAsync($"/api/locations/{hq.Id}", new
        {
            name = "Head office", regionCode = "047", town = "Nairobi", wageAreaCode = "cities", allCompanies = false,
            companyIds = new[] { a.Id }, mainFor = Array.Empty<Guid>(),
        });
        Assert.Equal("ONB-3515", (await ErrorFrom(unmark)).Code);

        await client.PostAsJsonAsync("/api/locations", Shop([a.Id], [a.Id]));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/locations/{hq.Id}")).StatusCode);
        Assert.Equal("Mombasa shop", Assert.Single(await Locations(client)).Name);
    }

    [Fact]
    public async Task Editing_changes_the_details_and_which_companies_use_it()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        var shop = (await (await client.PostAsJsonAsync("/api/locations", Shop([a.Id], []))).Content.ReadFromJsonAsync<Location>())!;

        var res = await client.PatchAsJsonAsync($"/api/locations/{shop.Id}", new
        {
            code = shop.Code, name = "Nyali shop", regionCode = "001", town = "Mombasa", wageAreaCode = "cities", allCompanies = false,
            companyIds = new[] { b.Id }, mainFor = new[] { b.Id },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var edited = (await res.Content.ReadFromJsonAsync<Location>())!;
        Assert.Equal(("Nyali shop", shop.Code), (edited.Name, edited.Code));
        Assert.Equal([b.Id], edited.CompanyIds);
        Assert.Equal([b.Id], edited.MainFor);
    }

    [Fact]
    public async Task Deleting_a_company_removes_the_locations_only_it_used()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        var shared = (await (await client.PostAsJsonAsync("/api/locations", Shop([a.Id, b.Id], []))).Content.ReadFromJsonAsync<Location>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/companies/{a.Id}")).StatusCode);

        var left = await Locations(client);
        Assert.Equal(2, left.Count);   // B's head office and the shared shop
        Assert.Equal([b.Id], left.Single(l => l.Id == shared.Id).CompanyIds);
    }

    [Fact]
    public async Task Another_organization_and_plain_members_get_nothing()
    {
        var (a, _, orgId) = await Onboarded();
        await AddCompany(a, "Oliech Restaurants Ltd");
        var hq = Assert.Single(await Locations(a));
        var (b, bEmail, _) = await Onboarded();

        Assert.DoesNotContain(await Locations(b), l => l.Id == hq.Id);
        Assert.Equal("ONB-3514", (await ErrorFrom(await b.DeleteAsync($"/api/locations/{hq.Id}"))).Code);

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

        var res = await b.PostAsJsonAsync("/api/locations", Shop(hq.CompanyIds.ToArray(), []));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("ACC-8001", (await ErrorFrom(res)).Code);
    }
}
