using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Pesaroll.Identity.Tests;

/// <summary>Issuing and checking access tokens. Unit tests: no database, no web server.</summary>
public sealed class AccessTokenTests
{
    private static readonly SigningKey Current = new() { Id = "2026-10", Secret = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray()) };
    private static readonly SigningKey Previous = new() { Id = "2026-09", Secret = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()) };
    private static readonly AccessTokenClaims Claims = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));

    private AccessTokens Tokens(params SigningKey[] keys) => Tokens(clock, keys);

    private static AccessTokens Tokens(TimeProvider at, params SigningKey[] keys) => new(Options.Create(new TokenOptions
    {
        Issuer = "https://api.pesaroll.co.ke",
        Audience = "pesaroll-app",
        Keys = keys.Length == 0 ? [Current] : [.. keys],
    }), at);

    [Fact]
    public async Task An_issued_token_checks_out_with_the_same_ids()
    {
        var tokens = Tokens();

        var issued = tokens.Issue(Claims);

        Assert.Equal("Bearer", issued.TokenType);
        Assert.Equal(900, issued.ExpiresIn);
        Assert.Equal(Claims, await tokens.ValidateAsync(issued.AccessToken));
    }

    [Fact]
    public async Task Without_an_organization_the_org_claim_is_left_out()
    {
        var tokens = Tokens();
        var claims = Claims with { OrganizationId = null };

        var issued = tokens.Issue(claims);

        Assert.False(new JsonWebToken(issued.AccessToken).TryGetPayloadValue<string>("org", out _));
        Assert.Equal(claims, await tokens.ValidateAsync(issued.AccessToken));
    }

    [Fact]
    public void The_token_carries_only_ids_and_standard_fields()
    {
        var token = new JsonWebToken(Tokens().Issue(Claims).AccessToken);

        Assert.Equal("HS256", token.Alg);
        Assert.Equal("2026-10", token.Kid);
        Assert.Equal(
            ["aud", "exp", "iat", "iss", "jti", "nbf", "org", "sid", "sub"],
            token.Claims.Select(c => c.Type).Distinct().Order());
        Assert.Equal(Claims.UserId.ToString(), token.Subject);
        Assert.Equal(clock.GetUtcNow().AddMinutes(15).UtcDateTime, token.ValidTo);
    }

    [Fact]
    public async Task A_token_is_refused_once_15_minutes_have_passed()
    {
        var tokens = Tokens();
        var issued = tokens.Issue(Claims);

        clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.NotNull(await tokens.ValidateAsync(issued.AccessToken));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await tokens.ValidateAsync(issued.AccessToken));
    }

    [Fact]
    public async Task A_token_dated_in_the_future_is_refused()
    {
        var fastClock = new FakeTimeProvider(clock.GetUtcNow().AddMinutes(5));
        var issued = Tokens(fastClock).Issue(Claims);

        Assert.Null(await Tokens().ValidateAsync(issued.AccessToken));
    }

    [Fact]
    public async Task A_changed_token_is_refused()
    {
        var tokens = Tokens();
        var parts = tokens.Issue(Claims).AccessToken.Split('.');
        var otherUser = new JsonWebToken(tokens.Issue(Claims with { UserId = Guid.CreateVersion7() }).AccessToken);

        // Someone else's payload with this token's signature.
        var tampered = $"{parts[0]}.{otherUser.EncodedPayload}.{parts[2]}";

        Assert.Null(await tokens.ValidateAsync(tampered));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.")]
    public async Task Garbage_and_unsigned_tokens_are_refused(string token)
    {
        Assert.Null(await Tokens().ValidateAsync(token));
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var issued = Tokens(Previous).Issue(Claims);

        Assert.Null(await Tokens(Current).ValidateAsync(issued.AccessToken));
    }

    [Fact]
    public async Task After_a_key_change_tokens_from_the_previous_key_still_work()
    {
        var beforeChange = Tokens(Previous).Issue(Claims);
        var tokens = Tokens(Current, Previous);

        Assert.Equal("2026-10", new JsonWebToken(tokens.Issue(Claims).AccessToken).Kid);
        Assert.Equal(Claims, await tokens.ValidateAsync(beforeChange.AccessToken));
    }

    [Fact]
    public async Task A_token_for_another_app_or_issuer_is_refused()
    {
        var other = new AccessTokens(Options.Create(new TokenOptions
        {
            Issuer = "https://staging-api.pesaroll.co.ke",
            Audience = "pesaroll-back-office",
            Keys = [Current],
        }), clock);

        Assert.Null(await Tokens().ValidateAsync(other.Issue(Claims).AccessToken));
    }

    [Fact]
    public void Keys_shorter_than_32_bytes_are_refused_at_startup()
    {
        var shortKey = new SigningKey { Id = "short", Secret = Convert.ToBase64String(new byte[16]) };

        Assert.Throws<InvalidOperationException>(() => Tokens(shortKey));
    }
}
