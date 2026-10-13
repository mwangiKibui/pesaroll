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
        Assert.Equal(["companies.manage", "employees.manage", "leave.manage", "locations.manage", "pay_items.manage", "payment_accounts.manage",
            "payroll_history.manage", "schedules.manage", "structure.manage", "team.manage"], me.Permissions.Select(p => p.Permission).Order());
        Assert.All(me.Permissions, p => Assert.True(p.AllCompanies));
    }

    [Fact]
    public async Task HR_for_one_company_sees_and_manages_only_its_people()
    {
        var (_, orgId, westlands, mombasa) = await Organization();
        var mary = await Colleague(orgId, "hr", westlands.Id);

        Assert.Equal([westlands.Id], (await mary.GetFromJsonAsync<List<Company>>("/api/companies"))!.Select(c => c.Id));
        var mine = (await mary.GetFromJsonAsync<Me>("/api/auth/me"))!.Permissions;
        Assert.Equal(["employees.manage", "leave.manage", "locations.manage", "structure.manage"], mine.Select(p => p.Permission).Order());
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
        Assert.Equal(["employees.manage", "pay_items.manage", "payment_accounts.manage", "payroll_history.manage", "schedules.manage"],
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
}
