using Pesaroll.Shared.Errors;

namespace Pesaroll.Workflows;

/// <summary>Where a request is: its status (submitted, approved, declined, cancelled), its step of how many, and who sent it.</summary>
public sealed record RequestState(string Status, int CurrentStep, int Steps, Guid SentBy);

/// <summary>
/// What a decision does: the request's new status and step, the action recorded in its history
/// (approved, self_approved, declined, cancelled), and whether the next step now starts.
/// </summary>
public sealed record Decision(string Status, int CurrentStep, string Action, string? Reason, bool NextStepStarts);

/// <summary>
/// The approval state machine (WF-01): steps approved in order; any step's approver may decline with
/// a reason; the sender may cancel while it waits; once approved, declined or cancelled it never changes.
/// </summary>
public static class ApprovalFlow
{
    public static Decision Approve(RequestState request, Guid by, IReadOnlyCollection<Guid> stepApprovers)
    {
        Waiting(request);
        if (!stepApprovers.Contains(by)) throw WorkflowErrors.CantDecide();
        var action = by == request.SentBy ? "self_approved" : "approved";
        return request.CurrentStep < request.Steps
            ? new Decision("submitted", request.CurrentStep + 1, action, null, NextStepStarts: true)
            : new Decision("approved", request.CurrentStep, action, null, NextStepStarts: false);
    }

    public static Decision Decline(RequestState request, Guid by, IReadOnlyCollection<Guid> stepApprovers, string? reason)
    {
        Waiting(request);
        if (!stepApprovers.Contains(by)) throw WorkflowErrors.CantDecide();
        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why) || why.Length > 500) throw WorkflowErrors.ReasonNeeded();
        return new Decision("declined", request.CurrentStep, "declined", why, NextStepStarts: false);
    }

    public static Decision Cancel(RequestState request, Guid by)
    {
        Waiting(request);
        if (by != request.SentBy) throw WorkflowErrors.CantDecide();
        return new Decision("cancelled", request.CurrentStep, "cancelled", null, NextStepStarts: false);
    }

    private static void Waiting(RequestState request)
    {
        if (request.Status != "submitted") throw WorkflowErrors.AlreadyDecided();
    }
}

/// <summary>Who a step's own way found, and its fallback permission's holders, before the sender is considered.</summary>
public sealed record StepCandidates(IReadOnlyList<Guid> First, IReadOnlyList<Guid> Fallback);

public sealed record ResolvedApprovers(IReadOnlyList<Guid> Users, bool ViaFallback);

/// <summary>
/// Who approves a step (WF-02): the people its own way found, else its fallback's; never the sender
/// unless the workflow allows self-approval; nobody left is refused (WF-5004).
/// </summary>
public static class Approvers
{
    public static ResolvedApprovers Resolve(StepCandidates candidates, Guid sender, bool allowSelfApproval)
    {
        IReadOnlyList<Guid> Eligible(IEnumerable<Guid> users) =>
            users.Distinct().Where(u => allowSelfApproval || u != sender).ToArray();

        var first = Eligible(candidates.First);
        if (first.Count > 0) return new ResolvedApprovers(first, ViaFallback: false);
        var fallback = Eligible(candidates.Fallback);
        if (fallback.Count > 0) return new ResolvedApprovers(fallback, ViaFallback: true);
        throw WorkflowErrors.NobodyToApprove();
    }
}

/// <summary>An active workflow: its kind, and the company it is for (null: every company).</summary>
public sealed record ActiveWorkflow(Guid Id, string Kind, Guid? CompanyId);

public sealed record WorkflowKind(string Key, bool Required);

/// <summary>Which workflow applies at a company (WF-03): the company's own, else the organization's.</summary>
public static class WorkflowChoice
{
    public static ActiveWorkflow? For(IEnumerable<ActiveWorkflow> active, string kind, Guid companyId)
    {
        var ofKind = active.Where(w => w.Kind == kind).ToArray();
        return ofKind.FirstOrDefault(w => w.CompanyId == companyId) ?? ofKind.FirstOrDefault(w => w.CompanyId is null);
    }

