using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pesaroll.Payroll;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// PLAT-03: named actions in the audit log and reading it. What people do (work out a payroll,
/// approve it, change someone's pay or bank account, switch on a cut-off) is recorded by name with
/// who did it and never an amount; admins with audit_log.view read it for their companies, filter
/// it ("who worked out payroll on the 25th?") and count it ("how many people approved payrolls?").
/// </summary>
public sealed class AuditLogTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Person(HttpClient Client, Guid UserId);
    private sealed record World(Person Owner, Guid OrgId, Company Westlands, Company Mombasa, Guid Schedule);
    private sealed record AddedEmployee(Guid Id);
    private sealed record Entry(Guid Id, DateTimeOffset At, string Kind, string Action, Guid? CompanyId, Guid? UserId, string? UserName, string? SubjectType,
        Guid? SubjectId, string? SubjectName, JsonElement? Details, string? TableName, Guid? RecordId, string[]? Changed, JsonElement? OldValues,
        JsonElement? NewValues, string? RequestRef, string Source);
    private sealed record Page(List<Entry> Items, int Total, int PageNumber, int PageSize);
    private sealed record Summary(int Entries, int People);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email)> SignUp()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"audit-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        return (client, email);
    }

    /// <summary>An organization (its owner is an Administrator) with two companies on the suggested monthly schedule.</summary>
    private async Task<World> Organization()
    {
        var (owner, email) = await SignUp();
        var created = (await (await owner.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        async Task<Company> Add(string name) => (await (await owner.PostAsJsonAsync("/api/companies",
            new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" })).Content.ReadFromJsonAsync<Company>())!;
        var westlands = await Add("Westlands Ltd");
        var mombasa = await Add("Mombasa Ltd");
        var schedule = await Scalar<Guid>("select default_pay_schedule_id from companies where id = @a", ("a", westlands.Id));
        return new World(new Person(owner, await UserId(email)), created.Organization.Id, westlands, mombasa, schedule);
    }

    private static async Task<T> Scalar<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Execute(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<Guid> UserId(string email) => Scalar<Guid>("select id from users where email = @e", ("e", email));

    /// <summary>Someone in the organization, in a group with this standard role for these companies (none: all), signed in.</summary>
    private async Task<Person> Colleague(Guid orgId, string role, string firstName, params Guid[] companies)
    {
        var (client, email) = await SignUp();
        await Execute("""
            update users set first_name = @first where email = @email;
            with m as (
                insert into memberships (id, organization_id, user_id) select uuidv7(), @org, id from users where email = @email returning id),
            g as (
                insert into security_groups (id, organization_id, name, all_companies) values (uuidv7(), @org, @name, @all) returning id),
            gc as (
                insert into security_group_companies (organization_id, security_group_id, company_id) select @org, g.id, unnest(@companies) from g),
            gr as (
                insert into security_group_roles (organization_id, security_group_id, role_id) select @org, g.id, r.id from g, roles r where r.key = @role and r.organization_id is null)
            insert into security_group_members (id, organization_id, security_group_id, membership_id) select uuidv7(), @org, g.id, m.id from g, m
            """, ("org", orgId), ("email", email), ("first", firstName), ("name", $"{role} {Guid.NewGuid():N}"), ("all", companies.Length == 0),
            ("companies", companies), ("role", role));
        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        return new Person(client, await UserId(email));
    }

    /// <summary>Someone added by hand, with ID number 2345678n and KRA PIN A00000000nZ (n: 1 for the first, 2 for another).</summary>
    private static async Task<Guid> AddEmployee(Person by, Company company, string first = "Wanjiru", int n = 1)
    {
        var res = await by.Client.PostAsJsonAsync("/api/employees", new
        {
            companyId = company.Id, firstName = first, lastName = "Kamau", hireDate = "2024-01-15", nationalId = $"2345678{n}", kraPin = $"A00000000{n}Z",
            job = new { employmentType = "permanent" },
            pay = new { basicAmount = 85000m, payBasis = "monthly" },
            account = new { method = "bank", bankCode = "68", branchCode = "047", accountNumber = "0123456789012" },
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<AddedEmployee>())!.Id;
    }

    private static async Task<PayRunDetail> Start(Person by, World world, Company company)
    {
        var res = await by.Client.PostAsJsonAsync("/api/pay-runs", new { companyId = company.Id, payScheduleId = world.Schedule, month = "2026-10" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PayRunDetail>())!;
    }

    /// <summary>Asks for the run to be worked out, then runs the job the worker would.</summary>
    private async Task Calculate(Person by, World world, PayRunDetail run)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await by.Client.PostAsync($"/api/pay-runs/{run.Id}/calculate", null)).StatusCode);
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PayRunCalculation>().RunAsync(run.Id, world.OrgId, run.CompanyId, by.UserId, CancellationToken.None);
    }

    private static async Task<Page> Log(Person by, string query) => (await by.Client.GetFromJsonAsync<Page>($"/api/audit-log?{query}"))!;

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd");

    [Fact]
    public async Task A_payroll_worked_out_sent_and_approved_is_recorded_by_name_with_who_did_each_and_no_amounts()
    {
        var world = await Organization();
        var approver = await Colleague(world.OrgId, "approver", "Brian");
        Assert.Equal(HttpStatusCode.Created, (await world.Owner.Client.PostAsJsonAsync("/api/workflows", new
        {
            kind = "pay_run", companyId = (Guid?)null, allowSelfApproval = false,
            steps = new[] { new { approvers = "permission", permission = "pay_run_approvals.manage", fallback = "companies.manage" } },
        })).StatusCode);
        await AddEmployee(world.Owner, world.Westlands);
        var run = await Start(world.Owner, world, world.Westlands);
        await Calculate(world.Owner, world, run);
        var sent = (await (await world.Owner.Client.PostAsync($"/api/pay-runs/{run.Id}/submit", null)).Content.ReadFromJsonAsync<PayRunDetail>())!;
        Assert.Equal(HttpStatusCode.OK, (await approver.Client.PostAsync($"/api/approvals/{sent.ApprovalRequestId}/approve", null)).StatusCode);

        // "Who worked out payroll today?"
        var calculated = Assert.Single((await Log(world.Owner, $"action=pay_run.calculated&from={Today}&to={Today}")).Items);
        Assert.Equal(("action", world.Owner.UserId, "Amina Otieno", "pay_run", run.Id, run.ScheduleName, world.Westlands.Id),
            (calculated.Kind, calculated.UserId, calculated.UserName, calculated.SubjectType, calculated.SubjectId, calculated.SubjectName, calculated.CompanyId));
        Assert.Equal(1, calculated.Details!.Value.GetProperty("people").GetInt32());
        Assert.Equal("2026-10-01", calculated.Details.Value.GetProperty("periodStart").GetString());
        Assert.StartsWith("pay-run-calculation:", calculated.RequestRef);

        var actions = (await Log(world.Owner, $"kind=action&subjectId={run.Id}")).Items;
        Assert.Equal(["pay_run.approved", "pay_run.sent", "pay_run.calculated", "pay_run.started"], actions.Select(a => a.Action));   // newest first
        Assert.Equal(approver.UserId, actions[0].UserId);
        Assert.Equal("Brian Otieno", actions[0].UserName);

        // Never an amount, anywhere in the log: not the salary, not the run's totals.
        var everything = await world.Owner.Client.GetStringAsync("/api/audit-log?pageSize=100");
        Assert.DoesNotContain("85000", everything);
        Assert.DoesNotContain("0123456789012", everything);
        Assert.DoesNotContain("23456781", everything);
        Assert.DoesNotContain("A000000001Z", everything);

        // One record's history: the changes to the run and what was done to it.
        var history = (await world.Owner.Client.GetFromJsonAsync<List<Entry>>($"/api/audit-log/records/pay_runs/{run.Id}"))!;
        Assert.Contains(history, e => e is { Kind: "change", Action: "insert", TableName: "pay_runs" });
        Assert.Contains(history, e => e is { Kind: "action", Action: "pay_run.approved" });
        Assert.All(history.Where(e => e.Kind == "change" && e.NewValues is not null && e.NewValues.Value.TryGetProperty("net_pay", out _)),
            e => Assert.Equal("masked", e.NewValues!.Value.GetProperty("net_pay").GetString()));
    }

    [Fact]
    public async Task Pay_and_bank_changes_and_a_cutoff_switched_on_are_recorded_without_the_values()
    {
        var world = await Organization();
        var wanjiru = await AddEmployee(world.Owner, world.Westlands);
        var patch = await world.Owner.Client.PatchAsJsonAsync($"/api/employees/{wanjiru}", new
        {
            companyId = world.Westlands.Id, firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", job = new { employmentType = "permanent" },
            pay = new { basicAmount = 97000m, payBasis = "monthly" },
            account = new { method = "mpesa", mpesaPhone = "0791569999" },
        });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var midMonth = new
        {
            name = "Mid-month", frequency = "monthly", payDayRule = "fixed_day", payDay = 15, usesCutoff = true, cutoffDay = 10, allCompanies = true,
            companyIds = Array.Empty<Guid>(), defaultFor = Array.Empty<Guid>(),
        };
        var added = await world.Owner.Client.PostAsJsonAsync("/api/pay-schedules", midMonth);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var scheduleId = (await added.Content.ReadFromJsonAsync<AddedEmployee>())!.Id;
        Assert.Equal(HttpStatusCode.OK,
            (await world.Owner.Client.PatchAsJsonAsync($"/api/pay-schedules/{scheduleId}", midMonth with { usesCutoff = false })).StatusCode);

        var about = (await Log(world.Owner, $"kind=action&subjectId={wanjiru}")).Items;
        Assert.Equal(["employee.bank_changed", "employee.pay_changed", "employee.added"], about.Select(a => a.Action));
        Assert.Equal("Wanjiru Kamau", about[0].SubjectName);
        Assert.Equal("mpesa", about[0].Details!.Value.GetProperty("method").GetString());
        Assert.Equal(["staffNo", "validFrom"], about[1].Details!.Value.EnumerateObject().Select(p => p.Name).Order());

        var schedule = (await Log(world.Owner, $"kind=action&subjectId={scheduleId}")).Items;
        Assert.Equal(["pay_schedule.cutoff_off", "pay_schedule.changed", "pay_schedule.cutoff_on", "pay_schedule.added"], schedule.Select(a => a.Action));
        Assert.All(schedule, a => Assert.Equal(("Mid-month", (Guid?)null), (a.SubjectName, a.CompanyId)));

        var everything = await world.Owner.Client.GetStringAsync("/api/audit-log?pageSize=100");
        foreach (var secret in new[] { "97000", "85000", "0791569999", "0123456789012", "23456781", "A000000001Z" })
        {
            Assert.DoesNotContain(secret, everything);
        }
    }

    [Fact]
    public async Task The_summary_counts_the_entries_and_the_people_who_did_an_action()
    {
        var world = await Organization();
        var payroll = await Colleague(world.OrgId, "payroll", "Kevin");
        await AddEmployee(world.Owner, world.Westlands);
        await AddEmployee(world.Owner, world.Mombasa, "Akinyi", 2);
        await Calculate(world.Owner, world, await Start(world.Owner, world, world.Westlands));
        var mombasa = await Start(payroll, world, world.Mombasa);
        await Calculate(payroll, world, mombasa);
        await Calculate(payroll, world, mombasa);   // worked out again

        var summary = (await world.Owner.Client.GetFromJsonAsync<Summary>($"/api/audit-log/summary?action=pay_run.calculated&from={Today}&to={Today}"))!;
        Assert.Equal(new Summary(3, 2), summary);
        Assert.Equal(new Summary(0, 0), (await world.Owner.Client.GetFromJsonAsync<Summary>("/api/audit-log/summary?action=pay_run.calculated&from=2026-01-01&to=2026-01-31"))!);
        Assert.Equal(new Summary(2, 1), (await world.Owner.Client.GetFromJsonAsync<Summary>(
            $"/api/audit-log/summary?action=pay_run.calculated&userId={payroll.UserId}"))!);
    }

    [Fact]
    public async Task Only_people_with_audit_log_view_read_the_log_and_only_for_their_companies()
    {
        var world = await Organization();
        var westlandsAdmin = await Colleague(world.OrgId, "admin", "Kevin", world.Westlands.Id);
        // Kevin also does payroll for every company: he belongs to Mombasa, but may read the audit log only for Westlands.
        await Execute("""
            with g as (
                insert into security_groups (id, organization_id, name, all_companies) values (uuidv7(), @org, @name, true) returning id),
            gr as (
                insert into security_group_roles (organization_id, security_group_id, role_id) select @org, g.id, r.id from g, roles r where r.key = 'payroll' and r.organization_id is null)
            insert into security_group_members (id, organization_id, security_group_id, membership_id)
            select uuidv7(), @org, g.id, m.id from g, memberships m where m.user_id = @user and m.organization_id = @org
            """, ("org", world.OrgId), ("name", $"payroll {Guid.NewGuid():N}"), ("user", westlandsAdmin.UserId));
        var payroll = await Colleague(world.OrgId, "payroll", "Otieno");
        var atWestlands = await AddEmployee(world.Owner, world.Westlands);
        var atMombasa = await AddEmployee(world.Owner, world.Mombasa, "Akinyi", 2);

        foreach (var path in new[] { "/api/audit-log", "/api/audit-log/summary?action=employee.added", $"/api/audit-log/records/employees/{atWestlands}" })
        {
            var refused = await payroll.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.StartsWith("ACC-", (await refused.Content.ReadFromJsonAsync<Error>())!.Code);
        }

        // A Westlands-only admin sees Westlands, not Mombasa, and not what belongs to every company (schedules, the organization).
        var seen = (await Log(westlandsAdmin, "pageSize=100")).Items;
        Assert.Contains(seen, e => e.SubjectId == atWestlands || e.RecordId == atWestlands);
        Assert.DoesNotContain(seen, e => e.SubjectId == atMombasa || e.RecordId == atMombasa);
        Assert.All(seen, e => Assert.Equal(world.Westlands.Id, e.CompanyId));
        Assert.Empty(await westlandsAdmin.Client.GetFromJsonAsync<List<Entry>>($"/api/audit-log/records/employees/{atMombasa}") ?? []);
        Assert.Equal(1, (await westlandsAdmin.Client.GetFromJsonAsync<Summary>("/api/audit-log/summary?action=employee.added"))!.Entries);
        Assert.Equal(2, (await world.Owner.Client.GetFromJsonAsync<Summary>("/api/audit-log/summary?action=employee.added"))!.Entries);

        // Asking for Mombasa by name is refused, not quietly empty.
        Assert.Equal(HttpStatusCode.Forbidden, (await westlandsAdmin.Client.GetAsync($"/api/audit-log?companyId={world.Mombasa.Id}")).StatusCode);
        // Another organization sees none of it.
        var other = await Organization();
        Assert.DoesNotContain((await Log(other.Owner, "pageSize=100")).Items, e => e.SubjectId == atWestlands || e.RecordId == atWestlands);
    }
}
