namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-06: when a monthly payroll pays. A wrong date means salaries paid late or early, so every
/// rule is checked across month ends, weekends and leap years.
/// </summary>
public sealed class PayDatesTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    [Theory]
    [InlineData(2026, 10, "2026-10-30")]   // the 31st is a Saturday
    [InlineData(2026, 11, "2026-11-30")]   // a Monday
    [InlineData(2027, 1, "2027-01-29")]    // the 31st is a Sunday
    [InlineData(2027, 2, "2027-02-26")]    // the 28th is a Sunday
    [InlineData(2028, 2, "2028-02-29")]    // leap year, a Tuesday
    public void Last_working_day_is_the_last_weekday_of_the_month(int year, int month, string expected) =>
        Assert.Equal(D(expected), PayDates.In(year, month, PayDates.LastWorkingDay, null));

    [Theory]
    [InlineData(2026, 10, "2026-10-31")]
    [InlineData(2027, 2, "2027-02-28")]
    [InlineData(2028, 2, "2028-02-29")]
    public void Last_day_is_the_last_calendar_day_even_on_a_weekend(int year, int month, string expected) =>
        Assert.Equal(D(expected), PayDates.In(year, month, PayDates.LastDay, null));

    [Theory]
    [InlineData(2026, 10, 15, "2026-10-15")]   // a Thursday
    [InlineData(2026, 11, 15, "2026-11-13")]   // the 15th is a Sunday: the Friday before
    [InlineData(2027, 5, 15, "2027-05-14")]    // the 15th is a Saturday
    [InlineData(2026, 12, 28, "2026-12-28")]
    public void A_fixed_day_moves_back_to_the_Friday_when_it_falls_on_a_weekend(int year, int month, int day, string expected) =>
        Assert.Equal(D(expected), PayDates.In(year, month, PayDates.FixedDay, day));

    [Fact]
    public void The_next_three_pay_dates_start_this_month_when_it_is_still_ahead()
    {
        Assert.Equal([D("2026-10-30"), D("2026-11-30"), D("2026-12-31")], PayDates.Next(D("2026-10-10"), PayDates.LastWorkingDay, null));
        Assert.Equal(D("2026-10-30"), PayDates.Next(D("2026-10-30"), PayDates.LastWorkingDay, null)[0]);   // pay day itself counts
        Assert.Equal(D("2026-11-30"), PayDates.Next(D("2026-10-31"), PayDates.LastWorkingDay, null)[0]);
        Assert.Equal([D("2026-11-13"), D("2026-12-15"), D("2027-01-15")], PayDates.Next(D("2026-10-16"), PayDates.FixedDay, 15));
    }

    [Fact]
    public void Weekly_pays_on_the_weekday_from_today_on()
    {
        Assert.Equal([D("2026-10-16"), D("2026-10-23"), D("2026-10-30")], PayDates.Weekly(D("2026-10-10"), DayOfWeek.Friday));
        Assert.Equal(D("2026-10-16"), PayDates.Weekly(D("2026-10-16"), DayOfWeek.Friday)[0]);   // pay day itself counts
        Assert.Equal([D("2026-10-12"), D("2026-10-19"), D("2026-10-26")], PayDates.Weekly(D("2026-10-10"), DayOfWeek.Monday));
    }

    [Fact]
    public void Every_two_weeks_counts_from_the_first_pay_date()
    {
        Assert.Equal([D("2026-10-16"), D("2026-10-30"), D("2026-11-13")], PayDates.Fortnightly(D("2026-10-10"), D("2026-10-16")));
        Assert.Equal([D("2026-10-30"), D("2026-11-13"), D("2026-11-27")], PayDates.Fortnightly(D("2026-10-17"), D("2026-10-16")));
        Assert.Equal([D("2026-10-16"), D("2026-10-30"), D("2026-11-13")], PayDates.Fortnightly(D("2026-10-10"), D("2026-09-18")));   // started earlier
        Assert.Equal([D("2026-12-25"), D("2027-01-08"), D("2027-01-22")], PayDates.Fortnightly(D("2026-10-10"), D("2026-12-25")));   // starts later
    }

    [Fact]
    public void Daily_pays_every_working_day()
    {
        Assert.Equal([D("2026-10-12"), D("2026-10-13"), D("2026-10-14")], PayDates.Daily(D("2026-10-10")));   // from a Saturday
        Assert.Equal([D("2026-10-16"), D("2026-10-19"), D("2026-10-20")], PayDates.Daily(D("2026-10-16")));   // Friday, then Monday
    }

    [Fact]
    public void Today_in_Kenya_is_three_hours_ahead_of_UTC() =>
        Assert.Equal(D("2026-10-11"), PayDates.TodayInKenya(new DateTimeOffset(2026, 10, 10, 21, 30, 0, TimeSpan.Zero)));
}
