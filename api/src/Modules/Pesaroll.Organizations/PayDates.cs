namespace Pesaroll.Organizations;

/// <summary>
/// When a payroll pays (ONB-06). Monthly schedules pay by a pay day rule; weekly ones on a weekday;
/// every-two-weeks ones every 14 days from their first pay date; daily ones every working day. Weekends are skipped for
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

    /// <summary>The next weekly pay dates on or after <paramref name="from"/>.</summary>
    public static IReadOnlyList<DateOnly> Weekly(DateOnly from, DayOfWeek day, int count = 3)
    {
        var first = from.AddDays(((int)day - (int)from.DayOfWeek + 7) % 7);
        return Enumerable.Range(0, count).Select(i => first.AddDays(7 * i)).ToList();
    }

    /// <summary>The next pay dates every two weeks, counted from <paramref name="firstPayDate"/>, on or after <paramref name="from"/>.</summary>
    public static IReadOnlyList<DateOnly> Fortnightly(DateOnly from, DateOnly firstPayDate, int count = 3)
    {
        var behind = from.DayNumber - firstPayDate.DayNumber;
        var first = behind <= 0 ? firstPayDate : firstPayDate.AddDays((behind + 13) / 14 * 14);
        return Enumerable.Range(0, count).Select(i => first.AddDays(14 * i)).ToList();
    }

    /// <summary>The next working days (Monday to Friday) on or after <paramref name="from"/>.</summary>
    public static IReadOnlyList<DateOnly> Daily(DateOnly from, int count = 3)
    {
        var dates = new List<DateOnly>(count);
        for (var day = from; dates.Count < count; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                dates.Add(day);
            }
        }

        return dates;
    }

    /// <summary>The next pay dates of a schedule with these fields (only its frequency's are read).</summary>
    public static IReadOnlyList<DateOnly> For(string frequency, string? payDayRule, int? payDay, int? payWeekday, DateOnly? firstPayDate, DateOnly from, int count = 3) =>
        frequency switch
        {
            "weekly" => Weekly(from, (DayOfWeek)(payWeekday!.Value % 7), count),
            "fortnightly" => Fortnightly(from, firstPayDate!.Value, count),
            "daily" => Daily(from, count),
            _ => Next(from, payDayRule!, payDay, count),
        };

    public static DateOnly TodayInKenya(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(Kenya).DateTime);

    private static DateOnly BackToFriday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(-1),
        DayOfWeek.Sunday => date.AddDays(-2),
        _ => date,
    };
}
