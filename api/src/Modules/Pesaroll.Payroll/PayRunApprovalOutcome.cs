using Npgsql;
using Pesaroll.Workflows;

namespace Pesaroll.Payroll;

/// <summary>
/// What a decided pay run request does (PAY-03), inside the decision's transaction (WF-01), so the
/// run changes with the decision or not at all. Approved: the run is approved, and from then on the
/// database keeps it as it is (PAY-2014). Declined or cancelled: it goes back to calculated, with
/// the decline reason, for Payroll to fix and send again. A request whose subject isn't a run
/// waiting at that company changes nothing.
/// </summary>
public sealed class PayRunApprovalOutcome(TimeProvider clock) : IApprovalOutcome
{
    public string Kind => "pay_run";

    public async Task DecidedAsync(DecidedRequest request, ApprovalTransaction transaction, CancellationToken ct)
    {
        var approved = request.Status == "approved";
        await using var command = new NpgsqlCommand(approved
            ? """
              update pay_runs set status = 'approved', approved_by = @by, approved_at = @at, decline_reason = null
              where id = @id and company_id = @company and status = 'awaiting_approval'
              """
            : """
              update pay_runs set status = 'calculated', decline_reason = @reason
              where id = @id and company_id = @company and status = 'awaiting_approval'
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
        await command.ExecuteNonQueryAsync(ct);
    }
}
