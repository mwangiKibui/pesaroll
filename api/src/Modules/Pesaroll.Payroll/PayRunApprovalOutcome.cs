using Npgsql;
using Pesaroll.Shared.Audit;
using Pesaroll.Workflows;

namespace Pesaroll.Payroll;

/// <summary>
/// What a decided pay run request does (PAY-03), inside the decision's transaction (WF-01), so the
/// run changes with the decision or not at all. Approved: the run is approved, and from then on the
/// database keeps it as it is (PAY-2014). Declined or cancelled: it goes back to calculated, with
/// the decline reason, for Payroll to fix and send again. A request whose subject isn't a run
/// waiting at that company changes nothing. The decision is recorded in the audit log (PLAT-03)
/// by the person who made it, without the reason they typed.
/// </summary>
public sealed class PayRunApprovalOutcome(TimeProvider clock) : IApprovalOutcome
{
    public string Kind => "pay_run";

    public async Task DecidedAsync(DecidedRequest request, ApprovalTransaction transaction, CancellationToken ct)
    {
        var approved = request.Status == "approved";
        Dictionary<string, object?> details;
        string? schedule;
        await using var command = new NpgsqlCommand(approved
            ? """
              update pay_runs set status = 'approved', approved_by = @by, approved_at = @at, decline_reason = null
              where id = @id and company_id = @company and status = 'awaiting_approval'
              returning kind, period_start, period_end, pay_date, (select s.name from pay_schedules s where s.id = pay_schedule_id)
              """
            : """
              update pay_runs set status = 'calculated', decline_reason = @reason
              where id = @id and company_id = @company and status = 'awaiting_approval'
              returning kind, period_start, period_end, pay_date, (select s.name from pay_schedules s where s.id = pay_schedule_id)
              """,
            (NpgsqlConnection)transaction.Connection, (NpgsqlTransaction)transaction.Transaction);
        command.Parameters.AddWithValue("id", request.SubjectId);
        command.Parameters.AddWithValue("company", request.CompanyId);
        if (approved)
        {
            command.Parameters.AddWithValue("by", request.DecidedBy);
            command.Parameters.AddWithValue("at", clock.GetUtcNow());
        }
        else
        {
            command.Parameters.Add(new NpgsqlParameter("reason", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)request.Reason ?? DBNull.Value });
        }
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return;
            details = new Dictionary<string, object?>
            {
                ["kind"] = reader.GetString(0), ["periodStart"] = reader.GetFieldValue<DateOnly>(1), ["periodEnd"] = reader.GetFieldValue<DateOnly>(2),
                ["payDate"] = reader.GetFieldValue<DateOnly>(3),
            };
            schedule = reader.IsDBNull(4) ? null : reader.GetString(4);
        }

        var action = request.Status switch
        {
            "approved" => AuditActions.PayRunApproved, "declined" => AuditActions.PayRunDeclined, _ => AuditActions.PayRunWithdrawn,
        };
        await Audit.RecordAsync(transaction.Connection, transaction.Transaction, action, request.CompanyId,
            new AuditSubject("pay_run", request.SubjectId, schedule), details, ct);
    }
}
