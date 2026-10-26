using Pesaroll.Organizations;

namespace Pesaroll.Payroll;

/// <summary>The fields of a schedule that say when it pays: a weekday (weekly, fortnightly) and a first pay date (fortnightly).</summary>
public sealed record ScheduleDates(string Frequency, int? PayWeekday, DateOnly? FirstPayDate);

/// <summary>
/// The days a weekly, fortnightly or daily run covers (PAY-10): the week or fortnight up to its pay
/// date, or the pay date itself. A run counts in the tax month of its pay date, so a week across two
/// months counts in the month it is paid.
/// </summary>
public static class RunPeriods
{
    public static RunPeriod Ending(string frequency, DateOnly payDate) => frequency switch
    {
        "weekly" => new RunPeriod(payDate.AddDays(-6), payDate),
        "fortnightly" => new RunPeriod(payDate.AddDays(-13), payDate),
        "daily" => new RunPeriod(payDate, payDate),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Monthly runs are by month."),
    };

    public static bool IsPayDate(ScheduleDates schedule, DateOnly date) =>
        PayDates.For(schedule.Frequency, null, null, schedule.PayWeekday, schedule.FirstPayDate, date, 1)[0] == date;

    /// <summary>
    /// A run's cut-off day (PAY-04): a monthly schedule's day in the period's month, else the days set
    /// before the pay date (null: the schedule doesn't use one, and a daily schedule never does).
    /// </summary>
    public static DateOnly? CutoffOn(string frequency, RunPeriod period, DateOnly payDate, int? cutoffDay, int? daysBefore) => frequency switch
    {
        "monthly" when cutoffDay is { } day => new DateOnly(period.End.Year, period.End.Month, Math.Min(day, DateTime.DaysInMonth(period.End.Year, period.End.Month))),
        "weekly" or "fortnightly" when daysBefore is { } before => payDate.AddDays(-before),
        _ => null,
    };

    public static (DateOnly Start, DateOnly End) TaxMonth(DateOnly payDate)
    {
        var start = new DateOnly(payDate.Year, payDate.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}
