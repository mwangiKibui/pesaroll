using Pesaroll.Shared.Errors;

namespace Pesaroll.Workflows.Tests;

/// <summary>
/// WF-03: which workflow applies at a company (its own, else the organization's), what's missing,
/// and what a workflow an admin sets up must have (WF-5009, field by field).
/// </summary>
public sealed class WorkflowSetupTests
{
    private static readonly Guid Westlands = Guid.CreateVersion7();
    private static readonly Guid Mombasa = Guid.CreateVersion7();
    private static readonly Guid ForAll = Guid.CreateVersion7();
    private static readonly Guid ForWestlands = Guid.CreateVersion7();

    private static readonly IReadOnlyList<ActiveWorkflow> Active =
    [
        new(ForAll, "pay_run", null),
        new(ForWestlands, "pay_run", Westlands),
    ];

    [Fact]
    public void A_companys_own_workflow_replaces_the_organizations_there()
    {
        Assert.Equal(ForWestlands, WorkflowChoice.For(Active, "pay_run", Westlands)?.Id);
        Assert.Equal(ForAll, WorkflowChoice.For(Active, "pay_run", Mombasa)?.Id);
        Assert.Null(WorkflowChoice.For(Active, "leave", Mombasa));
    }

    [Fact]
    public void Required_kinds_with_no_workflow_are_missing()
    {
        WorkflowKind[] kinds = [new("pay_run", true), new("leave", false), new("employee_termination", true)];

        Assert.Equal(["employee_termination"], WorkflowChoice.Missing(kinds, Active, Mombasa));
        Assert.Equal(["pay_run", "employee_termination"], WorkflowChoice.Missing(kinds, [], Mombasa));
    }

    private static readonly string[] Kinds = ["pay_run", "leave"];
    private static readonly string[] Permissions = ["pay_run_approvals.manage", "companies.manage", "leave.manage"];

    private static IReadOnlyDictionary<string, string> Fields(WorkflowInput input)
    {
        var error = Assert.Throws<AppException>(() => WorkflowRules.Check(input, Kinds, Permissions));
        Assert.Equal("WF-5009", error.Code);
        return error.Fields!;
    }

    private static StepInput ByPermission(string? permission = "pay_run_approvals.manage", string? fallback = "companies.manage") =>
        new("permission", permission, null, fallback);

    [Fact]
    public void A_suggested_pay_run_setup_is_fine() =>
        WorkflowRules.Check(new WorkflowInput("pay_run", null, false, [ByPermission()]), Kinds, Permissions);

    [Fact]
    public void Self_approval_is_allowed_when_the_admin_chooses_it() =>
        WorkflowRules.Check(new WorkflowInput("pay_run", Westlands, true, [ByPermission()]), Kinds, Permissions);

    [Fact]
    public void Each_problem_is_named_on_its_field()
    {
        Assert.Contains("kind", Fields(new WorkflowInput("expenses_xyz", null, false, [ByPermission()])).Keys);
        Assert.Contains("steps", Fields(new WorkflowInput("pay_run", null, false, [])).Keys);
        Assert.Contains("steps", Fields(new WorkflowInput("pay_run", null, false, Enumerable.Repeat(ByPermission(), 6).ToList())).Keys);
        Assert.Contains("steps[0].approvers", Fields(new WorkflowInput("pay_run", null, false, [new("anyone", null, null, null)])).Keys);
        Assert.Contains("steps[0].permission", Fields(new WorkflowInput("pay_run", null, false, [ByPermission(permission: null)])).Keys);
        Assert.Contains("steps[0].permission", Fields(new WorkflowInput("pay_run", null, false, [ByPermission(permission: "launch.rockets")])).Keys);
        Assert.Contains("steps[1].fallback", Fields(new WorkflowInput("pay_run", null, false, [ByPermission(), ByPermission(fallback: "nope.manage")])).Keys);
        Assert.Contains("steps[0].people", Fields(new WorkflowInput("pay_run", null, false, [new("people", null, [], null)])).Keys);
    }

    [Fact]
    public void A_line_manager_step_needs_a_fallback_for_people_with_no_manager_who_signs_in()
    {
        Assert.Contains("steps[0].fallback", Fields(new WorkflowInput("leave", null, false, [new("line_manager", null, null, null)])).Keys);
        WorkflowRules.Check(new WorkflowInput("leave", null, false, [new("line_manager", null, null, "leave.manage")]), Kinds, Permissions);
    }
}
