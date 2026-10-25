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

    private sealed record AreaName(string En, string Sw);
    private sealed record Area(string Key, AreaName Name);
    private sealed record AreaLevel(string Area, string Level);
    private sealed record Group(string Key, string Name, string? Description, bool Standard, bool Custom, bool AllCompanies, List<Guid> CompanyIds, int People, List<AreaLevel> Areas);
    private sealed record Groups(List<Area> Areas, List<Group> Items);

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

    // ACC-04: what each group can do, per area, and changing it for everyone in the group.
    [Fact]
    public async Task Groups_show_what_each_can_do_per_area()
    {
        var (owner, _, _) = await Organization();
        var groups = (await owner.GetFromJsonAsync<Groups>("/api/team/groups"))!;

        Assert.Equal(["companies", "employees", "pay", "locations", "structure", "leave", "schedules", "pay_items", "payment_accounts", "payroll_history", "team", "workflows", "pay_run_approvals", "pay_runs"],
            groups.Areas.Select(a => a.Key));
        Assert.Equal("Employees", groups.Areas[1].Name.En);
        Assert.Equal(["administrators", "hr", "payroll", "approvers"], groups.Items.Select(g => g.Key));
        var hr = groups.Items.Single(g => g.Key == "hr");
        Assert.True(hr.Standard);
        Assert.Equal(["companies:none", "employees:manage", "pay:none", "locations:manage", "structure:manage", "leave:manage", "schedules:view", "pay_items:none",
            "payment_accounts:none", "payroll_history:none", "team:none", "workflows:none", "pay_run_approvals:none", "pay_runs:none"], hr.Areas.Select(a => $"{a.Area}:{a.Level}"));
        Assert.All(groups.Items.Single(g => g.Key == "administrators").Areas, a => Assert.Equal("manage", a.Level));
        Assert.Equal(1, groups.Items.Single(g => g.Key == "administrators").People);
    }

    [Fact]
    public async Task Changing_a_group_applies_to_everyone_in_it_only_in_this_organization()
    {
        var (owner, westlands, _) = await Organization();
        var otieno = await Joined(owner, "payroll", westlands.Id);
        var (elsewhere, _, _) = await Organization();
        var theirs = await Joined(elsewhere, "payroll");

        var changed = await owner.PutAsJsonAsync("/api/team/groups/payroll/access", new
        {
            areas = new Dictionary<string, string> { ["employees"] = "manage", ["locations"] = "view", ["structure"] = "view", ["schedules"] = "manage", ["pay_items"] = "none" },
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var payroll = (await changed.Content.ReadFromJsonAsync<Group>())!;
        Assert.False(payroll.Standard);
        Assert.Equal(1, payroll.People);
        Assert.Equal("view", payroll.Areas.Single(a => a.Area == "locations").Level);

        Assert.Equal(HttpStatusCode.OK, (await otieno.GetAsync("/api/locations")).StatusCode);
        Assert.Equal("ACC-8001", await Code(await otieno.GetAsync("/api/pay-items")));
        Assert.Equal(HttpStatusCode.OK, (await theirs.GetAsync("/api/pay-items")).StatusCode);   // another organization's Payroll is unchanged

        var back = await owner.DeleteAsync("/api/team/groups/payroll/access");
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.True((await back.Content.ReadFromJsonAsync<Group>())!.Standard);
        Assert.Equal(HttpStatusCode.OK, (await otieno.GetAsync("/api/pay-items")).StatusCode);
    }

    [Fact]
    public async Task Changing_a_group_is_refused_when_it_is_wrong_or_would_lock_the_organization_out()
    {
        var (owner, _, _) = await Organization();
        var mary = await Joined(owner, "hr");

        Assert.Equal("ACC-8010", await Code(await owner.PutAsJsonAsync("/api/team/groups/hr/access", new { areas = new Dictionary<string, string> { ["employees"] = "edit" } })));
        Assert.Equal("ACC-8010", await Code(await owner.PutAsJsonAsync("/api/team/groups/hr/access", new { areas = new Dictionary<string, string> { ["kitchen"] = "view" } })));
        Assert.Equal("ACC-8010", await Code(await owner.PutAsJsonAsync("/api/team/groups/cooks/access", new { areas = new Dictionary<string, string>() })));
        var lockedOut = await owner.PutAsJsonAsync("/api/team/groups/administrators/access", new { areas = new Dictionary<string, string> { ["employees"] = "manage" } });
        Assert.Equal((HttpStatusCode.Conflict, "ACC-8007"), (lockedOut.StatusCode, await Code(lockedOut)));

        Assert.Equal("ACC-8001", await Code(await mary.GetAsync("/api/team/groups")));
        Assert.Equal("ACC-8001", await Code(await mary.PutAsJsonAsync("/api/team/groups/hr/access", new { areas = new Dictionary<string, string> { ["team"] = "manage" } })));
    }

    [Fact]
    public async Task View_on_team_access_lists_people_invitations_and_groups_but_changes_nothing()
    {
        var (owner, westlands, _) = await Organization();
        var mary = await Joined(owner, "hr");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/team/groups/hr/access", new
        {
            areas = new Dictionary<string, string> { ["employees"] = "manage", ["team"] = "view" },
        })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await mary.GetAsync("/api/team/members")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mary.GetAsync("/api/team/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mary.GetAsync("/api/team/groups")).StatusCode);
        Assert.Equal("ACC-8001", await Code(await mary.PostAsJsonAsync("/api/team/invitations",
            new { firstName = "Otieno", lastName = "Ouma", email = $"o-{Guid.NewGuid():N}@example.com", groups = new[] { "payroll" }, allCompanies = false, companyIds = new[] { westlands.Id } })));
        Assert.Equal("ACC-8001", await Code(await mary.PutAsJsonAsync("/api/team/groups/hr/access", new { areas = new Dictionary<string, string> { ["team"] = "manage" } })));
    }

    // ACC-08: an organization's own groups.
    [Fact]
    public async Task Own_groups_are_added_given_to_people_renamed_and_deleted_once_empty()
    {
        var (owner, westlands, _) = await Organization();
        var added = await owner.PostAsJsonAsync("/api/team/groups", new
        {
            name = "Branch managers", description = "Run a branch", areas = new Dictionary<string, string> { ["employees"] = "manage", ["leave"] = "view" },
        });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var group = (await added.Content.ReadFromJsonAsync<Group>())!;
        Assert.Equal(("branch_managers", "Branch managers", "Run a branch", true, 0), (group.Key, group.Name, group.Description, group.Custom, group.People));

        var ken = await Joined(owner, "branch_managers", westlands.Id);
        Assert.Equal(HttpStatusCode.OK, (await ken.GetAsync("/api/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ken.GetAsync("/api/leave-plans")).StatusCode);
        Assert.Equal("ACC-8001", await Code(await ken.GetAsync("/api/payroll-history/batches")));
        var listed = (await owner.GetFromJsonAsync<Groups>("/api/team/groups"))!.Items;
        Assert.Equal(["administrators", "hr", "payroll", "approvers", "branch_managers"], listed.Select(g => g.Key));
        Assert.Equal(1, listed.Single(g => g.Key == "branch_managers").People);

        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync("/api/team/groups/branch_managers", new { name = "Branch leads", description = "Lead a branch" })).StatusCode);
        Assert.Equal("Branch leads", (await owner.GetFromJsonAsync<Groups>("/api/team/groups"))!.Items.Single(g => g.Key == "branch_managers").Name);

        var inUse = await owner.DeleteAsync("/api/team/groups/branch_managers");
        Assert.Equal((HttpStatusCode.Conflict, "ACC-8012"), (inUse.StatusCode, await Code(inUse)));
        var kennedy = (await owner.GetFromJsonAsync<List<Member>>("/api/team/members"))!.Single(m => !m.IsYou);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/team/members/{kennedy.MembershipId}/access", new { groups = new[] { "hr" }, allCompanies = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/team/groups/branch_managers")).StatusCode);
        Assert.DoesNotContain("branch_managers", (await owner.GetFromJsonAsync<Groups>("/api/team/groups"))!.Items.Select(g => g.Key));
    }

    [Fact]
    public async Task Own_groups_need_a_new_name_stay_in_their_organization_and_need_team_management()
    {
        var (owner, _, _) = await Organization();
        var mary = await Joined(owner, "hr");
        Assert.Equal("ACC-8011", await Code(await owner.PostAsJsonAsync("/api/team/groups", new { name = "HR", areas = new Dictionary<string, string>() })));
        Assert.Equal("ACC-8011", await Code(await owner.PostAsJsonAsync("/api/team/groups", new { name = "x", areas = new Dictionary<string, string>() })));
        Assert.Equal("ACC-8010", await Code(await owner.PostAsJsonAsync("/api/team/groups", new { name = "Finance", areas = new Dictionary<string, string> { ["employees"] = "edit" } })));
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/team/groups", new { name = "Finance", areas = new Dictionary<string, string> { ["payroll_history"] = "view" } })).StatusCode);
        Assert.Equal("ACC-8011", await Code(await owner.PostAsJsonAsync("/api/team/groups", new { name = "finance", areas = new Dictionary<string, string>() })));
        Assert.Equal("ACC-8001", await Code(await mary.PostAsJsonAsync("/api/team/groups", new { name = "Kitchen leads", areas = new Dictionary<string, string>() })));
        Assert.Equal("ACC-8010", await Code(await owner.DeleteAsync("/api/team/groups/finance/access")));   // only standard groups have a standard

        var (elsewhere, westlands, _) = await Organization();
        Assert.DoesNotContain("finance", (await elsewhere.GetFromJsonAsync<Groups>("/api/team/groups"))!.Items.Select(g => g.Key));
        Assert.Equal("ACC-8004", await Code(await elsewhere.PostAsJsonAsync("/api/team/invitations", new
        {
            firstName = "Otieno", lastName = "Ouma", email = $"o-{Guid.NewGuid():N}@example.com", groups = new[] { "finance" }, allCompanies = false, companyIds = new[] { westlands.Id },
        })));
    }

    [Fact]
    public async Task An_own_group_for_some_companies_is_only_given_for_them()
    {
        var (owner, westlands, mombasa) = await Organization();
        var added = (await (await owner.PostAsJsonAsync("/api/team/groups", new
        {
            name = "Mombasa kitchen leads", areas = new Dictionary<string, string> { ["employees"] = "view" }, allCompanies = false, companyIds = new[] { mombasa.Id },
        })).Content.ReadFromJsonAsync<Group>())!;
        Assert.False(added.AllCompanies);
        Assert.Equal([mombasa.Id], added.CompanyIds);

        Assert.Equal("ACC-8013", await Code(await owner.PostAsJsonAsync("/api/team/invitations", new
        {
            firstName = "Otieno", lastName = "Ouma", email = $"o-{Guid.NewGuid():N}@example.com", groups = new[] { added.Key }, allCompanies = false, companyIds = new[] { westlands.Id },
        })));

        // Invited for all companies: they get the group's companies.
        var lead = await Joined(owner, added.Key);
        Assert.Equal(HttpStatusCode.OK, (await lead.GetAsync($"/api/employees?companyId={mombasa.Id}")).StatusCode);
        Assert.Equal("ACC-8001", await Code(await lead.GetAsync($"/api/employees?companyId={westlands.Id}")));

        var narrowed = await owner.PatchAsJsonAsync($"/api/team/groups/{added.Key}", new { name = "Mombasa kitchen leads", allCompanies = false, companyIds = new[] { westlands.Id } });
        Assert.Equal((HttpStatusCode.Conflict, "ACC-8014"), (narrowed.StatusCode, await Code(narrowed)));
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync($"/api/team/groups/{added.Key}", new { name = "Coast kitchen leads", allCompanies = true })).StatusCode);
        Assert.True((await owner.GetFromJsonAsync<Groups>("/api/team/groups"))!.Items.Single(g => g.Key == added.Key).AllCompanies);
    }
}
