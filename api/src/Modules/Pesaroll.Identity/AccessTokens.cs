using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Pesaroll.Identity;

/// <summary>A key for signing access tokens. <see cref="Secret"/> is base64, at least 32 bytes.</summary>
public sealed class SigningKey
{
    public string Id { get; set; } = "";

    public string Secret { get; set; } = "";
}

/// <summary>
/// Settings under <c>Auth:Tokens</c>. The first key signs; every key is accepted, so a key can be
/// replaced without signing everyone out (add the new key first, drop the old one 15 minutes later).
/// </summary>
public sealed class TokenOptions
{
    public string Issuer { get; set; } = "";

    public string Audience { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 15;

    public List<SigningKey> Keys { get; set; } = [];
}

/// <summary>What an access token says: who, which organization, which session. Nothing personal.</summary>
public sealed record AccessTokenClaims(Guid UserId, Guid? OrganizationId, Guid SessionId);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

/// <summary>
/// Issues and checks the short-lived access tokens (HS256 JWTs). A valid token only proves who is
/// calling; the session it names is still checked on every request, so sign-out takes effect at once.
/// </summary>
public sealed class AccessTokens
{
    private readonly TokenOptions options;
    private readonly TimeProvider clock;
    private readonly SymmetricSecurityKey[] keys;
    private readonly JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false, MapInboundClaims = false };

    public AccessTokens(IOptions<TokenOptions> options, TimeProvider clock)
    {
        this.options = options.Value;
        this.clock = clock;
        if (this.options.Keys.Count == 0)
        {
            throw new InvalidOperationException("Auth:Tokens:Keys is empty: set at least one signing key.");
        }

        keys = this.options.Keys.Select(ToSecurityKey).ToArray();
    }

    private TimeSpan Lifetime => TimeSpan.FromMinutes(options.AccessTokenMinutes);

    public TokenResponse Issue(AccessTokenClaims claims)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var payload = new Dictionary<string, object>
        {
            ["sub"] = claims.UserId.ToString(),
            ["sid"] = claims.SessionId.ToString(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (claims.OrganizationId is Guid organizationId)
        {
            payload["org"] = organizationId.ToString();
        }

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            Claims = payload,
            SigningCredentials = new SigningCredentials(keys[0], SecurityAlgorithms.HmacSha256),
        });
        return new TokenResponse(token, "Bearer", (int)Lifetime.TotalSeconds);
    }

    /// <summary>The token's claims, or null when it is malformed, changed, expired or not ours.</summary>
    public async Task<AccessTokenClaims?> ValidateAsync(string token)
    {
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKeys = keys,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            ValidateLifetime = false, // checked below against our clock, with no grace period
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (jwt.ValidTo == DateTime.MinValue || jwt.ValidTo <= now || jwt.ValidFrom > now)
        {
            return null;
        }

        if (!Guid.TryParse(jwt.Subject, out var userId)
            || !jwt.TryGetPayloadValue<string>("sid", out var sid) || !Guid.TryParse(sid, out var sessionId))
        {
            return null;
        }

        Guid? organizationId = null;
        if (jwt.TryGetPayloadValue<string>("org", out var org))
        {
            if (!Guid.TryParse(org, out var parsed))
            {
                return null;
            }

            organizationId = parsed;
        }

        return new AccessTokenClaims(userId, organizationId, sessionId);
    }

    private static SymmetricSecurityKey ToSecurityKey(SigningKey key)
    {
        var bytes = new byte[key.Secret.Length];
        if (string.IsNullOrWhiteSpace(key.Id)
            || !Convert.TryFromBase64String(key.Secret, bytes, out var length)
            || length < 32)
        {
            throw new InvalidOperationException(
                $"Signing key '{key.Id}' needs an id and a base64 secret of at least 32 bytes (openssl rand -base64 32).");
        }

        return new SymmetricSecurityKey(bytes[..length]) { KeyId = key.Id };
    }
}
