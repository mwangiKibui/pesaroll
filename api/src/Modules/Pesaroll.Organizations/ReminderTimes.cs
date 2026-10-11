namespace Pesaroll.Organizations;

/// <summary>A reminder and the timing of its schedule: all the worker needs to know when it is due.</summary>
public sealed record ReminderTiming(
    Guid ReminderId, Guid OrganizationId, int Amount, string Unit,
    string Frequency, string? PayDayRule, int? PayDay, int? PayWeekday, DateOnly? FirstPayDate, TimeOnly? PayTime);

/// <summary>
/// When reminders to run payroll go out (ONB-06). Day reminders go at 8:00 Kenya time that many days
/// before the pay day; hour reminders (daily pay) that many hours before the pay time. The worker
/// checks every hour and sends anything due in the last day, so a stopped worker catches up, but
/// never for a pay day that has passed. The send log stops a reminder going twice.
/// </summary>
public static class ReminderTimes
{
    private static readonly TimeSpan Kenya = TimeSpan.FromHours(3);
    private static readonly TimeOnly Morning = new(8, 0);
    private static readonly TimeSpan CatchUp = TimeSpan.FromDays(1);

    public static DateTimeOffset SendAt(DateOnly payDate, int amount, string unit, TimeOnly? payTime) => unit == "hours"
        ? new DateTimeOffset(payDate.ToDateTime(payTime ?? new TimeOnly(17, 0)), Kenya).AddHours(-amount)
        : new DateTimeOffset(payDate.AddDays(-amount).ToDateTime(Morning), Kenya);

    /// <summary>The pay dates this reminder is due for at <paramref name="now"/> (usually none, or one).</summary>
    public static IReadOnlyList<DateOnly> Due(ReminderTiming r, DateTimeOffset now)
    {
        var today = PayDates.TodayInKenya(now);
        return PayDates.For(r.Frequency, r.PayDayRule, r.PayDay, r.PayWeekday, r.FirstPayDate, today, count: 3)
            .Where(payDate => SendAt(payDate, r.Amount, r.Unit, r.PayTime) is var at && at <= now && at > now - CatchUp)
            .ToList();
    }
}
