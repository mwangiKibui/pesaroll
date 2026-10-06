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

        auth.MapPost("/signup", async (SignUpRequest request, AuthService service, HttpContext http, CancellationToken ct) =>
        {
            var user = await service.SignUpAsync(request, http, ct);
            return Results.Created("/api/auth/me", MeResponse.From(user, organizationId: null));
        }).RequireRateLimiting(SignInRateLimit);

        auth.MapPost("/signin", async (SignInRequest request, AuthService service, HttpContext http, CancellationToken ct) =>
        {
            var (user, organizationId) = await service.SignInAsync(request, http, ct);
            return Results.Ok(MeResponse.From(user, organizationId));
        }).RequireRateLimiting(SignInRateLimit);

        auth.MapPost("/signout", async (SessionService sessions, HttpContext http, CancellationToken ct) =>
        {
            await sessions.EndAsync(http, ct);
            return Results.NoContent();
        });

        auth.MapGet("/me", async (IdentityDbContext db, TenantContext tenant, CancellationToken ct) =>
        {
            if (tenant.UserId is not Guid userId)
            {
                throw AuthErrors.NotSignedIn();
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw AuthErrors.NotSignedIn();
            return Results.Ok(MeResponse.From(user, tenant.OrganizationId));
        });

        return app;
    }
}
