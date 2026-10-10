namespace Pesaroll.Organizations;

/// <summary>
/// When a monthly payroll pays (ONB-06), from a schedule's pay day rule. Weekends are skipped for
/// the last working day and for a fixed day (paid the Friday before); public holidays join this
/// calculation with the holidays module (HOL). Pay runs (PAY-03) take their pay dates from here.
/// </summary>
public static class PayDates
{
    public const string LastWorkingDay = "last_working_day";
    public const string LastDay = "last_day";
    public const string FixedDay = "fixed_day";

    public static readonly string[] Rules = [LastWorkingDay, LastDay, FixedDay];

    /// <summary>Kenya is on East Africa Time all year (UTC+3, no daylight saving).</summary>
    private static readonly TimeSpan Kenya = TimeSpan.FromHours(3);

    /// <summary>The pay date in a month. <paramref name="day"/> is the day (1 to 28) for a fixed day.</summary>
    public static DateOnly In(int year, int month, string rule, int? day)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return rule switch
        {
            LastDay => last,
            LastWorkingDay => BackToFriday(last),
            FixedDay => BackToFriday(new DateOnly(year, month, day ?? throw new ArgumentNullException(nameof(day)))),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown pay day rule."),
        };
    }

    /// <summary>The next pay dates on or after <paramref name="from"/>.</summary>
    public static IReadOnlyList<DateOnly> Next(DateOnly from, string rule, int? day, int count = 3)
    {
        var dates = new List<DateOnly>(count);
        for (var month = new DateOnly(from.Year, from.Month, 1); dates.Count < count; month = month.AddMonths(1))
        {
            var date = In(month.Year, month.Month, rule, day);
            if (date >= from)
            {
                dates.Add(date);
            }
        }

        return dates;
    }

    public static DateOnly TodayInKenya(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(Kenya).DateTime);

    private static DateOnly BackToFriday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(-1),
        DayOfWeek.Sunday => date.AddDays(-2),
        _ => date,
    };
}
