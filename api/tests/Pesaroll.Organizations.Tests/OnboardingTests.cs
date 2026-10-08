using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-02, the organization step, through the real API against a PostgreSQL with the schema
/// deployed (scripts/check.sh sets PESAROLL_TEST_DB). Every test signs up fresh people.
/// </summary>
public sealed class OnboardingTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    public sealed class Api : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Pesaroll", System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")
                ?? throw new InvalidOperationException("PESAROLL_TEST_DB is not set. Run scripts/check.sh."));
            builder.UseSetting("Auth:SecureCookie", "false");
            builder.UseSetting("Auth:RateLimitPerMinute", "1000");
            builder.UseSetting("App:AllowedOrigins:0", "http://localhost:5173");
            builder.UseSetting("Auth:Tokens:Keys:0:Id", "test");
            builder.UseSetting("Auth:Tokens:Keys:0:Secret", Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray()));
        }
    }

    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Error(string Code, string Message, string Reference);
    private sealed record Me(Guid Id, Guid? OrganizationId, string SignedUpVia, bool CanOnboardOrganization);

    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Password = "Mombasa-raha-7";

    private async Task<(HttpClient Client, Token Token, string Email)> SignUp()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"onb-{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/api/auth/signup",
            new { firstName = "Amina", lastName = "Otieno", email, password = Password, roleId = Guid.Parse(Ceo) });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (client, (await res.Content.ReadFromJsonAsync<Token>())!, email);
    }

    private static HttpRequestMessage With(Token token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return request;
    }

    private static async Task<Error> ErrorFrom(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task Naming_the_organization_creates_it_and_returns_a_token_that_carries_it()
    {
        var (client, token, _) = await SignUp();

        var res = await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = "  Mama Oliech Group " }));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = (await res.Content.ReadFromJsonAsync<Created>())!;
        Assert.Equal("Mama Oliech Group", created.Organization.Name);
        Assert.Equal(created.Organization.Id.ToString(), new JsonWebToken(created.Token.AccessToken).GetPayloadValue<string>("org"));

        var current = await client.SendAsync(With(created.Token, HttpMethod.Get, "/api/organizations/current"));
        Assert.Equal(created.Organization, await current.Content.ReadFromJsonAsync<Org>());
        var me = await client.SendAsync(With(created.Token, HttpMethod.Get, "/api/auth/me"));
        Assert.Equal(created.Organization.Id, (await me.Content.ReadFromJsonAsync<Me>())!.OrganizationId);
    }

    [Fact]
    public async Task The_old_token_stops_working_and_a_refresh_or_new_sign_in_carries_the_organization()
    {
        var (client, token, email) = await SignUp();
        var created = (await (await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = "Kampuni Ltd" })))
            .Content.ReadFromJsonAsync<Created>())!;

        var stale = await client.SendAsync(With(token, HttpMethod.Get, "/api/auth/me"));
        Assert.Equal("AUTH-1010", (await ErrorFrom(stale)).Code);

        var refreshed = (await (await client.PostAsync("/api/auth/refresh", null)).Content.ReadFromJsonAsync<Token>())!;
        Assert.Equal(created.Organization.Id.ToString(), new JsonWebToken(refreshed.AccessToken).GetPayloadValue<string>("org"));

        var signin = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });
        var signedIn = (await signin.Content.ReadFromJsonAsync<Token>())!;
        Assert.Equal(created.Organization.Id.ToString(), new JsonWebToken(signedIn.AccessToken).GetPayloadValue<string>("org"));
    }

    [Fact]
    public async Task A_person_onboards_one_organization_only()
    {
        var (client, token, _) = await SignUp();
        var created = (await (await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = "First Ltd" })))
            .Content.ReadFromJsonAsync<Created>())!;

        var second = await client.SendAsync(With(created.Token, HttpMethod.Post, "/api/organizations", new { name = "Second Ltd" }));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ONB-3502", (await ErrorFrom(second)).Code);
    }

    [Fact]
    public async Task A_double_click_creates_one_organization()
    {
        var (client, token, _) = await SignUp();

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(i =>
            client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = "Twice Ltd" }))));

        // Exactly one wins. The others are refused either because the person now has an organization
        // (409 ONB-3502) or, if they arrive after the session switched, because their token no longer
        // matches the session (401 AUTH-1010; the app refreshes and then gets the 409).
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var refused in results.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            Assert.Contains((await ErrorFrom(refused)).Code, new[] { "ONB-3502", "AUTH-1010" });
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData(null)]
    public async Task The_name_must_be_2_to_120_characters(string? name)
    {
        var (client, token, _) = await SignUp();

        var res = await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("ONB-3501", (await ErrorFrom(res)).Code);
        var current = await client.SendAsync(With(token, HttpMethod.Get, "/api/organizations/current"));
        Assert.Equal("ONB-3503", (await ErrorFrom(current)).Code);
    }

    [Fact]
    public async Task A_name_over_120_characters_is_refused()
    {
        var (client, token, _) = await SignUp();

        var res = await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = new string('x', 121) }));

        Assert.Equal("ONB-3501", (await ErrorFrom(res)).Code);
    }

    [Fact]
    public async Task Someone_who_signed_up_themselves_can_onboard_an_organization_until_they_have()
    {
        var (client, token, _) = await SignUp();

        var before = await (await client.SendAsync(With(token, HttpMethod.Get, "/api/auth/me"))).Content.ReadFromJsonAsync<Me>();
        var created = (await (await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = "Later Ltd" })))
            .Content.ReadFromJsonAsync<Created>())!;
        var after = await (await client.SendAsync(With(created.Token, HttpMethod.Get, "/api/auth/me"))).Content.ReadFromJsonAsync<Me>();

        Assert.Equal(("self", true), (before!.SignedUpVia, before.CanOnboardOrganization));
        Assert.False(after!.CanOnboardOrganization);
    }

    [Fact]
    public async Task Someone_who_did_not_sign_up_themselves_cannot_onboard_an_organization()
    {
        var (client, token, email) = await SignUp();
        await using (var connection = new Npgsql.NpgsqlConnection(System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")))
        {
            // As AUTH-08 will: an invited person's account.
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand("update users set signed_up_via = 'invitation' where email = @email", connection);
            command.Parameters.AddWithValue("email", email);
            await command.ExecuteNonQueryAsync();
        }

        var me = await (await client.SendAsync(With(token, HttpMethod.Get, "/api/auth/me"))).Content.ReadFromJsonAsync<Me>();
        var create = await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = "Not Mine Ltd" }));

        Assert.Equal(("invitation", false), (me!.SignedUpVia, me.CanOnboardOrganization));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("ONB-3504", (await ErrorFrom(create)).Code);
    }

    [Fact]
    public async Task The_person_who_onboarded_it_can_fix_the_name()
    {
        var (client, token, _) = await SignUp();
        var created = (await (await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = "Mama Olich Group" })))
            .Content.ReadFromJsonAsync<Created>())!;

        var renamed = await client.SendAsync(With(created.Token, HttpMethod.Patch, "/api/organizations/current", new { name = " Mama Oliech Group " }));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(new Org(created.Organization.Id, "Mama Oliech Group"), await renamed.Content.ReadFromJsonAsync<Org>());
        var current = await client.SendAsync(With(created.Token, HttpMethod.Get, "/api/organizations/current"));
        Assert.Equal("Mama Oliech Group", (await current.Content.ReadFromJsonAsync<Org>())!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public async Task A_rename_follows_the_same_name_rules(string name)
    {
        var (client, token, _) = await SignUp();
        var created = (await (await client.SendAsync(With(token, HttpMethod.Post, "/api/organizations", new { name = "Kept Ltd" })))
            .Content.ReadFromJsonAsync<Created>())!;

        var res = await client.SendAsync(With(created.Token, HttpMethod.Patch, "/api/organizations/current", new { name }));

        Assert.Equal("ONB-3501", (await ErrorFrom(res)).Code);
        var current = await client.SendAsync(With(created.Token, HttpMethod.Get, "/api/organizations/current"));
        Assert.Equal("Kept Ltd", (await current.Content.ReadFromJsonAsync<Org>())!.Name);
    }

    [Fact]
    public async Task Other_members_cannot_rename_it_until_permissions_exist()
    {
        var (aClient, aToken, _) = await SignUp();
        var created = (await (await aClient.SendAsync(With(aToken, HttpMethod.Post, "/api/organizations", new { name = "Alpha Ltd" })))
            .Content.ReadFromJsonAsync<Created>())!;
        var (bClient, _, bEmail) = await SignUp();
        await using (var connection = new Npgsql.NpgsqlConnection(System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")))
        {
            // As an invite will (AUTH-08): B becomes a member of A's organization.
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand(
                "insert into memberships (id, organization_id, user_id) select uuidv7(), @org, id from users where email = @email", connection);
            command.Parameters.AddWithValue("org", created.Organization.Id);
            command.Parameters.AddWithValue("email", bEmail);
            await command.ExecuteNonQueryAsync();
        }
        var bSignIn = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = bEmail, password = Password });
        var bToken = (await bSignIn.Content.ReadFromJsonAsync<Token>())!;

        var res = await bClient.SendAsync(With(bToken, HttpMethod.Patch, "/api/organizations/current", new { name = "Taken Over Ltd" }));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("ACC-8001", (await ErrorFrom(res)).Code);
    }

    [Fact]
    public async Task Signed_out_people_can_do_neither()
    {
        var client = api.CreateClient();

        var create = await client.PostAsJsonAsync("/api/organizations", new { name = "Nobody Ltd" });
        var current = await client.GetAsync("/api/organizations/current");
        var rename = await client.PatchAsJsonAsync("/api/organizations/current", new { name = "Nobody Ltd" });

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal("AUTH-1007", (await ErrorFrom(create)).Code);
        Assert.Equal("AUTH-1007", (await ErrorFrom(current)).Code);
        Assert.Equal("AUTH-1007", (await ErrorFrom(rename)).Code);
    }

    [Fact]
    public async Task Before_naming_one_current_says_there_is_no_organization_yet()
    {
        var (client, token, _) = await SignUp();

        var current = await client.SendAsync(With(token, HttpMethod.Get, "/api/organizations/current"));

        Assert.Equal(HttpStatusCode.NotFound, current.StatusCode);
        Assert.Equal("ONB-3503", (await ErrorFrom(current)).Code);
    }

    [Fact]
    public async Task Each_person_sees_only_their_own_organization()
    {
        var (aClient, aToken, _) = await SignUp();
        var (bClient, bToken, _) = await SignUp();
        var a = (await (await aClient.SendAsync(With(aToken, HttpMethod.Post, "/api/organizations", new { name = "Alpha Ltd" })))
            .Content.ReadFromJsonAsync<Created>())!;
        var b = (await (await bClient.SendAsync(With(bToken, HttpMethod.Post, "/api/organizations", new { name = "Beta Ltd" })))
            .Content.ReadFromJsonAsync<Created>())!;

        var bCurrent = await bClient.SendAsync(With(b.Token, HttpMethod.Get, "/api/organizations/current"));

        Assert.Equal("Beta Ltd", (await bCurrent.Content.ReadFromJsonAsync<Org>())!.Name);
        Assert.NotEqual(a.Organization.Id, b.Organization.Id);
    }
}
