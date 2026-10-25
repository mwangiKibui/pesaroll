using Pesaroll.Shared.Errors;

namespace Pesaroll.Payroll;

/// <summary>Pay run errors (PAY-2012 to PAY-2021, docs/standards/errors.md).</summary>
public static class PayRunErrors
{
    public static AppException AlreadyExists(string schedule, string month, string company) =>
        new("PAY-2012", $"A pay run for {schedule} in {month} already exists at {company}.", 409);

    public static AppException MonthlyOnly() => new("PAY-2013", "Only monthly payroll schedules can be run for now.", 422);

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
}
