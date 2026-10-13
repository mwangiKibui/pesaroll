using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ACC-03, AUTH-08: an administrator invites a colleague by email or SMS "as HR, for these
/// companies"; the link lets them set a password (or sign in with their account) and join with
/// exactly that access. Links work once, for 7 days; resending replaces the link; revoking ends it.
/// </summary>
public sealed class InvitationTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, Dictionary<string, string>? Fields);
    private sealed record Company(Guid Id, string LegalName);
    private sealed record Invite(Guid Id, string FirstName, string LastName, string? Email, string? Phone, string Group, bool AllCompanies,
        List<Guid> CompanyIds, string Status, int SendCount);
    private sealed record Invitation(string Organization, string InvitedBy, string FirstName, string LastName, string? Email, string? Phone,
        string Group, bool AllCompanies, List<string> Companies, bool HasAccount);
    private sealed record Grant(string Permission, bool AllCompanies, List<Guid> CompanyIds);
    private sealed record Me(Guid? OrganizationId, string SignedUpVia, List<Grant> Permissions);

    private const string Password = "Mombasa-raha-7";
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(HttpClient Client, string Email)> SignUp(string first = "Amina", string last = "Otieno")
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"invite-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = first, lastName = last, email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        Authorize(client, (await signup.Content.ReadFromJsonAsync<Token>())!);
        return (client, email);
    }

    private static void Authorize(HttpClient client, Token token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

    private async Task<(HttpClient Owner, string OwnerEmail, string OrgName, Company Westlands, Company Mombasa)> Organization()
    {
        var (owner, email) = await SignUp();
        var name = $"Org {Guid.NewGuid():N}";
        var created = (await (await owner.PostAsJsonAsync("/api/organizations", new { name })).Content.ReadFromJsonAsync<Created>())!;
        Authorize(owner, created.Token);
        async Task<Company> Add(string legalName) => (await (await owner.PostAsJsonAsync("/api/companies",
            new { legalName, countryCode = "KE", town = "Nairobi", regionCode = "047" })).Content.ReadFromJsonAsync<Company>())!;
        return (owner, email, name, await Add("Westlands Ltd"), await Add("Mombasa Ltd"));
    }

    /// <summary>The token in the latest invitation sent to this email or phone (from the queued message's link).</summary>
    private static async Task<(string Channel, string Token)> Sent(string recipient)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select channel, data->>'link' from notification_deliveries where recipient = @r and template = 'team_invitation' order by created_at desc limit 1", connection);
        command.Parameters.AddWithValue("r", recipient);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no invitation was sent to {recipient}");
        var link = reader.GetString(1);
        Assert.Contains("/invite/", link);
        return (reader.GetString(0), link[(link.LastIndexOf('/') + 1)..]);
    }

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    private HttpClient Guest() => api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    [Fact]
    public async Task An_invitation_by_email_as_HR_for_one_company_gives_exactly_that()
    {
        var (owner, _, orgName, westlands, _) = await Organization();
        var email = $"mary-{Guid.NewGuid():N}@example.com";
        var res = await owner.PostAsJsonAsync("/api/team/invitations", new
        {
            firstName = "Mary", lastName = "Wanjiku", email = email.ToUpperInvariant(), group = "hr", allCompanies = false, companyIds = new[] { westlands.Id },
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var invite = (await res.Content.ReadFromJsonAsync<Invite>())!;
        Assert.Equal((email, "hr", false, "open", 1), (invite.Email, invite.Group, invite.AllCompanies, invite.Status, invite.SendCount));
        Assert.Single((await owner.GetFromJsonAsync<List<Invite>>("/api/team/invitations"))!, i => i.Id == invite.Id);

        var (channel, token) = await Sent(email);
        Assert.Equal("email", channel);
        var guest = Guest();
        var shown = (await guest.GetFromJsonAsync<Invitation>($"/api/invitations/{token}"))!;
        Assert.Equal((orgName, "Amina Otieno", "Mary", "hr", false, false), (shown.Organization, shown.InvitedBy, shown.FirstName, shown.Group, shown.AllCompanies, shown.HasAccount));
        Assert.Equal(["Westlands Ltd"], shown.Companies);

        Assert.Equal("AUTH-1016", (await ErrorFrom(await guest.PostAsJsonAsync($"/api/invitations/{token}/accept", new { firstName = "", lastName = "Wanjiku", password = Password }))).Code);
        var accepted = await guest.PostAsJsonAsync($"/api/invitations/{token}/accept", new { firstName = "Mary", lastName = "Wanjiku Kariuki", password = Password });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Authorize(guest, (await accepted.Content.ReadFromJsonAsync<Token>())!);

        var me = (await guest.GetFromJsonAsync<Me>("/api/auth/me"))!;
        Assert.Equal("invitation", me.SignedUpVia);
        Assert.Equal(["employees.manage", "leave.manage", "locations.manage", "structure.manage"], me.Permissions.Select(p => p.Permission).Order());
        Assert.All(me.Permissions, p => Assert.Equal([westlands.Id], p.CompanyIds));
        Assert.Equal([westlands.Id], (await guest.GetFromJsonAsync<List<Company>>("/api/companies"))!.Select(c => c.Id));

        // Once: the link is used, and the invitation leaves the list.
        Assert.Equal("AUTH-1013", (await ErrorFrom(await Guest().GetAsync($"/api/invitations/{token}"))).Code);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<List<Invite>>("/api/team/invitations"))!, i => i.Id == invite.Id);
        // They sign in like anyone else.
        Assert.Equal(HttpStatusCode.OK, (await Guest().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task By_SMS_as_an_administrator_and_someone_with_an_account_signs_in_to_accept()
    {
        var (owner, _, _, westlands, mombasa) = await Organization();
        var (_, joyEmail) = await SignUp("Joy", "Achieng");
        var phone = $"07{Random.Shared.Next(10000000, 99999999)}";
        await using (var connection = new NpgsqlConnection(Db))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("update users set phone = @phone where email = @email", connection);
            command.Parameters.AddWithValue("phone", "+254" + phone[1..]);
            command.Parameters.AddWithValue("email", joyEmail);
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/team/invitations",
            new { firstName = "Joy", lastName = "Achieng", phone, group = "administrators", allCompanies = true, companyIds = Array.Empty<Guid>() })).StatusCode);
        var (channel, token) = await Sent("+254" + phone[1..]);
        Assert.Equal("sms", channel);

        var guest = Guest();
        Assert.True((await guest.GetFromJsonAsync<Invitation>($"/api/invitations/{token}"))!.HasAccount);
        Assert.Equal("AUTH-1001", (await ErrorFrom(await guest.PostAsJsonAsync($"/api/invitations/{token}/accept", new { password = "wrong-password" }))).Code);
        var accepted = await guest.PostAsJsonAsync($"/api/invitations/{token}/accept", new { password = Password });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Authorize(guest, (await accepted.Content.ReadFromJsonAsync<Token>())!);
        Assert.Equal([westlands.Id, mombasa.Id], (await guest.GetFromJsonAsync<List<Company>>("/api/companies"))!.Select(c => c.Id).Order());
        Assert.Contains((await guest.GetFromJsonAsync<Me>("/api/auth/me"))!.Permissions, p => p.Permission == "team.manage" && p.AllCompanies);
    }

    [Fact]
    public async Task Resending_replaces_the_link_and_revoking_ends_it()
    {
        var (owner, _, _, _, _) = await Organization();
        var email = $"otieno-{Guid.NewGuid():N}@example.com";
        var invite = (await (await owner.PostAsJsonAsync("/api/team/invitations",
            new { firstName = "Otieno", lastName = "Ouma", email, group = "payroll", allCompanies = true, companyIds = Array.Empty<Guid>() })).Content.ReadFromJsonAsync<Invite>())!;
        var (_, first) = await Sent(email);

        var resent = await owner.PostAsync($"/api/team/invitations/{invite.Id}/resend", null);
        Assert.Equal(2, (await resent.Content.ReadFromJsonAsync<Invite>())!.SendCount);
        var (_, second) = await Sent(email);
        Assert.NotEqual(first, second);
        Assert.Equal("AUTH-1011", (await ErrorFrom(await Guest().GetAsync($"/api/invitations/{first}"))).Code);
        Assert.Equal(HttpStatusCode.OK, (await Guest().GetAsync($"/api/invitations/{second}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/team/invitations/{invite.Id}")).StatusCode);
        Assert.Equal("AUTH-1014", (await ErrorFrom(await Guest().GetAsync($"/api/invitations/{second}"))).Code);
        Assert.Equal("ACC-8006", (await ErrorFrom(await owner.PostAsync($"/api/team/invitations/{invite.Id}/resend", null))).Code);
        Assert.Equal("ACC-8005", (await ErrorFrom(await owner.DeleteAsync($"/api/team/invitations/{Guid.NewGuid()}"))).Code);
        Assert.Equal("AUTH-1011", (await ErrorFrom(await Guest().GetAsync("/api/invitations/not-a-real-link"))).Code);

        // Revoked: they can be invited again.
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/team/invitations",
            new { firstName = "Otieno", lastName = "Ouma", email, group = "payroll", allCompanies = true, companyIds = Array.Empty<Guid>() })).StatusCode);
    }

    [Fact]
    public async Task An_expired_link_says_so_and_can_be_resent()
    {
        var (owner, _, _, _, _) = await Organization();
        var email = $"late-{Guid.NewGuid():N}@example.com";
        var invite = (await (await owner.PostAsJsonAsync("/api/team/invitations",
            new { firstName = "Late", lastName = "Comer", email, group = "approvers", allCompanies = true, companyIds = Array.Empty<Guid>() })).Content.ReadFromJsonAsync<Invite>())!;
        var (_, token) = await Sent(email);
        await using (var connection = new NpgsqlConnection(Db))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("update invitations set expires_at = now() - interval '1 minute' where id = @id", connection);
            command.Parameters.AddWithValue("id", invite.Id);
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal("AUTH-1012", (await ErrorFrom(await Guest().GetAsync($"/api/invitations/{token}"))).Code);
        Assert.Equal("expired", (await owner.GetFromJsonAsync<List<Invite>>("/api/team/invitations"))!.Single(i => i.Id == invite.Id).Status);
        Assert.Equal("open", (await (await owner.PostAsync($"/api/team/invitations/{invite.Id}/resend", null)).Content.ReadFromJsonAsync<Invite>())!.Status);
    }

    [Fact]
    public async Task Only_administrators_invite_and_the_details_are_checked()
    {
        var (owner, ownerEmail, _, westlands, _) = await Organization();
        object Body(string? email, string? phone = null, string group = "hr", bool all = true, Guid[]? companies = null) =>
            new { firstName = "Mary", lastName = "Wanjiku", email, phone, group, allCompanies = all, companyIds = companies ?? [] };

        Assert.Equal("ACC-8002", (await ErrorFrom(await owner.PostAsJsonAsync("/api/team/invitations", Body(ownerEmail)))).Code);
        var bad = await ErrorFrom(await owner.PostAsJsonAsync("/api/team/invitations", Body(null, null, "owners", false)));
        Assert.Equal("ACC-8004", bad.Code);
        Assert.Equal(["companyIds", "contact", "group"], bad.Fields!.Keys.Order());
        Assert.Equal("ACC-8004", (await ErrorFrom(await owner.PostAsJsonAsync("/api/team/invitations", Body("not-an-email")))).Code);

        var email = $"mary-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/team/invitations", Body(email, companies: [westlands.Id], all: false))).StatusCode);
        Assert.Equal("ACC-8003", (await ErrorFrom(await owner.PostAsJsonAsync("/api/team/invitations", Body(email)))).Code);

        // Mary, once in as HR, can't invite anyone.
        var (_, token) = await Sent(email);
        var mary = Guest();
        Authorize(mary, (await (await mary.PostAsJsonAsync($"/api/invitations/{token}/accept", new { firstName = "Mary", lastName = "Wanjiku", password = Password }))
            .Content.ReadFromJsonAsync<Token>())!);
        Assert.Equal("ACC-8001", (await ErrorFrom(await mary.PostAsJsonAsync("/api/team/invitations", Body($"x-{Guid.NewGuid():N}@example.com")))).Code);
        Assert.Equal("ACC-8001", (await ErrorFrom(await mary.GetAsync("/api/team/invitations"))).Code);
    }
}
