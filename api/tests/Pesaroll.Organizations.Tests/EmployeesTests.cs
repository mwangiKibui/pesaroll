using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-09, the Employees step: people with their numbers (encrypted, masked, no duplicates), job
/// (defaults from the company and schedule), pay (monthly, daily or hourly) and where pay goes,
/// and what's still missing before payroll.
/// </summary>
public sealed class EmployeesTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Schedule(Guid Id, string Name, List<Guid> DefaultFor);
    private sealed record Plan(Guid Id, string Name, List<Guid> DefaultFor);
    private sealed record Location(Guid Id, string Name, List<Guid> MainFor);
    private sealed record Missing(string Key);
    private sealed record Readiness(bool Ready, List<Missing> Missing);
    private sealed record Job(Guid? PositionId, Guid? DepartmentId, Guid? UnitId, Guid? LocationId, Guid? LineManagerId, Guid PayScheduleId,
        Guid LeavePlanId, string EmploymentType, DateOnly? ContractEnd);
    private sealed record Pay(decimal BasicAmount, string PayBasis, string Currency);
    private sealed record Account(string Method, string? BankCode, string? BranchCode, string? AccountMasked, string? MpesaMasked);
    private sealed record Employee(Guid Id, Guid CompanyId, string StaffNo, string FirstName, string? MiddleName, string LastName, string? Phone,
        string? Email, string? NationalIdMasked, string? KraPinMasked, string? NssfNo, string? ShaNo, DateOnly HireDate, DateOnly? ExitDate,
        Job Job, Pay? Pay, Account? Account, Readiness Readiness);
    private sealed record Row(Guid Id, string StaffNo, string Name, string? PositionTitle, string? DepartmentName, decimal? BasicAmount, string? PayBasis,
        DateOnly? ExitDate, Readiness Readiness);
    private sealed record Page(List<Row> Items, int Total, int PageNumber, int PageSize);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email, Guid OrganizationId)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"emp-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        return (client, email, created.Organization.Id);
    }

    private static async Task<Company> AddCompany(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/companies", new { legalName = name, countryCode = "KE", town = "Nairobi", regionCode = "047" }))
            .Content.ReadFromJsonAsync<Company>())!;

    private static async Task<Employee> Add(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/employees", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<Employee>())!;
    }

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    private static object Full(Guid companyId, string kraPin = "a012345678z", string nationalId = "12345678") => new
    {
        companyId, firstName = "Wanjiru", lastName = "Kamau", phone = "0798 765 432", email = "Wanjiru@Example.com", nationalId, kraPin,
        nssfNo = "2098765432", shaNo = "CR1234567-8", hireDate = "2024-01-15",
        job = new { employmentType = "permanent" },
        pay = new { basicAmount = 85000m, payBasis = "monthly" },
        account = new { method = "bank", bankCode = "68", branchCode = "047", accountNumber = "0123456789012" },
    };

    [Fact]
    public async Task An_employee_gets_the_companys_defaults_and_shows_whats_missing_for_payroll()
    {
        var (client, _, _) = await Onboarded();
        var company = await AddCompany(client, "Mama Oliech Restaurants Ltd");
        var schedule = Assert.Single((await client.GetFromJsonAsync<List<Schedule>>("/api/pay-schedules"))!);
        var plan = Assert.Single((await client.GetFromJsonAsync<List<Plan>>("/api/leave-plans"))!);
        var hq = Assert.Single((await client.GetFromJsonAsync<List<Location>>("/api/locations"))!);

        var amina = await Add(client, new { companyId = company.Id, firstName = "Amina", lastName = "Otieno", hireDate = "2025-03-01", job = new { employmentType = "permanent" } });

        Assert.Equal(("EMP-01", schedule.Id, plan.Id, hq.Id), (amina.StaffNo, amina.Job.PayScheduleId, amina.Job.LeavePlanId, amina.Job.LocationId));
        Assert.False(amina.Readiness.Ready);
        Assert.Equal(["kra_pin", "national_id", "basic_pay", "payment_account"], amina.Readiness.Missing.Select(m => m.Key));
    }

    [Fact]
    public async Task Numbers_are_checked_stored_one_way_and_only_shown_masked()
    {
        var (client, _, _) = await Onboarded();
        var company = await AddCompany(client, "Oliech Restaurants Ltd");

        var wanjiru = await Add(client, Full(company.Id));

        Assert.Equal(("•••• 678Z", "•••• 5678", "+254798765432", "wanjiru@example.com"), (wanjiru.KraPinMasked, wanjiru.NationalIdMasked, wanjiru.Phone, wanjiru.Email));
        Assert.Equal(("bank", "68", "047", "•••• 9012"), (wanjiru.Account!.Method, wanjiru.Account.BankCode, wanjiru.Account.BranchCode, wanjiru.Account.AccountMasked));
        Assert.Equal((85000m, "monthly", "KES"), (wanjiru.Pay!.BasicAmount, wanjiru.Pay.PayBasis, wanjiru.Pay.Currency));
        Assert.True(wanjiru.Readiness.Ready);
        var json = await client.GetStringAsync($"/api/employees/{wanjiru.Id}");
        Assert.DoesNotContain("A012345678Z", json);
        Assert.DoesNotContain("12345678\"", json);
        Assert.DoesNotContain("0123456789012", json);

        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select position(convert_to('A012345678Z', 'UTF8') in kra_pin_enc) > 0 from employees where id = @id", connection);
        command.Parameters.AddWithValue("id", wanjiru.Id);
        Assert.False((bool)(await command.ExecuteScalarAsync())!);   // encrypted at rest
    }

    [Fact]
    public async Task Details_are_checked_and_problems_named_by_field()
    {
        var (client, _, _) = await Onboarded();
        var company = await AddCompany(client, "Oliech Restaurants Ltd");
        var other = await AddCompany(client, "Oliech Catering Ltd");
        var hourly = (await (await client.PostAsJsonAsync("/api/pay-schedules", new
        {
            name = "Catering only", allCompanies = false, companyIds = new[] { other.Id },
        })).Content.ReadFromJsonAsync<Schedule>())!;

        var res = await client.PostAsJsonAsync("/api/employees", new
        {
            companyId = company.Id, firstName = "", lastName = "Kamau", phone = "020 123 4567", email = "wanjiru@", nationalId = "12", kraPin = "P051234567X",
            hireDate = "2024-01-15", exitDate = "2023-01-01",
            job = new { employmentType = "contractor", payScheduleId = hourly.Id },
            pay = new { basicAmount = 0m, payBasis = "weekly" },
            account = new { method = "mpesa", mpesaPhone = "12345" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await ErrorFrom(res);
        Assert.Equal("ONB-3538", error.Code);
        Assert.Equal(["account.mpesaPhone", "email", "exitDate", "firstName", "job.employmentType", "job.payScheduleId", "kraPin", "nationalId",
            "pay.basicAmount", "pay.payBasis", "phone"], error.Fields!.Keys.Order());
    }

    [Fact]
    public async Task Two_current_employees_cant_share_an_ID_number_or_KRA_PIN_but_a_rehire_can()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        var first = await Add(client, Full(a.Id));

        var twin = await client.PostAsJsonAsync("/api/employees", Full(b.Id, nationalId: "87654321"));   // same KRA PIN, another company
        Assert.Equal(HttpStatusCode.Conflict, twin.StatusCode);
        var error = await ErrorFrom(twin);
        Assert.Equal(("ONB-3540", "kraPin"), (error.Code, Assert.Single(error.Fields!.Keys)));
        Assert.Contains(first.StaffNo, error.Message);

        var left = await client.PatchAsJsonAsync($"/api/employees/{first.Id}", new
        {
            companyId = a.Id, firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", exitDate = "2025-06-30", job = new { employmentType = "permanent" },
        });
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        Assert.Equal(DateOnly.Parse("2025-06-30"), (await left.Content.ReadFromJsonAsync<Employee>())!.ExitDate);

        await Add(client, Full(b.Id));   // rehired at the other company
    }

    [Fact]
    public async Task A_daily_paid_casual_follows_the_daily_schedules_leave_plan()
    {
        var (client, _, _) = await Onboarded();
        var company = await AddCompany(client, "Oliech Restaurants Ltd");
        var standard = Assert.Single((await client.GetFromJsonAsync<List<Plan>>("/api/leave-plans"))!);
        var daily = (await (await client.PostAsJsonAsync("/api/pay-schedules", new { name = "Daily", frequency = "daily", allCompanies = true }))
            .Content.ReadFromJsonAsync<Schedule>())!;
        var types = (await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/leave-types"))!;
        var rules = (await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/leave-plans"))!;
        var dailyPlan = (await (await client.PostAsJsonAsync("/api/leave-plans", new
        {
            name = "Daily-paid", allCompanies = true, defaultForSchedules = new[] { daily.Id },
            rules = ((System.Text.Json.JsonElement)rules[0]["rules"]).EnumerateArray().Select(r => new
            {
                leaveTypeId = r.GetProperty("leaveTypeId").GetGuid(), earnMethod = r.GetProperty("earnMethod").GetString(),
                amount = r.GetProperty("amount").ValueKind == System.Text.Json.JsonValueKind.Null ? (decimal?)null : r.GetProperty("amount").GetDecimal(),
                unit = r.GetProperty("unit").GetString(), halfPay = r.GetProperty("halfPay").GetDecimal(), waitingMonths = r.GetProperty("waitingMonths").GetInt32(),
            }),
        })).Content.ReadFromJsonAsync<Plan>())!;

        var casual = await Add(client, new
        {
            companyId = company.Id, firstName = "Otieno", lastName = "Ouma", hireDate = "2025-08-01",
            job = new { employmentType = "casual", payScheduleId = daily.Id }, pay = new { basicAmount = 900m, payBasis = "daily" },
        });

        Assert.Equal((daily.Id, dailyPlan.Id, "daily"), (casual.Job.PayScheduleId, casual.Job.LeavePlanId, casual.Pay!.PayBasis));
        Assert.NotEqual(standard.Id, casual.Job.LeavePlanId);
    }

    [Fact]
    public async Task The_list_is_searched_paged_and_filtered_on_the_server()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        for (var i = 1; i <= 12; i++)
        {
            await Add(client, new { companyId = i <= 8 ? a.Id : b.Id, firstName = $"Person{i:00}", lastName = i == 3 ? "Wanjala" : "Kamau",
                hireDate = "2024-01-01", exitDate = i == 12 ? "2025-01-31" : null, job = new { employmentType = "permanent" } });
        }

        var first = (await client.GetFromJsonAsync<Page>("/api/employees?pageSize=5&page=1"))!;
        Assert.Equal((11, 5), (first.Total, first.Items.Count));   // current staff unless asked
        Assert.Equal(1, (await client.GetFromJsonAsync<Page>("/api/employees?search=wanjala"))!.Total);
        Assert.Equal(1, (await client.GetFromJsonAsync<Page>($"/api/employees?search=EMP-03&companyId={a.Id}"))!.Total);   // staff numbers are per company
        Assert.Equal(2, (await client.GetFromJsonAsync<Page>("/api/employees?search=EMP-03"))!.Total);
        Assert.Equal(3, (await client.GetFromJsonAsync<Page>($"/api/employees?companyId={b.Id}"))!.Total);
        Assert.Equal(11, (await client.GetFromJsonAsync<Page>($"/api/employees?companyIds={a.Id}&companyIds={b.Id}"))!.Total);
        Assert.Equal(1, (await client.GetFromJsonAsync<Page>("/api/employees?status=former"))!.Total);
        Assert.Equal(12, (await client.GetFromJsonAsync<Page>("/api/employees?status=all"))!.Total);
    }

    private sealed record Choice(Guid Id, string Name, string StaffNo, Guid? DepartmentId);

    [Fact]
    public async Task Line_managers_are_chosen_from_the_companys_current_staff()
    {
        var (client, _, _) = await Onboarded();
        var a = await AddCompany(client, "Oliech Restaurants Ltd");
        var b = await AddCompany(client, "Oliech Catering Ltd");
        await Add(client, new { companyId = a.Id, firstName = "Amina", lastName = "Otieno", hireDate = "2024-01-01", job = new { employmentType = "permanent" } });
        await Add(client, new { companyId = a.Id, firstName = "Left", lastName = "Already", hireDate = "2024-01-01", exitDate = "2025-01-31", job = new { employmentType = "permanent" } });
        await Add(client, new { companyId = b.Id, firstName = "Other", lastName = "Company", hireDate = "2024-01-01", job = new { employmentType = "permanent" } });

        var choices = (await client.GetFromJsonAsync<List<Choice>>($"/api/employees/choices?companyId={a.Id}"))!;

        Assert.Equal(("Amina Otieno", "EMP-01"), (Assert.Single(choices).Name, choices[0].StaffNo));
    }

    [Fact]
    public async Task Changing_pay_or_job_keeps_the_history()
    {
        var (client, _, _) = await Onboarded();
        var company = await AddCompany(client, "Oliech Restaurants Ltd");
        var wanjiru = await Add(client, Full(company.Id));

        var res = await client.PatchAsJsonAsync($"/api/employees/{wanjiru.Id}", new
        {
            companyId = company.Id, firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15",
            job = new { employmentType = "contract", contractEnd = "2026-12-31" }, pay = new { basicAmount = 95000m, payBasis = "monthly" },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var edited = (await res.Content.ReadFromJsonAsync<Employee>())!;
        Assert.Equal((95000m, "contract", "•••• 678Z"), (edited.Pay!.BasicAmount, edited.Job.EmploymentType, edited.KraPinMasked));   // numbers left out are kept
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select (select count(*) from compensations where employee_id = @id), (select count(*) from employments where employee_id = @id), " +
            "(select count(*) from compensations where employee_id = @id and valid_to is null)", connection);
        command.Parameters.AddWithValue("id", wanjiru.Id);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.Equal((2L, 2L, 1L), (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
    }

    [Fact]
    public async Task What_employees_use_cant_be_deleted_from_under_them()
    {
        var (client, _, _) = await Onboarded();
        var company = await AddCompany(client, "Oliech Restaurants Ltd");
        var kitchen = (await (await client.PostAsJsonAsync("/api/structure/departments", new { name = "Kitchen", allCompanies = true }))
            .Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        var kitchenId = Guid.Parse(kitchen["id"].ToString()!);
        await Add(client, new { companyId = company.Id, firstName = "Amina", lastName = "Otieno", hireDate = "2025-03-01", job = new { employmentType = "permanent", departmentId = kitchenId } });

        var res = await client.DeleteAsync($"/api/structure/departments/{kitchenId}");

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var error = await ErrorFrom(res);
        Assert.Equal("ONB-3541", error.Code);
        Assert.Contains("1 employee", error.Message);
    }

    [Fact]
    public async Task Another_organization_and_plain_members_get_nothing()
    {
        var (a, _, orgId) = await Onboarded();
        var company = await AddCompany(a, "Oliech Restaurants Ltd");
        var wanjiru = await Add(a, Full(company.Id));
        var (b, bEmail, _) = await Onboarded();

        Assert.Equal(0, (await b.GetFromJsonAsync<Page>("/api/employees"))!.Total);
        Assert.Equal("ONB-3539", (await ErrorFrom(await b.GetAsync($"/api/employees/{wanjiru.Id}"))).Code);

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

        var res = await b.PostAsJsonAsync("/api/employees", Full(company.Id, kraPin: "A099999999Z", nationalId: "99999999"));
        Assert.Equal((HttpStatusCode.Forbidden, "ACC-8001"), (res.StatusCode, (await ErrorFrom(res)).Code));
    }
}
