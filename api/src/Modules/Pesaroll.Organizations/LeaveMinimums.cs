namespace Pesaroll.Organizations;

/// <summary>The law's minimum for a leave type (statutory_leave): days on full pay, days on half pay, and the longest wait.</summary>
public sealed record LeaveMinimum(string Name, decimal FullPayDays, decimal HalfPayDays, int WaitingMonths);

/// <summary>How a plan gives one leave type: how it's earned, how much (in days or hours), the half-pay part and the wait.</summary>
public sealed record LeaveRuleTerms(string EarnMethod, decimal? Amount, string Unit, decimal HalfPay, int WaitingMonths);

/// <summary>
/// Legal minimums for leave (ONB-08), whatever the pay frequency. Each way of earning becomes a
/// yearly total in days: a set number a year as it is, ×12 months, ×52 weeks, ×261 working days for
/// leave per day worked, and through the plan's hours in a working day for hours. Leave given each
/// time counts per occasion. Leave earned per day or hour worked depends on what is worked, so it
/// can't be held to a yearly minimum here (the web warns; balances use the real days, LV-01).
/// </summary>
public static class LeaveMinimums
{
    /// <summary>Working days in a year (52 weeks of 5 days, plus one), for leave earned per day or hour worked.</summary>
    public const int WorkingDaysAYear = 261;

    /// <summary>What a way of earning adds up to in a year, in days (per occasion for each_time); null with no limit.</summary>
    public static decimal? DaysAYear(string earnMethod, decimal? amount, string unit, decimal hoursPerDay)
    {
        if (amount is not decimal a || earnMethod == "unlimited")
        {
            return null;
        }

        var inUnit = earnMethod switch
        {
            "monthly" => a * 12,
            "weekly" => a * 52,
            "per_day_worked" => a * WorkingDaysAYear,
            "per_hour_worked" => a * hoursPerDay * WorkingDaysAYear,
            _ => a,   // yearly, each_time
        };
        return Math.Round(unit == "hours" ? inUnit / hoursPerDay : inUnit, 1);
    }

    /// <summary>What falls short of the law, by field (amount, waitingMonths), with a message.</summary>
    public static IReadOnlyList<(string Field, string Message)> Check(LeaveRuleTerms rule, LeaveMinimum law, decimal hoursPerDay)
    {
        var problems = new List<(string, string)>();
        if (rule.EarnMethod is "yearly" or "monthly" or "weekly" or "each_time")
        {
            var full = DaysAYear(rule.EarnMethod, rule.Amount, rule.Unit, hoursPerDay) ?? 0;
            var half = DaysAYear(rule.EarnMethod, rule.HalfPay, rule.Unit, hoursPerDay) ?? 0;
            if (full < law.FullPayDays || full + half < law.FullPayDays + law.HalfPayDays)
            {
                problems.Add(("amount", law.HalfPayDays > 0
                    ? $"{law.Name} in Kenya is at least {law.FullPayDays:0.##} days on full pay and {law.HalfPayDays:0.##} on half pay a year."
                    : $"{law.Name} in Kenya is at least {law.FullPayDays:0.##} days{(rule.EarnMethod == "each_time" ? " each time" : " a year")}."));
            }
        }

        if (rule.WaitingMonths > law.WaitingMonths)
        {
            problems.Add(("waitingMonths", law.WaitingMonths == 0
                ? $"{law.Name} can be used from the start in Kenya."
                : $"{law.Name} can be used after {law.WaitingMonths} months' service in Kenya, not later."));
        }

        return problems;
    }
}
