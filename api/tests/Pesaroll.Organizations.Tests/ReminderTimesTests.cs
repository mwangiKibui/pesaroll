namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-06 reminders: when a reminder to run payroll goes out. Day reminders go at 8:00 Kenya time
/// that many days before the pay day; hour reminders (daily pay) that many hours before the pay
/// time. The worker checks every hour and catches up on anything due in the last day.
/// </summary>
public sealed class ReminderTimesTests
{
    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso + "Z");
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    private static readonly ReminderTiming MonthlyThreeDays = new(Guid.Empty, Guid.Empty, 3, "days",
        "monthly", PayDates.LastWorkingDay, null, null, null, null);

    [Fact]
    public void A_day_reminder_goes_at_eight_in_the_morning_Kenya_time() =>
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.FromHours(3)), ReminderTimes.SendAt(D("2026-10-30"), 3, "days", null));

    [Fact]
    public void An_hour_reminder_counts_back_from_the_pay_time() =>
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 15, 0, 0, TimeSpan.FromHours(3)), ReminderTimes.SendAt(D("2026-10-12"), 2, "hours", new TimeOnly(17, 0)));

    [Fact]
    public void It_is_due_from_its_time_and_for_a_day_after_it_in_case_the_worker_was_down()
    {
        // Pay day Fri 30 Oct 2026 (the 31st is a Saturday); 3 days before is Tue 27 Oct, 8:00 (05:00 UTC).
        Assert.Empty(ReminderTimes.Due(MonthlyThreeDays, Utc("2026-10-27T04:59:00")));
        Assert.Equal([D("2026-10-30")], ReminderTimes.Due(MonthlyThreeDays, Utc("2026-10-27T05:00:00")));
        Assert.Equal([D("2026-10-30")], ReminderTimes.Due(MonthlyThreeDays, Utc("2026-10-28T04:00:00")));
        Assert.Empty(ReminderTimes.Due(MonthlyThreeDays, Utc("2026-10-28T06:00:00")));
    }

    [Fact]
    public void Nothing_is_sent_for_a_pay_day_that_has_passed()
    {
        var oneDay = MonthlyThreeDays with { Amount = 1 };   // Thu 29 Oct, 8:00

        Assert.Equal([D("2026-10-30")], ReminderTimes.Due(oneDay, Utc("2026-10-30T03:00:00")));   // pay day morning: still useful
        Assert.Empty(ReminderTimes.Due(oneDay, Utc("2026-10-31T03:00:00")));
    }

    [Fact]
    public void Weekly_and_daily_reminders_follow_their_pay_days()
    {
        var weekly = new ReminderTiming(Guid.Empty, Guid.Empty, 2, "days", "weekly", null, null, 5, null, null);   // Fridays, Wednesday 8:00
        var daily = new ReminderTiming(Guid.Empty, Guid.Empty, 2, "hours", "daily", null, null, null, null, new TimeOnly(17, 0));

        Assert.Equal([D("2026-10-16")], ReminderTimes.Due(weekly, Utc("2026-10-14T05:00:00")));
        Assert.Equal([D("2026-10-12")], ReminderTimes.Due(daily, Utc("2026-10-12T12:00:00")));   // Monday 15:00 for 17:00
        Assert.Empty(ReminderTimes.Due(daily, Utc("2026-10-10T12:00:00")));                      // Saturday: no pay, no reminder
    }
}
