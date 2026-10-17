using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ACC-05: pay and the account people are paid into have their own access (Pay and bank details).
/// They are sent only when asked for (includePay) and only to people with View on pay for that
/// person's company; changing them needs Manage on pay; every time they are sent, who saw whose is
/// recorded (sensitive_views), never the amounts.
/// </summary>
public sealed class PayAccessTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Person(Guid Id);
    private sealed record Row(Guid Id, string StaffNo, decimal? BasicAmount, bool PayHidden);
    private sealed record Page(List<Row> Items);
    private sealed record Pay(decimal BasicAmount);
    private sealed record Profile(Guid Id, string? Phone, Pay? Pay, bool PayHidden);
    private sealed record MeId(Guid Id);
    private sealed record Problem(int Line, string? Column, string Message);
    private sealed record Check(List<Problem> Problems);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email)> SignUp()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"pay-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        return (client, email);
    }

    private async Task<(HttpClient Owner, Guid OrgId, Company Westlands, Company Mombasa)> Organization()
    {
        var (owner, _) = await SignUp();
        var created = (await (await owner.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        async Task<Company> Add(string name) => (await (await owner.PostAsJsonAsync("/api/companies",
            new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" })).Content.ReadFromJsonAsync<Company>())!;
        return (owner, created.Organization.Id, await Add("Westlands Ltd"), await Add("Mombasa Ltd"));
    }

    /// <summary>Someone in the organization, in a group with this role for these companies (none: all), signed in.</summary>
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

    private static object Paid(Guid companyId, string staffNo, decimal amount) => new
    {
        companyId, staffNo, firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", job = new { employmentType = "permanent" },
        pay = new { basicAmount = amount, payBasis = "monthly" },
    };

    private static object Unpaid(Guid companyId, string staffNo) => new
    {
        companyId, staffNo, firstName = "Baraka", lastName = "Mwangi", hireDate = "2024-01-15", job = new { employmentType = "permanent" },
    };

    private static async Task<string> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!.Code;

    /// <summary>The pay views recorded in the organization: who saw whose, newest first.</summary>
    private static async Task<List<Guid[]>> Views(Guid orgId)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select subject_ids from sensitive_views where organization_id = @org and kind = 'employee_pay' order by viewed_at desc", connection);
        command.Parameters.AddWithValue("org", orgId);
        var views = new List<Guid[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            views.Add(reader.GetFieldValue<Guid[]>(0));
        }

        return views;
    }

    [Fact]
    public async Task Pay_is_sent_only_when_asked_for_and_only_with_View_on_pay()
    {
        var (owner, orgId, westlands, _) = await Organization();
        var wanjiru = (await (await owner.PostAsJsonAsync("/api/employees", Paid(westlands.Id, "W-20", 85000m))).Content.ReadFromJsonAsync<Person>())!;

        // An administrator: dots until asked for.
        var row = (await owner.GetFromJsonAsync<Page>("/api/employees"))!.Items.Single();
        Assert.Equal((null, true), (row.BasicAmount, row.PayHidden));
        row = (await owner.GetFromJsonAsync<Page>("/api/employees?includePay=true"))!.Items.Single();
        Assert.Equal((85000m, false), (row.BasicAmount, row.PayHidden));
        var profile = (await owner.GetFromJsonAsync<Profile>($"/api/employees/{wanjiru.Id}"))!;
        Assert.Equal((null, true), (profile.Pay, profile.PayHidden));
        Assert.Equal(85000m, (await owner.GetFromJsonAsync<Profile>($"/api/employees/{wanjiru.Id}?includePay=true"))!.Pay!.BasicAmount);

        // HR: never, even asking.
        var mary = await Colleague(orgId, "hr");
        row = (await mary.GetFromJsonAsync<Page>("/api/employees?includePay=true"))!.Items.Single();
        Assert.Equal((null, true), (row.BasicAmount, row.PayHidden));
        Assert.Null((await mary.GetFromJsonAsync<Profile>($"/api/employees/{wanjiru.Id}?includePay=true"))!.Pay);
        Assert.Equal("ACC-8001", await Code(await mary.GetAsync("/api/employees?sort=basicPay")));
        Assert.Equal("ACC-8001", await Code(await mary.GetAsync("/api/employees?payFrom=1")));

        // Payroll: when asking.
        var otieno = await Colleague(orgId, "payroll");
        Assert.Equal(85000m, (await otieno.GetFromJsonAsync<Page>("/api/employees?includePay=true"))!.Items.Single().BasicAmount);
        Assert.Equal(HttpStatusCode.OK, (await otieno.GetAsync("/api/employees?sort=basicPay&payFrom=1")).StatusCode);
    }

    [Fact]
    public async Task Pay_is_shown_only_for_the_companies_where_they_may_see_it()
    {
        var (owner, orgId, westlands, mombasa) = await Organization();
        await owner.PostAsJsonAsync("/api/employees", Paid(westlands.Id, "W-21", 85000m));
        await owner.PostAsJsonAsync("/api/employees", Paid(mombasa.Id, "M-21", 42000m));

        // Payroll for Westlands, and HR for every company: sees everyone, Westlands' pay only.
        var njeri = await Colleague(orgId, "payroll", westlands.Id);
        await JoinAllCompanies(njeri, orgId, "hr");
        var rows = (await njeri.GetFromJsonAsync<Page>("/api/employees?includePay=true"))!.Items.ToDictionary(r => r.StaffNo);
        Assert.Equal((85000m, false), (rows["W-21"].BasicAmount, rows["W-21"].PayHidden));
        Assert.Equal((null, true), (rows["M-21"].BasicAmount, rows["M-21"].PayHidden));
        Assert.Equal("ACC-8001", await Code(await njeri.GetAsync($"/api/employees?companyId={mombasa.Id}&sort=basicPay")));
    }

    [Fact]
    public async Task Every_time_pay_is_sent_who_saw_whose_is_recorded()
    {
        var (owner, orgId, westlands, _) = await Organization();
        var wanjiru = (await (await owner.PostAsJsonAsync("/api/employees", Paid(westlands.Id, "W-22", 85000m))).Content.ReadFromJsonAsync<Person>())!;
        Assert.Empty(await Views(orgId));   // adding someone isn't seeing their pay

        await owner.GetAsync("/api/employees");
        await owner.GetAsync($"/api/employees/{wanjiru.Id}");
        Assert.Empty(await Views(orgId));

        await owner.GetAsync("/api/employees?includePay=true");
        Assert.Equal([[wanjiru.Id]], await Views(orgId));
        await owner.GetAsync($"/api/employees/{wanjiru.Id}?includePay=true");
        Assert.Equal(2, (await Views(orgId)).Count);

        var mary = await Colleague(orgId, "hr");
        await mary.GetAsync("/api/employees?includePay=true");
        Assert.Equal(2, (await Views(orgId)).Count);   // nothing was sent to HR
    }

    [Fact]
    public async Task Changing_pay_needs_Manage_on_pay()
    {
        var (owner, orgId, westlands, _) = await Organization();
        var wanjiru = (await (await owner.PostAsJsonAsync("/api/employees", Paid(westlands.Id, "W-23", 85000m))).Content.ReadFromJsonAsync<Person>())!;
        var mary = await Colleague(orgId, "hr");

        Assert.Equal("ACC-8001", await Code(await mary.PostAsJsonAsync("/api/employees", Paid(westlands.Id, "W-24", 30000m))));
        Assert.Equal(HttpStatusCode.Created, (await mary.PostAsJsonAsync("/api/employees", Unpaid(westlands.Id, "W-25"))).StatusCode);

        // HR changes someone's phone: their pay stays.
        var edit = new
        {
            companyId = westlands.Id, staffNo = "W-23", firstName = "Wanjiru", lastName = "Kamau", phone = "0712345678", hireDate = "2024-01-15", job = new { employmentType = "permanent" },
        };
        Assert.Equal(HttpStatusCode.OK, (await mary.PatchAsJsonAsync($"/api/employees/{wanjiru.Id}", edit)).StatusCode);
        Assert.Equal(85000m, (await owner.GetFromJsonAsync<Profile>($"/api/employees/{wanjiru.Id}?includePay=true"))!.Pay!.BasicAmount);
        Assert.Equal("ACC-8001", await Code(await mary.PatchAsJsonAsync($"/api/employees/{wanjiru.Id}",
            new { companyId = westlands.Id, staffNo = "W-23", firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", job = new { employmentType = "permanent" }, pay = new { basicAmount = 90000m, payBasis = "monthly" } })));

        var otieno = await Colleague(orgId, "payroll");
        Assert.Equal(HttpStatusCode.OK, (await otieno.PatchAsJsonAsync($"/api/employees/{wanjiru.Id}",
            new { companyId = westlands.Id, staffNo = "W-23", firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", job = new { employmentType = "permanent" }, pay = new { basicAmount = 90000m, payBasis = "monthly" } })).StatusCode);
        Assert.Equal(90000m, (await owner.GetFromJsonAsync<Profile>($"/api/employees/{wanjiru.Id}?includePay=true"))!.Pay!.BasicAmount);
    }

    [Fact]
    public async Task An_organization_can_give_HR_View_on_pay()
    {
        var (owner, orgId, westlands, _) = await Organization();
        await owner.PostAsJsonAsync("/api/employees", Paid(westlands.Id, "W-26", 85000m));
        var mary = await Colleague(orgId, "hr");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/team/groups/hr/access", new
        {
            areas = new Dictionary<string, string> { ["employees"] = "manage", ["pay"] = "view" },
        })).StatusCode);
        Assert.Equal(85000m, (await mary.GetFromJsonAsync<Page>("/api/employees?includePay=true"))!.Items.Single().BasicAmount);
        Assert.Equal("ACC-8001", await Code(await mary.PostAsJsonAsync("/api/employees", Paid(westlands.Id, "W-27", 30000m))));   // View, not Manage
    }

    [Fact]
    public async Task Uploading_pay_or_bank_details_needs_Manage_on_pay()
    {
        var (owner, orgId, _, _) = await Organization();
        var mary = await Colleague(orgId, "hr");
        object Body(params (string Key, string Value)[] more) => new
        {
            rows = new[] { new { line = 2, cells = new Dictionary<string, string> { ["company"] = "Westlands Ltd", ["firstName"] = "Wanjiru", ["lastName"] = "Kamau", ["hireDate"] = "2024-01-15" }
                .Concat(more.ToDictionary(m => m.Key, m => m.Value)).ToDictionary(c => c.Key, c => c.Value) } },
            allCompanies = false, companyIds = Array.Empty<Guid>(),
        };
        async Task<List<(string? Column, string Message)>> Problems(HttpClient who, params (string Key, string Value)[] more) =>
            (await (await who.PostAsJsonAsync("/api/employees/import/check", Body(more))).Content.ReadFromJsonAsync<Check>())!.Problems.Select(p => (p.Column, p.Message)).ToList();

        Assert.Empty(await Problems(mary));
        Assert.Equal([("basicPay", "Payroll adds pay and bank details. Leave this empty.")], await Problems(mary, ("basicPay", "30000")));
        Assert.Equal([("mpesa", "Payroll adds pay and bank details. Leave this empty.")], await Problems(mary, ("mpesa", "0712345678")));
        Assert.Empty(await Problems(owner, ("basicPay", "30000")));
    }
}
