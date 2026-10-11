using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-06: the hourly worker job sends each reminder when it is due, through the notification
/// service, once per pay date, and only from inside the reminder's organization.
/// </summary>
public sealed class PayrollRemindersJobTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    private sealed record Token(string AccessToken);
    private sealed record Org(Guid Id, string Name);
    private sealed record Created(Org Organization, Token Token);
    private sealed record Company(Guid Id);
    private sealed record Reminder(Guid Id);
    private sealed record Schedule(Guid Id, List<DateOnly> NextPayDates, List<Reminder> Reminders);

    private static readonly string Db = System.Environment.GetEnvironmentVariable("PESAROLL_TEST_DB")!;

    private async Task<(Schedule Schedule, Org Organization)> ScheduleWithReminder(string frequency = "monthly", string channel = "email", string recipient = "payroll@example.com", int amount = 3)
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            firstName = "Amina", lastName = "Otieno", email = $"rem-{Guid.NewGuid():N}@example.com", password = "Mombasa-raha-7",
            roleId = Guid.Parse("0199c0de-5a5e-7000-8000-000000000102"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signup.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var created = (await (await client.PostAsJsonAsync("/api/organizations", new { name = "Mama Oliech Group" })).Content.ReadFromJsonAsync<Created>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token.AccessToken);
        var company = (await (await client.PostAsJsonAsync("/api/companies", new { legalName = $"Oliech {Guid.NewGuid():N}", countryCode = "KE" }))
            .Content.ReadFromJsonAsync<Company>())!;
        var schedule = (await (await client.PostAsJsonAsync("/api/pay-schedules", new
        {
            name = "Reminded", frequency, payWeekday = 5, allCompanies = false, companyIds = new[] { company.Id },
            reminders = new[] { new { amount, channel, recipient } },
        })).Content.ReadFromJsonAsync<Schedule>())!;
        return (schedule, created.Organization);
    }

    private async Task<int> Run(DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PayrollRemindersJob>().RunAtAsync(now, CancellationToken.None);
    }

    private static async Task<List<(string Channel, string Recipient, string Data, DateOnly PayDate)>> Sent(Guid reminderId)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select d.channel, d.recipient, d.data::text, s.pay_date from pay_schedule_reminder_sends s " +
            "join notification_deliveries d on d.id = s.delivery_id where s.reminder_id = @id", connection);
        command.Parameters.AddWithValue("id", reminderId);
        var rows = new List<(string, string, string, DateOnly)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), DateOnly.FromDateTime(reader.GetDateTime(3))));
        }

        return rows;
    }

    [Fact]
    public async Task A_due_reminder_is_queued_once_with_the_schedule_and_pay_date()
    {
        var (schedule, organization) = await ScheduleWithReminder();
        var payDate = schedule.NextPayDates[0];
        var due = ReminderTimes.SendAt(payDate, 3, "days", null);
        var reminder = Assert.Single(schedule.Reminders);

        await Run(due.AddMinutes(-1));
        Assert.Empty(await Sent(reminder.Id));

        await Run(due.AddMinutes(5));
        await Run(due.AddMinutes(65));   // the next hour's run: already sent

        var (channel, recipient, data, sentFor) = Assert.Single(await Sent(reminder.Id));
        Assert.Equal(("email", "payroll@example.com", payDate), (channel, recipient, sentFor));
        Assert.Contains("\"schedule\": \"Reminded\"", data);
        Assert.Contains($"\"organization\": \"{organization.Name}\"", data);
        Assert.Contains(payDate.ToString("ddd d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture), data);
    }

    [Fact]
    public async Task Sms_reminders_and_daily_pay_follow_their_own_timing()
    {
        var (schedule, _) = await ScheduleWithReminder(frequency: "daily", channel: "sms", recipient: "0712 345 678", amount: 2);
        var payDate = schedule.NextPayDates[0];
        var reminder = Assert.Single(schedule.Reminders);

        await Run(ReminderTimes.SendAt(payDate, 2, "hours", new TimeOnly(17, 0)));

        var (channel, recipient, data, _) = Assert.Single(await Sent(reminder.Id));
        Assert.Equal(("sms", "+254712345678"), (channel, recipient));
        Assert.Contains("at 5:00 pm", data);
    }
}
