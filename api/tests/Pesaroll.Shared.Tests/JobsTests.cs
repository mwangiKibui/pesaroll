using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Pesaroll.Shared.Jobs;

namespace Pesaroll.Shared.Tests;

/// <summary>
/// PLAT-04: background jobs. The API queues work and the worker runs it, with the queue kept in
/// PostgreSQL (Hangfire, in its own hangfire schema), so no other server is needed.
/// </summary>
public sealed class JobsTests
{
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    public sealed class Ping
    {
        public static readonly TaskCompletionSource<string> Ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync(string message)
        {
            Ran.TrySetResult(message);
            return Task.CompletedTask;
        }
    }

    public sealed class Nightly : IRecurringJob
    {
        public string Id => "tests.nightly";
        public string Cron => "0 2 * * *";
        public Task RunAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_job_queued_by_the_API_runs_in_the_worker()
    {
        // The API side only queues.
        var api = new ServiceCollection().AddLogging().AddPesarollJobs(Db, runJobs: false).BuildServiceProvider();
        api.GetRequiredService<IBackgroundJobClient>().Enqueue<Ping>(p => p.RunAsync("hello"));

        // The worker side runs what is queued.
        var worker = Host.CreateApplicationBuilder();
        worker.Services.AddPesarollJobs(Db, runJobs: true);
        worker.Services.AddTransient<Ping>();
        using var host = worker.Build();
        await host.StartAsync();
        try
        {
            Assert.Equal("hello", await Ping.Ran.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Recurring_jobs_are_scheduled_by_the_worker_on_their_cron()
    {
        var worker = Host.CreateApplicationBuilder();
        worker.Services.AddPesarollJobs(Db, runJobs: true);
        worker.Services.AddRecurringJob<Nightly>();
        using var host = worker.Build();
        await host.StartAsync();
        try
        {
            using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
            var nightly = Assert.Single(connection.GetRecurringJobs(), j => j.Id == "tests.nightly");
            Assert.Equal("0 2 * * *", nightly.Cron);
            Assert.Equal("Africa/Nairobi", nightly.TimeZoneId);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task The_queue_lives_in_its_own_schema_not_in_public()
    {
        new ServiceCollection().AddLogging().AddPesarollJobs(Db, runJobs: false).BuildServiceProvider().GetRequiredService<JobStorage>();

        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) filter (where table_schema = 'hangfire'), count(*) filter (where table_schema = 'public' and table_name in ('job', 'state', 'jobqueue')) " +
            "from information_schema.tables", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.True(reader.GetInt64(0) > 0);
        Assert.Equal(0, reader.GetInt64(1));
    }
}
