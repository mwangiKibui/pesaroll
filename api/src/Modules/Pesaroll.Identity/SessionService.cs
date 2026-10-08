using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Identity;

/// <summary>
/// Signed-in sessions. Signing in returns a 15-minute access token (sent as
/// <c>Authorization: Bearer</c>) and sets a refresh token in an HttpOnly cookie that only the
/// /api/auth endpoints receive. The database stores only the refresh token's SHA-256 hash, so a
/// database leak doesn't hand out live sessions. Every request checks the session the access
/// token names, so sign-out works at once. A session ends after 12 hours idle, 7 days in total,
/// at sign-out, or when a replaced refresh token is used again (see rotate_session_token).
/// </summary>
public sealed class SessionService(
    IdentityDbContext db, TenantContext tenant, AccessTokens tokens, IConfiguration configuration, TimeProvider clock)
{
    public const string RefreshCookie = "pesaroll_refresh";
    public const string RefreshCookiePath = "/api/auth";
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private const string SessionIdKey = "pesaroll.session_id";
    private const string TokenRejectedKey = "pesaroll.token_rejected";

    /// <summary>The session behind this request's access token, or null when signed out.</summary>
    public static Guid? CurrentSessionId(HttpContext http) => http.Items[SessionIdKey] as Guid?;

    /// <summary>The error for a request that needs sign-in but has no working token.</summary>
    public static Exception RequireSignedIn(HttpContext http) =>
        http.Items.ContainsKey(TokenRejectedKey) ? AuthErrors.SessionEnded(invalidToken: true) : AuthErrors.NotSignedIn();

    public async Task<TokenResponse> StartAsync(Guid userId, Guid? organizationId, HttpContext http, CancellationToken ct)
    {
        var refreshToken = RandomNumberGenerator.GetBytes(32);
        var now = clock.GetUtcNow();
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var session = new Session
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OrganizationId = organizationId,
            TokenHash = SHA256.HashData(refreshToken),
            LastSeenAt = now,
            ExpiresAt = now + MaxAge,
            Ip = http.Connection.RemoteIpAddress,
            UserAgent = userAgent.Length > 300 ? userAgent[..300] : userAgent,
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        SetRefreshCookie(http, refreshToken, session.ExpiresAt);
        return tokens.Issue(new AccessTokenClaims(userId, organizationId, session.Id));
    }

    /// <summary>Swaps the refresh cookie for a new one and returns a new access token for the same session.</summary>
    public async Task<TokenResponse> RefreshAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.Cookies.TryGetValue(RefreshCookie, out var value))
        {
            throw AuthErrors.NotSignedIn();
        }

        if (!TryDecode(value, out var refreshToken))
        {
            throw AuthErrors.SessionEnded();
        }

        var next = RandomNumberGenerator.GetBytes(32);
        var rotated = (await db.QueryAsync(
            "select session_id, user_id, organization_id, expires_at from rotate_session_token(@hash, @next_hash)",
            r => new
            {
                Session = r.GetGuid(0),
                User = r.GetGuid(1),
                Organization = r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2),
                ExpiresAt = r.GetFieldValue<DateTimeOffset>(3),
            },
            new Dictionary<string, object?> { ["hash"] = SHA256.HashData(refreshToken), ["next_hash"] = SHA256.HashData(next) },
            ct)).FirstOrDefault() ?? throw AuthErrors.SessionEnded();

        SetRefreshCookie(http, next, rotated.ExpiresAt);
        return tokens.Issue(new AccessTokenClaims(rotated.User, rotated.Organization, rotated.Session));
    }

    /// <summary>
    /// Reads the access token and, when it and its session are live, fills the request's
    /// <see cref="TenantContext"/> with the person, their current organization and the companies they may see.
    /// No token, or one that no longer works, means a signed-out request: public endpoints (sign-in,
    /// sign-up, refresh) still work, and endpoints that need sign-in answer AUTH-1010 for a token that
    /// no longer works (see <see cref="RequireSignedIn"/>), so the app knows to refresh.
    /// </summary>
    public async Task ResumeAsync(HttpContext http, CancellationToken ct)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return;
        }

        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            || await tokens.ValidateAsync(header[scheme.Length..].Trim()) is not { } claims)
        {
            http.Items[TokenRejectedKey] = true;
            return;
        }

        // Row-level security lets a person read only their own sessions.
        tenant.SetUser(claims.UserId);
        var session = (await db.QueryAsync(
            """
            select s.organization_id, s.last_seen_at
            from sessions s
            join users u on u.id = s.user_id and u.status = 'active'
            where s.id = @session_id and s.user_id = @user_id
              and s.revoked_at is null
              and s.expires_at > now()
              and s.last_seen_at > now() - interval '12 hours'
            """,
            r => new
            {
                Organization = r.IsDBNull(0) ? (Guid?)null : r.GetGuid(0),
                LastSeenAt = r.GetFieldValue<DateTimeOffset>(1),
            },
            new Dictionary<string, object?> { ["session_id"] = claims.SessionId, ["user_id"] = claims.UserId },
            ct)).FirstOrDefault();

        // The organization must still be the session's: after a switch, older tokens stop working.
        if (session is null || session.Organization != claims.OrganizationId)
        {
            tenant.Clear();
            http.Items[TokenRejectedKey] = true;
            return;
        }

        if (claims.OrganizationId is Guid organizationId)
        {
            var companyIds = (await db.QueryAsync(
                "select member_company_ids(@user_id, @organization_id)",
                r => r.GetFieldValue<Guid[]>(0),
                new Dictionary<string, object?> { ["user_id"] = claims.UserId, ["organization_id"] = organizationId },
                ct)).Single();
            tenant.Set(organizationId, companyIds);
        }

        http.Items[SessionIdKey] = claims.SessionId;

        // Keep the idle timer fresh without writing on every request.
        var now = clock.GetUtcNow();
        if (session.LastSeenAt < now.AddMinutes(-5))
        {
            await db.Sessions
                .Where(s => s.Id == claims.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), ct);
        }
    }

    /// <summary>Ends the session named by the access token, or else by the refresh cookie, and clears the cookie.</summary>
    public async Task EndAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Items[SessionIdKey] is Guid sessionId)
        {
            var now = clock.GetUtcNow();
            await db.Sessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        }
        else if (http.Request.Cookies.TryGetValue(RefreshCookie, out var value) && TryDecode(value, out var refreshToken))
        {
            await db.QueryAsync(
                "select end_session_by_token(@hash)",
                _ => 0,
                new Dictionary<string, object?> { ["hash"] = SHA256.HashData(refreshToken) },
                ct);
        }

        http.Response.Cookies.Delete(RefreshCookie, CookieOptions(null));
    }

    private void SetRefreshCookie(HttpContext http, byte[] token, DateTimeOffset expires) =>
        http.Response.Cookies.Append(RefreshCookie, Base64Url.EncodeToString(token), CookieOptions(expires));

    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = configuration.GetValue("Auth:SecureCookie", true),
        SameSite = SameSiteMode.Strict,
        Path = RefreshCookiePath,
        Expires = expires,
        IsEssential = true,
    };

    private static bool TryDecode(string value, out byte[] token)
    {
        token = new byte[32];
        return Base64Url.IsValid(value) && Base64Url.TryDecodeFromChars(value, token, out var written) && written == 32;
    }
}
