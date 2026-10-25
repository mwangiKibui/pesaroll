using Pesaroll.Shared.Errors;

namespace Pesaroll.Workflows.Tests;

/// <summary>
/// WF-01: a request moves only forward: each step's approver approves in turn, any of them can
/// decline with a reason, the sender can cancel while it waits, and once decided nothing changes.
/// </summary>
public sealed class ApprovalFlowTests
{
    private static readonly Guid John = Guid.CreateVersion7();   // sends it (Payroll)
    private static readonly Guid Mary = Guid.CreateVersion7();   // approves step 1
    private static readonly Guid Amina = Guid.CreateVersion7();  // approves step 2
    private static readonly Guid Peter = Guid.CreateVersion7();  // no part in it

    private static RequestState Waiting(int step = 1, int steps = 1) => new("submitted", step, steps, John);

    private static string Code(Action act) => Assert.Throws<AppException>(act).Code;

    [Fact]
    public void One_step_approved_is_approved()
    {
        var decision = ApprovalFlow.Approve(Waiting(), Mary, [Mary]);

        Assert.Equal("approved", decision.Status);
        Assert.Equal("approved", decision.Action);
        Assert.False(decision.NextStepStarts);
    }

    [Fact]
    public void Two_steps_need_both_in_order()
    {
        var first = ApprovalFlow.Approve(Waiting(1, 2), Mary, [Mary]);
        Assert.Equal("submitted", first.Status);
        Assert.Equal(2, first.CurrentStep);
        Assert.True(first.NextStepStarts);

        var second = ApprovalFlow.Approve(Waiting(2, 2), Amina, [Amina]);
        Assert.Equal("approved", second.Status);
    }

    [Fact]
    public void Only_the_current_steps_approvers_decide()
    {
        Assert.Equal("WF-5001", Code(() => ApprovalFlow.Approve(Waiting(1, 2), Amina, [Mary])));   // step 2's approver, too early
        Assert.Equal("WF-5001", Code(() => ApprovalFlow.Approve(Waiting(), Peter, [Mary])));
        Assert.Equal("WF-5001", Code(() => ApprovalFlow.Decline(Waiting(), Peter, [Mary], "No")));
    }

    [Fact]
    public void A_decline_at_any_step_ends_it_and_needs_a_reason()
    {
        Assert.Equal("WF-5002", Code(() => ApprovalFlow.Decline(Waiting(1, 2), Mary, [Mary], "  ")));
        Assert.Equal("WF-5002", Code(() => ApprovalFlow.Decline(Waiting(1, 2), Mary, [Mary], null)));
        Assert.Equal("WF-5002", Code(() => ApprovalFlow.Decline(Waiting(1, 2), Mary, [Mary], new string('x', 501))));

        var decision = ApprovalFlow.Decline(Waiting(1, 2), Mary, [Mary], "  Overtime for Kisumu is missing  ");
        Assert.Equal("declined", decision.Status);
        Assert.Equal("Overtime for Kisumu is missing", decision.Reason);
        Assert.False(decision.NextStepStarts);
    }

    [Fact]
    public void Only_the_sender_cancels_while_it_waits()
    {
        Assert.Equal("cancelled", ApprovalFlow.Cancel(Waiting(), John).Status);
        Assert.Equal("WF-5001", Code(() => ApprovalFlow.Cancel(Waiting(), Mary)));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("declined")]
    [InlineData("cancelled")]
    public void Nothing_changes_once_decided(string status)
    {
        var done = new RequestState(status, 1, 1, John);

        Assert.Equal("WF-5003", Code(() => ApprovalFlow.Approve(done, Mary, [Mary])));
        Assert.Equal("WF-5003", Code(() => ApprovalFlow.Decline(done, Mary, [Mary], "late")));
        Assert.Equal("WF-5003", Code(() => ApprovalFlow.Cancel(done, John)));
    }

    [Fact]
    public void The_sender_approving_is_recorded_as_self_approved()
    {
        var decision = ApprovalFlow.Approve(Waiting(), John, [John]);

        Assert.Equal("approved", decision.Status);
        Assert.Equal("self_approved", decision.Action);
    }
}
