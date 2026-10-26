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

    /// <summary>An employee at the company on its default schedule (or the one given), paid this basic pay since 2024.</summary>
    private static async Task<Guid> Employee(World world, Company company, decimal basic, string name = "Brian Mwangi", string basis = "monthly",
        DateOnly? hired = null, Guid? schedule = null, decimal? usual = null)
    {
        var id = Guid.CreateVersion7();
        var parts = name.Split(' ');
        var from = hired ?? new DateOnly(2024, 1, 15);
        await Execute("""
            insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
            values (@id, @org, @co, @staff, @first, @last, @from);
            insert into employments (id, organization_id, company_id, employee_id, valid_from, pay_schedule_id, leave_plan_id, employment_type)
            select uuidv7(), @org, @co, @id, @from, coalesce(@schedule, c.default_pay_schedule_id), c.default_leave_plan_id, 'permanent' from companies c where c.id = @co;
            insert into compensations (id, organization_id, company_id, employee_id, valid_from, basic_amount, pay_basis, currency, usual_quantity)
            values (uuidv7(), @org, @co, @id, @from, @basic, @basis, 'KES', @usual);
            """, ("id", id), ("org", world.OrgId), ("co", company.Id), ("staff", $"E{Random.Shared.Next(10000, 99999)}"), ("first", parts[0]),
            ("last", parts[1]), ("from", from), ("basic", basic), ("basis", basis),
            ("schedule", schedule is Guid s ? s : DBNull.Value), ("usual", usual is decimal u ? u : DBNull.Value));
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
    public async Task One_regular_run_per_month()
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
        await Employee(world, world.Westlands, 25_000, name: "John Otieno", basis: "weekly");
        var run = await Start(world);

        var worked = await Calculate(world, run.Id);

        Assert.Equal(2, worked.Totals.People);
        Assert.Equal("no_time", worked.LeftOut.Single(l => l.Name == "Peter Kamau").Reason);    // a daily rate with no days entered
        Assert.Equal("other_basis", worked.LeftOut.Single(l => l.Name == "John Otieno").Reason);
        Assert.Equal(["joined", "prorated"], worked.Flagged.Single(f => f.Name == "Grace Njeri").Notes);
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
            Assert.Equal(HttpStatusCode.NotFound, (await person.Client.GetAsync($"/api/pay-runs/{run.Id}/inputs")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await mombasaPayroll.Client.PutAsJsonAsync($"/api/pay-runs/{run.Id}/inputs", new { entries = Array.Empty<object>() })).StatusCode);
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

    // PAY-10: weekly, fortnightly and daily runs.

    private static Task<Guid> Schedule(World world, string frequency) => Scalar<Guid>(frequency switch
    {
        "weekly" => """
            insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, pay_weekday, cutoff_days_before, all_companies)
            values (uuidv7(), @org, 'Weekly casuals', 'weekly', null, null, 5, 2, true) returning id
            """,
        _ => """
            insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, pay_time, uses_cutoff, cutoff_applies_to, all_companies)
            values (uuidv7(), @org, 'Daily casuals', 'daily', null, null, '17:00', false, '{}', true) returning id
            """,
    }, ("org", world.OrgId));

    private static Task<HttpResponseMessage> StartOn(World world, Guid schedule, string payDate, Company? company = null) =>
        world.Owner.Client.PostAsJsonAsync("/api/pay-runs", new { companyId = (company ?? world.Westlands).Id, payScheduleId = schedule, payDate });

    private static async Task<PayRunDetail> Started(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PayRunDetail>())!;
    }

    /// <summary>Moves the company's main location to a wage area (companies here start in Nairobi, a city).</summary>
    private static Task WorksIn(Company company, string area) => Execute(
        "update locations set wage_area_code = @a where id = (select main_location_id from companies where id = @c)", ("a", area), ("c", company.Id));

    private static Task<HttpResponseMessage> SetDeductions(Person by, Guid schedule, object choice) =>
        by.Client.PostAsJsonAsync($"/api/pay-schedules/{schedule}/deductions", choice);

    private static readonly string[] Every = ["paye", "shif", "nssf", "housing_levy", "nita"];

    [Fact]
    public async Task Weekly_runs_are_paid_in_order_and_a_month_of_them_adds_up_to_monthly_payroll()
    {
        var world = await Organization();
        var approver = await Colleague(world.OrgId, "approver");
        await SetUpApprovals(world);
        var weekly = await Schedule(world, "weekly");
        Assert.Equal(HttpStatusCode.OK, (await SetDeductions(world.Owner, weekly,
            new { makesDeductions = true, items = Every, appliesTo = "everyone", startsOn = "2026-09-01" })).StatusCode);
        var peter = await Employee(world, world.Westlands, 25_000, name: "Peter Kamau", basis: "weekly", schedule: weekly);

        var bad = await StartOn(world, weekly, "2026-10-08");   // a Thursday
        Assert.Equal("PAY-2022", await Code(bad));
        var first = await Started(await StartOn(world, weekly, "2026-10-02"));
        Assert.Equal(new DateOnly(2026, 9, 26), first.PeriodStart);   // counts in October, its pay date's month
        Assert.Equal("PAY-2012", await Code(await StartOn(world, weekly, "2026-10-02")));

        // The second week waits for the first to be approved.
        var second = await Started(await StartOn(world, weekly, "2026-10-09"));
        Assert.Equal("PAY-2023", await Code(await world.Owner.Client.PostAsync($"/api/pay-runs/{second.Id}/calculate", null)));

        var runs = new List<PayRunDetail>();
        foreach (var run in new[] { first, second }.Concat([await Started(await StartOn(world, weekly, "2026-10-16")),
                     await Started(await StartOn(world, weekly, "2026-10-23"))]))
        {
            await Calculate(world, run.Id);
            var sent = (await (await Submit(world.Owner, run.Id)).Content.ReadFromJsonAsync<PayRunDetail>())!;
            Assert.Equal(HttpStatusCode.OK, (await approver.Client.PostAsync($"/api/approvals/{sent.ApprovalRequestId}/approve", null)).StatusCode);
            runs.Add(await Get(world.Owner, run.Id));
        }

        Assert.All(runs, r => Assert.Equal(25_000m, r.Totals.GrossPay));
        Assert.Equal(6_000m, runs.Sum(r => r.Totals.NssfEmployee));
        Assert.Equal(2_750m, runs.Sum(r => r.Totals.Shif));
        Assert.Equal(1_500m, runs.Sum(r => r.Totals.HousingLevyEmployee));
        Assert.Equal(19_308.35m, runs.Sum(r => r.Totals.Paye));
        Assert.Equal(70_441.65m, runs.Sum(r => r.Totals.NetPay));
        // Each statutory line keeps the month to date and what earlier weeks took: NSSF is 6% of 50,000 by week two.
        Assert.Equal("3000.00 1500.00 1500.00", await Scalar<string>("""
            select concat_ws(' ', month_to_date, earlier, amount) from pay_run_lines where pay_run_id = @r and statutory_key = 'nssf_employee'
            """, ("r", runs[1].Id)));
        Assert.Equal(peter, (await People(world.Owner, runs[0].Id)).Items.Single().EmployeeId);
    }

    private sealed record DeductionChoice(bool MakesDeductions, string[] Items, string AppliesTo, Guid[] EmployeeIds, DateOnly? StartsOn, Guid? SetBy);
    private sealed record ScheduleDeductions(Guid PayScheduleId, string Frequency, DeductionChoice Current, DeductionChoice[] History);

    [Fact]
    public async Task A_daily_schedule_makes_no_deductions_until_switched_on_and_a_person_can_differ()
    {
        var world = await Organization();
        await WorksIn(world.Westlands, "other");   // 450 a day is above the minimum wage there, not in a city
        var daily = await Schedule(world, "daily");
        var peter = await Employee(world, world.Westlands, 450, name: "Peter Kamau", basis: "daily", schedule: daily);
        var mary = await Employee(world, world.Westlands, 450, name: "Mary Akinyi", basis: "daily", schedule: daily);

        // Off by default.
        var setting = (await world.Owner.Client.GetFromJsonAsync<ScheduleDeductions>($"/api/pay-schedules/{daily}/deductions"))!;
        Assert.False(setting.Current.MakesDeductions);
        Assert.Empty(setting.History);
        var worked = await Calculate(world, (await Started(await StartOn(world, daily, "2026-10-01"))).Id);
        Assert.Equal(900m, worked.Totals.NetPay);
        Assert.All(worked.Flagged, f => Assert.Contains("no_deductions", f.Notes));

        // Switched on for SHIF and the Housing Levy; Mary asks for SHIF only.
        Assert.Equal(HttpStatusCode.OK, (await SetDeductions(world.Owner, daily,
            new { makesDeductions = true, items = new[] { "shif", "housing_levy" }, appliesTo = "everyone", startsOn = "2026-10-02" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await world.Owner.Client.PostAsJsonAsync($"/api/companies/{world.Westlands.Id}/employees/{mary}/deductions",
            new { items = new[] { "shif" }, startsOn = "2026-10-02" })).StatusCode);
        setting = (await world.Owner.Client.GetFromJsonAsync<ScheduleDeductions>($"/api/pay-schedules/{daily}/deductions"))!;
        Assert.Equal(["housing_levy", "shif"], setting.Current.Items.Order());
        Assert.Equal(world.Owner.UserId, setting.Current.SetBy);

        var approver = await Colleague(world.OrgId, "approver");
        await SetUpApprovals(world);
        var day1 = (await Get(world.Owner, worked.Id));
        var sent = (await (await Submit(world.Owner, day1.Id)).Content.ReadFromJsonAsync<PayRunDetail>())!;
        await approver.Client.PostAsync($"/api/approvals/{sent.ApprovalRequestId}/approve", null);
        var day2 = await Calculate(world, (await Started(await StartOn(world, daily, "2026-10-02"))).Id);

        var people = (await People(world.Owner, day2.Id)).Items;
        var p = people.Single(x => x.EmployeeId == peter);
        Assert.Equal(300m, p.Shif);   // the month's minimum, on the first day it is made
        Assert.Equal(13.50m, p.HousingLevyEmployee);   // 1.5% of the 900 paid so far this month
        var m = people.Single(x => x.EmployeeId == mary);
        Assert.Equal(300m, m.Shif);
        Assert.Equal(0m, m.HousingLevyEmployee);
        Assert.Contains("some_deductions", m.Notes);
        Assert.Equal(world.Owner.UserId, await Scalar<Guid>("select deductions_chosen_by from pay_run_people where pay_run_id = @r and employee_id = @e",
            ("r", day2.Id), ("e", mary)));
        Assert.Equal(new[] { "shif" }, await Scalar<string[]>("select deductions_made from pay_run_people where pay_run_id = @r and employee_id = @e",
            ("r", day2.Id), ("e", mary)));
    }

    [Fact]
    public async Task Deductions_can_be_for_chosen_people_only()
    {
        var world = await Organization();
        var weekly = await Schedule(world, "weekly");
        var peter = await Employee(world, world.Westlands, 25_000, name: "Peter Kamau", basis: "weekly", schedule: weekly);
        var mary = await Employee(world, world.Westlands, 25_000, name: "Mary Akinyi", basis: "weekly", schedule: weekly);
        Assert.Equal(HttpStatusCode.OK, (await SetDeductions(world.Owner, weekly,
            new { makesDeductions = true, items = Every, appliesTo = "chosen", employeeIds = new[] { peter }, startsOn = "2026-10-01" })).StatusCode);

        var worked = await Calculate(world, (await Started(await StartOn(world, weekly, "2026-10-02"))).Id);

        var people = (await People(world.Owner, worked.Id)).Items;
        Assert.Equal(687.50m, people.Single(x => x.EmployeeId == peter).Shif);   // 2.75% of 25,000
        Assert.Equal(25_000m, people.Single(x => x.EmployeeId == mary).NetPay);
    }

    [Fact]
    public async Task Deduction_settings_are_checked()
    {
        var world = await Organization();
        var weekly = await Schedule(world, "weekly");
        var peter = await Employee(world, world.Westlands, 25_000, name: "Peter Kamau", basis: "weekly", schedule: weekly);

        async Task<Error> Refused(object choice, Guid? schedule = null) =>
            (await (await SetDeductions(world.Owner, schedule ?? weekly, choice)).Content.ReadFromJsonAsync<Error>())!;
        var monthly = await Refused(new { makesDeductions = false, items = Array.Empty<string>(), appliesTo = "everyone", startsOn = "2026-10-01" }, world.Schedule);
        Assert.Equal("PAY-2024", monthly.Code);   // monthly payroll makes every deduction
        var empty = await Refused(new { makesDeductions = true, items = Array.Empty<string>(), appliesTo = "everyone", startsOn = "2026-10-01" });
        Assert.Equal("PAY-2024", empty.Code);
        Assert.Contains("items", empty.Fields!.Keys);
        var unknown = await Refused(new { makesDeductions = true, items = new[] { "nhif" }, appliesTo = "chosen", employeeIds = Array.Empty<Guid>() });
        Assert.Equal(["employeeIds", "items", "startsOn"], unknown.Fields!.Keys.Order());
        var other = await Organization();
        var stranger = await Refused(new { makesDeductions = true, items = Every, appliesTo = "chosen", employeeIds = new[] { await Employee(other, other.Westlands, 1) },
            startsOn = "2026-10-01" });
        Assert.Contains("employeeIds", stranger.Fields!.Keys);
        Assert.Equal("PAY-2024", (await (await world.Owner.Client.PostAsJsonAsync($"/api/companies/{world.Westlands.Id}/employees/{peter}/deductions",
            new { items = new[] { "pension" }, startsOn = "2026-10-01" })).Content.ReadFromJsonAsync<Error>())!.Code);

        // Payroll for Mombasa can't change Westlands people or the organization's schedules; another organization sees nothing.
        var mombasaPayroll = await Colleague(world.OrgId, "payroll", world.Mombasa.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetDeductions(mombasaPayroll, weekly,
            new { makesDeductions = false, items = Array.Empty<string>(), appliesTo = "everyone", startsOn = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mombasaPayroll.Client.PostAsJsonAsync($"/api/companies/{world.Westlands.Id}/employees/{peter}/deductions",
            new { items = Array.Empty<string>(), startsOn = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.Client.GetAsync($"/api/pay-schedules/{weekly}/deductions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.Client.PostAsJsonAsync($"/api/companies/{other.Westlands.Id}/employees/{peter}/deductions",
            new { items = Array.Empty<string>(), startsOn = "2026-10-01" })).StatusCode);
    }

    private sealed record ExampleLine(string Key, decimal Amount);
    private sealed record ExampleRun(int Number, decimal Pay, ExampleLine[] Deductions, decimal TakeHome);
    private sealed record Example(string Frequency, decimal Wage, int Runs, ExampleRun[] EachRun, decimal Pay, ExampleLine[] Deductions, decimal TakeHome,
        ExampleLine[] EmployerCosts);

    [Fact]
    public async Task The_example_shows_a_month_of_pay_with_the_deductions_chosen()
    {
        var world = await Organization();

        var res = await world.Owner.Client.PostAsJsonAsync("/api/pay-schedules/deductions-example",
            new { frequency = "daily", wage = 450, items = new[] { "shif", "housing_levy" } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var example = (await res.Content.ReadFromJsonAsync<Example>())!;

        Assert.Equal(22, example.Runs);
        Assert.Equal(9_900m, example.Pay);
        Assert.Equal(300m, example.Deductions.Single(d => d.Key == "shif").Amount);
        Assert.Equal(148.50m, example.Deductions.Single(d => d.Key == "ahl_employee").Amount);
        Assert.DoesNotContain(example.Deductions, d => d.Key == "paye");
        Assert.Equal(9_451.50m, example.TakeHome);
        Assert.Equal(300m, example.EachRun[0].Deductions.Single(d => d.Key == "shif").Amount);   // the minimum, on the first day
        Assert.Equal(example.TakeHome, example.EachRun.Sum(r => r.TakeHome));

        var weekly = (await (await world.Owner.Client.PostAsJsonAsync("/api/pay-schedules/deductions-example",
            new { frequency = "weekly", wage = 25_000, items = Every })).Content.ReadFromJsonAsync<Example>())!;
        Assert.Equal(19_308.35m, weekly.Deductions.Single(d => d.Key == "paye").Amount);
        Assert.Equal(70_441.65m, weekly.TakeHome);

        var bad = (await (await world.Owner.Client.PostAsJsonAsync("/api/pay-schedules/deductions-example",
            new { frequency = "monthly", wage = 0, items = new[] { "nhif" } })).Content.ReadFromJsonAsync<Error>())!;
        Assert.Equal("PAY-2024", bad.Code);
        Assert.Equal(["frequency", "items", "wage"], bad.Fields!.Keys.Order());
    }

    [Fact]
    public async Task A_correction_replaces_its_week_in_the_month_to_date()
    {
        var world = await Organization();
        var approver = await Colleague(world.OrgId, "approver");
        await SetUpApprovals(world);
        var weekly = await Schedule(world, "weekly");
        await SetDeductions(world.Owner, weekly, new { makesDeductions = true, items = Every, appliesTo = "everyone", startsOn = "2026-09-01" });
        var peter = await Employee(world, world.Westlands, 25_000, name: "Peter Kamau", basis: "weekly", schedule: weekly);
        async Task<PayRunDetail> Approved(Guid run)
        {
            await Calculate(world, run);
            var sent = (await (await Submit(world.Owner, run)).Content.ReadFromJsonAsync<PayRunDetail>())!;
            Assert.Equal(HttpStatusCode.OK, (await approver.Client.PostAsync($"/api/approvals/{sent.ApprovalRequestId}/approve", null)).StatusCode);
            return await Get(world.Owner, run);
        }
        var week1 = await Approved((await Started(await StartOn(world, weekly, "2026-10-02"))).Id);
        await Approved((await Started(await StartOn(world, weekly, "2026-10-09"))).Id);

        // The first week should have been 30,000.
        await Execute("update compensations set basic_amount = 30000 where employee_id = @e", ("e", peter));
        var correction = (await (await world.Owner.Client.PostAsJsonAsync($"/api/pay-runs/{week1.Id}/corrections", new { employeeIds = new[] { peter } }))
            .Content.ReadFromJsonAsync<PayRunDetail>())!;
        await Calculate(world, correction.Id);

        // NSSF on the month's 55,000 is 3,300; the second week took 1,500, so the first week's is now 1,800 (it was 1,500).
        var line = (await People(world.Owner, correction.Id)).Items.Single().Lines.Single(l => l.StatutoryKey == "nssf_employee");
        Assert.Equal(3_300m, line.MonthToDate);
        Assert.Equal(1_500m, line.Earlier);
        Assert.Equal(1_800m, line.Amount);
    }

    // PAY-11: pay rates, days and hours.

    private static Task<HttpResponseMessage> EnterTime(Person by, Guid run, params object[] entries) =>
        by.Client.PutAsJsonAsync($"/api/pay-runs/{run}/inputs", new { entries });

    private static async Task<RunTimeResponse> Time(Person by, Guid run) => (await by.Client.GetFromJsonAsync<RunTimeResponse>($"/api/pay-runs/{run}/inputs"))!;

    private static PayRunPersonResponse Paid(PayRunPeoplePage page, Guid employee) => page.Items.Single(p => p.EmployeeId == employee);

    [Fact]
    public async Task Days_and_hours_are_entered_paid_at_each_persons_rate_and_fixed_once_sent()
    {
        var world = await Organization();
        await WorksIn(world.Westlands, "other");
        var weekly = await Schedule(world, "weekly");
        var peter = await Employee(world, world.Westlands, 800, name: "Peter Kamau", basis: "daily", schedule: weekly);
        var mary = await Employee(world, world.Westlands, 150, name: "Mary Akinyi", basis: "hourly", schedule: weekly, usual: 40);
        var john = await Employee(world, world.Westlands, 25_000, name: "John Otieno", basis: "weekly", schedule: weekly);
        var run = await Started(await StartOn(world, weekly, "2026-10-02"));

        // Who the run pays and what can be entered: the usual rates come with the organization.
        var time = await Time(world.Owner, run.Id);
        Assert.Equal(["day", "hour", null], new[] { peter, mary, john }.Select(id => time.People.Single(p => p.EmployeeId == id).Unit));
        Assert.Equal(40m, time.People.Single(p => p.EmployeeId == mary).UsualQuantity);
        var overtime = time.Items.Single(i => i.Code == "OVERTIME");
        Assert.Equal("hour", overtime.Unit);
        Assert.Contains(time.Items, i => i.Code == "REST-DAY");

        // A salary has no days; a quantity is 0 to 744; the item must be paid by the hour or day.
        var bad = await EnterTime(world.Owner, run.Id, new { employeeId = john, payItemId = (Guid?)null, quantity = 5 },
            new { employeeId = peter, payItemId = (Guid?)null, quantity = -1 }, new { employeeId = peter, payItemId = Guid.NewGuid(), quantity = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var error = (await bad.Content.ReadFromJsonAsync<Error>())!;
        Assert.Equal("PAY-2025", error.Code);
        Assert.Equal(["entries[0].payItemId", "entries[1].quantity", "entries[2].payItemId"], error.Fields!.Keys.Order());

        Assert.Equal(HttpStatusCode.OK, (await EnterTime(world.Owner, run.Id, new { employeeId = peter, payItemId = (Guid?)null, quantity = 5 },
            new { employeeId = peter, payItemId = overtime.PayItemId, quantity = 4 })).StatusCode);
        await Calculate(world, run.Id);
        var people = await People(world.Owner, run.Id);
        var p = Paid(people, peter);
        var basic = p.Lines.Single(l => l.Code == "BASIC");
        Assert.Equal((5m, 800m, 4_000m), (basic.Quantity, basic.UnitRate, basic.Amount));
        var ot = p.Lines.Single(l => l.Code == "OVERTIME");
        Assert.Equal((4m, 150m, 600m), (ot.Quantity, ot.UnitRate, ot.Amount));   // 800 a day / 8 hours x 1.5
        Assert.Equal(4_600m, p.GrossPay);
        Assert.Equal(6_000m, Paid(people, mary).BasicPay);                        // her usual 40 hours
        Assert.Contains("usual_time", Paid(people, mary).Notes);
        Assert.Equal(25_000m, Paid(people, john).BasicPay);

        // A change after it was worked out sends it back to be worked out again.
        await EnterTime(world.Owner, run.Id, new { employeeId = mary, payItemId = (Guid?)null, quantity = 38.5 });
        Assert.Equal("draft", (await Get(world.Owner, run.Id)).Status);
        Assert.Equal("PAY-2015", await Code(await Submit(world.Owner, run.Id)));
        var entered = (await Time(world.Owner, run.Id)).People.Single(x => x.EmployeeId == mary).Entries.Single();
        Assert.Equal(("entered", world.Owner.UserId), (entered.Source, entered.EnteredBy));
        await Calculate(world, run.Id);
        Assert.Equal(5_775m, Paid(await People(world.Owner, run.Id), mary).BasicPay);

        // Sent: fixed. Approved: fixed for good.
        var approver = await Colleague(world.OrgId, "approver");
        await SetUpApprovals(world);
        var sent = (await (await Submit(world.Owner, run.Id)).Content.ReadFromJsonAsync<PayRunDetail>())!;
        Assert.Equal("PAY-2021", await Code(await EnterTime(world.Owner, run.Id, new { employeeId = mary, payItemId = (Guid?)null, quantity = 40 })));
        Assert.Equal(HttpStatusCode.OK, (await approver.Client.GetAsync($"/api/pay-runs/{run.Id}/inputs")).StatusCode);   // what they approve
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.Client.PutAsJsonAsync($"/api/pay-runs/{run.Id}/inputs", new { entries = Array.Empty<object>() })).StatusCode);
        await approver.Client.PostAsync($"/api/approvals/{sent.ApprovalRequestId}/approve", null);
        Assert.Equal("PAY-2014", await Code(await EnterTime(world.Owner, run.Id, new { employeeId = mary, payItemId = (Guid?)null, quantity = 40 })));
        Assert.Equal(38.5m, await Scalar<decimal>("select quantity from pay_run_inputs where pay_run_id = @r and employee_id = @e", ("r", run.Id), ("e", mary)));
    }

    [Fact]
    public async Task Last_runs_days_and_hours_can_be_copied_for_whoever_has_none_yet()
    {
        var world = await Organization();
        await WorksIn(world.Westlands, "other");
        var approver = await Colleague(world.OrgId, "approver");
        await SetUpApprovals(world);
        var weekly = await Schedule(world, "weekly");
        var peter = await Employee(world, world.Westlands, 800, name: "Peter Kamau", basis: "daily", schedule: weekly);
        var mary = await Employee(world, world.Westlands, 800, name: "Mary Akinyi", basis: "daily", schedule: weekly);
        var first = await Started(await StartOn(world, weekly, "2026-10-02"));
        Assert.Equal("PAY-2026", await Code(await world.Owner.Client.PostAsync($"/api/pay-runs/{first.Id}/inputs/copy-previous", null)));

        await EnterTime(world.Owner, first.Id, new { employeeId = peter, payItemId = (Guid?)null, quantity = 6 },
            new { employeeId = mary, payItemId = (Guid?)null, quantity = 3 });
        await Calculate(world, first.Id);
        var sent = (await (await Submit(world.Owner, first.Id)).Content.ReadFromJsonAsync<PayRunDetail>())!;
        await approver.Client.PostAsync($"/api/approvals/{sent.ApprovalRequestId}/approve", null);

        var second = await Started(await StartOn(world, weekly, "2026-10-09"));
        await EnterTime(world.Owner, second.Id, new { employeeId = mary, payItemId = (Guid?)null, quantity = 5 });
        var copied = await world.Owner.Client.PostAsync($"/api/pay-runs/{second.Id}/inputs/copy-previous", null);
        Assert.Equal(HttpStatusCode.OK, copied.StatusCode);
        var time = (await copied.Content.ReadFromJsonAsync<RunTimeResponse>())!;
        var peters = time.People.Single(x => x.EmployeeId == peter).Entries.Single();
        Assert.Equal((6m, "copied"), (peters.Quantity, peters.Source));
        var marys = time.People.Single(x => x.EmployeeId == mary).Entries.Single();
        Assert.Equal((5m, "entered"), (marys.Quantity, marys.Source));     // what was entered stays
    }

    [Fact]
    public async Task A_rate_below_the_minimum_wage_where_someone_works_stops_the_run_being_sent()
    {
        var world = await Organization();   // Westlands' main location is in Nairobi: a city, 775.39 a day
        await Colleague(world.OrgId, "approver");
        await SetUpApprovals(world);
        var daily = await Schedule(world, "daily");
        var peter = await Employee(world, world.Westlands, 450, name: "Peter Kamau", basis: "daily", schedule: daily);
        var run = await Calculate(world, (await Started(await StartOn(world, daily, "2026-10-01"))).Id);

        Assert.Contains("below_minimum", run.Flagged.Single().Notes);
        Assert.Equal("PAY-2002", await Code(await Submit(world.Owner, run.Id)));

        // Where they work decides it: in "all other areas" the minimum is 435.66 a day.
        await WorksIn(world.Westlands, "other");
        run = await Calculate(world, run.Id);
        Assert.DoesNotContain(run.Flagged, f => f.Notes.Contains("below_minimum"));
        Assert.Equal(HttpStatusCode.OK, (await Submit(world.Owner, run.Id)).StatusCode);
        Assert.Equal(peter, Assert.Single((await People(world.Owner, run.Id)).Items).EmployeeId);
    }

    [Fact]
    public async Task Items_paid_by_the_hour_or_day_are_set_up_with_a_unit_and_a_multiplier_or_an_amount()
    {
        var world = await Organization();
        async Task<HttpResponseMessage> Add(object item) => await world.Owner.Client.PostAsJsonAsync("/api/pay-items", item);

        var night = await Add(new { name = "Night shift", kind = "earning", taxable = true, calcMethod = "per_unit", unit = "hour", defaultAmount = 200, allCompanies = true });
        Assert.Equal(HttpStatusCode.Created, night.StatusCode);
        var created = (await night.Content.ReadFromJsonAsync<PayItemShape>())!;
        Assert.Equal(("hour", (decimal?)200m, (decimal?)null), (created.Unit, created.DefaultAmount, created.Multiplier));

        var refused = await Add(new { name = "Docked", kind = "deduction", calcMethod = "per_unit", unit = "week", multiplier = 1.5, defaultAmount = 100, allCompanies = true });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(["kind", "multiplier", "unit"], (await refused.Content.ReadFromJsonAsync<Error>())!.Fields!.Keys.Order());
    }

    private sealed record WorkingHours(Guid CompanyId, decimal? HoursPerDay, decimal? HoursPerMonth, decimal UsedPerDay, decimal UsedPerMonth,
        Guid? SetBy);

    [Fact]
    public async Task Each_company_sets_its_own_hours_and_overtime_follows_them()
    {
        var world = await Organization();
        await WorksIn(world.Westlands, "other");
        var weekly = await Schedule(world, "weekly");
        var peter = await Employee(world, world.Westlands, 900, name: "Peter Kamau", basis: "daily", schedule: weekly);

        // The wage order's hours until the company says otherwise.
        var hours = (await world.Owner.Client.GetFromJsonAsync<WorkingHours>($"/api/companies/{world.Westlands.Id}/working-hours"))!;
        Assert.Equal(((decimal?)null, (decimal?)null, 8m, 225m), (hours.HoursPerDay, hours.HoursPerMonth, hours.UsedPerDay, hours.UsedPerMonth));

        var bad = await world.Owner.Client.PutAsJsonAsync($"/api/companies/{world.Westlands.Id}/working-hours", new { hoursPerDay = 25, hoursPerMonth = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var error = (await bad.Content.ReadFromJsonAsync<Error>())!;
        Assert.Equal("PAY-2027", error.Code);
        Assert.Equal(["hoursPerDay", "hoursPerMonth"], error.Fields!.Keys.Order());

        var set = await world.Owner.Client.PutAsJsonAsync($"/api/companies/{world.Westlands.Id}/working-hours", new { hoursPerDay = 9, hoursPerMonth = (decimal?)null });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        hours = (await set.Content.ReadFromJsonAsync<WorkingHours>())!;
        Assert.Equal((9m, 9m, 225m, (Guid?)world.Owner.UserId), (hours.HoursPerDay!.Value, hours.UsedPerDay, hours.UsedPerMonth, hours.SetBy));

        // Overtime for someone on 900 a day: 900 / 9 hours x 1.5 = 150 an hour, not 168.75 on 8 hours.
        var run = await Started(await StartOn(world, weekly, "2026-10-02"));
        var overtime = (await Time(world.Owner, run.Id)).Items.Single(i => i.Code == "OVERTIME").PayItemId;
        await EnterTime(world.Owner, run.Id, new { employeeId = peter, payItemId = (Guid?)null, quantity = 5 }, new { employeeId = peter, payItemId = overtime, quantity = 2 });
        await Calculate(world, run.Id);
        Assert.Equal(150m, Paid(await People(world.Owner, run.Id), peter).Lines.Single(l => l.Code == "OVERTIME").UnitRate);

        // Mombasa still works on the wage order's; someone with no say over pay at Westlands can't change it.
        Assert.Null((await world.Owner.Client.GetFromJsonAsync<WorkingHours>($"/api/companies/{world.Mombasa.Id}/working-hours"))!.HoursPerDay);
        var approver = await Colleague(world.OrgId, "approver");
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.Client.PutAsJsonAsync($"/api/companies/{world.Westlands.Id}/working-hours",
            new { hoursPerDay = 10 })).StatusCode);
    }

    private sealed record PayItemShape(Guid Id, string Code, string CalcMethod, decimal? DefaultAmount, string? Unit, decimal? Multiplier);

    // PAY-04: pay changed from an earlier date.

    private static Task<HttpResponseMessage> ChangePay(Person by, Company company, Guid employee, object change) =>
        by.Client.PostAsJsonAsync($"/api/companies/{company.Id}/employees/{employee}/pay-changes", change);

    private static async Task<List<PayChangeRow>> PayHistory(Person by, Company company, Guid employee) =>
        (await by.Client.GetFromJsonAsync<List<PayChangeRow>>($"/api/companies/{company.Id}/employees/{employee}/pay-changes"))!;

    [Fact]
    public async Task Pay_changed_from_an_earlier_date_fixes_a_run_not_yet_sent()
    {
        var world = await Organization();   // Nairobi: 775.39 a day at the least
        await Colleague(world.OrgId, "approver");
        await SetUpApprovals(world);
        var daily = await Schedule(world, "daily");
        var peter = await Employee(world, world.Westlands, 450, name: "Peter Kamau", basis: "daily", schedule: daily);
        var run = await Calculate(world, (await Started(await StartOn(world, daily, "2026-10-01"))).Id);
        Assert.Equal("PAY-2002", await Code(await Submit(world.Owner, run.Id)));

        var res = await ChangePay(world.Owner, world.Westlands, peter,
            new { amount = 800, basis = "daily", effectiveFrom = "2026-10-01", reason = "Raised to the minimum wage from October" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var change = (await res.Content.ReadFromJsonAsync<PayChangeResponse>())!;
        Assert.Equal([run.Id], change.SentBackToDraft);
        Assert.Empty(change.NeedCorrection);
        Assert.Equal("draft", (await Get(world.Owner, run.Id)).Status);

        run = await Calculate(world, run.Id);
        Assert.Equal(HttpStatusCode.OK, (await Submit(world.Owner, run.Id)).StatusCode);
        Assert.Equal(800m, Paid(await People(world.Owner, run.Id), peter).Lines.Single(l => l.Code == "BASIC").Amount);

        // Nothing is rewritten: the old rate ends the day before, the new one says who and why. Looking is recorded.
        const string Looks = "select count(*) from sensitive_views where user_id = @u and kind = 'employee_pay' and subject_ids = array[@e]";
        var looked = await Scalar<long>(Looks, ("u", world.Owner.UserId), ("e", peter));
        var history = await PayHistory(world.Owner, world.Westlands, peter);
        Assert.Equal([(new DateOnly(2026, 10, 1), (DateOnly?)null, 800m), (new DateOnly(2024, 1, 15), new DateOnly(2026, 9, 30), 450m)],
            history.Select(h => (h.ValidFrom, h.ValidTo, h.Amount)));
        Assert.Equal(("Raised to the minimum wage from October", (Guid?)world.Owner.UserId), (history[0].Reason, history[0].ChangedBy));
        Assert.Equal(looked + 1, await Scalar<long>(Looks, ("u", world.Owner.UserId), ("e", peter)));
    }

    [Fact]
    public async Task Approved_runs_are_never_changed_by_an_earlier_pay_change_only_listed_for_a_correction()
    {
        var (world, approver, run, brian) = await Sent();
        var raise = new { amount = 120_000, basis = "monthly", effectiveFrom = "2026-10-01", reason = "Promotion agreed in September" };

        // Waiting for approval: its figures must not move under the approver.
        Assert.Equal("PAY-2021", await Code(await ChangePay(world.Owner, world.Westlands, brian, raise)));
        await approver.Client.PostAsync($"/api/approvals/{run.ApprovalRequestId}/approve", null);

        var change = (await (await ChangePay(world.Owner, world.Westlands, brian, raise)).Content.ReadFromJsonAsync<PayChangeResponse>())!;
        Assert.Equal(run.Id, Assert.Single(change.NeedCorrection).PayRunId);
        Assert.Equal(("approved", 100_000m), ((await Get(world.Owner, run.Id)).Status, (await Get(world.Owner, run.Id)).Totals.GrossPay));

        // A change between two others runs until the next one.
        var june = await ChangePay(world.Owner, world.Westlands, brian, new { amount = 110_000, basis = "monthly", effectiveFrom = "2026-06-01", reason = "Annual review" });
        Assert.Equal(HttpStatusCode.OK, june.StatusCode);
        Assert.Equal(new DateOnly(2026, 9, 30), (await june.Content.ReadFromJsonAsync<PayChangeResponse>())!.Change.ValidTo);
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 6, 1), new DateOnly(2024, 1, 15)],
            (await PayHistory(world.Owner, world.Westlands, brian)).Select(h => h.ValidFrom));
    }

    [Fact]
    public async Task A_wrong_starting_rate_is_replaced_and_the_old_one_kept()
    {
        var world = await Organization();
        var grace = await Employee(world, world.Westlands, 100_000, name: "Grace Njeri", hired: new DateOnly(2026, 10, 12));
        var run = await Calculate(world, (await Start(world)).Id);
        Assert.Equal(64_516.13m, Paid(await People(world.Owner, run.Id), grace).BasicPay);       // 100,000 x 20 / 31

        var res = await ChangePay(world.Owner, world.Westlands, grace,
            new { amount = 120_000, basis = "monthly", effectiveFrom = "2026-10-12", reason = "Her offer letter says 120,000" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal([run.Id], (await res.Content.ReadFromJsonAsync<PayChangeResponse>())!.SentBackToDraft);
        await Calculate(world, run.Id);
        Assert.Equal(77_419.35m, Paid(await People(world.Owner, run.Id), grace).BasicPay);       // 120,000 x 20 / 31

        // The wrong rate is kept, marked replaced by whom and when; only the new one is paid or shown as current.
        var history = await PayHistory(world.Owner, world.Westlands, grace);
        Assert.Equal([(120_000m, (Guid?)null), (100_000m, world.Owner.UserId)], history.Select(h => (h.Amount, h.ReplacedBy)));
        Assert.NotNull(history[1].ReplacedAt);
        Assert.Equal(new DateOnly(2026, 10, 12), history[1].ValidFrom);
        Assert.Equal(120_000m, (await world.Owner.Client.GetFromJsonAsync<EmployeePay>($"/api/employees/{grace}?includePay=true"))!.Pay!.BasicAmount);
    }

    private sealed record EmployeePayShape(decimal BasicAmount);
    private sealed record EmployeePay(EmployeePayShape? Pay);

    [Fact]
    public async Task An_earlier_pay_change_is_checked_and_needs_a_say_over_pay()
    {
        var world = await Organization();
        var brian = await Employee(world, world.Westlands, 100_000);
        await ChangePay(world.Owner, world.Westlands, brian, new { amount = 105_000, basis = "monthly", effectiveFrom = "2026-08-01", reason = "Review" });

        async Task<string[]> Fields(object change)
        {
            var res = await ChangePay(world.Owner, world.Westlands, brian, change);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var error = (await res.Content.ReadFromJsonAsync<Error>())!;
            Assert.Equal("PAY-2028", error.Code);
            return [.. error.Fields!.Keys.Order()];
        }

        Assert.Equal(["amount", "basis", "effectiveFrom", "reason"], await Fields(new { amount = 0, basis = "yearly", reason = " " }));
        Assert.Equal(["effectiveFrom"], await Fields(new { amount = 1, basis = "monthly", effectiveFrom = "2023-12-31", reason = "Before they started" }));
        Assert.Equal(["effectiveFrom"], await Fields(new { amount = 1, basis = "monthly",
            effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), reason = "In the future" }));
        Assert.Equal(["usualQuantity"], await Fields(new { amount = 1, basis = "monthly", usualQuantity = 5, effectiveFrom = "2026-09-01", reason = "Days on a salary" }));

        var approver = await Colleague(world.OrgId, "approver");
        Assert.Equal(HttpStatusCode.Forbidden, (await ChangePay(approver, world.Westlands, brian,
            new { amount = 1, basis = "monthly", effectiveFrom = "2026-09-01", reason = "No say" })).StatusCode);
        Assert.Equal("ONB-3539", await Code(await ChangePay(world.Owner, world.Mombasa, brian,
            new { amount = 1, basis = "monthly", effectiveFrom = "2026-09-01", reason = "Wrong company" })));
    }
}
