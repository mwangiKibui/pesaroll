using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pesaroll.Shared.Data;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

public static class OrganizationsModule
{
    /// <summary>
    /// Registers the request's tenant context and the Organizations data context. Every
    /// connection the context opens goes through <see cref="TenantSessionInterceptor"/>.
    /// </summary>
    public static IServiceCollection AddOrganizationsModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<TenantSessionInterceptor>();
        services.AddDbContext<OrganizationsDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<TenantSessionInterceptor>()));
        services.AddTransient<PayrollRemindersJob>();   // scheduled by the worker (AddRecurringJob)
        return services;
    }
}
