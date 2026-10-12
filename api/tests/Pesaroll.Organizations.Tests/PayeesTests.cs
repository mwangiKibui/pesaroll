using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-07: who a deduction's money is paid to. Payees are the organization's, paid by bank, M-Pesa
/// paybill, cheque or cash; deductions and employer costs name one; each person's member or
/// account number goes on who gets the item.
/// </summary>
public sealed class PayeesTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Item(Guid Id);
    private sealed record Payee(Guid Id, string Name, string Method, string? BankCode, string? BankName, string? BranchCode, string? AccountNumber,
        string? Paybill, string? PaybillAccount, string? ChequePayableTo, string? OurReference, int UsedBy, string? Institution = null, string? SaccoCode = null, string? SaccoName = null,
        string? SaccoBranchName = null, string? ReferenceSource = null);
    private sealed record Sacco(string Code, string Name, List<Branch> Branches);
    private sealed record Branch(string Code, string Name);
    private sealed record PayItem(Guid Id, string Name, Guid? PayeeId);
    private sealed record Person(Guid EmployeeId, string? Reference);
    private sealed record Assignment(Guid CompanyId, List<Person> People);
    private sealed record Mine(string Name, string? Reference, string? PayeeName);

    private async Task<(HttpClient Client, Company Company)> Onboarded()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email = $"payees-{Guid.NewGuid():N}@example.com", password = "Mombasa-raha-7",
            roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = $"Org {Guid.NewGuid():N}" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        var company = (await (await client.PostAsJsonAsync("/api/companies", new { legalName = "Pesaroll Ltd", countryCode = "KE", town = "Nairobi", regionCode = "047" }))
            .Content.ReadFromJsonAsync<Company>())!;
        return (client, company);
    }

    private static async Task<Guid> Make(HttpClient client, string path, object body)
    {
        var res = await client.PostAsJsonAsync(path, body);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<Item>())!.Id;
    }

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task Payees_are_paid_one_way_with_that_ways_details()
    {
        var (client, _) = await Onboarded();

        var stima = await Make(client, "/api/payees", new { name = "Stima SACCO", method = "mpesa_paybill", paybill = "510800", paybillAccount = "EMP-4471", ourReference = "EMP-4471" });
        await Make(client, "/api/payees", new { name = "Hotel Workers Union", method = "bank", institution = "bank", bankCode = "68", branchCode = "047", accountNumber = "0123 4567 89012" });
        var saccos = (await client.GetFromJsonAsync<List<Sacco>>("/api/saccos"))!;
        Assert.Equal(["Stima SACCO", "Mentor SACCO"], saccos.Select(s => s.Name));
        Assert.Contains("Kenol", saccos[1].Branches.Select(b => b.Name));
        await Make(client, "/api/payees", new { name = "Teachers Welfare Fund", method = "bank", institution = "sacco", saccoCode = "STIMA", saccoBranchCode = "005", accountNumber = "ST-00451", bankCode = "68" });
        await Make(client, "/api/payees", new { name = "Jubilee Insurance", method = "cheque", chequePayableTo = "Jubilee Health Insurance Ltd", paybill = "999999" });   // a stray paybill is dropped

        var list = (await client.GetFromJsonAsync<List<Payee>>("/api/payees"))!;
        Assert.Equal(["Hotel Workers Union", "Jubilee Insurance", "Stima SACCO", "Teachers Welfare Fund"], list.Select(p => p.Name));
        var union = list[0];
        Assert.Equal(("bank", "bank", "Equity Bank", "0123456789012"), (union.Method, union.Institution, union.BankName, union.AccountNumber));
        Assert.Null(list[1].Paybill);
        var welfare = list[3];
        Assert.Equal(("sacco", "Stima SACCO", "Mombasa", "ST-00451", (string?)null),
            (welfare.Institution, welfare.SaccoName, welfare.SaccoBranchName, welfare.AccountNumber, welfare.BankCode));   // no stray bank

        var bad = await ErrorFrom(await client.PostAsJsonAsync("/api/payees", new { name = "X", method = "bank", institution = "bank", bankCode = "68", branchCode = "999", accountNumber = "123" }));
        Assert.Equal("ONB-3548", bad.Code);
        Assert.Equal(["accountNumber", "branchCode", "name"], bad.Fields!.Keys.Order());
        var sacco = await ErrorFrom(await client.PostAsJsonAsync("/api/payees", new { name = "No SACCO", method = "bank", institution = "sacco", accountNumber = "MW-1" }));
        Assert.Equal("Choose the SACCO.", sacco.Fields!["saccoCode"]);
        var branch = await ErrorFrom(await client.PostAsJsonAsync("/api/payees", new { name = "Wrong branch", method = "bank", institution = "sacco", saccoCode = "MENTOR", saccoBranchCode = "012", accountNumber = "MT-1" }));
        Assert.Equal("Choose a branch of Mentor SACCO.", branch.Fields!["saccoBranchCode"]);
        var which = await ErrorFrom(await client.PostAsJsonAsync("/api/payees", new { name = "Bank or SACCO", method = "bank", accountNumber = "123456" }));
        Assert.Equal("Choose bank or SACCO.", which.Fields!["institution"]);
        var paybill = await ErrorFrom(await client.PostAsJsonAsync("/api/payees", new { name = "Bad paybill", method = "mpesa_paybill", paybill = "12" }));
        Assert.Equal("Enter the paybill number: 5 to 7 digits.", paybill.Fields!["paybill"]);
        var twin = await client.PostAsJsonAsync("/api/payees", new { name = "stima sacco", method = "cash" });
        Assert.Equal(("ONB-3551", HttpStatusCode.Conflict), ((await ErrorFrom(twin)).Code, twin.StatusCode));

        // Accounts per person from their profiles: a transfer into each person's own bank account needs no
        // bank details of the payee's; a paybill no account; cheques and cash have no account numbers.
        var savings = await Make(client, "/api/payees", new { name = "Staff savings", method = "bank", referenceSource = "bank_account" });
        var helb = await Make(client, "/api/payees", new { name = "HELB", method = "mpesa_paybill", paybill = "200800", paybillAccount = "ignored", referenceSource = "national_id" });
        var cheque = await Make(client, "/api/payees", new { name = "Insurer", method = "cheque", referenceSource = "national_id" });
        var now = (await client.GetFromJsonAsync<List<Payee>>("/api/payees"))!;
        Assert.Equal(("bank_account", (string?)null, (string?)null), (now.Single(p => p.Id == savings).ReferenceSource, now.Single(p => p.Id == savings).Institution, now.Single(p => p.Id == savings).AccountNumber));
        Assert.Equal(("national_id", (string?)null), (now.Single(p => p.Id == helb).ReferenceSource, now.Single(p => p.Id == helb).PaybillAccount));
        Assert.Equal("typed", now.Single(p => p.Id == cheque).ReferenceSource);

        var edit = await client.PatchAsJsonAsync($"/api/payees/{stima}", new { name = "Stima DT SACCO", method = "mpesa_paybill", paybill = "510800", paybillAccount = "EMP-4471" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Contains("Stima DT SACCO", (await client.GetFromJsonAsync<List<Payee>>("/api/payees"))!.Select(p => p.Name));
    }

    [Fact]
    public async Task Deductions_name_their_payee_and_people_their_member_number()
    {
        var (client, company) = await Onboarded();
        var stima = await Make(client, "/api/payees", new { name = "Stima SACCO", method = "mpesa_paybill", paybill = "510800" });
        var shares = await Make(client, "/api/pay-items", new { name = "SACCO shares", kind = "deduction", calcMethod = "fixed", defaultAmount = 1500m, allCompanies = true, payeeId = stima });
        await Make(client, "/api/pay-items", new { name = "SACCO loan", kind = "deduction", calcMethod = "fixed", defaultAmount = 5000m, allCompanies = true, payeeId = stima });
        var earning = await ErrorFrom(await client.PostAsJsonAsync("/api/pay-items", new { name = "Bonus", kind = "earning", taxable = true, calcMethod = "each_run", allCompanies = true, payeeId = stima }));
        Assert.Equal("Earnings aren't paid to anyone else.", earning.Fields!["payeeId"]);
        var unknown = await ErrorFrom(await client.PostAsJsonAsync("/api/pay-items", new { name = "Union dues", kind = "deduction", calcMethod = "fixed", allCompanies = true, payeeId = Guid.NewGuid() }));
        Assert.Equal("Choose a payee from the list.", unknown.Fields!["payeeId"]);

        Assert.Equal(stima, (await client.GetFromJsonAsync<List<PayItem>>("/api/pay-items"))!.Single(i => i.Id == shares).PayeeId);
        Assert.Equal(2, (await client.GetFromJsonAsync<List<Payee>>("/api/payees"))!.Single().UsedBy);

        var wanjiru = await Make(client, "/api/employees", new { companyId = company.Id, firstName = "Wanjiru", lastName = "Kamau", hireDate = "2024-01-15", job = new { employmentType = "permanent" } });
        var res = await client.PutAsJsonAsync($"/api/pay-items/{shares}/assignments", new
        {
            companyId = company.Id, everyone = false, people = new object[] { new { employeeId = wanjiru, amount = 2000m, reference = " M-10234 " } },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("M-10234", Assert.Single(Assert.Single((await res.Content.ReadFromJsonAsync<List<Assignment>>())!).People).Reference);
        var mine = Assert.Single((await client.GetFromJsonAsync<List<Mine>>($"/api/employees/{wanjiru}/pay-items"))!);
        Assert.Equal(("SACCO shares", "M-10234", "Stima SACCO"), (mine.Name, mine.Reference, mine.PayeeName));

        // In use: not deleted.
        var delete = await client.DeleteAsync($"/api/payees/{stima}");
        Assert.Equal(("ONB-3550", HttpStatusCode.Conflict), ((await ErrorFrom(delete)).Code, delete.StatusCode));
        Assert.Equal("Stima SACCO is the payee of 2 earnings and deductions. Choose another payee for them first.", (await ErrorFrom(await client.DeleteAsync($"/api/payees/{stima}"))).Message);
    }
}
