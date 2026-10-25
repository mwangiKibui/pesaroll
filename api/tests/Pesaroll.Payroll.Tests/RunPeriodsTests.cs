namespace Pesaroll.Payroll.Tests;

/// <summary>PAY-10: the days a weekly, fortnightly or daily run covers, and which days are a schedule's pay dates.</summary>
public sealed class RunPeriodsTests
{
    [Theory]
    [InlineData("weekly", "2026-10-09", "2026-10-03")]
    [InlineData("fortnightly", "2026-10-09", "2026-09-26")]
    [InlineData("daily", "2026-10-09", "2026-10-09")]
    [InlineData("weekly", "2026-10-02", "2026-09-26")]   // across two months: counts in October, its pay date's
    public void A_run_covers_the_days_up_to_its_pay_date(string frequency, string payDate, string start)
    {
        var period = RunPeriods.Ending(frequency, DateOnly.Parse(payDate));

        Assert.Equal(DateOnly.Parse(start), period.Start);
        Assert.Equal(DateOnly.Parse(payDate), period.End);
    }

    [Theory]
    [InlineData("weekly", "2026-10-09", true)]        // a Friday
    [InlineData("weekly", "2026-10-08", false)]
    [InlineData("fortnightly", "2026-10-16", true)]   // two weeks after 2 October
    [InlineData("fortnightly", "2026-10-09", false)]
    [InlineData("daily", "2026-10-09", true)]
    [InlineData("daily", "2026-10-10", false)]        // a Saturday
    public void Only_the_schedules_pay_dates_can_be_run(string frequency, string date, bool payDate)
    {
        var schedule = new ScheduleDates(frequency, 5, frequency == "fortnightly" ? new DateOnly(2026, 10, 2) : null);

        Assert.Equal(payDate, RunPeriods.IsPayDate(schedule, DateOnly.Parse(date)));
    }

    [Fact]
    public void The_tax_month_is_the_pay_dates()
    {
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), RunPeriods.TaxMonth(new DateOnly(2026, 10, 2)));
    }
}
