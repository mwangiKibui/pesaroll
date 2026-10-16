using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pesaroll.Identity.Tests;

/// <summary>
/// AUTH-05: where you're signed in. Each person lists their own active sessions (the one they're on
/// marked) and ends any of them, or every other one; a session ended stops working on its next
/// request, and its refresh cookie no longer works. Nobody can see or end someone else's.
/// </summary>
public sealed class SessionsTests(AuthTests.Api api) : IClassFixture<AuthTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Error(string Code);
    private sealed record SignedIn(Guid Id, string Device, string? IpAddress, DateTimeOffset SignedInAt, DateTimeOffset LastActiveAt, bool Current);

    private const string Password = "correct-horse-battery";
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";
    private const string Iphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

    /// <summary>A signed-in browser: its client (sending the access token) and its refresh cookie.</summary>
    private sealed record Browser(HttpClient Client, string RefreshCookie);

    private async Task<string> NewAccount()
    {
        var email = $"devices-{Guid.NewGuid():N}@example.com";
        var signup = await api.CreateClient().PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Wanjiru", lastName = "Kamau", email, password = Password, roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        return email;
    }

    private async Task<Browser> SignIn(string email, string userAgent)
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        var signin = await client.PostAsJsonAsync("/api/auth/signin", new { identifier = email, password = Password });
        Assert.Equal(HttpStatusCode.OK, signin.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signin.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var cookie = signin.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pesaroll_refresh=")).Split(';')[0];
        return new Browser(client, cookie);
    }

    private static async Task<List<SignedIn>> Devices(Browser browser) =>
        (await browser.Client.GetFromJsonAsync<List<SignedIn>>("/api/auth/sessions"))!;

    private static async Task<string> Code(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<Error>())!.Code;

    private static Task<HttpResponseMessage> Refresh(Browser browser) =>
        browser.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Headers = { { "Cookie", browser.RefreshCookie } } });

    [Fact]
    public async Task You_see_where_you_are_signed_in_with_this_device_marked()
    {
        var email = await NewAccount();
        var laptop = await SignIn(email, Chrome);
        var phone = await SignIn(email, Iphone);

        var seen = await Devices(laptop);
        Assert.Equal(2, seen.Count(d => d.Device is "Chrome on Windows" or "Safari on iPhone"));
        var current = Assert.Single(seen, d => d.Current);
        Assert.Equal("Chrome on Windows", current.Device);
        Assert.Equal("Safari on iPhone", Assert.Single(await Devices(phone), d => d.Current).Device);
    }

    [Fact]
    public async Task Signing_out_another_device_stops_it_at_its_next_request_and_refresh()
    {
        var email = await NewAccount();
        var laptop = await SignIn(email, Chrome);
        var phone = await SignIn(email, Iphone);
        var phoneSession = (await Devices(phone)).Single(d => d.Current).Id;

        Assert.Equal(HttpStatusCode.NoContent, (await laptop.Client.PostAsync($"/api/auth/sessions/{phoneSession}/end", null)).StatusCode);

        var after = await phone.Client.GetAsync("/api/auth/me");
        Assert.Equal((HttpStatusCode.Unauthorized, "AUTH-1010"), (after.StatusCode, await Code(after)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(phone)).StatusCode);
        Assert.DoesNotContain(await Devices(laptop), d => d.Id == phoneSession);
        Assert.Equal(HttpStatusCode.OK, (await laptop.Client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_all_other_devices_keeps_this_one()
    {
        var email = await NewAccount();
        var laptop = await SignIn(email, Chrome);
        var phone = await SignIn(email, Iphone);
        var tablet = await SignIn(email, Iphone);

        Assert.Equal(HttpStatusCode.NoContent, (await laptop.Client.PostAsync("/api/auth/sessions/end-others", null)).StatusCode);

        Assert.True(Assert.Single(await Devices(laptop)).Current);
        Assert.Equal("AUTH-1010", await Code(await phone.Client.GetAsync("/api/auth/me")));
        Assert.Equal("AUTH-1010", await Code(await tablet.Client.GetAsync("/api/auth/me")));
    }

    [Fact]
    public async Task Signing_out_this_device_ends_it_and_clears_its_cookie()
    {
        var laptop = await SignIn(await NewAccount(), Chrome);
        var mine = (await Devices(laptop)).Single(d => d.Current).Id;

        var ended = await laptop.Client.PostAsync($"/api/auth/sessions/{mine}/end", null);
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Contains(ended.Headers.GetValues("Set-Cookie"), c => c.StartsWith("pesaroll_refresh=;") || c.StartsWith("pesaroll_refresh=\"\""));
        Assert.Equal("AUTH-1010", await Code(await laptop.Client.GetAsync("/api/auth/me")));
    }

    [Fact]
    public async Task Nobody_sees_or_ends_someone_else_s_sessions()
    {
        var mine = await SignIn(await NewAccount(), Chrome);
        var theirs = await SignIn(await NewAccount(), Iphone);
        var theirSession = (await Devices(theirs)).Single(d => d.Current).Id;

        Assert.DoesNotContain(await Devices(mine), d => d.Id == theirSession);
        var refused = await mine.Client.PostAsync($"/api/auth/sessions/{theirSession}/end", null);
        Assert.Equal((HttpStatusCode.NotFound, "AUTH-1017"), (refused.StatusCode, await Code(refused)));
        Assert.Equal(HttpStatusCode.OK, (await theirs.Client.GetAsync("/api/auth/me")).StatusCode);

        var signedOut = await api.CreateClient().GetAsync("/api/auth/sessions");
        Assert.Equal((HttpStatusCode.Unauthorized, "AUTH-1007"), (signedOut.StatusCode, await Code(signedOut)));
    }
}
