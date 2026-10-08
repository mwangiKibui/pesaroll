using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pesaroll.Shared.Security;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-03, paying accounts: where each company's payroll is paid from. The account number is stored
/// encrypted and only ever shown masked, and history is kept. A company can't run payroll without one.
/// </summary>
public sealed class PaymentAccountsTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Branch(string Code, string Name);
    private sealed record Bank(string Code, string Name, List<Branch> Branches);
    private sealed record Account(Guid Id, string Method, string? BankCode, string? BankName, string? BranchCode, string? BranchName,
        string? AccountName, string? AccountNumberMasked, string? MpesaShortcode, bool IsDefault);
    private sealed record Missing(string Kind, string Key);
    private sealed record Readiness(bool Ready, List<Missing> Missing);
    private sealed record Company(Guid Id, Readiness Readiness);

    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email, Guid OrganizationId, Guid CompanyId)> CompanyReadyButForAccount()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"pay-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse(Ceo) });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        var company = (await (await client.PostAsJsonAsync("/api/companies", new
        {
            legalName = "Mama Oliech Restaurants Ltd", countryCode = "KE",
            identifiers = new Dictionary<string, string> { ["kra_pin"] = "P051234567X", ["nssf_employer_no"] = "NS12345", ["sha_employer_no"] = "SHA9876" },
        })).Content.ReadFromJsonAsync<Company>())!;
        return (client, email, created.Organization.Id, company.Id);
    }

    private static object EquityAccount(string number = "0123-4567 8901") => new
    {
        method = "bank", bankCode = "68", branchCode = "047", accountName = "Mama Oliech Restaurants Ltd", accountNumber = number,
    };

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task Kenyan_banks_come_with_their_branches()
    {
        var (client, _, _, _) = await CompanyReadyButForAccount();

        var banks = (await client.GetFromJsonAsync<List<Bank>>("/api/banks?country=KE"))!;

        Assert.Equal(["Equity Bank", "KCB Bank", "NCBA Bank", "Absa Bank", "I&M Bank"], banks.Select(b => b.Name));
        Assert.All(banks, b => Assert.Equal(3, b.Branches.Count));
        Assert.Contains(new Branch("047", "Moi Avenue, Nairobi"), banks[0].Branches);
    }

    [Fact]
    public async Task Adding_a_bank_account_stores_the_number_encrypted_and_makes_the_company_ready()
    {
        var (client, _, _, companyId) = await CompanyReadyButForAccount();
        var path = $"/api/companies/{companyId}/payment-accounts";

        var res = await client.PostAsJsonAsync(path, EquityAccount());

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var account = (await res.Content.ReadFromJsonAsync<Account>())!;
        Assert.Equal(("bank", "Equity Bank", "Moi Avenue, Nairobi", "•••• 8901", true),
            (account.Method, account.BankName, account.BranchName, account.AccountNumberMasked, account.IsDefault));
        Assert.DoesNotContain("012345678901", await res.Content.ReadAsStringAsync());
        Assert.Equal(account.Id, Assert.Single((await client.GetFromJsonAsync<List<Account>>(path))!).Id);

        // In the database: only the last four in the clear; the number decrypts for this company only.
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select account_no_enc, account_last4 from company_payment_accounts where id = @id", connection);
        command.Parameters.AddWithValue("id", account.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var stored = (byte[])reader[0];
        Assert.Equal("8901", reader.GetString(1));
        var encryptor = api.Services.GetRequiredService<FieldEncryptor>();
        Assert.Equal("012345678901", encryptor.Decrypt(stored, PaymentAccountContext(companyId)));

        var company = (await client.GetFromJsonAsync<List<Company>>("/api/companies"))!.Single();
        Assert.True(company.Readiness.Ready);
    }

    private static string PaymentAccountContext(Guid companyId) => $"company_payment_accounts.account_no:{companyId}";

    [Fact]
    public async Task Details_are_checked_against_the_bank_list_and_formats()
    {
        var (client, _, _, companyId) = await CompanyReadyButForAccount();
        var path = $"/api/companies/{companyId}/payment-accounts";

        var bank = await client.PostAsJsonAsync(path, new { method = "bank", bankCode = "68", branchCode = "100", accountName = "A", accountNumber = "12ab" });
        Assert.Equal(HttpStatusCode.BadRequest, bank.StatusCode);
        var error = await ErrorFrom(bank);
        Assert.Equal("ONB-3510", error.Code);
        Assert.Equal(["accountName", "accountNumber", "branchCode"], error.Fields!.Keys.Order());

        var unknownBank = await ErrorFrom(await client.PostAsJsonAsync(path, new { method = "bank", bankCode = "99", branchCode = "001", accountName = "Mama Oliech", accountNumber = "0123456789" }));
        Assert.Equal(["bankCode"], unknownBank.Fields!.Keys);

        var mpesa = await ErrorFrom(await client.PostAsJsonAsync(path, new { method = "mpesa", mpesaShortcode = "12" }));
        Assert.Equal(["mpesaShortcode"], mpesa.Fields!.Keys);

        var method = await ErrorFrom(await client.PostAsJsonAsync(path, new { method = "cash" }));
        Assert.Equal(["method"], method.Fields!.Keys);
        Assert.Empty((await client.GetFromJsonAsync<List<Account>>(path))!);
    }

    [Fact]
    public async Task An_mpesa_account_can_be_added_and_made_the_default()
    {
        var (client, _, _, companyId) = await CompanyReadyButForAccount();
        var path = $"/api/companies/{companyId}/payment-accounts";
        var bank = (await (await client.PostAsJsonAsync(path, EquityAccount())).Content.ReadFromJsonAsync<Account>())!;

        var res = await client.PostAsJsonAsync(path, new { method = "mpesa", mpesaShortcode = " 600123 " });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var mpesa = (await res.Content.ReadFromJsonAsync<Account>())!;
        Assert.Equal(("mpesa", "600123", false), (mpesa.Method, mpesa.MpesaShortcode, mpesa.IsDefault));

        var made = await client.PostAsync($"{path}/{mpesa.Id}/default", null);

        Assert.Equal(HttpStatusCode.OK, made.StatusCode);
        var accounts = (await made.Content.ReadFromJsonAsync<List<Account>>())!;
        Assert.Equal([(bank.Id, false), (mpesa.Id, true)], accounts.Select(a => (a.Id, a.IsDefault)));
    }

    [Fact]
    public async Task Removing_closes_the_account_but_keeps_its_history_and_the_next_one_becomes_default()
    {
        var (client, _, _, companyId) = await CompanyReadyButForAccount();
        var path = $"/api/companies/{companyId}/payment-accounts";
        var first = (await (await client.PostAsJsonAsync(path, EquityAccount())).Content.ReadFromJsonAsync<Account>())!;
        var second = (await (await client.PostAsJsonAsync(path, new { method = "mpesa", mpesaShortcode = "600123" }))
            .Content.ReadFromJsonAsync<Account>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{path}/{first.Id}")).StatusCode);

        var left = Assert.Single((await client.GetFromJsonAsync<List<Account>>(path))!);
        Assert.Equal((second.Id, true), (left.Id, left.IsDefault));
        await using (var connection = new NpgsqlConnection(Db))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("select closed_at is not null and closed_by is not null from company_payment_accounts where id = @id", connection);
            command.Parameters.AddWithValue("id", first.Id);
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{path}/{second.Id}")).StatusCode);
        var company = (await client.GetFromJsonAsync<List<Company>>("/api/companies"))!.Single();
        Assert.Equal(["payment_account"], company.Readiness.Missing.Select(m => m.Key));

        var again = await client.DeleteAsync($"{path}/{second.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("ONB-3511", (await ErrorFrom(again)).Code);
    }

    [Fact]
    public async Task Another_organizations_company_and_plain_members_get_nothing()
    {
        var (a, _, orgId, companyId) = await CompanyReadyButForAccount();
        await a.PostAsJsonAsync($"/api/companies/{companyId}/payment-accounts", EquityAccount());
        var (b, bEmail, _, _) = await CompanyReadyButForAccount();

        var other = await b.GetAsync($"/api/companies/{companyId}/payment-accounts");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal("ONB-3509", (await ErrorFrom(other)).Code);
        Assert.Equal("ONB-3509", (await ErrorFrom(await b.PostAsJsonAsync($"/api/companies/{companyId}/payment-accounts", EquityAccount()))).Code);

        // B joins A's organization as a plain member: they can't add or change paying accounts.
        await using (var connection = new NpgsqlConnection(Db))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                insert into memberships (id, organization_id, user_id) select uuidv7(), @org, id from users where email = @email;
                update memberships set status = 'removed' where user_id = (select id from users where email = @email) and organization_id <> @org;
                """, connection);
            command.Parameters.AddWithValue("org", orgId);
            command.Parameters.AddWithValue("email", bEmail);
            await command.ExecuteNonQueryAsync();
        }
        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = bEmail, password = Password });
        b.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);

        var res = await b.PostAsJsonAsync($"/api/companies/{companyId}/payment-accounts", EquityAccount());

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("ACC-8001", (await ErrorFrom(res)).Code);
    }
}
