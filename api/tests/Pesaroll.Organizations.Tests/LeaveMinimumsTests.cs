namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ONB-08: a leave plan can't give less than the law, however the leave is earned. Each way of
/// earning becomes a yearly total in days (hours through the plan's hours in a working day) and is
/// compared with the minimum: annual 21 days, sick 7 full + 7 half pay, maternity 3 months.
/// </summary>
public sealed class LeaveMinimumsTests
{
    private static readonly LeaveMinimum Annual = new("Annual leave", 21, 0, 0);
    private static readonly LeaveMinimum Sick = new("Sick leave", 7, 7, 2);
    private static readonly LeaveMinimum Maternity = new("Maternity leave", 90, 0, 0);

    private static List<string> Problems(LeaveMinimum min, string earn, decimal? amount, string unit = "days", decimal halfPay = 0, int waiting = 0, decimal hoursPerDay = 8) =>
        LeaveMinimums.Check(new LeaveRuleTerms(earn, amount, unit, halfPay, waiting), min, hoursPerDay).Select(p => p.Field).ToList();

    [Theory]
    [InlineData("yearly", 21)]
    [InlineData("yearly", 25)]
    [InlineData("monthly", 1.75)]   // 21 a year
    [InlineData("weekly", 0.4038)]  // 20.9976: rounds to 21
    public void Annual_leave_meets_the_law_however_it_is_earned(string earn, double amount) =>
        Assert.Empty(Problems(Annual, earn, (decimal)amount));

    [Theory]
    [InlineData("yearly", 15)]
    [InlineData("monthly", 1.5)]
    [InlineData("weekly", 0.3)]
    public void Less_than_21_days_a_year_is_refused(string earn, double amount) =>
        Assert.Equal(["amount"], Problems(Annual, earn, (decimal)amount));

    [Fact]
    public void Hours_count_through_the_plans_hours_in_a_working_day()
    {
        Assert.Empty(Problems(Annual, "monthly", 14, unit: "hours"));                      // 14 h × 12 / 8 h = 21 days
        Assert.Equal(["amount"], Problems(Annual, "monthly", 14, unit: "hours", hoursPerDay: 10));   // 16.8 days
    }

    [Fact]
    public void Sick_leave_needs_7_days_on_full_pay_and_14_in_all()
    {
        Assert.Empty(Problems(Sick, "yearly", 7, halfPay: 7));
        Assert.Empty(Problems(Sick, "yearly", 14));                        // all on full pay is more
        Assert.Equal(["amount"], Problems(Sick, "yearly", 10));            // a flat 10 days is less than 14
        Assert.Equal(["amount"], Problems(Sick, "yearly", 5, halfPay: 9)); // 14 in all, but only 5 on full pay
        Assert.Equal(["waitingMonths"], Problems(Sick, "yearly", 7, halfPay: 7, waiting: 3));   // the law: after 2 months
        Assert.Empty(Problems(Sick, "yearly", 7, halfPay: 7, waiting: 1));
    }

    [Fact]
    public void Leave_given_each_time_counts_per_occasion()
    {
        Assert.Empty(Problems(Maternity, "each_time", 90));
        Assert.Equal(["amount"], Problems(Maternity, "each_time", 60));
    }

    [Fact]
    public void Leave_earned_per_day_or_hour_worked_or_with_no_limit_cant_be_checked_against_a_yearly_minimum()
    {
        Assert.Empty(Problems(Annual, "per_day_worked", 0.01m));
        Assert.Empty(Problems(Annual, "per_hour_worked", 0.001m, unit: "hours"));
        Assert.Empty(Problems(Annual, "unlimited", null));
    }

    [Theory]
    [InlineData("yearly", 20, 20)]
    [InlineData("monthly", 1.75, 21)]
    [InlineData("weekly", 0.4038, 21)]
    [InlineData("per_day_worked", 0.0805, 21)]   // over a full year of about 261 working days
    public void A_yearly_total_is_shown_for_each_way_of_earning(string earn, double amount, double days) =>
        Assert.Equal((decimal)days, LeaveMinimums.DaysAYear(earn, (decimal)amount, "days", 8));
}