    /// <summary>The required kinds a company can't send yet because no workflow covers it.</summary>
    public static IReadOnlyList<string> Missing(IEnumerable<WorkflowKind> kinds, IReadOnlyList<ActiveWorkflow> active, Guid companyId) =>
        kinds.Where(k => k.Required && For(active, k.Key, companyId) is null).Select(k => k.Key).ToArray();
}

/// <summary>A workflow as an admin sets it up: its kind, all companies (null) or one, self-approval, and its steps in order.</summary>
public sealed record WorkflowInput(string? Kind, Guid? CompanyId, bool AllowSelfApproval, List<StepInput>? Steps);

/// <summary>A step: approvers by <c>permission</c> (its key), <c>people</c> (membership ids) or <c>line_manager</c>; the fallback permission when that finds nobody.</summary>
public sealed record StepInput(string? Approvers, string? Permission, List<Guid>? People, string? Fallback);

public static class WorkflowRules
{
    public const int MaxSteps = 5;
    public static readonly IReadOnlyList<string> Ways = ["permission", "people", "line_manager"];

    /// <summary>Refuses a workflow that can't work (WF-5009), naming each field. Whether named people are members is checked against the database.</summary>
    public static void Check(WorkflowInput input, IReadOnlyCollection<string> kinds, IReadOnlyCollection<string> permissions)
    {
        var fields = new Dictionary<string, string>();
        if (input.Kind is null || !kinds.Contains(input.Kind)) fields["kind"] = "Choose what this workflow approves.";
        var steps = input.Steps ?? [];
        if (steps.Count is 0 or > MaxSteps) fields["steps"] = $"Add between 1 and {MaxSteps} steps.";
        for (var i = 0; i < Math.Min(steps.Count, MaxSteps); i++)
        {
            var step = steps[i];
            var at = $"steps[{i}]";
            if (step.Approvers is null || !Ways.Contains(step.Approvers))
            {
                fields[$"{at}.approvers"] = "Choose how approvers are found: a permission, named people or the line manager.";
                continue;
            }
            if (step.Approvers == "permission" && (step.Permission is null || !permissions.Contains(step.Permission)))
                fields[$"{at}.permission"] = "Choose the permission approvers hold.";
            if (step.Approvers == "people" && (step.People is null || step.People.Count == 0))
                fields[$"{at}.people"] = "Choose at least one person.";
            if (step.Fallback is not null && !permissions.Contains(step.Fallback))
                fields[$"{at}.fallback"] = "Choose a permission for when nobody else is found.";
            else if (step.Approvers == "line_manager" && step.Fallback is null)
                fields[$"{at}.fallback"] = "Choose who approves for people whose manager doesn't use Pesaroll.";
        }
        if (fields.Count > 0) throw WorkflowErrors.Invalid(fields);
    }
}

public static class WorkflowErrors
{
    public static AppException CantDecide() => new("WF-5001", "You can't decide this request.", 403);

    public static AppException ReasonNeeded() => new("WF-5002", "Give a reason for declining, so the sender knows what to fix.")
    {
        Fields = new Dictionary<string, string> { ["reason"] = "Give a reason, up to 500 characters." },
    };

    public static AppException AlreadyDecided() => new("WF-5003", "This request has already been decided or cancelled.", 409);

    public static AppException NobodyToApprove() =>
        new("WF-5004", "Nobody else can approve this. Add an approver to the workflow, or allow the person who sends it to approve it.", 409);

    public static AppException NotSetUp(string kind, string company) => new("WF-5005", $"Choose who approves {kind} for {company} first.", 409);

    public static AppException RequestNotFound() => new("WF-5007", "We couldn't find that request. It may be at a company you can't see.", 404);

    public static AppException WorkflowNotFound() => new("WF-5008", "We couldn't find that workflow. It may have been changed or switched off.", 404);

    public static AppException Invalid(IReadOnlyDictionary<string, string> fields) =>
        new("WF-5009", "Some workflow details don't look right. Check the fields marked.") { Fields = fields };
}
