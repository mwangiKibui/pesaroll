using Pesaroll.Shared.Errors;

namespace Pesaroll.Workflows.Tests;

/// <summary>
/// WF-02: who approves a step. The step's own way first (a permission's holders, named people, the
/// line manager); its fallback when that finds nobody; never the sender unless the workflow allows
/// it; and nobody at all is refused before the request is sent (WF-5004).
/// </summary>
public sealed class ApproversTests
{
    private static readonly Guid John = Guid.CreateVersion7();   // the sender
    private static readonly Guid Mary = Guid.CreateVersion7();
    private static readonly Guid Amina = Guid.CreateVersion7();

    [Fact]
    public void The_steps_own_approvers_come_first()
    {
        var found = Approvers.Resolve(new StepCandidates([Mary, Amina], [Amina]), John, allowSelfApproval: false);

        Assert.Equal([Mary, Amina], found.Users);
        Assert.False(found.ViaFallback);
    }

    [Fact]
    public void The_fallback_approves_when_the_step_finds_nobody()
    {
        var found = Approvers.Resolve(new StepCandidates([], [Amina]), John, allowSelfApproval: false);   // no line manager who can sign in

        Assert.Equal([Amina], found.Users);
        Assert.True(found.ViaFallback);
    }

    [Fact]
    public void The_sender_is_left_out_and_the_fallback_used_if_they_were_the_only_one()
    {
        Assert.Equal([Mary], Approvers.Resolve(new StepCandidates([John, Mary], []), John, false).Users);

        var found = Approvers.Resolve(new StepCandidates([John], [Amina, John]), John, false);
        Assert.Equal([Amina], found.Users);
        Assert.True(found.ViaFallback);
    }

    [Fact]
    public void Allowing_self_approval_keeps_the_sender()
    {
        // A small team: the one accountant prepares and approves.
        var found = Approvers.Resolve(new StepCandidates([John], []), John, allowSelfApproval: true);

        Assert.Equal([John], found.Users);
        Assert.False(found.ViaFallback);
    }

    [Fact]
    public void Nobody_left_is_WF_5004()
    {
        Assert.Equal("WF-5004", Assert.Throws<AppException>(() => Approvers.Resolve(new StepCandidates([John], [John]), John, false)).Code);
        Assert.Equal("WF-5004", Assert.Throws<AppException>(() => Approvers.Resolve(new StepCandidates([], []), John, true)).Code);
    }

    [Fact]
    public void Each_person_is_listed_once() =>
        Assert.Equal([Mary], Approvers.Resolve(new StepCandidates([Mary, Mary], []), John, false).Users);
}
