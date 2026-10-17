using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// OP-30: the people numbers on HR's Home (GET /api/home/people) for the company chosen in the top
/// bar: employees now, who joined this month (a few names), and readiness for the next pay run,
/// grouped by what's missing with its code, the same as the Employees readiness. Counted in the
/// database; no pay, no ID numbers; employees.view for that company or ACC-8001.
/// </summary>
public sealed class HomeTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Label(string En, string Sw);
    private sealed record Joiner(Guid Id, string Name, DateOnly HireDate);
    private sealed record Missing(string Key, Label Label, string? Code, int People);
    private sealed record Readiness(DateOnly? NextPayDate, int Ready, int Total, List<Missing> Missing);
    private sealed record People(Guid CompanyId, int Employees, int JoinedThisMonth, List<Joiner> Joined, Readiness Readiness);
    private sealed record RowMissing(string Key);
    private sealed record RowReadiness(bool Ready, List<RowMissing> Missing);
    private sealed record Row(Guid Id, RowReadiness Readiness);
    private sealed record Page(List<Row> Items, int Total);
    private sealed record Schedule(Guid Id, List<Guid> DefaultFor, List<DateOnly> NextPayDates);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    private async Task<(HttpClient Client, string Email)> SignUp()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"home-{Guid.NewGuid():N}@example.com";
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

    private static int number;

    /// <summary>
    /// Adds someone with every detail payroll needs unless told otherwise (no KRA PIN, no ID, no pay, no account).
    /// Each person gets their own KRA PIN and ID number (unique among current staff).
    /// </summary>
    private static async Task<Guid> Add(HttpClient client, Guid companyId, string firstName, string hireDate = "2024-01-15", string? exitDate = null,
        bool kraPin = true, bool nationalId = true, bool pay = true, bool account = true)
    {
        var n = Interlocked.Increment(ref number);
        var res = await client.PostAsJsonAsync("/api/employees", new
        {
            companyId, firstName, lastName = "Kamau", hireDate, exitDate,
            kraPin = kraPin ? $"A{n:D9}Z" : null, nationalId = nationalId ? $"{30000000 + n}" : null,
            job = new { employmentType = "permanent" },
            pay = pay ? new { basicAmount = 85000m, payBasis = "monthly" } : null,
            account = account ? new { method = "bank", bankCode = "68", branchCode = "047", accountNumber = $"{n:D13}" } : null,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<Row>())!.Id;
    }

    private static async Task<string> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!.Code;

    [Fact]
    public async Task Home_counts_the_chosen_companys_people_and_whats_missing_for_payroll()
    {
        var (owner, _, westlands, mombasa) = await Organization();
        await Add(owner, westlands.Id, "Wanjiru");
        await Add(owner, westlands.Id, "Achieng");
        await Add(owner, westlands.Id, "Baraka", hireDate: Today.ToString("yyyy-MM-dd"));
        await Add(owner, westlands.Id, "Kiprono", kraPin: false);
        await Add(owner, westlands.Id, "Former", hireDate: "2023-01-02", exitDate: "2024-12-31", kraPin: false, pay: false);
        await Add(owner, mombasa.Id, "Juma", kraPin: false, nationalId: false, pay: false, account: false);

        var home = (await owner.GetFromJsonAsync<People>($"/api/home/people?companyId={westlands.Id}"))!;
        Assert.Equal((westlands.Id, 4, 1), (home.CompanyId, home.Employees, home.JoinedThisMonth));
        Assert.Equal("Baraka Kamau", Assert.Single(home.Joined).Name);
        Assert.Equal((3, 4), (home.Readiness.Ready, home.Readiness.Total));
        var kra = Assert.Single(home.Readiness.Missing);
        Assert.Equal(("kra_pin", "KRA PIN", "PAY-2001", 1), (kra.Key, kra.Label.En, kra.Code, kra.People));

        // The next pay date of the company's main payroll schedule.
        var schedules = (await owner.GetFromJsonAsync<List<Schedule>>("/api/pay-schedules"))!;
        Assert.Equal(schedules.Single(s => s.DefaultFor.Contains(westlands.Id)).NextPayDates[0], home.Readiness.NextPayDate);

        // Another company: its own numbers; someone missing everything shows one line per detail.
        var other = (await owner.GetFromJsonAsync<People>($"/api/home/people?companyId={mombasa.Id}"))!;
        Assert.Equal((1, 0, 1), (other.Employees, other.Readiness.Ready, other.Readiness.Total));
        Assert.Equal(["kra_pin", "national_id", "basic_pay", "payment_account"], other.Readiness.Missing.Select(m => m.Key));
        Assert.All(other.Readiness.Missing, m => Assert.Equal(1, m.People));
        Assert.Equal([("kra_pin", "PAY-2001")], other.Readiness.Missing.Where(m => m.Code is not null).Select(m => (m.Key, m.Code!)));
    }

    [Fact]
    public async Task Home_readiness_matches_the_Employees_readiness_exactly()
    {
        var (owner, _, westlands, _) = await Organization();
        await Add(owner, westlands.Id, "Wanjiru");
        await Add(owner, westlands.Id, "Kiprono", kraPin: false);
        await Add(owner, westlands.Id, "Halima", kraPin: false, account: false);
        await Add(owner, westlands.Id, "Otieno", nationalId: false, pay: false);
        await Add(owner, westlands.Id, "Njeri", hireDate: Today.ToString("yyyy-MM-dd"), account: false);

        var home = (await owner.GetFromJsonAsync<People>($"/api/home/people?companyId={westlands.Id}"))!;
        var rows = (await owner.GetFromJsonAsync<Page>($"/api/employees?companyId={westlands.Id}"))!;
        Assert.Equal(rows.Total, home.Employees);
        Assert.Equal(rows.Items.Count(r => r.Readiness.Ready), home.Readiness.Ready);
        var expected = rows.Items.SelectMany(r => r.Readiness.Missing).GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(expected, home.Readiness.Missing.ToDictionary(m => m.Key, m => m.People));
        // Most people first.
        Assert.Equal(home.Readiness.Missing.OrderByDescending(m => m.People).Select(m => m.People), home.Readiness.Missing.Select(m => m.People));
    }

    [Fact]
    public async Task Home_names_at_most_three_joiners_newest_first()
    {
        var (owner, _, westlands, _) = await Organization();
        var first = new DateOnly(Today.Year, Today.Month, 1);
        var days = Enumerable.Range(0, 5).Select(i => first.AddDays(Math.Min(i, Today.Day - 1))).ToList();
        foreach (var (day, i) in days.Select((d, i) => (d, i)))
        {
            await Add(owner, westlands.Id, $"Joiner{i}", hireDate: day.ToString("yyyy-MM-dd"));
        }

        await Add(owner, westlands.Id, "Earlier", hireDate: first.AddDays(-1).ToString("yyyy-MM-dd"));
        var home = (await owner.GetFromJsonAsync<People>($"/api/home/people?companyId={westlands.Id}"))!;
        Assert.Equal((6, 5), (home.Employees, home.JoinedThisMonth));
        Assert.Equal(3, home.Joined.Count);
        Assert.Equal(home.Joined.OrderByDescending(j => j.HireDate).Select(j => j.HireDate), home.Joined.Select(j => j.HireDate));
        Assert.Equal(days.Max(), home.Joined[0].HireDate);
    }

    [Fact]
    public async Task Home_sends_no_pay_and_no_ID_numbers()
    {
        var (owner, _, westlands, _) = await Organization();
        await Add(owner, westlands.Id, "Wanjiru", hireDate: Today.ToString("yyyy-MM-dd"));
        await Add(owner, westlands.Id, "Kiprono", kraPin: false);

        var text = await owner.GetStringAsync($"/api/home/people?companyId={westlands.Id}");
        // Exactly these fields, nothing about pay or anyone's numbers.
        var names = new SortedSet<string>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    names.Add(p.Name);
                    Walk(p.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                {
                    Walk(item);
                }
            }
        }

        Walk(JsonDocument.Parse(text).RootElement);
        Assert.Equal(["code", "companyId", "employees", "en", "hireDate", "id", "joined", "joinedThisMonth", "key", "label", "missing", "name",
            "nextPayDate", "people", "ready", "readiness", "sw", "total"], names);
        Assert.DoesNotContain("85000", text);
    }

    [Fact]
    public async Task HR_for_one_company_gets_that_companys_numbers_and_never_anothers()
    {
        var (owner, orgId, westlands, mombasa) = await Organization();
        await Add(owner, westlands.Id, "Wanjiru");
        await Add(owner, mombasa.Id, "Juma");

        var mary = await Colleague(orgId, "hr", westlands.Id);
        Assert.Equal(1, (await mary.GetFromJsonAsync<People>($"/api/home/people?companyId={westlands.Id}"))!.Employees);
        var refused = await mary.GetAsync($"/api/home/people?companyId={mombasa.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("ACC-8001", await Code(refused));
    }

    [Fact]
    public async Task Home_needs_View_on_employees()
    {
        var (owner, orgId, westlands, _) = await Organization();
        await Add(owner, westlands.Id, "Wanjiru");

        // An Approver may not view employees anywhere.
        var approver = await Colleague(orgId, "approver");
        var refused = await approver.GetAsync($"/api/home/people?companyId={westlands.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("ACC-8001", await Code(refused));
    }

    [Fact]
    public async Task Another_organizations_company_is_refused()
    {
        var (owner, _, _, _) = await Organization();
        var (stranger, _, theirs, _) = await Organization();
        await Add(stranger, theirs.Id, "Siri");

        // The owner may view employees in every company of theirs; that never reaches another organization's.
        var refused = await owner.GetAsync($"/api/home/people?companyId={theirs.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("ACC-8001", await Code(refused));
    }
}
