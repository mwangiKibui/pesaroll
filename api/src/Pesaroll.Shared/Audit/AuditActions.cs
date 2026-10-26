namespace Pesaroll.Shared.Audit;

/// <summary>
/// The named actions the audit log records (PLAT-03), by key, each with the details it may carry:
/// the catalogue in docs/standards/audit.md, worded in web/src/i18n/locales (audit.actions). Details
/// are only ever what is listed here, never personal data (CLAUDE.md rule 8): no amounts, ID numbers
/// or accounts. A new feature adds its actions here, to the doc and to the locales.
/// </summary>
public static class AuditActions
{
    public const string PayRunStarted = "pay_run.started";
    public const string PayRunCalculated = "pay_run.calculated";
    public const string PayRunSent = "pay_run.sent";
    public const string PayRunApproved = "pay_run.approved";
    public const string PayRunDeclined = "pay_run.declined";
    public const string PayRunWithdrawn = "pay_run.withdrawn";
    public const string PayRunCancelled = "pay_run.cancelled";
    public const string PayRunCorrected = "pay_run.corrected";
    public const string EmployeeAdded = "employee.added";
    public const string EmployeeRemoved = "employee.removed";
    public const string EmployeePayChanged = "employee.pay_changed";
    public const string EmployeeBankChanged = "employee.bank_changed";
    public const string RecordsImported = "records.imported";
    public const string PayScheduleAdded = "pay_schedule.added";
    public const string PayScheduleChanged = "pay_schedule.changed";
    public const string PayScheduleRemoved = "pay_schedule.removed";
    public const string PayScheduleCutoffOn = "pay_schedule.cutoff_on";
    public const string PayScheduleCutoffOff = "pay_schedule.cutoff_off";
    public const string WorkflowSaved = "workflow.saved";
    public const string WorkflowSwitchedOff = "workflow.switched_off";
    public const string TeamAccessChanged = "team.access_changed";
    public const string TeamMemberRemoved = "team.member_removed";
    public const string PayViewed = "pay.viewed";

    private static readonly string[] Run = ["kind", "periodStart", "periodEnd", "payDate"];

    /// <summary>Every action, with the detail keys it may carry.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> All = new Dictionary<string, string[]>
    {
        [PayRunStarted] = Run,
        [PayRunCalculated] = [.. Run, "people"],
        [PayRunSent] = [.. Run, "people"],
        [PayRunApproved] = Run,
        [PayRunDeclined] = Run,
        [PayRunWithdrawn] = Run,
        [PayRunCancelled] = Run,
        [PayRunCorrected] = [.. Run, "correctionId", "people"],
        [EmployeeAdded] = ["staffNo"],
        [EmployeeRemoved] = ["staffNo"],
        [EmployeePayChanged] = ["staffNo", "validFrom"],
        [EmployeeBankChanged] = ["staffNo", "method"],
        [RecordsImported] = ["things", "rows"],
        [PayScheduleAdded] = ["frequency"],
        [PayScheduleChanged] = ["frequency"],
        [PayScheduleRemoved] = ["frequency"],
        [PayScheduleCutoffOn] = ["frequency"],
        [PayScheduleCutoffOff] = ["frequency"],
        [WorkflowSaved] = ["kind"],
        [WorkflowSwitchedOff] = ["kind"],
        [TeamAccessChanged] = ["groups", "allCompanies"],
        [TeamMemberRemoved] = [],
        [PayViewed] = ["people"],
    };

    /// <summary>Refuses an action that isn't in the catalogue, or details it doesn't allow.</summary>
    public static void Check(string action, IReadOnlyDictionary<string, object?>? details)
    {
        if (!All.TryGetValue(action, out var allowed))
        {
            throw new ArgumentException($"{action} is not in the audit action catalogue.", nameof(action));
        }

        var other = details?.Keys.Where(k => !allowed.Contains(k)).ToArray() ?? [];
        if (other.Length > 0)
        {
            throw new ArgumentException($"{action} doesn't record {string.Join(", ", other)}.", nameof(details));
        }
    }
}
