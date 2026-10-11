using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pesaroll.Notifications;
using Pesaroll.Shared.Jobs;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// Sends reminders to run payroll (ONB-06), every hour in the worker. It finds every reminder's
/// timing across organizations (pay_schedule_reminder_timings, which never says who they go to),
/// works out which are due (<see cref="ReminderTimes"/>), then, inside each reminder's organization
/// under row-level security, claims the pay date in the send log and queues the email or SMS. A
/// claimed pay date is never sent again, even if queuing fails: a missed reminder is better than
/// two SMS.
/// </summary>
public sealed class PayrollRemindersJob(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration configuration, ILogger<PayrollRemindersJob> logger)
    : IRecurringJob
{
    public string Id => "reminders.payroll";
    public string Cron => "0 * * * *";

    public Task RunAsync(CancellationToken ct) => RunAtAsync(clock.GetUtcNow(), ct);

    /// <summary>Sends what is due at <paramref name="now"/>; returns how many reminders were queued.</summary>
    public async Task<int> RunAtAsync(DateTimeOffset now, CancellationToken ct)
    {
        now = now.ToUniversalTime();   // timestamptz takes UTC
        var timings = await TimingsAsync(ct);
        var due = timings.SelectMany(t => ReminderTimes.Due(t.Timing, now).Select(payDate => (t.Timing, t.CompanyIds, PayDate: payDate))).ToList();
        var queued = 0;
        foreach (var organization in due.GroupBy(d => d.Timing.OrganizationId))
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(organization.Key, organization.First().CompanyIds);
            var db = scope.ServiceProvider.GetRequiredService<OrganizationsDbContext>();
            var notifier = scope.ServiceProvider.GetRequiredService<Notifier>();
            var name = await db.Organizations.Where(o => o.Id == organization.Key).Select(o => o.Name).SingleAsync(ct);
            foreach (var (timing, _, payDate) in organization)
            {
                if (await SendAsync(db, notifier, name, timing, payDate, now, ct))
                {
                    queued++;
                }
            }
        }

        return queued;
    }

    private async Task<bool> SendAsync(OrganizationsDbContext db, Notifier notifier, string organization, ReminderTiming timing, DateOnly payDate,
        DateTimeOffset now, CancellationToken ct)
    {
        var reminder = await db.PayScheduleReminders.SingleOrDefaultAsync(r => r.Id == timing.ReminderId, ct);
        var schedule = reminder is null ? null : await db.PaySchedules.SingleOrDefaultAsync(s => s.Id == reminder.PayScheduleId, ct);
        if (reminder is null || schedule is null)
        {
            return false;   // deleted since
        }

        var send = new ReminderSend { OrganizationId = reminder.OrganizationId, ReminderId = reminder.Id, PayDate = payDate, CreatedAt = now };
        db.ReminderSends.Add(send);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(send).State = EntityState.Detached;
            return false;   // already sent for this pay date
        }

        try
        {
            var when = payDate.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);
            if (schedule.Frequency == "daily")
            {
                when += " at " + (schedule.PayTime ?? new TimeOnly(17, 0)).ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
            }

            send.DeliveryId = await notifier.QueueAsync(new Message(reminder.Channel, reminder.Recipient, "payroll_reminder", new Dictionary<string, string>
            {
                ["organization"] = organization, ["schedule"] = schedule.Name, ["payDate"] = when,
                ["link"] = configuration["App:Url"] ?? "https://app.pesaroll.co.ke",
            }), ct);
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Reminder {Reminder} for {PayDate} was claimed but not queued", reminder.Id, payDate);
            return false;
        }
    }

    private sealed record Found(ReminderTiming Timing, Guid[] CompanyIds);

    private async Task<List<Found>> TimingsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();   // no organization: the function is the only way in
        var connection = scope.ServiceProvider.GetRequiredService<OrganizationsDbContext>().Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "select reminder_id, organization_id, company_ids, amount, unit, frequency, pay_day_rule, pay_day, " +
                              "pay_weekday, first_pay_date, pay_time from pay_schedule_reminder_timings()";
        var found = new List<Found>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            int? Int(int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
            found.Add(new Found(new ReminderTiming(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), Int(7), Int(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<TimeOnly>(10)),
                reader.GetFieldValue<Guid[]>(2)));
        }

        return found;
    }
}
