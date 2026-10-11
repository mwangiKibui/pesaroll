using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Notifications.Tests;

/// <summary>
/// NTF-01: the app queues a message for the signed-in organization; the worker sends it through
/// the channel, retries a few times when the provider refuses, and records what was billed.
/// </summary>
public sealed class NotifierTests
{
    private static readonly string Db = Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private sealed class Jobs : IBackgroundJobClient
    {
        public List<Job> Queued { get; } = [];
        public string Create(Job job, IState state) { Queued.Add(job); return Queued.Count.ToString(); }
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class Channel : IEmailChannel, ISmsChannel
    {
        public List<(string To, Rendered Message)> Sent { get; } = [];
        public string? Refuse { get; set; }
        public Task<Sent> SendAsync(string to, Rendered message, CancellationToken ct)
        {
            if (Refuse is not null) throw new DeliveryException(Refuse);
            Sent.Add((to, message));
            return Task.FromResult(new Sent($"p-{Sent.Count}", SmsParts.Count(message.Sms)));
        }
    }

    private static readonly Dictionary<string, string> Reminder = new()
    {
        ["organization"] = "Mama Oliech Group", ["schedule"] = "Monthly", ["payDate"] = "Fri 30 Oct 2026", ["link"] = "https://app.pesaroll.co.ke",
    };

    private sealed record World(ServiceProvider Services, Jobs Jobs, Channel Email, Channel Sms, Guid OrganizationId);

    private static async Task<World> Organization()
    {
        var org = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        await using (var connection = new NpgsqlConnection(Db))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "insert into users (id, email, first_name, last_name) values (@user, @email, 'Amina', 'Otieno'); " +
                "insert into organizations (id, name, onboarded_by) values (@org, 'Mama Oliech Group', @user);", connection);
            command.Parameters.AddWithValue("user", user);
            command.Parameters.AddWithValue("org", org);
            command.Parameters.AddWithValue("email", $"ntf-{user:N}@example.com");
            await command.ExecuteNonQueryAsync();
        }

        var jobs = new Jobs();
        var email = new Channel();
        var sms = new Channel();
        var services = new ServiceCollection().AddLogging()
            .AddNotificationsModule(Db, new ConfigurationBuilder().Build(), isProduction: false)
            .AddSingleton<IBackgroundJobClient>(jobs)
            .AddSingleton<IEmailChannel>(email)
            .AddSingleton<ISmsChannel>(sms)
            .BuildServiceProvider();
        return new World(services, jobs, email, sms, org);
    }

    private static async Task<Guid> Queue(World world, string channel, string recipient)
    {
        using var scope = world.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.OrganizationId, []);
        return await scope.ServiceProvider.GetRequiredService<Notifier>()
            .QueueAsync(new Message(channel, recipient, "payroll_reminder", Reminder), CancellationToken.None);
    }

    /// <summary>Runs the job as the worker would: in its own scope, told only the ids.</summary>
    private static async Task Run(World world, Guid id, Guid? organizationId = null)
    {
        using var scope = world.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationJob>().SendAsync(id, organizationId ?? world.OrganizationId, CancellationToken.None);
    }

    private static async Task<Delivery> Read(World world, Guid id)
    {
        using var scope = world.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.OrganizationId, []);
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Deliveries.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    [Fact]
    public async Task A_queued_email_is_sent_by_the_worker_and_recorded()
    {
        var world = await Organization();

        var id = await Queue(world, "email", " Wanjiru@Example.com ");

        var job = Assert.Single(world.Jobs.Queued);
        Assert.Equal((typeof(NotificationJob), nameof(NotificationJob.SendAsync)), (job.Type, job.Method.Name));
        Assert.Equal([id, world.OrganizationId], job.Args.Take(2));
        Assert.Equal("queued", (await Read(world, id)).Status);

        await Run(world, id);

        var (to, message) = Assert.Single(world.Email.Sent);
        Assert.Equal("wanjiru@example.com", to);
        Assert.Equal("Reminder: run payroll for Monthly by Fri 30 Oct 2026", message.Subject);
        var sent = await Read(world, id);
        Assert.Equal(("sent", 1, 1, "p-1"), (sent.Status, sent.Attempts, sent.Units, sent.ProviderMessageId));
        Assert.NotNull(sent.SentAt);

        await Run(world, id);   // a second run (a retry after a crash) sends nothing more
        Assert.Single(world.Email.Sent);
    }

    [Fact]
    public async Task An_sms_goes_to_the_number_as_plus_254()
    {
        var world = await Organization();

        var id = await Queue(world, "sms", "0712 345 678");
        await Run(world, id);

        Assert.Equal("+254712345678", Assert.Single(world.Sms.Sent).To);
        Assert.Empty(world.Email.Sent);
    }

    [Fact]
    public async Task Recipients_are_checked_before_anything_is_queued()
    {
        var world = await Organization();

        var phone = await Assert.ThrowsAsync<AppException>(() => Queue(world, "sms", "020 123 4567"));
        var email = await Assert.ThrowsAsync<AppException>(() => Queue(world, "email", "wanjiru@"));

        Assert.Equal(("NTF-0501", "NTF-0501"), (phone.Code, email.Code));
        Assert.Equal("recipient", Assert.Single(phone.Fields!.Keys));
        Assert.Empty(world.Jobs.Queued);
    }

    [Fact]
    public async Task A_refused_message_is_retried_then_marked_failed_with_the_reason()
    {
        var world = await Organization();
        var id = await Queue(world, "sms", "+254712345678");
        world.Sms.Refuse = "Africa's Talking UserInBlacklist (406)";

        for (var attempt = 1; attempt < NotificationJob.MaxAttempts; attempt++)
        {
            await Assert.ThrowsAsync<DeliveryException>(() => Run(world, id));   // Hangfire tries again later
            Assert.Equal(("queued", attempt), ((await Read(world, id)).Status, (await Read(world, id)).Attempts));
        }

        await Run(world, id);   // the last attempt gives up quietly

        var failed = await Read(world, id);
        Assert.Equal(("failed", NotificationJob.MaxAttempts, "NTF-0503", "Africa's Talking UserInBlacklist (406)"),
            (failed.Status, failed.Attempts, failed.ErrorCode, failed.Error));
    }

    [Fact]
    public async Task Usage_counts_the_units_sent_this_month_per_channel()
    {
        var world = await Organization();
        foreach (var id in new[] { await Queue(world, "email", "a@example.com"), await Queue(world, "email", "b@example.com"), await Queue(world, "sms", "0712345678") })
        {
            await Run(world, id);
        }

        await Queue(world, "sms", "0712345679");   // queued, not sent: not billed
        using var scope = world.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.OrganizationId, []);
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3));
        var usage = await Usage.MonthAsync(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>(), now.Year, now.Month, CancellationToken.None);

        Assert.Equal([new ChannelUsage("email", 2, 2), new ChannelUsage("sms", 1, 1)], usage);
    }

    [Fact]
    public async Task A_job_only_reaches_messages_of_the_organization_it_was_queued_for()
    {
        var one = await Organization();
        var two = await Organization();
        var id = await Queue(one, "email", "wanjiru@example.com");

        await Run(one, id, organizationId: two.OrganizationId);

        Assert.Empty(one.Email.Sent);
        Assert.Equal("queued", (await Read(one, id)).Status);
    }
}
