using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Notifications;

/// <summary>An email or SMS for an organization (database/schema/tenancy/notifications.sql).</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    /// <summary>email or sms.</summary>
    public required string Channel { get; set; }
    /// <summary>An email address, or a Kenyan mobile number as +254….</summary>
    public required string Recipient { get; set; }
    public required string Template { get; set; }
    /// <summary>The template's values, as JSON. Never pay figures.</summary>
    public string Data { get; set; } = "{}";
    public string Language { get; set; } = "en";
    /// <summary>queued, sent or failed.</summary>
    public string Status { get; set; } = "queued";
    public int Attempts { get; set; }
    /// <summary>What the provider bills once sent: SMS parts, or 1 for an email.</summary>
    public int? Units { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder model) =>
        model.Entity<Notification>().Property(n => n.Data).HasColumnType("jsonb");
}

/// <summary>A message to send: by email or sms, to whom, and which template with which values.</summary>
public sealed record Message(string Channel, string Recipient, string Template, IReadOnlyDictionary<string, string> Data, string Language = "en");

public static class NotificationErrors
{
    public static AppException RecipientInvalid(string channel) =>
        new("NTF-0501", channel == "sms"
            ? "Enter a Kenyan mobile number, like 0712 345 678."
            : "Enter a valid email address.") { Fields = new Dictionary<string, string> { ["recipient"] = channel == "sms" ? "Enter a Kenyan mobile number, like 0712 345 678." : "Enter a valid email address." } };

    /// <summary>Recorded on a message the provider kept refusing (support reads the reason in error).</summary>
    public const string EmailNotDelivered = "NTF-0502";
    public const string SmsNotDelivered = "NTF-0503";
}

/// <summary>
/// Queues messages for the current organization (NTF-01). The message is saved first and the worker
/// sends it (<see cref="NotificationJob"/>), so a slow or failing provider never holds up a request.
/// </summary>
public sealed class Notifier(NotificationsDbContext db, TenantContext tenant, IBackgroundJobClient jobs, TimeProvider clock)
{
    public async Task<Guid> QueueAsync(Message message, CancellationToken ct)
    {
        var organizationId = tenant.OrganizationId ?? throw new InvalidOperationException("Messages are queued for an organization.");
        var recipient = message.Channel switch
        {
            "email" => Recipients.Email(message.Recipient),
            "sms" => Recipients.KenyanMobile(message.Recipient),
            _ => throw new ArgumentException($"Unknown channel {message.Channel}.", nameof(message)),
        } ?? throw NotificationErrors.RecipientInvalid(message.Channel);
        Templates.Render(message.Template, message.Language, message.Data);   // fails now, not in the worker, if a value is missing

        var now = clock.GetUtcNow();
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, Channel = message.Channel, Recipient = recipient,
            Template = message.Template, Data = JsonSerializer.Serialize(message.Data), Language = message.Language, CreatedAt = now, UpdatedAt = now,
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
        jobs.Enqueue<NotificationJob>(job => job.SendAsync(notification.Id, organizationId, CancellationToken.None));
        return notification.Id;
    }
}

/// <summary>
/// Sends one queued message (runs in the worker). It works inside the message's organization, so
/// row-level security applies as for a request. A refusal is retried by Hangfire with growing
/// delays; after <see cref="MaxAttempts"/> the message is marked failed with the provider's reason.
/// </summary>
public sealed class NotificationJob(
    NotificationsDbContext db, TenantContext tenant, IEmailChannel email, ISmsChannel sms, TimeProvider clock, ILogger<NotificationJob> logger)
{
    public const int MaxAttempts = 5;

    [AutomaticRetry(Attempts = MaxAttempts - 1)]
    public async Task SendAsync(Guid notificationId, Guid organizationId, CancellationToken ct)
    {
        tenant.Set(organizationId, []);
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == notificationId, ct);
        if (notification is not { Status: "queued" })
        {
            return;   // another organization's, already sent, or given up on
        }

        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(notification.Data) ?? [];
        var message = Templates.Render(notification.Template, notification.Language, data);
        notification.Attempts++;
        notification.UpdatedAt = clock.GetUtcNow();
        try
        {
            var sent = notification.Channel == "sms"
                ? await sms.SendAsync(notification.Recipient, message, ct)
                : await email.SendAsync(notification.Recipient, message, ct);
            notification.Status = "sent";
            notification.Units = sent.Units;
            notification.ProviderMessageId = sent.ProviderId;
            notification.SentAt = clock.GetUtcNow();
            notification.Error = null;
            notification.ErrorCode = null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DeliveryException error)
        {
            notification.Error = error.Message;
            notification.ErrorCode = notification.Channel == "sms" ? NotificationErrors.SmsNotDelivered : NotificationErrors.EmailNotDelivered;
            var last = notification.Attempts >= MaxAttempts;
            if (last)
            {
                notification.Status = "failed";
            }

            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("{Channel} to {To} refused (attempt {Attempt}): {Reason}",
                notification.Channel, Recipients.Mask(notification.Recipient), notification.Attempts, error.Message);
            if (!last)
            {
                throw;
            }
        }
    }
}

public sealed record ChannelUsage(string Channel, int Messages, int Units);

/// <summary>What the organization sent in a month (Kenya time), per channel: what BILL-10 bills against the allowance.</summary>
public static class Usage
{
    private static readonly TimeSpan Kenya = TimeSpan.FromHours(3);

    public static async Task<List<ChannelUsage>> MonthAsync(NotificationsDbContext db, int year, int month, CancellationToken ct)
    {
        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, Kenya);
        var (from, to) = (start.ToUniversalTime(), start.AddMonths(1).ToUniversalTime());   // timestamptz takes UTC
        var rows = await db.Notifications
            .Where(n => n.Status == "sent" && n.SentAt >= from && n.SentAt < to)
            .GroupBy(n => n.Channel)
            .Select(g => new { Channel = g.Key, Messages = g.Count(), Units = g.Sum(n => n.Units ?? 0) })
            .ToListAsync(ct);
        return rows.OrderBy(r => r.Channel).Select(r => new ChannelUsage(r.Channel, r.Messages, r.Units)).ToList();
    }
}
