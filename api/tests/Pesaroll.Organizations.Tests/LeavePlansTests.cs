using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-08, the Leave plans step. The law's leave types and a Standard plan at the legal minimums
/// come with the first company; plans give leave however people are paid, never below the law.
/// </summary>
public sealed class LeavePlansTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Schedule(Guid Id, string Name);
    private sealed record LeaveType(Guid Id, string Code, string Name, string Counts, bool Paid, bool NeedsProof, string WhenUsedUp,
        bool Locked, string? LockedKey, decimal? MinFullPayDays, decimal? MinHalfPayDays, int? MinWaitingMonths);
    private sealed record Rule(Guid Id, Guid LeaveTypeId, string EarnMethod, decimal? Amount, string Unit, decimal HalfPay, bool Prorate,
        decimal CarryOverMax, int WaitingMonths, decimal? DaysAYear);
    private sealed record Plan(Guid Id, string Name, string LeaveYear, decimal HoursPerDay, bool AllCompanies, List<Guid> CompanyIds,
        List<Guid> DefaultFor, List<Guid> DefaultForSchedules, List<Rule> Rules);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email, Guid OrganizationId)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"leave-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        return (client, email, created.Organization.Id);
    }

    private static async Task<Company> AddCompany(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE" })).Content.ReadFromJsonAsync<Company>())!;

    private static async Task<List<LeaveType>> Types(HttpClient client) => (await client.GetFromJsonAsync<List<LeaveType>>("/api/leave-types"))!;
    private static async Task<List<Plan>> Plans(HttpClient client, Guid? companyId = null) =>
        (await client.GetFromJsonAsync<List<Plan>>(companyId is null ? "/api/leave-plans" : $"/api/leave-plans?companyId={companyId}"))!;
    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    /// <summary>A plan with the Standard plan's rules, changed by <paramref name="change"/> (by locked key or code).</summary>
    private static object PlanBody(string name, Plan from, List<LeaveType> types, Dictionary<string, object>? change = null, Guid[]? companyIds = null,
        Guid[]? defaultFor = null, Guid[]? schedules = null) => new
    {
        name, leaveYear = "calendar", hoursPerDay = 8m, allCompanies = companyIds is null, companyIds = companyIds ?? [],
        defaultFor = defaultFor ?? [], defaultForSchedules = schedules ?? [],
        rules = from.Rules.Select(r =>
        {
            var type = types.Single(t => t.Id == r.LeaveTypeId);
            return change?.GetValueOrDefault(type.LockedKey ?? type.Code) ?? new
            {
                leaveTypeId = r.LeaveTypeId, earnMethod = r.EarnMethod, amount = r.Amount, unit = r.Unit, halfPay = r.HalfPay, prorate = r.Prorate,
                carryOverMax = r.CarryOverMax, waitingMonths = r.WaitingMonths,
            };
        }).ToList(),
    };

    [Fact]
    public async Task The_first_company_brings_the_laws_leave_and_a_Standard_plan_as_its_default()
    {
        var (client, _, _) = await Onboarded();

        var a = await AddCompany(client, "Mama Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Mama Oliech Catering Ltd");

        var types = await Types(client);
        Assert.Equal(["ANNUAL", "SICK", "MATERNITY", "PATERNITY", "ADOPTION", "UNPAID"], types.Select(t => t.Code));
        Assert.All(types.Take(5), t => Assert.True(t.Locked));
        var sickType = types[1];
        Assert.Equal((7m, 7m, 2, true), (sickType.MinFullPayDays!.Value, sickType.MinHalfPayDays!.Value, sickType.MinWaitingMonths!.Value, sickType.NeedsProof));
        Assert.Equal(("calendar_days", false), (types[2].Counts, types[5].Paid));

        var standard = Assert.Single(await Plans(client));
        Assert.Equal(("Standard", true, "calendar", 8m), (standard.Name, standard.AllCompanies, standard.LeaveYear, standard.HoursPerDay));
        Assert.Equal([a.Id, b.Id], standard.DefaultFor.Order());
        var rule = (string code) => standard.Rules.Single(r => r.LeaveTypeId == types.Single(t => t.Code == code).Id);
        Assert.Equal(("monthly", 1.75m, 21m), (rule("ANNUAL").EarnMethod, rule("ANNUAL").Amount!.Value, rule("ANNUAL").DaysAYear!.Value));
        Assert.Equal(("yearly", 7m, 7m, 2), (rule("SICK").EarnMethod, rule("SICK").Amount!.Value, rule("SICK").HalfPay, rule("SICK").WaitingMonths));
        Assert.Equal(("each_time", 90m), (rule("MATERNITY").EarnMethod, rule("MATERNITY").Amount!.Value));
        Assert.Equal(("unlimited", (decimal?)null), (rule("UNPAID").EarnMethod, rule("UNPAID").Amount));
    }

    [Fact]
    public async Task Plans_give_leave_however_people_are_paid_and_can_be_a_schedules_default()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var types = await Types(client);
        var standard = Assert.Single(await Plans(client));
        var daily = (await (await client.PostAsJsonAsync("/api/pay-schedules", new { name = "Daily", frequency = "daily", allCompanies = true }))
            .Content.ReadFromJsonAsync<Schedule>())!;
        var annual = types.Single(t => t.LockedKey == "annual");

        var res = await client.PostAsJsonAsync("/api/leave-plans", PlanBody("Daily-paid", standard, types,
            new() { ["annual"] = new { leaveTypeId = annual.Id, earnMethod = "per_hour_worked", amount = 0.0805m, unit = "hours", halfPay = 0m, prorate = true, carryOverMax = 5m, waitingMonths = 0 } },
            companyIds: [a.Id], schedules: [daily.Id]));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var plan = (await res.Content.ReadFromJsonAsync<Plan>())!;
        Assert.Equal([daily.Id], plan.DefaultForSchedules);
        Assert.Equal([a.Id], plan.CompanyIds);
        var annualRule = plan.Rules.Single(r => r.LeaveTypeId == annual.Id);
        Assert.Equal(("per_hour_worked", "hours", 5m), (annualRule.EarnMethod, annualRule.Unit, annualRule.CarryOverMax));
        Assert.Equal([a.Id], (await Plans(client)).Single(p => p.Id == standard.Id).DefaultFor);   // the company's default stays
    }

    [Fact]
    public async Task Plans_cant_give_less_than_the_law()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");
        var types = await Types(client);
        var standard = Assert.Single(await Plans(client));
        var id = (string key) => types.Single(t => t.LockedKey == key).Id;

        var more = await client.PostAsJsonAsync("/api/leave-plans", PlanBody("Generous", standard, types, new()
        {
            ["annual"] = new { leaveTypeId = id("annual"), earnMethod = "yearly", amount = 25m, unit = "days", prorate = true },
            ["sick"] = new { leaveTypeId = id("sick"), earnMethod = "yearly", amount = 14m, unit = "days", halfPay = 0m, waitingMonths = 0 },
        }));
        Assert.Equal(HttpStatusCode.Created, more.StatusCode);

        var res = await client.PostAsJsonAsync("/api/leave-plans", PlanBody("Mean", standard, types, new()
        {
            ["annual"] = new { leaveTypeId = id("annual"), earnMethod = "weekly", amount = 0.3m, unit = "days" },
            ["sick"] = new { leaveTypeId = id("sick"), earnMethod = "yearly", amount = 10m, unit = "days", waitingMonths = 3 },
            ["maternity"] = new { leaveTypeId = id("maternity"), earnMethod = "each_time", amount = 60m, unit = "days" },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await ErrorFrom(res);
        Assert.Equal("ONB-3532", error.Code);
        var annualAt = standard.Rules.FindIndex(r => r.LeaveTypeId == id("annual"));
        var sickAt = standard.Rules.FindIndex(r => r.LeaveTypeId == id("sick"));
        Assert.Contains($"rules[{annualAt}].amount", error.Fields!.Keys);
        Assert.Contains($"rules[{sickAt}].waitingMonths", error.Fields.Keys);
        Assert.Contains("at least 21 days", error.Fields[$"rules[{annualAt}].amount"]);

        var withoutSick = await client.PostAsJsonAsync("/api/leave-plans", new
        {
            name = "No sick leave", allCompanies = true,
            rules = standard.Rules.Where(r => r.LeaveTypeId != id("sick")).Select(r => new { leaveTypeId = r.LeaveTypeId, earnMethod = r.EarnMethod, amount = r.Amount, unit = r.Unit, halfPay = r.HalfPay, waitingMonths = r.WaitingMonths }),
        });
        Assert.Equal("rules", Assert.Single((await ErrorFrom(withoutSick)).Fields!.Keys));
    }

    [Fact]
    public async Task Own_leave_types_can_be_added_to_plans_and_the_laws_cant_be_deleted()
    {
        var (client, _, _) = await Onboarded();
        await AddCompany(client, "Oliech Restaurants Ltd");
        var compassionate = (await (await client.PostAsJsonAsync("/api/leave-types", new { name = "Compassionate leave", counts = "working_days", paid = true, whenUsedUp = "unpaid" }))
            .Content.ReadFromJsonAsync<LeaveType>())!;
        Assert.Equal(("COM-01", false, "unpaid"), (compassionate.Code, compassionate.Locked, compassionate.WhenUsedUp));
        var types = await Types(client);
        var standard = Assert.Single(await Plans(client));

        var body = PlanBody("Standard", standard, types);
        var withIt = new
        {
            name = "Standard", leaveYear = "anniversary", hoursPerDay = 8m, allCompanies = true, companyIds = Array.Empty<Guid>(),
            defaultFor = standard.DefaultFor, defaultForSchedules = Array.Empty<Guid>(),
            rules = ((IEnumerable<object>)body.GetType().GetProperty("rules")!.GetValue(body)!)
                .Append(new { leaveTypeId = compassionate.Id, earnMethod = "each_time", amount = 5m, unit = "days" }).ToList(),
        };
        var edit = await client.PatchAsJsonAsync($"/api/leave-plans/{standard.Id}", withIt);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = (await edit.Content.ReadFromJsonAsync<Plan>())!;
        Assert.Equal(("anniversary", 7), (edited.LeaveYear, edited.Rules.Count));

        var inUse = await client.DeleteAsync($"/api/leave-types/{compassionate.Id}");
        Assert.Equal((HttpStatusCode.Conflict, "ONB-3537"), (inUse.StatusCode, (await ErrorFrom(inUse)).Code));
        var locked = await client.DeleteAsync($"/api/leave-types/{types[0].Id}");
        Assert.Equal((HttpStatusCode.Conflict, "ONB-3535"), (locked.StatusCode, (await ErrorFrom(locked)).Code));
    }

    [Fact]
    public async Task A_default_plan_stays_until_another_is_made_default()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var types = await Types(client);
        var standard = Assert.Single(await Plans(client));

        var delete = await client.DeleteAsync($"/api/leave-plans/{standard.Id}");
        Assert.Equal((HttpStatusCode.Conflict, "ONB-3536"), (delete.StatusCode, (await ErrorFrom(delete)).Code));
        Assert.Contains("Oliech Restaurants Ltd", (await ErrorFrom(await client.DeleteAsync($"/api/leave-plans/{standard.Id}"))).Message);

        await client.PostAsJsonAsync("/api/leave-plans", PlanBody("Company plan", standard, types, companyIds: [a.Id], defaultFor: [a.Id]));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/leave-plans/{standard.Id}")).StatusCode);
        Assert.Equal("Company plan", Assert.Single(await Plans(client)).Name);
    }

    [Fact]
    public async Task Another_organization_and_plain_members_get_nothing()
    {
        var (a, _, orgId) = await Onboarded();
        await AddCompany(a, "Oliech Restaurants Ltd");
        var standard = Assert.Single(await Plans(a));
        var (b, bEmail, _) = await Onboarded();

        Assert.DoesNotContain(await Plans(b), p => p.Id == standard.Id);
        Assert.Equal("ONB-3534", (await ErrorFrom(await b.DeleteAsync($"/api/leave-plans/{standard.Id}"))).Code);

        await using (var connection = new NpgsqlConnection(Db))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "insert into memberships (id, organization_id, user_id) select uuidv7(), @org, id from users where email = @email; " +
                "update memberships set status = 'removed' where user_id = (select id from users where email = @email) and organization_id <> @org;",
                connection);
            command.Parameters.AddWithValue("org", orgId);
            command.Parameters.AddWithValue("email", bEmail);
            await command.ExecuteNonQueryAsync();
        }
        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = bEmail, password = Password });
        b.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);

        var res = await b.PostAsJsonAsync("/api/leave-types", new { name = "Study leave" });
        Assert.Equal((HttpStatusCode.Forbidden, "ACC-8001"), (res.StatusCode, (await ErrorFrom(res)).Code));
    }
}
