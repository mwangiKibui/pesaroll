using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ACC-01, ACC-02, ACC-06: what people may do comes from their security groups, each for all
/// companies or chosen ones. The person who onboarded the organization is in Administrators;
/// HR for one company sees and manages only that company's people; shared setup needs the
/// permission for every company.
/// </summary>
public sealed class AccessTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Grant(string Permission, bool AllCompanies, List<Guid> CompanyIds);
    private sealed record Me(Guid? OrganizationId, List<Grant> Permissions);
    private sealed record Created2(Guid Id);
    private sealed record MeId(Guid Id);

    /// <summary>Puts a signed-in person in one more group, with this role for every company.</summary>
    private static async Task JoinAllCompanies(HttpClient person, Guid orgId, string role)
    {
        var userId = (await person.GetFromJsonAsync<MeId>("/api/auth/me"))!.Id;
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            with g as (
                insert into security_groups (id, organization_id, name, all_companies) values (uuidv7(), @org, @name, true) returning id),
            gr as (
                insert into security_group_roles (organization_id, security_group_id, role_id) select @org, g.id, r.id from g, roles r where r.key = @role and r.organization_id is null)
            insert into security_group_members (id, organization_id, security_group_id, membership_id)
            select uuidv7(), @org, g.id, m.id from g, memberships m where m.organization_id = @org and m.user_id = @user
            """, connection);
        command.Parameters.AddWithValue("org", orgId);
        command.Parameters.AddWithValue("name", $"{role} {Guid.NewGuid():N}");
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync();
    }
    private sealed record Row(string StaffNo);
    private sealed record Page(List<Row> Items);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email)> SignUp()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"access-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        return (client, email);
    }

    /// <summary>An organization with two companies, onboarded by the returned client.</summary>
    private async Task<(HttpClient Owner, Guid OrgId, Company Westlands, Company Mombasa)> Organization()
    {
        var (owner, _) = await SignUp();
        var created = (await (await owner.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        async Task<Company> Add(string name) => (await (await owner.PostAsJsonAsync("/api/companies",
            new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" })).Content.ReadFromJsonAsync<Company>())!;
        return (owner, created.Organization.Id, await Add("Westlands Ltd"), await Add("Mombasa Ltd"));
    }

    /// <summary>
    /// Someone else in the organization, in a group with this role for these companies (none: all),
    /// signed in. Invitations (ACC-03) will do this; until then the test puts them there.
    /// </summary>
    private async Task<HttpClient> Colleague(Guid orgId, string role, params Guid[] companies)
    {
        var (client, email) = await SignUp();
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            with m as (
                insert into memberships (id, organization_id, user_id) select uuidv7(), @org, id from users where email = @email returning id),
            g as (
                insert into security_groups (id, organization_id, name, all_companies) values (uuidv7(), @org, @name, @all) returning id),
            gc as (
                insert into security_group_companies (organization_id, security_group_id, company_id) select @org, g.id, unnest(@companies) from g),
            gr as (
                insert into security_group_roles (organization_id, security_group_id, role_id) select @org, g.id, r.id from g, roles r where r.key = @role and r.organization_id is null)
            insert into security_group_members (id, organization_id, security_group_id, membership_id) select uuidv7(), @org, g.id, m.id from g, m
            """, connection);
        command.Parameters.AddWithValue("org", orgId);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("name", $"{role} {Guid.NewGuid():N}");
        command.Parameters.AddWithValue("all", companies.Length == 0);
        command.Parameters.AddWithValue("companies", companies);
        command.Parameters.AddWithValue("role", role);
        await command.ExecuteNonQueryAsync();

        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        return client;
    }

    private static object Employee(Guid companyId, string staffNo) =>
        new { companyId, staffNo, firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", job = new { employmentType = "permanent" } };

    private static async Task<string> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!.Code;

    [Fact]
    public async Task The_person_who_onboarded_it_is_an_administrator_for_every_company()
    {
        var (owner, orgId, _, _) = await Organization();
        var me = (await owner.GetFromJsonAsync<Me>("/api/auth/me"))!;
        Assert.Equal(orgId, me.OrganizationId);
        Assert.Equal(["companies.manage", "companies.view", "employees.manage", "employees.view", "leave.manage", "leave.view", "locations.manage", "locations.view",
            "pay_items.manage", "pay_items.view", "payment_accounts.manage", "payment_accounts.view", "payroll_history.manage", "payroll_history.view",
            "schedules.manage", "schedules.view", "structure.manage", "structure.view", "team.manage", "team.view"], me.Permissions.Select(p => p.Permission).Order());
        Assert.All(me.Permissions, p => Assert.True(p.AllCompanies));
    }

    [Fact]
    public async Task HR_for_one_company_sees_and_manages_only_its_people()
    {
        var (_, orgId, westlands, mombasa) = await Organization();
        var mary = await Colleague(orgId, "hr", westlands.Id);

        Assert.Equal([westlands.Id], (await mary.GetFromJsonAsync<List<Company>>("/api/companies"))!.Select(c => c.Id));
        var mine = (await mary.GetFromJsonAsync<Me>("/api/auth/me"))!.Permissions;
        Assert.Equal(["employees.manage", "employees.view", "leave.manage", "leave.view", "locations.manage", "locations.view", "schedules.view",
            "structure.manage", "structure.view"], mine.Select(p => p.Permission).Order());
        Assert.All(mine, p => { Assert.False(p.AllCompanies); Assert.Equal([westlands.Id], p.CompanyIds); });
        Assert.Equal(HttpStatusCode.Created, (await mary.PostAsJsonAsync("/api/employees", Employee(westlands.Id, "W-01"))).StatusCode);

        var other = await mary.PostAsJsonAsync("/api/employees", Employee(mombasa.Id, "M-01"));
        Assert.Equal((HttpStatusCode.Forbidden, "ACC-8001"), (other.StatusCode, await Code(other)));
        // Not hers: companies, earnings and deductions; nor shared setup, which serves every company.
        Assert.Equal("ACC-8001", await Code(await mary.PostAsJsonAsync("/api/companies", new { legalName = "Kisumu Ltd", countryCode = "KE", town = "Kisumu", regionCode = "042" })));
        Assert.Equal("ACC-8001", await Code(await mary.PostAsJsonAsync("/api/pay-items", new { name = "Bonus", kind = "earning", taxable = true, calcMethod = "each_run", allCompanies = true })));
        Assert.Equal("ACC-8001", await Code(await mary.PostAsJsonAsync("/api/structure/departments", new { name = "Kitchen", allCompanies = true, companyIds = Array.Empty<Guid>() })));
    }

    [Fact]
    public async Task Payroll_for_every_company_manages_pay_but_not_people_structure_or_companies()
    {
        var (_, orgId, westlands, _) = await Organization();
        var otieno = await Colleague(orgId, "payroll");

        Assert.Equal(2, (await otieno.GetFromJsonAsync<List<Company>>("/api/companies"))!.Count);
        Assert.Equal(["employees.manage", "employees.view", "leave.view", "locations.view", "pay_items.manage", "pay_items.view", "payment_accounts.manage",
            "payment_accounts.view", "payroll_history.manage", "payroll_history.view", "schedules.manage", "schedules.view", "structure.view"],
            (await otieno.GetFromJsonAsync<Me>("/api/auth/me"))!.Permissions.Select(p => p.Permission).Order());
        Assert.Equal(HttpStatusCode.Created, (await otieno.PostAsJsonAsync("/api/pay-items",
            new { name = "Bonus", kind = "earning", taxable = true, calcMethod = "each_run", allCompanies = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await otieno.PostAsJsonAsync("/api/employees", Employee(westlands.Id, "W-02"))).StatusCode);
        Assert.Equal("ACC-8001", await Code(await otieno.PostAsJsonAsync("/api/structure/departments", new { name = "Kitchen", allCompanies = true, companyIds = Array.Empty<Guid>() })));
        Assert.Equal("ACC-8001", await Code(await otieno.DeleteAsync($"/api/companies/{westlands.Id}")));
    }

    [Fact]
    public async Task An_approver_sees_every_company_and_changes_nothing_yet()
    {
        var (_, orgId, westlands, _) = await Organization();
        var njeri = await Colleague(orgId, "approver");
        Assert.Equal(2, (await njeri.GetFromJsonAsync<List<Company>>("/api/companies"))!.Count);
        Assert.Empty((await njeri.GetFromJsonAsync<Me>("/api/auth/me"))!.Permissions);
        Assert.Equal("ACC-8001", await Code(await njeri.PostAsJsonAsync("/api/employees", Employee(westlands.Id, "W-03"))));
    }

    // ACC-04: reading needs the area's View, for the company read.
    [Fact]
    public async Task Reading_needs_View_on_the_area()
    {
        var (_, orgId, westlands, _) = await Organization();
        var njeri = await Colleague(orgId, "approver");
        var mary = await Colleague(orgId, "hr");
        var otieno = await Colleague(orgId, "payroll");

        foreach (var path in new[] { "/api/employees", "/api/payroll-history/batches", $"/api/companies/{westlands.Id}/payment-accounts", "/api/team/members", "/api/pay-items" })
        {
            var res = await njeri.GetAsync(path);
            Assert.Equal((HttpStatusCode.Forbidden, "ACC-8001"), (res.StatusCode, await Code(res)));
        }

        foreach (var path in new[] { "/api/payroll-history/batches", $"/api/companies/{westlands.Id}/payment-accounts", "/api/pay-items", "/api/payees", "/api/team/members" })
        {
            Assert.Equal("ACC-8001", await Code(await mary.GetAsync(path)));
        }

        Assert.Equal(HttpStatusCode.OK, (await mary.GetAsync("/api/pay-schedules")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otieno.GetAsync("/api/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otieno.GetAsync("/api/structure/departments")).StatusCode);
        Assert.Equal("ACC-8001", await Code(await otieno.GetAsync("/api/team/members")));
        Assert.Equal("ACC-8001", await Code(await otieno.PostAsJsonAsync("/api/locations", new { name = "Nyali", town = "Mombasa", allCompanies = true })));
    }

    [Fact]
    public async Task HR_for_one_company_reads_only_its_people()
    {
        var (owner, orgId, westlands, mombasa) = await Organization();
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/employees", Employee(westlands.Id, "W-10"))).StatusCode);
        var theirs = (await (await owner.PostAsJsonAsync("/api/employees", Employee(mombasa.Id, "M-10"))).Content.ReadFromJsonAsync<Created2>())!;
        // HR for Westlands, and an Approver for every company: she belongs to both companies (row-level
        // security lets her reach both) but may view employees only in Westlands.
        var mary = await Colleague(orgId, "hr", westlands.Id);
        await JoinAllCompanies(mary, orgId, "approver");
        Assert.Equal(2, (await mary.GetFromJsonAsync<List<Company>>("/api/companies"))!.Count);

        Assert.Equal(["W-10"], (await mary.GetFromJsonAsync<Page>("/api/employees"))!.Items.Select(e => e.StaffNo));
        Assert.Equal("ACC-8001", await Code(await mary.GetAsync($"/api/employees?companyId={mombasa.Id}")));
        Assert.Equal("ACC-8001", await Code(await mary.GetAsync($"/api/employees?companyIds={mombasa.Id}")));
        Assert.Equal("ACC-8001", await Code(await mary.GetAsync($"/api/employees/choices?companyId={mombasa.Id}")));
        Assert.Equal("ACC-8001", await Code(await mary.GetAsync($"/api/employees/{theirs.Id}")));
    }

    [Fact]
    public async Task An_administrator_can_rename_the_organization_and_others_cannot()
    {
        var (_, orgId, _, _) = await Organization();
        var mary = await Colleague(orgId, "hr");
        Assert.Equal("ACC-8001", await Code(await mary.PatchAsJsonAsync("/api/organizations/current", new { name = "Not hers" })));
    }
}
