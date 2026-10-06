using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Identity.Tests;

/// <summary>
/// Sign-up, sign-in, sessions and lockout through the real API, against a PostgreSQL with the
/// schema deployed (scripts/check.sh sets PESAROLL_TEST_DB). Each test uses fresh accounts,
/// so tests don't depend on each other.
/// </summary>
public sealed class AuthTests(AuthTests.Api api) : IClassFixture<AuthTests.Api>
{
    public sealed class Api : WebApplicationFactory<Program>
    {
        public int RateLimitPerMinute { get; init; } = 1000;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")
                ?? throw new InvalidOperationException("PESAROLL_TEST_DB is not set. Run scripts/check.sh.");
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Pesaroll", connectionString);
            builder.UseSetting("Auth:SecureCookie", "false");
            builder.UseSetting("Auth:RateLimitPerMinute", RateLimitPerMinute.ToString());
            builder.UseSetting("App:AllowedOrigins:0", "http://localhost:5173");
        }
    }

    private sealed record Error(string Code, string Message, string Reference);

    private sealed record Me(Guid Id, string Name, string? Email, string? Phone, string? SignupRole, Guid? OrganizationId);

    private const string Password = "correct-horse-battery";

    private static string NewEmail() => $"test-{Guid.NewGuid():N}@example.com";

    private static string NewPhone() => "07" + Random.Shared.Next(10_000_000, 99_999_999);

    private static object SignUpBody(string? email = null, string? phone = null, string password = Password, string role = "owner") =>
        new { name = "Wanjiru Kamau", email, phone, password, role };

    private static async Task<Error> ErrorFrom(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Error>())!;

    [Fact]
    public async Task Sign_up_creates_the_account_and_signs_in()
    {
        var client = api.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var me = await client.GetFromJsonAsync<Me>("/api/auth/me");
        Assert.Equal(email, me!.Email);
        Assert.Equal("Wanjiru Kamau", me.Name);
        Assert.Equal("owner", me.SignupRole);
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
        Assert.Equal("+254" + phone[1..], (await byPhone.Content.ReadFromJsonAsync<Me>())!.Phone);
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
    [InlineData("short", "test@example.com", null, "owner", "AUTH-1004")]
    [InlineData(Password, "not-an-email", null, "owner", "AUTH-1005")]
    [InlineData(Password, null, "0812345678", "owner", "AUTH-1005")]
    [InlineData(Password, null, null, "owner", "AUTH-1005")]
    [InlineData(Password, "test@example.com", null, "ceo", "AUTH-1008")]
    public async Task Sign_up_rejects_bad_input_with_its_error_code(string password, string? email, string? phone, string role, string code)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/signup", SignUpBody(email, phone, password, role));

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
    public async Task Sign_out_ends_the_session_and_the_old_cookie_stops_working()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var signup = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));
        var cookie = signup.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        var before = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me") { Headers = { { "Cookie", cookie } } };
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(before)).StatusCode);

        var signout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signout") { Headers = { { "Cookie", cookie } } };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(signout)).StatusCode);

        var after = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me") { Headers = { { "Cookie", cookie } } };
        var response = await client.SendAsync(after);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH-1007", (await ErrorFrom(response)).Code);
    }

    [Fact]
    public async Task Session_cookie_is_http_only_and_same_site()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var signup = await client.PostAsJsonAsync("/api/auth/signup", SignUpBody(email: NewEmail()));

        var cookie = signup.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        Assert.StartsWith("pesaroll_session=", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
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
