using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-11, Team access: an administrator sees who is in the organization with their group and
/// companies, changes someone's group and companies, or removes them; the organization always keeps
/// an administrator; others can't.
/// </summary>
public sealed class TeamTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Access(string? Group, string Name, bool AllCompanies, List<Guid> CompanyIds);
    private sealed record Member(Guid MembershipId, string FirstName, string LastName, string? Email, bool IsYou, List<Access> Groups);
    private sealed record Grant(string Permission, bool AllCompanies, List<Guid> CompanyIds);
    private sealed record Me(Guid? OrganizationId, List<Grant> Permissions);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private static void Authorize(HttpClient client, Token token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

    private HttpClient Guest() => api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<(HttpClient Owner, Company Westlands, Company Mombasa)> Organization()
    {
        var owner = Guest();
        var signup = await owner.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email = $"team-{Guid.NewGuid():N}@example.com", password = Password,
            roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        Authorize(owner, (await signup.Content.ReadFromJsonAsync<Token>())!);
        var created = (await (await owner.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        Authorize(owner, created.Token);
        async Task<Company> Add(string legalName) => (await (await owner.PostAsJsonAsync("/api/companies",
            new { legalName, countryCode = "KE", town = "Nairobi", regionCode = "047" })).Content.ReadFromJsonAsync<Company>())!;
        return (owner, await Add("Westlands Ltd"), await Add("Mombasa Ltd"));
    }

    /// <summary>Invites someone and has them accept: their signed-in client.</summary>
    private async Task<HttpClient> Joined(HttpClient owner, string group, params Guid[] companies)
    {
        var email = $"colleague-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/team/invitations",
            new { firstName = "Mary", lastName = "Wanjiku", email, groups = new[] { group }, allCompanies = companies.Length == 0, companyIds = companies })).StatusCode);
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select data->>'link' from notification_deliveries where recipient = @r and template = 'team_invitation' order by created_at desc limit 1", connection);
        command.Parameters.AddWithValue("r", email);
        var link = (string)(await command.ExecuteScalarAsync())!;
        var client = Guest();
        Authorize(client, (await (await client.PostAsJsonAsync($"/api/invitations/{link[(link.LastIndexOf('/') + 1)..]}/accept",
            new { firstName = "Mary", lastName = "Wanjiku", password = Password })).Content.ReadFromJsonAsync<Token>())!);
        return client;
    }

    private static async Task<string> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!.Code;

    [Fact]
    public async Task The_team_shows_each_person_with_their_group_and_companies()
    {
        var (owner, westlands, _) = await Organization();
        await Joined(owner, "hr", westlands.Id);

        var team = (await owner.GetFromJsonAsync<List<Member>>("/api/team/members"))!;
        var amina = team.Single(m => m.FirstName == "Amina");
        Assert.True(amina.IsYou);
        Assert.Equal(("administrators", "Administrators", true), (amina.Groups.Single().Group, amina.Groups.Single().Name, amina.Groups.Single().AllCompanies));
        var mary = team.Single(m => m.FirstName == "Mary");
        Assert.False(mary.IsYou);
        var access = Assert.Single(mary.Groups);
        Assert.Equal(("hr", "HR · Westlands Ltd", false), (access.Group, access.Name, access.AllCompanies));
        Assert.Equal([westlands.Id], access.CompanyIds);
    }

    [Fact]
    public async Task Changing_someones_access_takes_effect_on_their_next_request()
    {
        var (owner, westlands, mombasa) = await Organization();
        var mary = await Joined(owner, "hr", westlands.Id);
        var id = (await owner.GetFromJsonAsync<List<Member>>("/api/team/members"))!.Single(m => m.FirstName == "Mary").MembershipId;

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/team/members/{id}/access",
            new { groups = new[] { "payroll" }, allCompanies = false, companyIds = new[] { mombasa.Id } })).StatusCode);
        var grants = (await mary.GetFromJsonAsync<Me>("/api/auth/me"))!.Permissions;
        Assert.Contains(grants, g => g.Permission == "pay_items.manage");
        Assert.DoesNotContain(grants, g => g.Permission == "structure.manage");
        Assert.Equal([mombasa.Id], (await mary.GetFromJsonAsync<List<Company>>("/api/companies"))!.Select(c => c.Id));
        Assert.Equal(["Payroll · Mombasa Ltd"], (await owner.GetFromJsonAsync<List<Member>>("/api/team/members"))!.Single(m => m.MembershipId == id).Groups.Select(g => g.Name));

        // HR and Payroll for Mombasa: both groups, listed on the team.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/team/members/{id}/access",
            new { groups = new[] { "hr", "payroll" }, allCompanies = false, companyIds = new[] { mombasa.Id } })).StatusCode);
        Assert.Contains((await mary.GetFromJsonAsync<Me>("/api/auth/me"))!.Permissions, g => g.Permission == "structure.manage");
        Assert.Equal(["HR · Mombasa Ltd", "Payroll · Mombasa Ltd"], (await owner.GetFromJsonAsync<List<Member>>("/api/team/members"))!.Single(m => m.MembershipId == id).Groups.Select(g => g.Name).Order());

        var bad = await owner.PutAsJsonAsync($"/api/team/members/{id}/access", new { groups = new[] { "owners" }, allCompanies = false, companyIds = Array.Empty<Guid>() });
        var error = (await bad.Content.ReadFromJsonAsync<Error>())!;
        Assert.Equal("ACC-8009", error.Code);
        Assert.Equal(["companyIds", "groups"], error.Fields!.Keys.Order());
        Assert.Equal("ACC-8008", await Code(await owner.PutAsJsonAsync($"/api/team/members/{Guid.NewGuid()}/access", new { groups = new[] { "hr" }, allCompanies = true })));
    }

    [Fact]
    public async Task Removing_someone_ends_their_access_but_never_the_last_administrator()
    {
        var (owner, _, _) = await Organization();
        var mary = await Joined(owner, "hr");
        var team = (await owner.GetFromJsonAsync<List<Member>>("/api/team/members"))!;
        var aminaId = team.Single(m => m.IsYou).MembershipId;
        var maryId = team.Single(m => !m.IsYou).MembershipId;

        // Mary isn't an administrator: she can't see the team or change it.
        Assert.Equal("ACC-8001", await Code(await mary.GetAsync("/api/team/members")));
        Assert.Equal("ACC-8001", await Code(await mary.DeleteAsync($"/api/team/members/{aminaId}")));

        // Amina is the only administrator.
        Assert.Equal("ACC-8007", await Code(await owner.PutAsJsonAsync($"/api/team/members/{aminaId}/access", new { groups = new[] { "hr" }, allCompanies = true })));
        Assert.Equal("ACC-8007", await Code(await owner.DeleteAsync($"/api/team/members/{aminaId}")));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/team/members/{maryId}")).StatusCode);
        Assert.Empty((await mary.GetFromJsonAsync<List<Company>>("/api/companies"))!);
        Assert.Empty((await mary.GetFromJsonAsync<Me>("/api/auth/me"))!.Permissions);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<List<Member>>("/api/team/members"))!, m => m.MembershipId == maryId);
        Assert.Equal("ACC-8008", await Code(await owner.DeleteAsync($"/api/team/members/{maryId}")));
    }
}
