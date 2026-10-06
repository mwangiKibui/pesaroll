using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantSessionInterceptor>();
        services.AddDbContext<OrganizationsDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<TenantSessionInterceptor>()));
        return services;
    }
}
