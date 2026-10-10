using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-06, the Payroll schedules step: how often and when people are paid. A schedule serves chosen
/// companies or all of them; each company has one default schedule, the suggested "Monthly" made
/// with the first company.
/// </summary>
public sealed class PaySchedulesTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Schedule(Guid Id, string Name, string Frequency, string? PayDayRule, int? PayDay, int? CutoffDay,
        int? PayWeekday, DateOnly? FirstPayDate, int? CutoffDaysBefore, TimeOnly? PayTime,
        bool AllCompanies, List<Guid> CompanyIds, List<Guid> DefaultFor, List<DateOnly> NextPayDates);

    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email, Guid OrganizationId)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"pay-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse(Ceo) });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        return (client, email, created.Organization.Id);
    }

    private static async Task<Company> AddCompany(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" }))
            .Content.ReadFromJsonAsync<Company>())!;

    private static async Task<List<Schedule>> Schedules(HttpClient client, Guid? companyId = null) =>
        (await client.GetFromJsonAsync<List<Schedule>>(companyId is null ? "/api/pay-schedules" : $"/api/pay-schedules?companyId={companyId}"))!;

    private static object MidMonth(Guid[] companyIds, Guid[] defaultFor, string name = "Mid-month") => new
    {
        name, frequency = "monthly", payDayRule = "fixed_day", payDay = 15, cutoffDay = 10, allCompanies = false, companyIds, defaultFor,
    };

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task The_first_company_gets_the_suggested_monthly_schedule_as_its_default()
    {
        var (client, _, _) = await Onboarded();

        var a = await AddCompany(client, "Mama Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Mama Oliech Catering Ltd");

        var monthly = Assert.Single(await Schedules(client));   // one for all companies, not one each
        Assert.Equal(("Monthly", "monthly", "last_working_day", (int?)null, 25, true),
            (monthly.Name, monthly.Frequency, monthly.PayDayRule, monthly.PayDay, monthly.CutoffDay, monthly.AllCompanies));
        Assert.Equal([a.Id, b.Id], monthly.DefaultFor.Order());
        Assert.Equal(3, monthly.NextPayDates.Count);
        Assert.All(monthly.NextPayDates, d => Assert.DoesNotContain(d.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));
    }

    [Fact]
    public async Task A_schedule_can_serve_chosen_companies_and_be_default_for_one_of_them()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");

        var res = await client.PostAsJsonAsync("/api/pay-schedules", MidMonth([a.Id, b.Id], [b.Id]));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var mid = (await res.Content.ReadFromJsonAsync<Schedule>())!;
        Assert.Equal(("fixed_day", (int?)15, 10), (mid.PayDayRule, mid.PayDay, mid.CutoffDay));
        Assert.Equal([a.Id, b.Id], mid.CompanyIds.Order());
        Assert.Equal([b.Id], mid.DefaultFor);
        Assert.All(mid.NextPayDates, d => Assert.True(d.Day is 15 or 14 or 13));   // the 15th, or the Friday before

        var monthly = (await Schedules(client)).Single(s => s.Name == "Monthly");
        Assert.Equal([a.Id], monthly.DefaultFor);   // B's default moved

        var forA = await Schedules(client, a.Id);
        Assert.Equal(2, forA.Count);
    }

    [Fact]
    public async Task Pay_dates_can_be_previewed_before_saving()
    {
        var (client, _, _) = await Onboarded();

        var dates = (await client.GetFromJsonAsync<List<DateOnly>>("/api/pay-schedules/pay-dates?payDayRule=last_day"))!;

        Assert.Equal(3, dates.Count);
        Assert.All(dates, d => Assert.Equal(DateTime.DaysInMonth(d.Year, d.Month), d.Day));
        var bad = await client.GetAsync("/api/pay-schedules/pay-dates?payDayRule=fixed_day&payDay=31");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("ONB-3524", (await ErrorFrom(bad)).Code);
    }

    [Fact]
    public async Task Details_are_checked_and_problems_named_by_field()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");

        var res = await client.PostAsJsonAsync("/api/pay-schedules", new
        {
            name = "M", frequency = "monthly", payDayRule = "fixed_day", payDay = (int?)null, cutoffDay = 31,
            allCompanies = false, companyIds = new[] { a.Id }, defaultFor = new[] { b.Id },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await ErrorFrom(res);
        Assert.Equal("ONB-3524", error.Code);
        Assert.Equal(["cutoffDay", "defaultFor", "name", "payDay"], error.Fields!.Keys.Order());

        var hourly = await client.PostAsJsonAsync("/api/pay-schedules", new { name = "Hourly", frequency = "hourly", allCompanies = true });
        Assert.Equal("frequency", Assert.Single((await ErrorFrom(hourly)).Fields!.Keys));

        // Weekly and every two weeks pay on a weekday; every two weeks starts on that weekday.
        var weekend = await client.PostAsJsonAsync("/api/pay-schedules", new
        {
            name = "Fortnightly", frequency = "fortnightly", payWeekday = 6, firstPayDate = "2026-10-15", cutoffDaysBefore = 14, allCompanies = true,
        });
        Assert.Equal(["cutoffDaysBefore", "payWeekday"], (await ErrorFrom(weekend)).Fields!.Keys.Order());
        var offDay = await client.PostAsJsonAsync("/api/pay-schedules", new
        {
            name = "Fortnightly", frequency = "fortnightly", payWeekday = 5, firstPayDate = "2026-10-15", cutoffDaysBefore = 2, allCompanies = true,
        });
        Assert.Equal("firstPayDate", Assert.Single((await ErrorFrom(offDay)).Fields!.Keys));

        var none = await client.PostAsJsonAsync("/api/pay-schedules", MidMonth([], []));
        Assert.Equal("companyIds", Assert.Single((await ErrorFrom(none)).Fields!.Keys));
    }

    [Fact]
    public async Task Weekly_every_two_weeks_and_daily_schedules_keep_only_their_own_fields()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        async Task<Schedule> Add(object body) =>
            (await (await client.PostAsJsonAsync("/api/pay-schedules", body)).Content.ReadFromJsonAsync<Schedule>())!;

        var weekly = await Add(new
        {
            name = "Casuals weekly", frequency = "weekly", payDayRule = "fixed_day", payDay = 15, cutoffDay = 20, payWeekday = 5, cutoffDaysBefore = 2,
            allCompanies = false, companyIds = new[] { a.Id },
        });
        var fortnightly = await Add(new
        {
            name = "Every two weeks", frequency = "fortnightly", payWeekday = 5, firstPayDate = "2026-10-16", cutoffDaysBefore = 3,
            allCompanies = false, companyIds = new[] { a.Id },
        });
        var daily = await Add(new { name = "Casuals daily", frequency = "daily", payTime = "17:30", allCompanies = false, companyIds = new[] { a.Id } });

        Assert.Equal(("weekly", (string?)null, (int?)null, (int?)null, (int?)5, (int?)2),
            (weekly.Frequency, weekly.PayDayRule, weekly.PayDay, weekly.CutoffDay, weekly.PayWeekday, weekly.CutoffDaysBefore));
        Assert.All(weekly.NextPayDates, d => Assert.Equal(DayOfWeek.Friday, d.DayOfWeek));
        Assert.Equal(7, weekly.NextPayDates[1].DayNumber - weekly.NextPayDates[0].DayNumber);
        Assert.Equal((DateOnly?)DateOnly.Parse("2026-10-16"), fortnightly.FirstPayDate);
        Assert.Equal(14, fortnightly.NextPayDates[1].DayNumber - fortnightly.NextPayDates[0].DayNumber);
        Assert.Equal(0, (fortnightly.NextPayDates[0].DayNumber - DateOnly.Parse("2026-10-16").DayNumber) % 14);
        Assert.Equal(("daily", (TimeOnly?)new TimeOnly(17, 30), (int?)null, (int?)null), (daily.Frequency, daily.PayTime, daily.CutoffDay, daily.CutoffDaysBefore));
        Assert.All(daily.NextPayDates, d => Assert.DoesNotContain(d.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));

        var preview = (await client.GetFromJsonAsync<List<DateOnly>>("/api/pay-schedules/pay-dates?frequency=weekly&payWeekday=1"))!;
        Assert.All(preview, d => Assert.Equal(DayOfWeek.Monday, d.DayOfWeek));
    }

    [Fact]
    public async Task Names_are_unique_in_the_organization()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");

        var res = await client.PostAsJsonAsync("/api/pay-schedules", MidMonth([a.Id], [], name: " monthly "));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ONB-3525", (await ErrorFrom(res)).Code);
    }

    [Fact]
    public async Task A_default_schedule_stays_until_another_is_made_default()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var monthly = Assert.Single(await Schedules(client));

        var delete = await client.DeleteAsync($"/api/pay-schedules/{monthly.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var error = await ErrorFrom(delete);
        Assert.Equal("ONB-3527", error.Code);
        Assert.Contains("Oliech Restaurants Ltd", error.Message);
        var unmark = await client.PatchAsJsonAsync($"/api/pay-schedules/{monthly.Id}", new
        {
            name = "Monthly", frequency = "monthly", payDayRule = "last_working_day", cutoffDay = 25, allCompanies = true,
            companyIds = Array.Empty<Guid>(), defaultFor = Array.Empty<Guid>(),
        });
        Assert.Equal("ONB-3527", (await ErrorFrom(unmark)).Code);

        await client.PostAsJsonAsync("/api/pay-schedules", MidMonth([a.Id], [a.Id]));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pay-schedules/{monthly.Id}")).StatusCode);
        Assert.Equal("Mid-month", Assert.Single(await Schedules(client)).Name);
    }

    [Fact]
    public async Task Editing_changes_the_pay_day_and_which_companies_use_it()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        var mid = (await (await client.PostAsJsonAsync("/api/pay-schedules", MidMonth([a.Id], []))).Content.ReadFromJsonAsync<Schedule>())!;

        var res = await client.PatchAsJsonAsync($"/api/pay-schedules/{mid.Id}", new
        {
            name = "Month end", frequency = "monthly", payDayRule = "last_day", payDay = 15, cutoffDay = 20,
            allCompanies = false, companyIds = new[] { b.Id }, defaultFor = new[] { b.Id },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var edited = (await res.Content.ReadFromJsonAsync<Schedule>())!;
        Assert.Equal(("Month end", "last_day", (int?)null, 20), (edited.Name, edited.PayDayRule, edited.PayDay, edited.CutoffDay));   // no day unless fixed
        Assert.Equal([b.Id], edited.CompanyIds);
        Assert.Equal([b.Id], edited.DefaultFor);
    }

    [Fact]
    public async Task A_company_added_later_gets_the_all_companies_schedule_as_default()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var monthly = Assert.Single(await Schedules(client));
        // Even renamed and moved to a fixed day, it is still the one for all companies.
        await client.PatchAsJsonAsync($"/api/pay-schedules/{monthly.Id}", new
        {
            name = "Group payroll", frequency = "monthly", payDayRule = "fixed_day", payDay = 28, cutoffDay = 20, allCompanies = true,
            companyIds = Array.Empty<Guid>(), defaultFor = new[] { a.Id },
        });

        var later = await AddCompany(client, "Oliech Bakery Ltd");

        var group = Assert.Single(await Schedules(client));
        Assert.Equal([a.Id, later.Id], group.DefaultFor.Order());
    }

    [Fact]
    public async Task Deleting_a_company_removes_the_schedules_only_it_used()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        await client.PostAsJsonAsync("/api/pay-schedules", MidMonth([a.Id], [a.Id], name: "Only A"));
        var shared = (await (await client.PostAsJsonAsync("/api/pay-schedules", MidMonth([a.Id, b.Id], [], name: "Shared")))
            .Content.ReadFromJsonAsync<Schedule>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/companies/{a.Id}")).StatusCode);

        var left = await Schedules(client);
        Assert.Equal(["Monthly", "Shared"], left.Select(s => s.Name).Order());
        Assert.Equal([b.Id], left.Single(s => s.Id == shared.Id).CompanyIds);
    }

    [Fact]
    public async Task Another_organization_and_plain_members_get_nothing()
    {
        var (a, _, orgId) = await Onboarded();
        var company = await AddCompany(a, "Oliech Restaurants Ltd");
        var monthly = Assert.Single(await Schedules(a));
        var (b, bEmail, _) = await Onboarded();

        Assert.DoesNotContain(await Schedules(b), s => s.Id == monthly.Id);
        Assert.Equal("ONB-3526", (await ErrorFrom(await b.DeleteAsync($"/api/pay-schedules/{monthly.Id}"))).Code);

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

        var res = await b.PostAsJsonAsync("/api/pay-schedules", MidMonth([company.Id], []));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("ACC-8001", (await ErrorFrom(res)).Code);
    }
}
