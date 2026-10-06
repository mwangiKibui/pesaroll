using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Identity;

/// <summary>
/// Signed-in sessions. The cookie carries a random 32-byte token; the database stores only its
/// SHA-256 hash, so a database leak doesn't hand out live sessions. A session ends after
/// 12 hours idle or 7 days in total (checked in find_session), or when the person signs out.
/// </summary>
public sealed class SessionService(IdentityDbContext db, TenantContext tenant, IConfiguration configuration, TimeProvider clock)
{
    public const string CookieName = "pesaroll_session";
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private const string SessionIdKey = "pesaroll.session_id";

    public async Task StartAsync(Guid userId, Guid? organizationId, HttpContext http, CancellationToken ct)
    {
        var token = RandomNumberGenerator.GetBytes(32);
        var now = clock.GetUtcNow();
        var userAgent = http.Request.Headers.UserAgent.ToString();

        db.Sessions.Add(new Session
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OrganizationId = organizationId,
            TokenHash = SHA256.HashData(token),
            LastSeenAt = now,
            ExpiresAt = now + MaxAge,
            Ip = http.Connection.RemoteIpAddress,
            UserAgent = userAgent.Length > 300 ? userAgent[..300] : userAgent,
        });
        await db.SaveChangesAsync(ct);

        http.Response.Cookies.Append(CookieName, Base64Url.EncodeToString(token), CookieOptions(now + MaxAge));
    }

    /// <summary>
    /// Reads the session cookie and, when it's live, fills the request's <see cref="TenantContext"/>
    /// with the person, their current organization and the companies they may see.
    /// </summary>
    public async Task ResumeAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var value) || !TryDecode(value, out var token))
        {
            return;
        }

        var parameters = new Dictionary<string, object?> { ["hash"] = SHA256.HashData(token) };
        var found = (await db.QueryAsync(
            "select session_id, user_id, organization_id from find_session(@hash)",
            r => (Session: r.GetGuid(0), User: r.GetGuid(1), Organization: r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2)),
            parameters, ct)).FirstOrDefault();
        if (found == default)
        {
            return;
        }

        tenant.SetUser(found.User);
        if (found.Organization is Guid organizationId)
        {
            var companyIds = (await db.QueryAsync(
                "select member_company_ids(@user_id, @organization_id)",
                r => r.GetFieldValue<Guid[]>(0),
                new Dictionary<string, object?> { ["user_id"] = found.User, ["organization_id"] = organizationId },
                ct)).Single();
            tenant.Set(organizationId, companyIds);
        }

        http.Items[SessionIdKey] = found.Session;

        // Keep the idle timer fresh without writing on every request.
        var now = clock.GetUtcNow();
        var stale = now.AddMinutes(-5);
        await db.Sessions
            .Where(s => s.Id == found.Session && s.LastSeenAt < stale)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), ct);
    }

    public async Task EndAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Items[SessionIdKey] is Guid sessionId)
        {
            var now = clock.GetUtcNow();
            await db.Sessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        }

        http.Response.Cookies.Delete(CookieName, CookieOptions(null));
    }

    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = configuration.GetValue("Auth:SecureCookie", true),
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };

    private static bool TryDecode(string value, out byte[] token)
    {
        token = new byte[32];
        return Base64Url.IsValid(value) && Base64Url.TryDecodeFromChars(value, token, out var written) && written == 32;
    }
}
