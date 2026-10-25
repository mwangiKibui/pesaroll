using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pesaroll.Payroll;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// PAY-03 end to end: Payroll starts a company's monthly run, it is worked out (the job, run here
/// directly), checked and sent for approval through the organization's workflow; approved, it never
/// changes; declined, it comes back with the reason; a correction is a new run. Approvers see only
/// what waits for them, other companies and organizations see nothing, and every look at people's
/// pay is recorded.
/// </summary>
public sealed class PayRunsTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Person(HttpClient Client, Guid UserId);
    private sealed record World(Person Owner, Guid OrgId, Company Westlands, Company Mombasa, Guid Schedule);

    private const string Password = "Mombasa-raha-7";
    private const string October = "2026-10";
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email)> SignUp()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"run-{Guid.NewGuid():N}@example.com";
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
    private async Task<Person> Colleague(Guid orgId, string role, params Guid[] companies)
    {
        var (client, email) = await SignUp();
        await Execute("""
            with m as (
                insert into memberships (id, organization_id, user_id) select uuidv7(), @org, id from users where email = @email returning id),
            g as (
                insert into security_groups (id, organization_id, name, all_companies) values (uuidv7(), @org, @name, @all) returning id),
            gc as (
                insert into security_group_companies (organization_id, security_group_id, company_id) select @org, g.id, unnest(@companies) from g),
            gr as (
                insert into security_group_roles (organization_id, security_group_id, role_id) select @org, g.id, r.id from g, roles r where r.key = @role and r.organization_id is null)
            insert into security_group_members (id, organization_id, security_group_id, membership_id) select uuidv7(), @org, g.id, m.id from g, m
            """, ("org", orgId), ("email", email), ("name", $"{role} {Guid.NewGuid():N}"), ("all", companies.Length == 0), ("companies", companies), ("role", role));
        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        return new Person(client, await UserId(email));
    }

    /// <summary>An employee at the company on its default schedule, paid this monthly basic pay since 2024.</summary>
    private static async Task<Guid> Employee(World world, Company company, decimal basic, string name = "Brian Mwangi", string basis = "monthly",
        DateOnly? hired = null)
    {
        var id = Guid.CreateVersion7();
        var parts = name.Split(' ');
        var from = hired ?? new DateOnly(2024, 1, 15);
        await Execute("""
            insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
            values (@id, @org, @co, @staff, @first, @last, @from);
            insert into employments (id, organization_id, company_id, employee_id, valid_from, pay_schedule_id, leave_plan_id, employment_type)
            select uuidv7(), @org, @co, @id, @from, c.default_pay_schedule_id, c.default_leave_plan_id, 'permanent' from companies c where c.id = @co;
            insert into compensations (id, organization_id, company_id, employee_id, valid_from, basic_amount, pay_basis, currency)
            values (uuidv7(), @org, @co, @id, @from, @basic, @basis, 'KES');
            """, ("id", id), ("org", world.OrgId), ("co", company.Id), ("staff", $"E{Random.Shared.Next(10000, 99999)}"), ("first", parts[0]),
            ("last", parts[1]), ("from", from), ("basic", basic), ("basis", basis));
        return id;
    }

    /// <summary>A deduction the organization set up, given to one person every month from January.</summary>
    private static Task Deduction(World world, Company company, Guid employee, decimal amount) => Execute("""
        with item as (
            insert into pay_items (id, organization_id, code, name, kind, calc_method, all_companies)
            values (uuidv7(), @org, @code, 'Staff loan', 'deduction', 'fixed', true) returning id)
        insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on)
        select uuidv7(), @org, @co, item.id, @emp, @amount, '2026-01-01' from item
        """, ("org", world.OrgId), ("code", $"LOAN{Random.Shared.Next(100, 999)}"), ("co", company.Id), ("emp", employee), ("amount", amount));

    private static async Task<string> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!.Code;

    private static async Task<PayRunDetail> Start(World world, Company? company = null, string month = October)
    {
        var res = await world.Owner.Client.PostAsJsonAsync("/api/pay-runs", new { companyId = (company ?? world.Westlands).Id, payScheduleId = world.Schedule, month });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PayRunDetail>())!;
    }

    /// <summary>Asks for the run to be worked out, then runs the job the worker would.</summary>
    private async Task<PayRunDetail> Calculate(World world, Guid run, Person? by = null)
    {
        var res = await (by ?? world.Owner).Client.PostAsync($"/api/pay-runs/{run}/calculate", null);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal("calculating", (await res.Content.ReadFromJsonAsync<PayRunDetail>())!.Status);
        var detail = await Get(world.Owner, run);
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PayRunCalculation>()
            .RunAsync(run, world.OrgId, detail.CompanyId, (by ?? world.Owner).UserId, CancellationToken.None);
        return await Get(world.Owner, run);
    }

    private static async Task<PayRunDetail> Get(Person person, Guid run) => (await person.Client.GetFromJsonAsync<PayRunDetail>($"/api/pay-runs/{run}"))!;

    private static async Task<PayRunPeoplePage> People(Person person, Guid run) =>
        (await person.Client.GetFromJsonAsync<PayRunPeoplePage>($"/api/pay-runs/{run}/people"))!;

    private static Task<HttpResponseMessage> Submit(Person person, Guid run) => person.Client.PostAsync($"/api/pay-runs/{run}/submit", null);

    private static async Task SetUpApprovals(World world) =>
        Assert.Equal(HttpStatusCode.Created, (await world.Owner.Client.PostAsJsonAsync("/api/workflows", new
        {
            kind = "pay_run", companyId = (Guid?)null, allowSelfApproval = false,
            steps = new[] { new { approvers = "permission", permission = "pay_run_approvals.manage", fallback = "companies.manage" } },
        })).StatusCode);

    /// <summary>A run for October at Westlands, worked out and sent, with an approver waiting.</summary>
    private async Task<(World World, Person Approver, PayRunDetail Run, Guid Employee)> Sent()
    {
        var world = await Organization();
        var approver = await Colleague(world.OrgId, "approver");
        await SetUpApprovals(world);
        var employee = await Employee(world, world.Westlands, 100_000);
        var run = await Start(world);
        await Calculate(world, run.Id);
        var sent = await Submit(world.Owner, run.Id);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        return (world, approver, (await sent.Content.ReadFromJsonAsync<PayRunDetail>())!, employee);
    }

    [Fact]
    public async Task A_run_is_worked_out_sent_approved_and_then_never_changes()
    {
        var (world, approver, run, employee) = await Sent();

        Assert.Equal("awaiting_approval", run.Status);
        Assert.NotNull(run.ApprovalRequestId);
        Assert.Equal(new DateOnly(2026, 10, 30), run.PayDate);   // last working day: 31 October 2026 is a Saturday
        Assert.Equal("KE-2026.02", run.RulePackVersion);
        Assert.Equal(1, run.Totals.People);
        Assert.Equal(100_000m, run.Totals.GrossPay);
        Assert.Equal(19_308.35m, run.Totals.Paye);
        Assert.Equal(70_441.65m, run.Totals.NetPay);

        // The approver sees what waits for them, with each person's pay, and approves it.
        var people = await People(approver, run.Id);
        var brian = Assert.Single(people.Items);
        Assert.Equal(employee, brian.EmployeeId);
        Assert.Equal(6_000m, brian.NssfEmployee);
        Assert.Equal(2_750m, brian.Shif);
        Assert.Equal(1_500m, brian.HousingLevyEmployee);
        Assert.Equal(19_308.35m, brian.Paye);
        Assert.Equal(70_441.65m, brian.NetPay);
        Assert.Contains(brian.Lines, l => l.Code == "PAYE" && l.StatutoryKey == "paye");
        Assert.Equal(HttpStatusCode.OK, (await approver.Client.PostAsync($"/api/approvals/{run.ApprovalRequestId}/approve", null)).StatusCode);

        var approved = await Get(world.Owner, run.Id);
        Assert.Equal("approved", approved.Status);
        Assert.NotNull(approved.ApprovedAt);
        Assert.Equal("PAY-2014", await Code(await world.Owner.Client.PostAsync($"/api/pay-runs/{run.Id}/calculate", null)));
        Assert.Equal("PAY-2014", await Code(await world.Owner.Client.PostAsync($"/api/pay-runs/{run.Id}/cancel", null)));
        Assert.Equal("PAY-2014", await Code(await Submit(world.Owner, run.Id)));
        Assert.Equal(1, await Scalar<long>("select count(*) from pay_run_people where pay_run_id = @r", ("r", run.Id)));
    }

    [Fact]
    public async Task Looking_at_peoples_pay_is_recorded_without_the_amounts()
    {
        var (world, approver, run, employee) = await Sent();

        await People(approver, run.Id);

        Assert.Equal(1, await Scalar<long>("select count(*) from sensitive_views where user_id = @u and kind = 'employee_pay' and subject_ids = array[@e]",
            ("u", approver.UserId), ("e", employee)));
    }

    [Fact]
    public async Task A_declined_run_comes_back_with_the_reason_and_can_be_sent_again()
    {
        var (world, approver, run, _) = await Sent();
        Assert.Equal("PAY-2021", await Code(await world.Owner.Client.PostAsync($"/api/pay-runs/{run.Id}/calculate", null)));

        await approver.Client.PostAsJsonAsync($"/api/approvals/{run.ApprovalRequestId}/decline", new { reason = "Overtime for Kisumu is missing" });

        var back = await Get(world.Owner, run.Id);
        Assert.Equal("calculated", back.Status);
        Assert.Equal("Overtime for Kisumu is missing", back.DeclineReason);
        // Back in Payroll's hands: the approver no longer sees it.
        Assert.Equal(HttpStatusCode.NotFound, (await approver.Client.GetAsync($"/api/pay-runs/{run.Id}")).StatusCode);

        await Calculate(world, run.Id);
        var again = (await (await Submit(world.Owner, run.Id)).Content.ReadFromJsonAsync<PayRunDetail>())!;
        Assert.Equal("awaiting_approval", again.Status);
        Assert.Null(again.DeclineReason);
        Assert.NotEqual(run.ApprovalRequestId, again.ApprovalRequestId);
        Assert.Equal(HttpStatusCode.OK, (await approver.Client.PostAsync($"/api/approvals/{again.ApprovalRequestId}/approve", null)).StatusCode);
        Assert.Equal("approved", (await Get(world.Owner, run.Id)).Status);
    }

    [Fact]
    public async Task One_regular_run_per_month_and_monthly_schedules_only()
    {
        var world = await Organization();
        var run = await Start(world);

        var again = await world.Owner.Client.PostAsJsonAsync("/api/pay-runs", new { companyId = world.Westlands.Id, payScheduleId = world.Schedule, month = October });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("PAY-2012", await Code(again));
        // Another company, or once the first is cancelled, is fine.
        await Start(world, world.Mombasa);
        Assert.Equal(HttpStatusCode.OK, (await world.Owner.Client.PostAsync($"/api/pay-runs/{run.Id}/cancel", null)).StatusCode);
        await Start(world);

        var weekly = await Scalar<Guid>("""
            insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, pay_weekday, cutoff_days_before, all_companies)
            values (uuidv7(), @org, 'Weekly', 'weekly', null, null, 5, 2, true) returning id
            """, ("org", world.OrgId));
        Assert.Equal("PAY-2013", await Code(await world.Owner.Client.PostAsJsonAsync("/api/pay-runs",
            new { companyId = world.Westlands.Id, payScheduleId = weekly, month = October })));

        var bad = (await (await world.Owner.Client.PostAsJsonAsync("/api/pay-runs",
            new { companyId = world.Westlands.Id, payScheduleId = Guid.CreateVersion7(), month = "October" })).Content.ReadFromJsonAsync<Error>())!;
        Assert.Equal("PAY-2020", bad.Code);
        Assert.Equal(["month", "payScheduleId"], bad.Fields!.Keys.Order());
    }

    [Fact]
    public async Task Only_a_worked_out_run_with_nobody_below_zero_and_a_workflow_can_be_sent()
    {
        var world = await Organization();
        var run = await Start(world);
        Assert.Equal("PAY-2015", await Code(await Submit(world.Owner, run.Id)));

        // Nobody on the schedule: nobody to pay.
        await Calculate(world, run.Id);
        Assert.Equal("PAY-2017", await Code(await Submit(world.Owner, run.Id)));

        var brian = await Employee(world, world.Westlands, 50_000);
        await Deduction(world, world.Westlands, brian, 60_000);
        var worked = await Calculate(world, run.Id);
        Assert.Contains("negative_net", Assert.Single(worked.Flagged).Notes);
        Assert.Equal("PAY-2019", await Code(await Submit(world.Owner, run.Id)));

        await Execute("delete from employee_pay_items where employee_id = @e", ("e", brian));
        await Calculate(world, run.Id);
        Assert.Equal("WF-5005", await Code(await Submit(world.Owner, run.Id)));
        Assert.Equal("calculated", (await Get(world.Owner, run.Id)).Status);   // not left waiting for nobody
    }

    [Fact]
    public async Task People_it_cant_pay_are_listed_with_the_reason()
    {
        var world = await Organization();
        await Employee(world, world.Westlands, 100_000);
        await Employee(world, world.Westlands, 1_000, name: "Peter Kamau", basis: "daily");
        await Employee(world, world.Westlands, 100_000, name: "Grace Njeri", hired: new DateOnly(2026, 10, 19));
        var run = await Start(world);

        var worked = await Calculate(world, run.Id);

        Assert.Equal(2, worked.Totals.People);
        Assert.Equal("not_monthly", Assert.Single(worked.LeftOut).Reason);
        Assert.Equal(["joined"], worked.Flagged.Single(f => f.Name == "Grace Njeri").Notes);
    }

    [Fact]
    public async Task No_rule_pack_for_the_pay_date_leaves_a_draft_saying_so()
    {
        var world = await Organization();
        await Employee(world, world.Westlands, 100_000);
        var run = await Start(world, month: "2020-01");

        var worked = await Calculate(world, run.Id);

        Assert.Equal("draft", worked.Status);
        Assert.Equal("PAY-2010", worked.CalculationError);
    }

    [Fact]
    public async Task A_correction_is_a_new_run_for_the_people_chosen_showing_what_changes()
    {
        var (world, approver, run, brian) = await Sent();
        var amina = await Employee(world, world.Westlands, 80_000, name: "Amina Wanjiru");
        Assert.Equal("PAY-2018", await Code(await world.Owner.Client.PostAsJsonAsync($"/api/pay-runs/{run.Id}/corrections", new { employeeIds = new[] { brian } })));
        await approver.Client.PostAsync($"/api/approvals/{run.ApprovalRequestId}/approve", null);
        // Brian's pay for October should have been 110,000.
        await Execute("update compensations set basic_amount = 110000 where employee_id = @e", ("e", brian));

        Assert.Equal("PAY-2020", await Code(await world.Owner.Client.PostAsJsonAsync($"/api/pay-runs/{run.Id}/corrections", new { employeeIds = Array.Empty<Guid>() })));
        var res = await world.Owner.Client.PostAsJsonAsync($"/api/pay-runs/{run.Id}/corrections", new { employeeIds = new[] { brian } });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var correction = (await res.Content.ReadFromJsonAsync<PayRunDetail>())!;
        Assert.Equal("correction", correction.Kind);
        Assert.Equal(run.Id, correction.CorrectsPayRunId);
        Assert.Equal(run.PeriodStart, correction.PeriodStart);

        await Calculate(world, correction.Id);
        var person = Assert.Single((await People(world.Owner, correction.Id)).Items);   // Amina isn't in it
        Assert.Equal(brian, person.EmployeeId);
        Assert.Equal(110_000m, person.GrossPay);
        Assert.Equal(run.Id, person.ApprovedBefore!.PayRunId);
        Assert.Equal(70_441.65m, person.ApprovedBefore.NetPay);
        Assert.Equal(person.NetPay - 70_441.65m, person.ApprovedBefore.NetPayChange);
        Assert.True(person.ApprovedBefore.NetPayChange > 0);
        // The approved run is as it was.
        Assert.Equal(100_000m, (await Get(world.Owner, run.Id)).Totals.GrossPay);
        Assert.NotEqual(amina, person.EmployeeId);
    }

    [Fact]
    public async Task Approvers_never_see_drafts_and_other_companies_and_organizations_see_nothing()
    {
        var world = await Organization();
        var approver = await Colleague(world.OrgId, "approver");
        var mombasaPayroll = await Colleague(world.OrgId, "payroll", world.Mombasa.Id);
        var other = await Organization();
        await Employee(world, world.Westlands, 100_000);
        var run = await Start(world);
        await Calculate(world, run.Id);

        foreach (var person in new[] { approver, mombasaPayroll, other.Owner })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await person.Client.GetAsync($"/api/pay-runs/{run.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await person.Client.GetAsync($"/api/pay-runs/{run.Id}/people")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await mombasaPayroll.Client.GetAsync($"/api/pay-runs?companyId={world.Westlands.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.Client.PostAsJsonAsync("/api/pay-runs",
            new { companyId = world.Westlands.Id, payScheduleId = world.Schedule, month = "2026-11" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mombasaPayroll.Client.PostAsync($"/api/pay-runs/{run.Id}/calculate", null)).StatusCode);

        // Payroll for Mombasa runs Mombasa's payroll.
        var res = await mombasaPayroll.Client.PostAsJsonAsync("/api/pay-runs", new { companyId = world.Mombasa.Id, payScheduleId = world.Schedule, month = October });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var list = (await mombasaPayroll.Client.GetFromJsonAsync<PayRunPage>($"/api/pay-runs?companyId={world.Mombasa.Id}"))!;
        Assert.Equal("Monthly", Assert.Single(list.Items).ScheduleName);
    }
}
