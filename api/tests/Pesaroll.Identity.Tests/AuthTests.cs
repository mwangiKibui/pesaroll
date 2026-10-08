using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Pesaroll.Identity.Tests;

/// <summary>
/// Sign-up, sign-in, tokens, sessions and lockout through the real API, against a PostgreSQL with the
/// schema deployed (scripts/check.sh sets PESAROLL_TEST_DB). Each test uses fresh accounts,
/// so tests don't depend on each other.
/// </summary>
public sealed class AuthTests(AuthTests.Api api) : IClassFixture<AuthTests.Api>
{
    public sealed class Api : WebApplicationFactory<Program>
    {
        public int RateLimitPerMinute { get; init; } = 1000;

        public string Environment { get; init; } = "Testing";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")
                ?? throw new InvalidOperationException("PESAROLL_TEST_DB is not set. Run scripts/check.sh.");
            builder.UseEnvironment(Environment);
            builder.UseSetting("ConnectionStrings:Pesaroll", connectionString);
            builder.UseSetting("Auth:SecureCookie", "false");
            builder.UseSetting("Auth:RateLimitPerMinute", RateLimitPerMinute.ToString());
            builder.UseSetting("App:AllowedOrigins:0", "http://localhost:5173");
            builder.UseSetting("Auth:Tokens:Keys:0:Id", "test");
            builder.UseSetting("Auth:Tokens:Keys:0:Secret", Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray()));
            builder.UseSetting("Encryption:Keys:0:Id", "test");
            builder.UseSetting("Encryption:Keys:0:Secret", Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()));
        }
    }

    private sealed record Error(string Code, string Message, string Reference);

    private sealed record Me(Guid Id, string FirstName, string? MiddleName, string LastName, string FullName,
        string? Email, string? Phone, Guid? SignupRoleId, string? SignupRoleOther, Guid? OrganizationId);

    private sealed record Token(string AccessToken, string TokenType, int ExpiresIn);

    private const string Password = "correct-horse-battery";

    private static string NewEmail() => $"test-{Guid.NewGuid():N}@example.com";

    private static string NewPhone() => "07" + Random.Shared.Next(10_000_000, 99_999_999);

    // Fixed ids from database/post-deploy/0002_signup_roles.sql.
    private const string Ceo = "0199c0de-5a5e-7000-8000-000000000102";
    private const string Other = "0199c0de-5a5e-7000-8000-000000000502";

    private static object SignUpBody(string? email = null, string? phone = null, string password = Password,
        string roleId = Ceo, string? roleOther = null) =>
        new { firstName = "Wanjiru", lastName = "Kamau", email, phone, password, roleId = Guid.Parse(roleId), roleOther };

    private static async Task<Error> ErrorFrom(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Error>())!;

    private static async Task<Token> TokenFrom(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Token>())!;
    }

    /// <summary>A client that sends the access token, as the web app will.</summary>
    private HttpClient Bearer(Token token, HttpClient? client = null)
    {
        client ??= api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private async Task<Me> MeWith(Token token) =>
        (await Bearer(token).GetFromJsonAsync<Me>("/api/auth/me"))!;

    /// <summary>The refresh cookie from a response, as "name=value".</summary>
    private static string RefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pesaroll_refresh=")).Split(';')[0];

    private static HttpRequestMessage WithCookie(HttpMethod method, string path, string cookie) =>
        new(method, path) { Headers = { { "Cookie", cookie } } };

    private HttpClient CookieFreeClient() =>
        api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private sealed record Text(string En, string Sw);

    private sealed record RoleOption(Guid Id, string Code, Text Name, Text Description, bool IsOther);

    private sealed record RoleGroup(string Code, Text Name, List<RoleOption> Roles);

    [Fact]
    public async Task Sign_up_roles_come_grouped_from_the_list_in_display_order()
    {
        var groups = await api.CreateClient().GetFromJsonAsync<List<RoleGroup>>("/api/auth/signup-roles");

        Assert.Equal(["leadership", "hr_payroll", "finance", "operations", "other"], groups!.Select(g => g.Code));
        Assert.Equal(["founder", "ceo", "general_manager", "director"], groups[0].Roles.Select(r => r.Code));
        Assert.Equal("CEO or Managing Director", groups[0].Roles[1].Name.En);
        Assert.Equal(Guid.Parse(Ceo), groups[0].Roles[1].Id);
        var all = groups.SelectMany(g => g.Roles).ToList();
        Assert.Equal(15, all.Count);
        Assert.Equal(Guid.Parse(Other), Assert.Single(all, r => r.IsOther).Id);
        Assert.All(all, r => Assert.False(string.IsNullOrWhiteSpace(r.Name.Sw)));
    }

    [Fact]
    public async Task Middle_name_is_optional_and_the_full_name_includes_it()
    {
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = " Wanjiru ", middleName = "Akinyi", lastName = "Kamau ",
            email = NewEmail(), password = Password, roleId = Guid.Parse(Ceo),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var me = await MeWith(await TokenFrom(response));
        Assert.Equal(("Wanjiru", "Akinyi", "Kamau"), (me!.FirstName, me.MiddleName, me.LastName));
        Assert.Equal("Wanjiru Akinyi Kamau", me.FullName);
    }

    [Theory]
    [InlineData(null, "Kamau")]
    [InlineData("Wanjiru", "")]
    [InlineData("Wanjiru", "   ")]
    public async Task First_and_last_name_are_required(string? firstName, string? lastName)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/signup", new
        {
            firstName, lastName, email = NewEmail(), password = Password, roleId = Guid.Parse(Ceo),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AUTH-1008", (await ErrorFrom(response)).Code);
    }

    [Fact]
    public async Task Other_needs_a_typed_role_which_is_saved()
    {
        var client = api.CreateClient();

        var missing = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail(), roleId: Other));
        var typed = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail(), roleId: Other, roleOther: "  Head Chef "));

        Assert.Equal("AUTH-1009", (await ErrorFrom(missing)).Code);
        Assert.Equal(HttpStatusCode.Created, typed.StatusCode);
        var me = await MeWith(await TokenFrom(typed));
        Assert.Equal(Guid.Parse(Other), me.SignupRoleId);
        Assert.Equal("Head Chef", me.SignupRoleOther);
    }

    [Fact]
    public async Task Typed_text_is_ignored_when_a_listed_role_is_chosen()
    {
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail(), roleId: Ceo, roleOther: "Boss"));

        var me = await MeWith(await TokenFrom(response));
        Assert.Equal(Guid.Parse(Ceo), me.SignupRoleId);
        Assert.Null(me.SignupRoleOther);
    }

    [Fact]
    public async Task Sign_up_creates_the_account_and_returns_a_token()
    {
        var client = api.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/auth/me", response.Headers.Location?.ToString());
        var token = await TokenFrom(response);
        Assert.Equal(("Bearer", 900), (token.TokenType, token.ExpiresIn));
        var me = await MeWith(token);
        Assert.Equal(email, me.Email);
        Assert.Equal("Wanjiru", me.FirstName);
        Assert.Null(me.MiddleName);
        Assert.Equal("Kamau", me.LastName);
        Assert.Equal("Wanjiru Kamau", me.FullName);
        Assert.Equal(Guid.Parse(Ceo), me.SignupRoleId);
        Assert.Null(me.OrganizationId);
    }

    [Fact]
    public async Task Email_is_matched_without_case_and_phone_in_any_Kenyan_format()
    {
        var email = NewEmail();
        var phone = NewPhone(); // 07XXXXXXXX
        await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email.ToUpperInvariant(), phone: phone));

        var byEmail = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });
        var byPhone = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = "+254 " + phone[1..], password = Password });

        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byPhone.StatusCode);
        Assert.Equal("+254" + phone[1..], (await MeWith(await TokenFrom(byPhone))).Phone);
    }

    [Fact]
    public async Task Sign_up_with_an_email_already_in_use_is_refused()
    {
        var email = NewEmail();
        await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email));

        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("AUTH-1003", (await ErrorFrom(response)).Code);
    }

    [Theory]
    [InlineData("short", "test@example.com", null, Ceo, "AUTH-1004")]
    [InlineData(Password, "not-an-email", null, Ceo, "AUTH-1005")]
    [InlineData(Password, null, "0812345678", Ceo, "AUTH-1005")]
    [InlineData(Password, null, null, Ceo, "AUTH-1005")]
    [InlineData(Password, "test@example.com", null, "0199c0de-5a5e-7000-8000-000000009999", "AUTH-1008")]
    public async Task Sign_up_rejects_bad_input_with_its_error_code(string password, string? email, string? phone, string roleId, string code)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email, phone, password, roleId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await ErrorFrom(response)).Code);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_account_give_the_same_answer()
    {
        var email = NewEmail();
        await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email));

        var wrongPassword = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = "wrong-password-123" });
        var unknown = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = NewEmail(), password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        var a = await ErrorFrom(wrongPassword);
        var b = await ErrorFrom(unknown);
        Assert.Equal(("AUTH-1001", a.Message), ("AUTH-1001", b.Message));
        Assert.Equal(a.Code, b.Code);
    }

    [Fact]
    public async Task Fifth_wrong_password_locks_the_account_even_against_the_right_one()
    {
        var email = NewEmail();
        await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email));
        var client = api.CreateClient();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = "wrong-password-123" });
            Assert.Equal("AUTH-1001", (await ErrorFrom(wrong)).Code);
        }

        var fifth = await client.PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = "wrong-password-123" });
        var right = await client.PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });

        Assert.Equal("AUTH-1002", (await ErrorFrom(fifth)).Code);
        Assert.Equal("AUTH-1002", (await ErrorFrom(right)).Code);
    }

    [Fact]
    public async Task Sign_in_returns_only_a_token_whose_ids_match_the_account()
    {
        var email = NewEmail();
        var me = await MeWith(await TokenFrom(await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email))));

        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(email, body);
        Assert.DoesNotContain("Wanjiru", body);
        var token = new JsonWebToken((await TokenFrom(response)).AccessToken);
        Assert.Equal(me.Id.ToString(), token.Subject);
        Assert.True(token.TryGetPayloadValue<string>("sid", out _));
    }

    [Fact]
    public async Task Refresh_cookie_is_http_only_strict_and_sent_only_to_auth_endpoints()
    {
        var signup = await CookieFreeClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));

        var cookie = signup.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        Assert.StartsWith("pesaroll_refresh=", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/api/auth", cookie);
        Assert.Contains("expires=", cookie);
    }

    [Fact]
    public async Task The_refresh_cookie_alone_does_not_sign_you_in()
    {
        var client = CookieFreeClient();
        var cookie = RefreshCookie(await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail())));

        var response = await client.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", cookie));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH-1007", (await ErrorFrom(response)).Code);
    }

    [Fact]
    public async Task Refresh_gives_a_new_token_and_a_new_refresh_cookie()
    {
        var client = CookieFreeClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));
        var first = await TokenFrom(signup);

        var refreshed = await client.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/refresh", RefreshCookie(signup)));

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = await TokenFrom(refreshed);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.NotEqual(RefreshCookie(signup), RefreshCookie(refreshed));
        Assert.Equal((await MeWith(first)).Id, (await MeWith(second)).Id);
        Assert.Equal(new JsonWebToken(first.AccessToken).GetPayloadValue<string>("sid"),
            new JsonWebToken(second.AccessToken).GetPayloadValue<string>("sid"));
    }

    [Fact]
    public async Task Using_an_old_refresh_cookie_again_ends_the_session()
    {
        var client = CookieFreeClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));
        var stolen = RefreshCookie(signup);
        var refreshed = await client.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/refresh", stolen));
        var latest = await TokenFrom(refreshed);

        var reuse = await client.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/refresh", stolen));

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal("AUTH-1010", (await ErrorFrom(reuse)).Code);
        // The session is over for everyone: the latest token and refresh cookie stop working too.
        var me = await Bearer(latest).GetAsync("/api/auth/me");
        Assert.Equal("AUTH-1010", (await ErrorFrom(me)).Code);
        var again = await client.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/refresh", RefreshCookie(refreshed)));
        Assert.Equal("AUTH-1010", (await ErrorFrom(again)).Code);
    }

    [Theory]
    [InlineData(null, "AUTH-1007")]
    [InlineData("pesaroll_refresh=not-a-token", "AUTH-1010")]
    [InlineData("pesaroll_refresh=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "AUTH-1010")]
    public async Task Refresh_without_a_live_cookie_is_refused(string? cookie, string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var response = await CookieFreeClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(code, (await ErrorFrom(response)).Code);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_at_once_for_the_token_and_the_refresh_cookie()
    {
        var client = CookieFreeClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));
        var token = await TokenFrom(signup);
        Assert.Equal(HttpStatusCode.OK, (await Bearer(token).GetAsync("/api/auth/me")).StatusCode);

        var signout = await Bearer(token).PostAsync("/api/auth/signout", null);

        Assert.Equal(HttpStatusCode.NoContent, signout.StatusCode);
        var me = await Bearer(token).GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal("AUTH-1010", (await ErrorFrom(me)).Code);
        var refresh = await client.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/refresh", RefreshCookie(signup)));
        Assert.Equal("AUTH-1010", (await ErrorFrom(refresh)).Code);
    }

    [Fact]
    public async Task Sign_out_with_only_the_refresh_cookie_ends_the_session_and_clears_the_cookie()
    {
        var client = CookieFreeClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));
        var token = await TokenFrom(signup);

        var signout = await client.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/signout", RefreshCookie(signup)));

        Assert.Equal(HttpStatusCode.NoContent, signout.StatusCode);
        Assert.Contains(signout.Headers.GetValues("Set-Cookie"), c => c.StartsWith("pesaroll_refresh=;"));
        Assert.Equal("AUTH-1010", (await ErrorFrom(await Bearer(token).GetAsync("/api/auth/me"))).Code);
    }

    [Fact]
    public async Task Sign_out_and_refresh_still_work_when_the_access_token_is_no_good()
    {
        var client = CookieFreeClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));
        var token = await TokenFrom(signup);
        var expired = token with { AccessToken = token.AccessToken[..^4] + "AAAA" };

        var refresh = WithCookie(HttpMethod.Post, "/api/auth/refresh", RefreshCookie(signup));
        refresh.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expired.AccessToken);
        var refreshed = await client.SendAsync(refresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var signout = WithCookie(HttpMethod.Post, "/api/auth/signout", RefreshCookie(refreshed));
        signout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expired.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(signout)).StatusCode);

        var after = await client.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/refresh", RefreshCookie(refreshed)));
        Assert.Equal("AUTH-1010", (await ErrorFrom(after)).Code);
    }

    [Fact]
    public async Task A_stale_token_does_not_block_signing_in_again_or_public_pages()
    {
        // Swagger (and a browser tab left open) keeps sending the last token after it stops working.
        var email = NewEmail();
        var token = await TokenFrom(await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email)));
        await Bearer(token).PostAsync("/api/auth/signout", null);
        var stale = Bearer(token);

        Assert.Equal(HttpStatusCode.OK, (await stale.GetAsync("/api/auth/signup-roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stale.GetAsync("/api/health")).StatusCode);
        var signin = await stale.PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });
        Assert.Equal(HttpStatusCode.OK, signin.StatusCode);
        var signup = await stale.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);

        // Anything that needs sign-in still says the session has ended, so the app knows to refresh.
        Assert.Equal("AUTH-1010", (await ErrorFrom(await stale.GetAsync("/api/auth/me"))).Code);
        Assert.Equal((await MeWith(token with { AccessToken = (await TokenFrom(signin)).AccessToken })).Email, email);
    }

    [Fact]
    public async Task A_changed_token_is_refused()
    {
        var token = await TokenFrom(await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail())));
        var other = await TokenFrom(await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail())));
        var parts = token.AccessToken.Split('.');
        var tampered = $"{parts[0]}.{new JsonWebToken(other.AccessToken).EncodedPayload}.{parts[2]}";

        var response = await Bearer(token with { AccessToken = tampered }).GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH-1010", (await ErrorFrom(response)).Code);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Me_without_a_session_is_refused()
    {
        var response = await api.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await ErrorFrom(response);
        Assert.Equal("AUTH-1007", error.Code);
        Assert.Matches("^[0-9A-Z]{8}$", error.Reference);
    }

    [Fact]
    public async Task In_development_Swagger_offers_sign_in_with_a_bearer_token()
    {
        await using var development = new Api { Environment = "Development" };

        var json = await development.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"bearer\"", json);
        Assert.Contains("\"bearerFormat\": \"JWT\"", json);
    }

    [Theory]
    [InlineData("/api/auth/signup", "post", "201", "TokenResponse")]
    [InlineData("/api/auth/signin", "post", "200", "TokenResponse")]
    [InlineData("/api/auth/refresh", "post", "200", "TokenResponse")]
    [InlineData("/api/auth/me", "get", "200", "MeResponse")]
    [InlineData("/api/auth/signup-roles", "get", "200", "SignupRoleGroupOption")]
    [InlineData("/api/auth/signin", "post", "401", "ErrorResponse")]
    [InlineData("/api/auth/signup", "post", "400", "ErrorResponse")]
    [InlineData("/api/auth/me", "get", "401", "ErrorResponse")]
    public async Task The_API_description_says_what_each_endpoint_returns(string path, string method, string status, string schema)
    {
        // The web app's TypeScript types are generated from this document, so responses must be in it.
        await using var development = new Api { Environment = "Development" };
        using var document = JsonDocument.Parse(await development.CreateClient().GetStringAsync("/openapi/v1.json"));

        var response = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty(status);

        Assert.Contains($"#/components/schemas/{schema}", response.GetRawText());
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task Swagger_and_the_API_description_are_not_published_outside_development(string path)
    {
        var response = await api.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Requests_from_another_website_are_blocked()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signin")
        {
            Content = JsonContent.Create(new { identifier = NewEmail(), password = Password }),
            Headers = { { "Origin", "https://evil.example" } },
        };

        var response = await api.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("PLAT-9002", (await ErrorFrom(response)).Code);
    }
}

public sealed class RateLimitTests
{
    [Fact]
    public async Task Too_many_sign_in_attempts_from_one_address_are_slowed_down()
    {
        await using var api = new AuthTests.Api { RateLimitPerMinute = 3 };
        var client = api.CreateClient();
        var body = new { identifier = "nobody@example.com", password = "wrong-password-123" };

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/api/auth/signin", body);
        }

        var fourth = await client.PostAsJsonAsync("/api/auth/signin", body);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Contains("AUTH-1006", await fourth.Content.ReadAsStringAsync());
    }
}
