using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pesaroll.Shared.Data;
using Pesaroll.Shared.Tenancy;
using Pesaroll.Workflows;

namespace Pesaroll.Payroll;

public static class PayrollModule
{
    /// <summary>
    /// Registers pay runs (PAY-03): their data context, the job that works a run out (run by the
    /// worker) and what a decided pay_run request does. Every connection the context opens goes
    /// through <see cref="TenantSessionInterceptor"/>. Needs the Organizations module (people, items,
    /// rule packs).
    /// </summary>
    public static IServiceCollection AddPayrollModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<PayrollDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<TenantSessionInterceptor>()));
        services.AddScoped<PayRunCalculation>();
        services.AddScoped<IApprovalOutcome, PayRunApprovalOutcome>();
        return services;
    }
}
