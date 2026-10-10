using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pesaroll.Shared.Jobs;

/// <summary>A job the worker runs on a schedule (cron, Kenya time), such as sending due reminders.</summary>
public interface IRecurringJob
{
    /// <summary>Stable and unique, like "reminders.send"; renaming it leaves the old schedule behind.</summary>
    string Id { get; }

    /// <summary>When it runs, as cron in Kenya time: "0 * * * *" is every hour on the hour.</summary>
    string Cron { get; }

    Task RunAsync(CancellationToken ct);
}

/// <summary>
/// Background jobs (PLAT-04) with Hangfire, the queue kept in PostgreSQL in its own hangfire schema
/// (Hangfire creates and upgrades it; pgschema only manages public), so no other server is needed.
/// The API queues work; the worker (runJobs) runs it and keeps the recurring jobs scheduled.
/// </summary>
public static class JobsSetup
{
    public const string Schema = "hangfire";

    /// <summary>Kenya is on East Africa Time all year; recurring jobs follow it.</summary>
    public static readonly TimeZoneInfo Kenya = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");

    public static IServiceCollection AddPesarollJobs(this IServiceCollection services, string connectionString, bool runJobs)
    {
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(storage => storage.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = Schema, PrepareSchemaIfNecessary = true, QueuePollInterval = TimeSpan.FromSeconds(5) }));
        if (runJobs)
        {
            services.AddHangfireServer(options => options.WorkerCount = 4);
            services.AddHostedService<RecurringJobsScheduler>();
        }

        return services;
    }

    /// <summary>A job the worker runs on its <see cref="IRecurringJob.Cron"/>.</summary>
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services) where TJob : class, IRecurringJob
    {
        services.AddTransient<TJob>();
        services.AddSingleton(new RecurringJobRegistration((provider, manager) =>
        {
            var job = provider.GetRequiredService<TJob>();
            manager.AddOrUpdate<TJob>(job.Id, j => j.RunAsync(CancellationToken.None), job.Cron, new RecurringJobOptions { TimeZone = Kenya });
        }));
        return services;
    }

    internal sealed record RecurringJobRegistration(Action<IServiceProvider, IRecurringJobManager> Schedule);

    /// <summary>Puts every registered recurring job on its schedule when the worker starts.</summary>
    private sealed class RecurringJobsScheduler(IServiceProvider provider, IRecurringJobManager manager, IEnumerable<RecurringJobRegistration> jobs) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = provider.CreateScope();
            foreach (var job in jobs)
            {
                job.Schedule(scope.ServiceProvider, manager);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
