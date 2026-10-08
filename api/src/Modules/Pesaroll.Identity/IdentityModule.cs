using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pesaroll.Shared.Data;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Identity;

public sealed record LocalizedText(string En, string Sw);

public sealed record SignupRoleOption(Guid Id, string Code, LocalizedText Name, LocalizedText Description, bool IsOther);

public sealed record SignupRoleGroupOption(string Code, LocalizedText Name, IReadOnlyList<SignupRoleOption> Roles);

public static class IdentityModule
{
    public const string SignInRateLimit = "auth";

    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<IdentityDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<TenantSessionInterceptor>()));
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddOptions<TokenOptions>()
            .Bind(configuration.GetSection("Auth:Tokens"))
            .Validate(o => o.Keys.Count > 0 && o.Issuer.Length > 0 && o.Audience.Length > 0,
                "Auth:Tokens needs an Issuer, an Audience and at least one signing key.")
            .ValidateOnStart();
        services.AddSingleton<AccessTokens>();
        services.AddScoped<SessionService>();
        services.AddScoped<AuthService>();

        // Sign-up and sign-in attempts per IP address per minute.
        var perMinute = configuration.GetValue("Auth:RateLimitPerMinute", 10);
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(SignInRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
            options.OnRejected = (context, _) => new ValueTask(ErrorResponseMiddleware.WriteAsync(
                context.HttpContext, StatusCodes.Status429TooManyRequests, "AUTH-1006",
                "Too many attempts. Wait a minute and try again."));
        });

        return services;
    }

    /// <summary>Resumes the session on every request, so <see cref="TenantContext"/> is filled.</summary>
    public static IApplicationBuilder UseSessions(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            await http.RequestServices.GetRequiredService<SessionService>().ResumeAsync(http, http.RequestAborted);
            await next(http);
        });

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        // The sign-up screen's role cards: groups in order, each with its active roles in order.
        auth.MapGet("/signup-roles", async (IdentityDbContext db, CancellationToken ct) =>
        {
            var groups = await db.SignupRoleGroups.OrderBy(g => g.SortOrder).ToListAsync(ct);
            var roles = await db.SignupRoles.Where(r => r.Active).OrderBy(r => r.SortOrder).ToListAsync(ct);
            return Results.Ok(groups
                .Select(g => new SignupRoleGroupOption(g.Code, new LocalizedText(g.NameEn, g.NameSw),
                    roles.Where(r => r.GroupCode == g.Code)
                        .Select(r => new SignupRoleOption(r.Id, r.Code,
                            new LocalizedText(r.NameEn, r.NameSw),
                            new LocalizedText(r.DescriptionEn, r.DescriptionSw),
                            r.IsOther))
                        .ToList()))
                .Where(g => g.Roles.Count > 0)
                .ToList());
        }).Produces<List<SignupRoleGroupOption>>();

        // Sign-up and sign-in return an access token and set the refresh cookie; GET /me has the details.
        auth.MapPost("/signup", async (SignUpRequest request, AuthService service, HttpContext http, CancellationToken ct) =>
                Results.Created("/api/auth/me", await service.SignUpAsync(request, http, ct)))
            .Produces<TokenResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(SignInRateLimit);

        auth.MapPost("/signin", async (SignInRequest request, AuthService service, HttpContext http, CancellationToken ct) =>
                Results.Ok(await service.SignInAsync(request, http, ct)))
            .Produces<TokenResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status423Locked)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(SignInRateLimit);

        // A new access token from the refresh cookie. The web app calls this when a token is
        // about to expire or a request fails with AUTH-1010, one call at a time.
        auth.MapPost("/refresh", async (SessionService sessions, HttpContext http, CancellationToken ct) =>
                Results.Ok(await sessions.RefreshAsync(http, ct)))
            .Produces<TokenResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(SignInRateLimit);

        auth.MapPost("/signout", async (SessionService sessions, HttpContext http, CancellationToken ct) =>
        {
            await sessions.EndAsync(http, ct);
            return Results.NoContent();
        });

        auth.MapGet("/me", async (IdentityDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            if (tenant.UserId is not Guid userId)
            {
                throw SessionService.RequireSignedIn(http);
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw AuthErrors.NotSignedIn();
            var hasOnboarded = (await db.QueryAsync("select has_onboarded_organization()", r => r.GetBoolean(0),
                new Dictionary<string, object?>(), ct)).Single();
            return Results.Ok(MeResponse.From(user, tenant.OrganizationId, hasOnboarded));
        }).Produces<MeResponse>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        return app;
    }
}
