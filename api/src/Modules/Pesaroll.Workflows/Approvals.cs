using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Pesaroll.Notifications;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Workflows;

/// <summary>A request once it is approved, declined or cancelled, for the module that owns its subject.</summary>
public sealed record DecidedRequest(Guid Id, string Kind, Guid CompanyId, Guid SubjectId, string Status, Guid DecidedBy, string? Reason);

/// <summary>The connection and transaction a decision is saved in, so the subject's change commits with it or not at all.</summary>
public sealed record ApprovalTransaction(DbConnection Connection, DbTransaction Transaction);

/// <summary>
/// What a module does when one of its requests is decided (a pay run locks when approved, goes back
/// to draft when declined). Registered per kind; runs inside the decision's transaction.
/// </summary>
public interface IApprovalOutcome
{
    string Kind { get; }

    Task DecidedAsync(DecidedRequest request, ApprovalTransaction transaction, CancellationToken ct);
}

/// <summary>
/// The approval engine (WF-01): other modules send requests; approvers approve or decline them step
/// by step; the sender may cancel. Every action is recorded in approval_actions. Approvers are found
/// when each step starts (WF-02) and emailed; the sender is emailed the decision. No amounts and no
/// reasons in emails (rule 8): they say where to look.
/// </summary>
public sealed class Approvals(
    WorkflowsDbContext db, TenantContext tenant, Notifier notifier, IEnumerable<IApprovalOutcome> outcomes,
    TimeProvider clock, IConfiguration configuration, IHttpContextAccessor http)
{
    /// <summary>Sends a request for the workflow that applies at the company (WF-5005 if none); returns its id.</summary>
    public async Task<Guid> SubmitAsync(string kind, Guid companyId, Guid subjectId, Guid? employeeId, CancellationToken ct)
    {
        var (sender, organizationId) = Who();
        if (!tenant.CompanyIds.Contains(companyId)) throw AccessErrors.NoPermission();

        var active = await db.Workflows.AsNoTracking().Where(w => w.Active && w.Kind == kind)
            .Select(w => new ActiveWorkflow(w.Id, w.Kind, w.CompanyId)).ToListAsync(ct);
        var chosen = WorkflowChoice.For(active, kind, companyId);
        if (chosen is null)
        {
            var kindName = await db.Kinds.Where(k => k.Key == kind).Select(k => k.NameEn).SingleOrDefaultAsync(ct) ?? kind;
            var company = await db.Companies.Where(c => c.Id == companyId).Select(c => c.LegalName).SingleAsync(ct);
            throw WorkflowErrors.NotSetUp(kindName.ToLowerInvariant(), company);
        }

        var workflow = await LoadWorkflowAsync(chosen.Id, ct);
        var request = new ApprovalRequest
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, CompanyId = companyId, Kind = kind, SubjectId = subjectId,
            EmployeeId = employeeId, WorkflowId = workflow.Id, SentBy = sender, SentAt = clock.GetUtcNow(),
        };
        var approvers = await ResolveAsync(workflow, workflow.Steps[0], request, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Requests.Add(request);
        await db.SaveChangesAsync(ct);   // the request first: its approvers and history point at it
        AddApprovers(request, 1, approvers);
        Record(request, 1, "sent", sender, null);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await EmailApproversAsync(request, approvers.Users, ct);
        return request.Id;
    }

    public Task<ApprovalRequest> ApproveAsync(Guid requestId, CancellationToken ct) =>
        DecideAsync(requestId, (state, by, approvers) => ApprovalFlow.Approve(state, by, approvers), ct);

    public Task<ApprovalRequest> DeclineAsync(Guid requestId, string? reason, CancellationToken ct) =>
        DecideAsync(requestId, (state, by, approvers) => ApprovalFlow.Decline(state, by, approvers, reason), ct);

    public Task<ApprovalRequest> CancelAsync(Guid requestId, CancellationToken ct) =>
        DecideAsync(requestId, (state, by, _) => ApprovalFlow.Cancel(state, by), ct);

    private async Task<ApprovalRequest> DecideAsync(Guid requestId, Func<RequestState, Guid, IReadOnlyCollection<Guid>, Decision> decide, CancellationToken ct)
    {
        var (by, _) = Who();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Locked until this decision commits, so two approvers can't both move the same step.
        var request = await db.Requests.FromSql($"select * from approval_requests where id = {requestId} for update").SingleOrDefaultAsync(ct)
            ?? throw WorkflowErrors.RequestNotFound();
        var workflow = await LoadWorkflowAsync(request.WorkflowId, ct);
        var step = workflow.Steps.Single(s => s.Position == request.CurrentStep);
        var stepApprovers = await db.RequestApprovers.Where(a => a.RequestId == request.Id && a.Step == request.CurrentStep).ToListAsync(ct);

        // Still allowed: an approver found by a permission must still hold it for this company.
        var mine = stepApprovers.SingleOrDefault(a => a.UserId == by);
        var needed = mine is null ? null : mine.ViaFallback ? step.FallbackPermissionKey : step.Approvers == "permission" ? step.PermissionKey : null;
        var allowed = mine is not null && (needed is null || tenant.Can(needed, request.CompanyId));
        var decision = decide(new RequestState(request.Status, request.CurrentStep, workflow.Steps.Count, request.SentBy), by,
            allowed ? [by] : []);

        Record(request, request.CurrentStep, decision.Action, by, decision.Reason);
        request.Status = decision.Status;
        request.CurrentStep = decision.CurrentStep;
        ResolvedApprovers? next = null;
        if (decision.NextStepStarts)
        {
            next = await ResolveAsync(workflow, workflow.Steps.Single(s => s.Position == decision.CurrentStep), request, ct);
            AddApprovers(request, decision.CurrentStep, next);
        }
        else
        {
            request.DecidedBy = by;
            request.DecidedAt = clock.GetUtcNow();
            request.DeclineReason = decision.Status == "declined" ? decision.Reason : null;
        }
        await db.SaveChangesAsync(ct);

        if (request.Status != "submitted")
        {
            var decided = new DecidedRequest(request.Id, request.Kind, request.CompanyId, request.SubjectId, request.Status, by, request.DeclineReason);
            var shared = new ApprovalTransaction(db.Database.GetDbConnection(), transaction.GetDbTransaction());
            foreach (var outcome in outcomes.Where(o => o.Kind == request.Kind))
                await outcome.DecidedAsync(decided, shared, ct);
        }
        await transaction.CommitAsync(ct);

        if (next is not null) await EmailApproversAsync(request, next.Users, ct);
        else if (request.Status is "approved" or "declined" && request.SentBy != by) await EmailSenderAsync(request, by, ct);
        return request;
    }

    private async Task<Workflow> LoadWorkflowAsync(Guid id, CancellationToken ct)
    {
        var workflow = await db.Workflows.AsNoTracking().Include(w => w.Steps).ThenInclude(s => s.People)
            .SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw WorkflowErrors.WorkflowNotFound();
        workflow.Steps = workflow.Steps.OrderBy(s => s.Position).ToList();
        return workflow;
    }

    /// <summary>The step's approvers (WF-02): its own way, else its fallback, never the sender unless allowed.</summary>
    private async Task<ResolvedApprovers> ResolveAsync(Workflow workflow, WorkflowStep step, ApprovalRequest request, CancellationToken ct)
    {
        IReadOnlyList<Guid> first = step.Approvers switch
        {
            "permission" => await HoldersAsync(request.CompanyId, step.PermissionKey!, ct),
            "people" => await PeopleAsync(step.People.Select(p => p.MembershipId).ToArray(), ct),
            "line_manager" when request.EmployeeId is Guid employee => await LineManagerAsync(employee, ct),
            _ => [],
        };
        IReadOnlyList<Guid> fallback = step.FallbackPermissionKey is { } key ? await HoldersAsync(request.CompanyId, key, ct) : [];
        return Approvers.Resolve(new StepCandidates(first, fallback), request.SentBy, workflow.AllowSelfApproval);
    }

    private async Task<IReadOnlyList<Guid>> HoldersAsync(Guid companyId, string permission, CancellationToken ct) =>
        await db.Database.SqlQuery<Guid>($"select m as \"Value\" from members_with_permission({companyId}, {permission}) as m").ToListAsync(ct);

    private async Task<IReadOnlyList<Guid>> PeopleAsync(Guid[] memberships, CancellationToken ct) =>
        await db.Members.Where(m => memberships.Contains(m.Id) && m.Status == "active").Select(m => m.UserId).ToListAsync(ct);

    private async Task<IReadOnlyList<Guid>> LineManagerAsync(Guid employeeId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(3)).DateTime);
        var manager = await db.Database.SqlQuery<Guid?>($"select line_manager_user({employeeId}, {today}) as \"Value\"").SingleAsync(ct);
        return manager is Guid user ? [user] : [];
    }

    private void AddApprovers(ApprovalRequest request, int step, ResolvedApprovers approvers) =>
        db.RequestApprovers.AddRange(approvers.Users.Select(u => new ApprovalRequestApprover
        {
            OrganizationId = request.OrganizationId, CompanyId = request.CompanyId, RequestId = request.Id, Step = step,
            UserId = u, ViaFallback = approvers.ViaFallback,
        }));

    private void Record(ApprovalRequest request, int step, string action, Guid by, string? reason) =>
        db.Actions.Add(new ApprovalAction
        {
            Id = Guid.CreateVersion7(), OrganizationId = request.OrganizationId, CompanyId = request.CompanyId, RequestId = request.Id,
            Step = step, Action = action, ByUser = by, At = clock.GetUtcNow(), Reason = reason, Reference = http.HttpContext?.TraceIdentifier,
        });

    private (Guid User, Guid Organization) Who() =>
        tenant.UserId is Guid user && tenant.OrganizationId is Guid organization ? (user, organization) : throw AccessErrors.NoPermission();

    private string Link(Guid requestId) => $"{(configuration["App:Url"] ?? "https://app.pesaroll.co.ke").TrimEnd('/')}/approvals/{requestId}";

    private async Task EmailApproversAsync(ApprovalRequest request, IReadOnlyList<Guid> users, CancellationToken ct)
    {
        var (kind, company) = await NamesAsync(request, ct);
        var sender = await db.Users.Where(u => u.Id == request.SentBy).Select(u => u.FirstName + " " + u.LastName).SingleAsync(ct);
        foreach (var user in await db.Users.Where(u => users.Contains(u.Id) && u.Email != null).ToListAsync(ct))
        {
            await notifier.QueueAsync(new Message("email", user.Email!, "approval_waiting", new Dictionary<string, string>
            {
                ["firstName"] = user.FirstName, ["sender"] = sender, ["kind"] = kind, ["company"] = company, ["link"] = Link(request.Id),
            }), ct);
        }
    }

    private async Task EmailSenderAsync(ApprovalRequest request, Guid decidedBy, CancellationToken ct)
    {
        var (kind, company) = await NamesAsync(request, ct);
        var sender = await db.Users.SingleAsync(u => u.Id == request.SentBy, ct);
        var decider = await db.Users.Where(u => u.Id == decidedBy).Select(u => u.FirstName + " " + u.LastName).SingleAsync(ct);
        if (sender.Email is null) return;
        await notifier.QueueAsync(new Message("email", sender.Email, $"approval_{request.Status}", new Dictionary<string, string>
        {
            ["firstName"] = sender.FirstName, ["decider"] = decider, ["kind"] = kind, ["company"] = company, ["link"] = Link(request.Id),
        }), ct);
    }

    private async Task<(string Kind, string Company)> NamesAsync(ApprovalRequest request, CancellationToken ct) =>
        (await db.Kinds.Where(k => k.Key == request.Kind).Select(k => k.NameEn).SingleAsync(ct),
         await db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.LegalName).SingleAsync(ct));
}
