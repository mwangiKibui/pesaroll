using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-05, the Org structure step: divisions, departments (in a division, under a parent), positions
/// and units, each for all or chosen companies.
/// </summary>
public sealed class StructureTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Division(Guid Id, string Code, string Name, bool AllCompanies, List<Guid> CompanyIds);
    private sealed record Department(Guid Id, string Code, string Name, Guid? DivisionId, Guid? ParentId, bool AllCompanies, List<Guid> CompanyIds);
    private sealed record Position(Guid Id, string Code, string Title, Guid? DepartmentId, bool AllCompanies, List<Guid> CompanyIds);
    private sealed record Unit(Guid Id, string Code, string Name, Guid DepartmentId, bool AllCompanies, List<Guid> CompanyIds);

    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Password = "Mombasa-raha-7";

    private async Task<(HttpClient Client, string Email, Guid OrganizationId)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"str-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse(Ceo) });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        return (client, email, created.Organization.Id);
    }

    private static async Task<Company> AddCompany(HttpClient client, string name, string industry = "restaurants_bars") =>
        (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE", industryCode = industry }))
            .Content.ReadFromJsonAsync<Company>())!;

    private static async Task<T> Post<T>(HttpClient client, string path, object body)
    {
        var res = await client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task Departments_sit_in_divisions_and_under_other_departments_without_loops()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var division = await Post<Division>(client, "/api/structure/divisions", new { name = "Restaurants", allCompanies = true, companyIds = Array.Empty<Guid>() });
        var ops = await Post<Department>(client, "/api/structure/departments",
            new { name = "Operations", divisionId = division.Id, allCompanies = false, companyIds = new[] { a.Id } });
        var kitchen = await Post<Department>(client, "/api/structure/departments",
            new { code = "kit", name = "Kitchen", parentId = ops.Id, allCompanies = false, companyIds = new[] { a.Id } });

        Assert.Equal(("RES-01", "OPE-01", "KIT"), (division.Code, ops.Code, kitchen.Code));
        Assert.Equal((division.Id, (Guid?)null), (ops.DivisionId, ops.ParentId));
        Assert.Equal(ops.Id, kitchen.ParentId);

        var loop = await client.PatchAsJsonAsync($"/api/structure/departments/{ops.Id}",
            new { code = ops.Code, name = "Operations", parentId = kitchen.Id, allCompanies = false, companyIds = new[] { a.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, loop.StatusCode);
        var error = await ErrorFrom(loop);
        Assert.Equal("ONB-3519", error.Code);
        Assert.Equal(["parentId"], error.Fields!.Keys);
    }

    [Fact]
    public async Task Positions_sit_in_a_department_or_are_shared_and_units_need_a_department()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var kitchen = await Post<Department>(client, "/api/structure/departments", new { name = "Kitchen", allCompanies = true, companyIds = Array.Empty<Guid>() });

        var cook = await Post<Position>(client, "/api/structure/positions",
            new { title = "Cook", departmentId = kitchen.Id, allCompanies = true, companyIds = Array.Empty<Guid>() });
        var clerk = await Post<Position>(client, "/api/structure/positions", new { title = "Clerk", allCompanies = false, companyIds = new[] { a.Id } });
        var shift = await Post<Unit>(client, "/api/structure/units", new { name = "Morning shift", departmentId = kitchen.Id, allCompanies = true, companyIds = Array.Empty<Guid>() });

        Assert.Equal((kitchen.Id, "COO-01"), (cook.DepartmentId!.Value, cook.Code));
        Assert.Null(clerk.DepartmentId);
        Assert.Equal(kitchen.Id, shift.DepartmentId);
        var noDepartment = await ErrorFrom(await client.PostAsJsonAsync("/api/structure/units", new { name = "Night shift", allCompanies = true, companyIds = Array.Empty<Guid>() }));
        Assert.Equal(["departmentId"], noDepartment.Fields!.Keys);
    }

    [Fact]
    public async Task Details_are_checked_and_codes_are_unique_within_each_level()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");

        var bad = await ErrorFrom(await client.PostAsJsonAsync("/api/structure/departments", new
        {
            code = "bad code!", name = "A", divisionId = Guid.NewGuid(), parentId = Guid.NewGuid(), allCompanies = false, companyIds = Array.Empty<Guid>(),
        }));
        Assert.Equal("ONB-3519", bad.Code);
        Assert.Equal(["code", "companyIds", "divisionId", "name", "parentId"], bad.Fields!.Keys.Order());

        await Post<Department>(client, "/api/structure/departments", new { code = "ADM", name = "Admin", allCompanies = true, companyIds = Array.Empty<Guid>() });
        var taken = await client.PostAsJsonAsync("/api/structure/departments", new { code = "adm", name = "Admin two", allCompanies = true, companyIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("ONB-3520", (await ErrorFrom(taken)).Code);
        // Another level may use the same code.
        await Post<Position>(client, "/api/structure/positions", new { code = "ADM", title = "Administrator", allCompanies = true, companyIds = Array.Empty<Guid>() });
    }

    [Fact]
    public async Task What_is_in_use_cannot_be_deleted_until_it_is_moved_or_removed()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");
        var all = new { allCompanies = true, companyIds = Array.Empty<Guid>() };
        var division = await Post<Division>(client, "/api/structure/divisions", new { name = "Restaurants", all.allCompanies, all.companyIds });
        var kitchen = await Post<Department>(client, "/api/structure/departments", new { name = "Kitchen", divisionId = division.Id, all.allCompanies, all.companyIds });
        var cook = await Post<Position>(client, "/api/structure/positions", new { title = "Cook", departmentId = kitchen.Id, all.allCompanies, all.companyIds });
        await Post<Unit>(client, "/api/structure/units", new { name = "Grill team", departmentId = kitchen.Id, all.allCompanies, all.companyIds });

        var dept = await client.DeleteAsync($"/api/structure/departments/{kitchen.Id}");
        Assert.Equal(HttpStatusCode.Conflict, dept.StatusCode);
        var error = await ErrorFrom(dept);
        Assert.Equal("ONB-3522", error.Code);
        Assert.Contains("1 position", error.Message);
        Assert.Contains("1 unit", error.Message);
        Assert.Equal("ONB-3522", (await ErrorFrom(await client.DeleteAsync($"/api/structure/divisions/{division.Id}"))).Code);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/structure/positions/{cook.Id}")).StatusCode);
        var unit = Assert.Single((await client.GetFromJsonAsync<List<Unit>>("/api/structure/units"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/structure/units/{unit.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/structure/departments/{kitchen.Id}")).StatusCode);
        Assert.Equal("ONB-3521", (await ErrorFrom(await client.DeleteAsync($"/api/structure/departments/{kitchen.Id}"))).Code);
    }

    [Fact]
    public async Task Editing_changes_details_and_companies_and_the_filter_shows_one_companys_structure()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd", "hotels");
        var bar = await Post<Department>(client, "/api/structure/departments", new { name = "Bar", allCompanies = false, companyIds = new[] { a.Id } });

        Assert.DoesNotContain((await client.GetFromJsonAsync<List<Department>>($"/api/structure/departments?companyId={b.Id}"))!, d => d.Id == bar.Id);

        var res = await client.PatchAsJsonAsync($"/api/structure/departments/{bar.Id}",
            new { code = bar.Code, name = "Bar and lounge", allCompanies = false, companyIds = new[] { b.Id } });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var edited = (await res.Content.ReadFromJsonAsync<Department>())!;
        Assert.Equal("Bar and lounge", edited.Name);
        Assert.Equal([b.Id], edited.CompanyIds);
        Assert.Contains((await client.GetFromJsonAsync<List<Department>>($"/api/structure/departments?companyId={b.Id}"))!, d => d.Id == bar.Id);
    }

    [Fact]
    public async Task Another_organization_and_plain_members_get_nothing()
    {
        var (a, _, orgId) = await Onboarded();
        await AddCompany(a, "Oliech Restaurants Ltd");
        var kitchen = await Post<Department>(a, "/api/structure/departments", new { name = "Kitchen", allCompanies = true, companyIds = Array.Empty<Guid>() });
        var (b, bEmail, _) = await Onboarded();
        await AddCompany(b, "Other Ltd");

        Assert.DoesNotContain((await b.GetFromJsonAsync<List<Department>>("/api/structure/departments"))!, d => d.Id == kitchen.Id);
        Assert.Equal("ONB-3521", (await ErrorFrom(await b.DeleteAsync($"/api/structure/departments/{kitchen.Id}"))).Code);
        var stranger = await ErrorFrom(await b.PostAsJsonAsync("/api/structure/positions",
            new { title = "Spy", departmentId = kitchen.Id, allCompanies = true, companyIds = Array.Empty<Guid>() }));
        Assert.Equal(["departmentId"], stranger.Fields!.Keys);

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

        var res = await b.PostAsJsonAsync("/api/structure/divisions", new { name = "Mine", allCompanies = true, companyIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("ACC-8001", (await ErrorFrom(res)).Code);
    }
}
