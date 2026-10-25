using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Shared.Data;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;
using Pesaroll.Workflows;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// WF-01..03 end to end: an administrator sets up who approves pay runs, per company or for all
/// (nothing is set up for them); a request is sent by the module that owns it (here, as PAY-03 will);
/// it waits for its approvers, never its sender unless the workflow allows it; deciding is checked on
/// the server; everything done is recorded; another organization or company sees none of it.
/// </summary>
public sealed class WorkflowsTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Person(HttpClient Client, Guid UserId, Guid MembershipId);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email)> SignUp()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"wf-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        return (client, email);
    }

    private async Task<(Person Owner, Guid OrgId, Company Westlands, Company Mombasa)> Organization()
    {
        var (owner, email) = await SignUp();
        var created = (await (await owner.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        async Task<Company> Add(string name) => (await (await owner.PostAsJsonAsync("/api/companies",
            new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" })).Content.ReadFromJsonAsync<Company>())!;
        var (userId, membershipId) = await Ids(email, created.Organization.Id);
        return (new Person(owner, userId, membershipId), created.Organization.Id, await Add("Westlands Ltd"), await Add("Mombasa Ltd"));
    }

    private static async Task<(Guid User, Guid Membership)> Ids(string email, Guid orgId)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select u.id, m.id from users u join memberships m on m.user_id = u.id and m.organization_id = @org where u.email = @email", connection);
        command.Parameters.AddWithValue("org", orgId);
        command.Parameters.AddWithValue("email", email);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetGuid(0), reader.GetGuid(1));
    }

    /// <summary>Someone in the organization, in a group with this standard role for these companies (none: all), signed in.</summary>
    private async Task<Person> Colleague(Guid orgId, string role, params Guid[] companies)
    {
        var (client, email) = await SignUp();
        await using (var connection = new NpgsqlConnection(Db))
        {
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
        }
        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var (userId, membershipId) = await Ids(email, orgId);
        return new Person(client, userId, membershipId);
    }

    private static object PayRunSetup(Guid? companyId = null, bool allowSelfApproval = false, params object[] steps) => new
    {
        kind = "pay_run", companyId, allowSelfApproval,
        steps = steps.Length > 0 ? steps : [new { approvers = "permission", permission = "pay_run_approvals.manage", fallback = "companies.manage" }],
    };

    private static object People(params Guid[] memberships) => new { approvers = "people", people = memberships };

    private static async Task SetUp(Person admin, object setup) =>
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsJsonAsync("/api/workflows", setup)).StatusCode);

    /// <summary>Sends a pay run for approval the way the pay run module does, as this person.</summary>
    private async Task<Guid> Send(WebApplicationFactory<Program> factory, Guid orgId, Person sender, Guid companyId, Guid? subject = null)
    {
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenant.SetUser(sender.UserId);
        tenant.Set(orgId, await CompaniesOf(orgId));
        return await scope.ServiceProvider.GetRequiredService<Approvals>().SubmitAsync("pay_run", companyId, subject ?? Guid.CreateVersion7(), null, CancellationToken.None);
    }

    private Task<Guid> Send(Guid orgId, Person sender, Guid companyId) => Send(api, orgId, sender, companyId);

    private async Task<string> SendFails(Guid orgId, Person sender, Guid companyId) =>
        (await Assert.ThrowsAsync<AppException>(() => Send(orgId, sender, companyId))).Code;

    private static async Task<Guid[]> CompaniesOf(Guid orgId)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select array_agg(id) from companies where organization_id = @org", connection);
        command.Parameters.AddWithValue("org", orgId);
        return (Guid[])(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<(string Template, string Data)>> EmailsTo(Guid userId)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select template, data::text from notification_deliveries where recipient = (select email from users where id = @id) order by created_at", connection);
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        var emails = new List<(string, string)>();
        while (await reader.ReadAsync()) emails.Add((reader.GetString(0), reader.GetString(1)));
        return emails;
    }

    private static async Task<string> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!.Code;

    private static async Task<List<WaitingRequest>> Waiting(Person person, Guid companyId) =>
        (await person.Client.GetFromJsonAsync<List<WaitingRequest>>($"/api/approvals?companyId={companyId}"))!;

    private static Task<HttpResponseMessage> Approve(Person person, Guid request) => person.Client.PostAsync($"/api/approvals/{request}/approve", null);

    private static Task<HttpResponseMessage> Decline(Person person, Guid request, string? reason) =>
        person.Client.PostAsJsonAsync($"/api/approvals/{request}/decline", new { reason });

    [Fact]
    public async Task Nothing_is_set_up_for_a_new_organization_and_pay_runs_show_as_missing()
    {
        var (owner, _, westlands, _) = await Organization();

        var kinds = (await owner.Client.GetFromJsonAsync<List<KindResponse>>("/api/workflow-kinds"))!;
        Assert.Equal(["pay_run", "leave"], kinds.Select(k => k.Key));
        Assert.Equal("pay_run_approvals.manage", kinds[0].SuggestedSteps[0].GetProperty("permission").GetString());

        var missing = (await owner.Client.GetFromJsonAsync<MissingWorkflows>($"/api/workflows/missing?companyId={westlands.Id}"))!;
        Assert.Equal(["pay_run"], missing.Missing.Select(m => m.Kind));
        var workflows = (await owner.Client.GetFromJsonAsync<CompanyWorkflows>($"/api/workflows?companyId={westlands.Id}"))!;
        Assert.All(workflows.Kinds, k => Assert.Null(k.Workflow));
    }

    [Fact]
    public async Task Sending_with_no_workflow_asks_to_set_one_up()
    {
        var (owner, orgId, westlands, _) = await Organization();
        var john = await Colleague(orgId, "payroll");

        Assert.Equal("WF-5005", await SendFails(orgId, john, westlands.Id));
    }

    [Fact]
    public async Task A_companys_own_workflow_replaces_the_organizations_there_only()
    {
        var (owner, orgId, westlands, mombasa) = await Organization();
        var amina = await Colleague(orgId, "approver");

        await SetUp(owner, PayRunSetup());
        await SetUp(owner, PayRunSetup(westlands.Id, false, People(amina.MembershipId)));

        var atWestlands = (await owner.Client.GetFromJsonAsync<CompanyWorkflows>($"/api/workflows?companyId={westlands.Id}"))!.Kinds[0].Workflow!;
        var atMombasa = (await owner.Client.GetFromJsonAsync<CompanyWorkflows>($"/api/workflows?companyId={mombasa.Id}"))!.Kinds[0].Workflow!;
        Assert.Equal(westlands.Id, atWestlands.CompanyId);
        Assert.Equal("people", atWestlands.Steps[0].Approvers);
        Assert.Equal(amina.MembershipId, atWestlands.Steps[0].People[0].MembershipId);
        Assert.Null(atMombasa.CompanyId);
        Assert.Empty((await owner.Client.GetFromJsonAsync<MissingWorkflows>($"/api/workflows/missing?companyId={mombasa.Id}"))!.Missing);

        // Setting it up again replaces it; the old one is kept, switched off.
        await SetUp(owner, PayRunSetup());
        var again = (await owner.Client.GetFromJsonAsync<CompanyWorkflows>($"/api/workflows?companyId={mombasa.Id}"))!.Kinds[0].Workflow!;
        Assert.NotEqual(atMombasa.Id, again.Id);
        Assert.Equal("WF-5008", await Code(await owner.Client.PostAsync($"/api/workflows/{atMombasa.Id}/switch-off", null)));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsync($"/api/workflows/{atWestlands.Id}/switch-off", null)).StatusCode);
        Assert.Null((await owner.Client.GetFromJsonAsync<CompanyWorkflows>($"/api/workflows?companyId={westlands.Id}"))!.Kinds[0].Workflow!.CompanyId);
    }

    [Fact]
    public async Task Only_administrators_set_workflows_up_and_bad_setups_name_the_field()
    {
        var (owner, orgId, westlands, _) = await Organization();
        var john = await Colleague(orgId, "payroll");
        var mary = await Colleague(orgId, "approver");
        var (stranger, _, _, _) = await Organization();

        Assert.Equal("ACC-8001", await Code(await john.Client.PostAsJsonAsync("/api/workflows", PayRunSetup())));
        Assert.Equal(HttpStatusCode.OK, (await john.Client.GetAsync($"/api/workflows?companyId={westlands.Id}")).StatusCode);   // Payroll sees who approves
        Assert.Equal("ACC-8001", await Code(await mary.Client.GetAsync($"/api/workflows?companyId={westlands.Id}")));

        var bad = await owner.Client.PostAsJsonAsync("/api/workflows", PayRunSetup(null, false, new { approvers = "permission", permission = "launch.rockets" }));
        var error = (await bad.Content.ReadFromJsonAsync<Error>())!;
        Assert.Equal("WF-5009", error.Code);
        Assert.Contains("steps[0].permission", error.Fields!.Keys);

        var outsider = await owner.Client.PostAsJsonAsync("/api/workflows", PayRunSetup(null, false, People(stranger.MembershipId)));
        Assert.Contains("steps[0].people", (await outsider.Content.ReadFromJsonAsync<Error>())!.Fields!.Keys);
    }

    [Fact]
    public async Task A_pay_run_waits_for_its_approver_who_approves_it_and_everything_is_recorded()
    {
        var (owner, orgId, westlands, mombasa) = await Organization();
        await SetUp(owner, PayRunSetup());
        var john = await Colleague(orgId, "payroll");
        var mary = await Colleague(orgId, "approver", westlands.Id);
        var (stranger, _, _, _) = await Organization();

        var request = await Send(orgId, john, westlands.Id);

        Assert.Equal([request], (await Waiting(mary, westlands.Id)).Select(r => r.Id));
        Assert.Empty(await Waiting(john, westlands.Id));                     // the sender never approves their own
        Assert.Equal("ACC-8001", await Code(await mary.Client.GetAsync($"/api/approvals?companyId={mombasa.Id}")));
        Assert.Equal("WF-5007", await Code(await stranger.Client.GetAsync($"/api/approvals/{request}")));
        Assert.Equal("WF-5007", await Code(await Approve(stranger, request)));

        Assert.Equal("WF-5001", await Code(await Approve(john, request)));   // Payroll can't approve
        Assert.Equal("WF-5002", await Code(await Decline(mary, request, " ")));
        var approved = await Approve(mary, request);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var done = (await approved.Content.ReadFromJsonAsync<RequestResponse>())!;
        Assert.Equal("approved", done.Status);
        Assert.Equal(["sent", "approved"], done.History.Select(h => h.Action));
        Assert.False(done.CanDecide);
        Assert.Equal("WF-5003", await Code(await Approve(mary, request)));

        // The approver was told it was waiting; the sender was told it was approved. Never a reason or amount.
        Assert.Contains(await EmailsTo(mary.UserId), e => e.Template == "approval_waiting");
        Assert.Contains(await EmailsTo(john.UserId), e => e.Template == "approval_approved");
    }

    [Fact]
    public async Task A_decline_records_the_reason_and_the_email_leaves_it_out()
    {
        var (owner, orgId, westlands, _) = await Organization();
        await SetUp(owner, PayRunSetup());
        var john = await Colleague(orgId, "payroll");
        var mary = await Colleague(orgId, "approver");

        var request = await Send(orgId, john, westlands.Id);
        var declined = (await (await Decline(mary, request, "Overtime for Kisumu is missing")).Content.ReadFromJsonAsync<RequestResponse>())!;

        Assert.Equal("declined", declined.Status);
        Assert.Equal("Overtime for Kisumu is missing", declined.DeclineReason);
        var email = Assert.Single(await EmailsTo(john.UserId), e => e.Template == "approval_declined");
        Assert.DoesNotContain("Kisumu", email.Data);
    }

    [Fact]
    public async Task The_preparer_approves_only_when_the_workflow_allows_it_and_it_is_marked_self_approved()
    {
        var (owner, orgId, westlands, mombasa) = await Organization();
        var john = await Colleague(orgId, "payroll");
        await SetUp(owner, PayRunSetup(westlands.Id, allowSelfApproval: true, People(john.MembershipId)));   // a small team: one accountant
        await SetUp(owner, PayRunSetup(mombasa.Id, allowSelfApproval: false, People(john.MembershipId)));

        var request = await Send(orgId, john, westlands.Id);
        var done = (await (await Approve(john, request)).Content.ReadFromJsonAsync<RequestResponse>())!;
        Assert.Equal("approved", done.Status);
        Assert.Equal("self_approved", done.History[^1].Action);

        Assert.Equal("WF-5004", await SendFails(orgId, john, mombasa.Id));   // nobody else, and it isn't allowed
    }

    [Fact]
    public async Task Two_steps_are_approved_in_order_and_a_decline_at_either_ends_it()
    {
        var (owner, orgId, westlands, _) = await Organization();
        var john = await Colleague(orgId, "payroll");
        var mary = await Colleague(orgId, "approver");
        var amina = await Colleague(orgId, "approver");
        await SetUp(owner, PayRunSetup(null, false, People(mary.MembershipId), People(amina.MembershipId)));

        var request = await Send(orgId, john, westlands.Id);
        Assert.Empty(await Waiting(amina, westlands.Id));
        Assert.Equal("WF-5001", await Code(await Approve(amina, request)));
        var first = (await (await Approve(mary, request)).Content.ReadFromJsonAsync<RequestResponse>())!;
        Assert.Equal(("submitted", 2, 2), (first.Status, first.CurrentStep, first.Steps));
        Assert.Equal([request], (await Waiting(amina, westlands.Id)).Select(r => r.Id));
        Assert.Empty(await Waiting(mary, westlands.Id));

        var declined = (await (await Decline(amina, request, "Wrong month")).Content.ReadFromJsonAsync<RequestResponse>())!;
        Assert.Equal("declined", declined.Status);
        Assert.Equal(["sent", "approved", "declined"], declined.History.Select(h => h.Action));
    }

    [Fact]
    public async Task An_approver_who_lost_the_permission_can_no_longer_decide()
    {
        var (owner, orgId, westlands, _) = await Organization();
        await SetUp(owner, PayRunSetup());
        var john = await Colleague(orgId, "payroll");
        var mary = await Colleague(orgId, "approver");
        var request = await Send(orgId, john, westlands.Id);

        await using (var connection = new NpgsqlConnection(Db))
        {
            await connection.OpenAsync();
            // Moved from Approvers to HR: she still sees the company, but can't approve pay runs.
            await using var command = new NpgsqlCommand("""
                update security_group_members set removed_at = now() where membership_id = @m;
                with g as (insert into security_groups (id, organization_id, name, all_companies) values (uuidv7(), @org, @name, true) returning id),
                gr as (insert into security_group_roles (organization_id, security_group_id, role_id) select @org, g.id, r.id from g, roles r where r.key = 'hr' and r.organization_id is null)
                insert into security_group_members (id, organization_id, security_group_id, membership_id) select uuidv7(), @org, g.id, @m from g;
                """, connection);
            command.Parameters.AddWithValue("m", mary.MembershipId);
            command.Parameters.AddWithValue("org", orgId);
            command.Parameters.AddWithValue("name", $"hr {Guid.NewGuid():N}");
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal("WF-5001", await Code(await Approve(mary, request)));
    }

    [Fact]
    public async Task The_sender_can_cancel_while_it_waits()
    {
        var (owner, orgId, westlands, _) = await Organization();
        await SetUp(owner, PayRunSetup());
        var john = await Colleague(orgId, "payroll");
        var mary = await Colleague(orgId, "approver");
        var request = await Send(orgId, john, westlands.Id);

        Assert.Equal("WF-5001", await Code(await mary.Client.PostAsync($"/api/approvals/{request}/cancel", null)));
        var cancelled = (await (await john.Client.PostAsync($"/api/approvals/{request}/cancel", null)).Content.ReadFromJsonAsync<RequestResponse>())!;
        Assert.Equal("cancelled", cancelled.Status);
        Assert.Empty(await Waiting(mary, westlands.Id));
    }

    private sealed class Outcome : IApprovalOutcome
    {
        public static readonly List<DecidedRequest> Seen = [];
        public static bool Fail;

        public string Kind => "pay_run";

        public async Task DecidedAsync(DecidedRequest request, ApprovalTransaction transaction, CancellationToken ct)
        {
            await using var command = transaction.Connection.CreateCommand();
            command.Transaction = transaction.Transaction;
            command.CommandText = "select status from approval_requests where id = @id";
            var id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = request.Id;
            command.Parameters.Add(id);
            Assert.Equal(request.Status, (string)(await command.ExecuteScalarAsync(ct))!);   // sees the decision: same transaction
            lock (Seen) Seen.Add(request);
            if (Fail) throw new InvalidOperationException("the pay run couldn't lock");
        }
    }

    [Fact]
    public async Task The_owning_module_reacts_in_the_same_transaction_so_a_failure_undoes_the_decision()
    {
        var factory = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IApprovalOutcome, Outcome>()));
        var (owner, orgId, westlands, _) = await Organization();
        await SetUp(owner, PayRunSetup());
        var john = await Colleague(orgId, "payroll");
        var mary = await Colleague(orgId, "approver");
        var maryHere = mary with { Client = factory.CreateClient() };
        maryHere.Client.DefaultRequestHeaders.Authorization = mary.Client.DefaultRequestHeaders.Authorization;

        var first = await Send(factory, orgId, john, westlands.Id);
        Assert.Equal(HttpStatusCode.OK, (await Approve(maryHere, first)).StatusCode);
        Assert.Contains(Outcome.Seen, r => r.Id == first && r.Status == "approved" && r.DecidedBy == mary.UserId);

        var second = await Send(factory, orgId, john, westlands.Id);
        Outcome.Fail = true;
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Approve(maryHere, second)).StatusCode);
        }
        finally
        {
            Outcome.Fail = false;
        }
        var after = (await mary.Client.GetFromJsonAsync<RequestResponse>($"/api/approvals/{second}"))!;
        Assert.Equal("submitted", after.Status);
        Assert.Equal(["sent"], after.History.Select(h => h.Action));
    }

    [Fact]
    public async Task Another_organization_or_company_reads_none_of_its_workflows_requests_or_history()
    {
        var (owner, orgId, westlands, mombasa) = await Organization();
        await SetUp(owner, PayRunSetup(westlands.Id));
        var john = await Colleague(orgId, "payroll");
        await Colleague(orgId, "approver");
        await Send(orgId, john, westlands.Id);
        var (_, strangerOrg, _, _) = await Organization();

        WorkflowsDbContext ContextFor(Guid org, params Guid[] companies)
        {
            var tenant = new TenantContext();
            tenant.Set(org, companies);
            return new WorkflowsDbContext(new DbContextOptionsBuilder<WorkflowsDbContext>().UseNpgsql(Db).UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantSessionInterceptor(tenant)).Options);
        }

        await using (var own = ContextFor(orgId, westlands.Id))
        {
            Assert.Equal(1, await own.Workflows.CountAsync());
            Assert.Equal(1, await own.Requests.CountAsync());
            Assert.NotEmpty(await own.Actions.ToListAsync());
        }
        await using (var otherCompany = ContextFor(orgId, mombasa.Id))
        {
            Assert.Equal(0, await otherCompany.Workflows.CountAsync());
            Assert.Equal(0, await otherCompany.Steps.CountAsync());
            Assert.Equal(0, await otherCompany.Requests.CountAsync());
            Assert.Equal(0, await otherCompany.RequestApprovers.CountAsync());
            Assert.Equal(0, await otherCompany.Actions.CountAsync());
        }
        await using var other = ContextFor(strangerOrg, westlands.Id);   // even naming the company
        Assert.Equal(0, await other.Workflows.CountAsync());
        Assert.Equal(0, await other.Steps.CountAsync());
        Assert.Equal(0, await other.StepPeople.CountAsync());
        Assert.Equal(0, await other.Requests.CountAsync());
        Assert.Equal(0, await other.Actions.CountAsync());
    }
}
