using Pesaroll.Shared.Errors;

namespace Pesaroll.Payroll;

/// <summary>Pay run errors (PAY-2002, PAY-2012 to PAY-2028, docs/standards/errors.md; PAY-2013 is retired).</summary>
public static class PayRunErrors
{
    public static AppException AlreadyExists(string schedule, string period, string company) =>
        new("PAY-2012", $"A pay run for {schedule} in {period} already exists at {company}.", 409);

    public static AppException Final(string status) =>
        new("PAY-2014", $"This pay run is {status} and can't change. To fix an approved pay run, run a correction.", 409);

    public static AppException NotCalculated() => new("PAY-2015", "Work out this pay run before sending it for approval.", 409);

    public static AppException NotFound() => new("PAY-2016", "We couldn't find that pay run. It may be at a company you can't see.", 404);

    public static AppException NobodyPaid() => new("PAY-2017", "Nobody is paid in this pay run. Check who is left out and why.", 409);

    public static AppException OnlyApprovedCorrected() => new("PAY-2018", "Only an approved pay run can be corrected.", 409);

    public static AppException BelowZero(int people) =>
        new("PAY-2019", $"{people} {(people == 1 ? "person" : "people")} would get pay below zero. Check their deductions, then recalculate.", 409);

    public static AppException Invalid(IReadOnlyDictionary<string, string> fields) =>
        new("PAY-2020", "Some pay run details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException WaitingForApproval() => new("PAY-2021", "This pay run is waiting for approval. Cancel the request to change it.", 409);

    public static AppException NotAPayDate(string schedule, string date) =>
        new("PAY-2022", $"{schedule} doesn't pay on {date}. Choose one of its pay dates.") { Fields = new Dictionary<string, string> { ["payDate"] = "Choose one of the schedule's pay dates." } };

    public static AppException EarlierNotApproved(string date) =>
        new("PAY-2023", $"The pay run paid on {date} isn't approved yet. Approve or cancel it first: each run takes what earlier ones in the month didn't.", 409);

    // The catalogue's codes for a schedule or employee this person can't find (Organizations returns them too).
    public static AppException ScheduleNotFound() => new("ONB-3526", "We couldn't find that schedule. It may have been deleted.", 404);

    public static AppException EmployeeNotFound() => new("ONB-3539", "We couldn't find that employee. They may have been deleted.", 404);

    public static AppException BelowMinimumWage(int people) =>
        new("PAY-2002", $"{people} {(people == 1 ? "person is" : "people are")} paid below the minimum wage where they work. Check their rates, then recalculate.", 409);

    public static AppException InputsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("PAY-2025", "Some days or hours don't look right. Check the fields marked.") { Fields = fields };

    public static AppException HoursInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("PAY-2027", "Some working hours don't look right. Check the fields marked.") { Fields = fields };

    public static AppException PayChangeInvalid(Dictionary<string, string> fields) =>
        new("PAY-2028", "Some pay change details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException CompanyNotFound() => new("ONB-3509", "We couldn't find that company.", 404);

    public static AppException NothingToCopy() =>
        new("PAY-2026", "There's no approved pay run on this schedule before this one to copy days and hours from.", 409);

    public static AppException DeductionsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("PAY-2024", "Some deduction details don't look right. Check the fields marked.") { Fields = fields };
}
