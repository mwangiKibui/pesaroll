using Microsoft.EntityFrameworkCore;
using Pesaroll.Organizations;
using Pesaroll.Shared.Jobs;

namespace Pesaroll.Payroll;

/// <summary>
/// A run's cut-off (PAY-04), as the calculation and the inputs screen both see it: the schedule's
/// cut-off day for the run and what it applies to (none for a correction, which pays what it is for),
/// the schedule's last approved regular run (the one whose period ends the day before), and the
/// leavers it paid to its cut-off, whom this run pays the rest.
/// </summary>
public static class RunCutoffs
{
    public static async Task<RunCutoff?> ForAsync(OrganizationsDbContext org, PayRun run, CancellationToken ct)
    {
        if (run.EmployeeIds is not null) return null;
        var schedule = await org.PaySchedules.AsNoTracking().SingleAsync(s => s.Id == run.PayScheduleId, ct);
        if (!schedule.UsesCutoff) return null;
        return RunPeriods.CutoffOn(schedule.Frequency, new RunPeriod(run.PeriodStart, run.PeriodEnd), run.PayDate, schedule.CutoffDay, schedule.CutoffDaysBefore)
            is { } on ? new RunCutoff(on, schedule.CutoffAppliesTo) : null;
    }

    public static Task<PayRun?> LastAsync(PayrollDbContext db, PayRun run, CancellationToken ct) =>
        db.PayRuns.AsNoTracking().Where(r => r.CompanyId == run.CompanyId && r.PayScheduleId == run.PayScheduleId && r.Kind == "regular"
            && r.Status == "approved" && r.PeriodEnd == run.PeriodStart.AddDays(-1)).SingleOrDefaultAsync(ct);

    public static async Task<Guid[]> LeaversOwedAsync(PayrollDbContext db, PayRun run, CancellationToken ct)
    {
        if (run.EmployeeIds is not null || await LastAsync(db, run, ct) is not { CutoffOn: not null } last) return [];
        return await db.People.AsNoTracking().Where(p => p.PayRunId == last.Id && p.Notes.Contains("paid_to_cutoff")).Select(p => p.EmployeeId).ToArrayAsync(ct);
    }

    /// <summary>The day something was entered, in East Africa Time: entered after the cut-off day means from midnight after it.</summary>
    public static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, JobsSetup.Kenya).DateTime);
}
