using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pesaroll.Shared.Data;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Workflows;

public static class WorkflowsModule
{
    /// <summary>
    /// Registers the approval engine (<see cref="Approvals"/>) and its data context. Every connection the
    /// context opens goes through <see cref="TenantSessionInterceptor"/>. Modules that own a kind of
    /// request register an <see cref="IApprovalOutcome"/> for it.
    /// </summary>
    public static IServiceCollection AddWorkflowsModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddDbContext<WorkflowsDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<TenantSessionInterceptor>()));
        services.AddScoped<Approvals>();
        return services;
    }
}
