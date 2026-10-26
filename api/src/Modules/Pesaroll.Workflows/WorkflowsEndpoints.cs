using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Identity;
using Pesaroll.Shared.Audit;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Workflows;

public sealed record KindName(string En, string Sw);

/// <summary>A kind of request, whether it must be set up before one can be sent, and the setup the screen pre-fills.</summary>
public sealed record KindResponse(string Key, KindName Name, bool Required, JsonElement SuggestedSteps);

public sealed record PersonResponse(Guid MembershipId, string Name);

public sealed record StepResponse(int Position, string Approvers, string? Permission, List<PersonResponse> People, string? Fallback);

/// <summary>A workflow: for every company (companyId null) or one; who set it up and when; its steps in order.</summary>
public sealed record WorkflowResponse(Guid Id, string Kind, Guid? CompanyId, bool AllowSelfApproval, string SetUpBy, DateTimeOffset CreatedAt, List<StepResponse> Steps);

/// <summary>Each kind at a company, with the workflow that applies there (its own, else every company's), or none yet.</summary>
public sealed record KindWorkflow(string Kind, KindName Name, bool Required, WorkflowResponse? Workflow);

public sealed record CompanyWorkflows(Guid CompanyId, List<KindWorkflow> Kinds);

public sealed record MissingKind(string Kind, KindName Name);

/// <summary>The required kinds a company can't send yet: the Approvals setup step and the Home prompt.</summary>
public sealed record MissingWorkflows(Guid CompanyId, List<MissingKind> Missing);

public sealed record WaitingRequest(Guid Id, string Kind, KindName KindName, Guid CompanyId, string Company, Guid SubjectId, string SentBy, DateTimeOffset SentAt, int Step, int Steps);

public sealed record ActionResponse(string Action, int? Step, string By, DateTimeOffset At, string? Reason);

/// <summary>A request with what the person may do now, who it waits for, and everything done to it.</summary>
public sealed record RequestResponse(
    Guid Id, string Kind, KindName KindName, Guid CompanyId, string Company, Guid SubjectId, string Status, int CurrentStep, int Steps,
    string SentBy, DateTimeOffset SentAt, string? DeclineReason, bool CanDecide, bool CanCancel, List<string> WaitingFor, List<ActionResponse> History);

public sealed record DeclineRequest(string? Reason);

/// <summary>
/// WF-01..03 endpoints. Setting up who approves: Approval workflows View or Manage, for the company
/// (every company for an organization-wide workflow). Deciding: whoever the request waits for, with
/// the step's permission checked again; cancelling: its sender; seeing one: either of them, or anyone
/// who may view workflows at that company.
/// </summary>
public static class WorkflowsEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workflow-kinds", async (WorkflowsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            SignedIn(tenant, http);
            var kinds = await db.Kinds.AsNoTracking().OrderBy(k => k.SortOrder).ToListAsync(ct);
            return Results.Ok(kinds.Select(k => new KindResponse(k.Key, new KindName(k.NameEn, k.NameSw), k.Required,
                JsonDocument.Parse(k.SuggestedSteps).RootElement.Clone())).ToList());
        }).Produces<List<KindResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .WithTags("Approval workflows").RequirePermission(Permissions.WorkflowsView);

        var workflows = app.MapGroup("/api/workflows").WithTags("Approval workflows");

        workflows.MapGet("/", async (Guid companyId, WorkflowsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            SignedIn(tenant, http);
            Require(tenant, Permissions.WorkflowsView, companyId);
            var kinds = await db.Kinds.AsNoTracking().OrderBy(k => k.SortOrder).ToListAsync(ct);
            var active = await ActiveAsync(db, ct);
            var result = new List<KindWorkflow>();
            foreach (var kind in kinds)
            {
                var chosen = WorkflowChoice.For(active.Select(w => new ActiveWorkflow(w.Id, w.Kind, w.CompanyId)), kind.Key, companyId);
                result.Add(new KindWorkflow(kind.Key, new KindName(kind.NameEn, kind.NameSw), kind.Required,
                    chosen is null ? null : await DescribeAsync(db, active.Single(w => w.Id == chosen.Id), ct)));
            }
            return Results.Ok(new CompanyWorkflows(companyId, result));
        }).Produces<CompanyWorkflows>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .RequirePermission(Permissions.WorkflowsView);

        workflows.MapGet("/missing", async (Guid companyId, WorkflowsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            SignedIn(tenant, http);
            Require(tenant, Permissions.WorkflowsView, companyId);
            var kinds = await db.Kinds.AsNoTracking().OrderBy(k => k.SortOrder).ToListAsync(ct);
            var active = (await ActiveAsync(db, ct)).Select(w => new ActiveWorkflow(w.Id, w.Kind, w.CompanyId)).ToList();
            var missing = WorkflowChoice.Missing(kinds.Select(k => new WorkflowKind(k.Key, k.Required)), active, companyId);
            return Results.Ok(new MissingWorkflows(companyId, kinds.Where(k => missing.Contains(k.Key))
                .Select(k => new MissingKind(k.Key, new KindName(k.NameEn, k.NameSw))).ToList()));
        }).Produces<MissingWorkflows>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .RequirePermission(Permissions.WorkflowsView);

        // Sets up who approves a kind, for every company or one. Replaces the workflow already there
        // (switched off, kept), so requests already sent keep the steps they were sent with.
        workflows.MapPost("/", async (WorkflowInput input, WorkflowsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var user = SignedIn(tenant, http);
            var organizationId = tenant.OrganizationId!.Value;
            if (input.CompanyId is Guid company) Require(tenant, Permissions.Workflows, company);
            else if (!tenant.Can(Permissions.Workflows)) throw AccessErrors.NoPermission();
            if (input.CompanyId is Guid c && !await db.Companies.AnyAsync(x => x.Id == c, ct))
                throw WorkflowErrors.Invalid(new Dictionary<string, string> { ["companyId"] = "Choose a company in your organization." });

            WorkflowRules.Check(input, await db.Kinds.Select(k => k.Key).ToListAsync(ct), await db.Permissions.Select(p => p.Key).ToListAsync(ct));
            var steps = input.Steps!;
            var named = steps.SelectMany(s => s.Approvers == "people" ? s.People! : []).Distinct().ToArray();
            var members = await db.Members.Where(m => named.Contains(m.Id) && m.Status == "active").Select(m => m.Id).ToListAsync(ct);
            var notMembers = steps.Select((s, i) => (s, i)).Where(x => x.s.Approvers == "people" && x.s.People!.Any(p => !members.Contains(p)))
                .ToDictionary(x => $"steps[{x.i}].people", _ => "Choose people who are in your organization.");
            if (notMembers.Count > 0) throw WorkflowErrors.Invalid(notMembers);

            var now = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var replaced = await db.Workflows.Where(w => w.Active && w.Kind == input.Kind && w.CompanyId == input.CompanyId).ToListAsync(ct);
            foreach (var old in replaced)
            {
                old.Active = false;
                old.SwitchedOffAt = now;
            }
            await db.SaveChangesAsync(ct);

            var workflow = new Workflow
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, Kind = input.Kind!, CompanyId = input.CompanyId,
                AllowSelfApproval = input.AllowSelfApproval, SetUpBy = user, CreatedAt = now,
            };
            workflow.Steps = steps.Select((s, i) =>
            {
                var id = Guid.CreateVersion7();
                return new WorkflowStep
                {
                    Id = id, OrganizationId = organizationId, WorkflowId = workflow.Id, Position = i + 1, Approvers = s.Approvers!,
                    PermissionKey = s.Approvers == "permission" ? s.Permission : null, FallbackPermissionKey = s.Fallback,
                    People = s.Approvers == "people"
                        ? s.People!.Distinct().Select(m => new WorkflowStepPerson { OrganizationId = organizationId, StepId = id, MembershipId = m }).ToList()
                        : [],
                };
            }).ToList();
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync(ct);
            await RecordAsync(db, AuditActions.WorkflowSaved, workflow, ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/workflows/{workflow.Id}", await DescribeAsync(db, workflow, ct));
        }).Produces<WorkflowResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .RequirePermission(Permissions.Workflows);

        workflows.MapPost("/{id:guid}/switch-off", async (Guid id, WorkflowsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            SignedIn(tenant, http);
            var workflow = await db.Workflows.SingleOrDefaultAsync(w => w.Id == id && w.Active, ct) ?? throw WorkflowErrors.WorkflowNotFound();
            if (workflow.CompanyId is Guid company) Require(tenant, Permissions.Workflows, company);
            else if (!tenant.Can(Permissions.Workflows)) throw AccessErrors.NoPermission();
            workflow.Active = false;
            workflow.SwitchedOffAt = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await RecordAsync(db, AuditActions.WorkflowSwitchedOff, workflow, ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .RequirePermission(Permissions.Workflows);

        var approvals = app.MapGroup("/api/approvals").WithTags("Approvals");

        // What waits for me at a company.
        approvals.MapGet("/", async (Guid companyId, WorkflowsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var user = SignedIn(tenant, http);
            if (!tenant.CompanyIds.Contains(companyId)) throw AccessErrors.NoPermission();
            var rows = await (
                from r in db.Requests
                where r.CompanyId == companyId && r.Status == "submitted"
                      && db.RequestApprovers.Any(a => a.RequestId == r.Id && a.Step == r.CurrentStep && a.UserId == user)
                join k in db.Kinds on r.Kind equals k.Key
                join c in db.Companies on r.CompanyId equals c.Id
                join u in db.Users on r.SentBy equals u.Id
                orderby r.SentAt
                select new { r, k.NameEn, k.NameSw, c.LegalName, Sender = u.FirstName + " " + u.LastName,
                             Steps = db.Steps.Count(s => s.WorkflowId == r.WorkflowId) })
                .AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(x => new WaitingRequest(x.r.Id, x.r.Kind, new KindName(x.NameEn, x.NameSw), x.r.CompanyId, x.LegalName,
                x.r.SubjectId, x.Sender, x.r.SentAt, x.r.CurrentStep, x.Steps)).ToList());
        }).Produces<List<WaitingRequest>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .SignedIn();

        approvals.MapGet("/{id:guid}", async (Guid id, WorkflowsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
            Results.Ok(await DescribeAsync(db, tenant, SignedIn(tenant, http), id, ct)))
          .Produces<RequestResponse>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .SignedIn();

        approvals.MapPost("/{id:guid}/approve", async (Guid id, Approvals engine, WorkflowsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var user = SignedIn(tenant, http);
            await engine.ApproveAsync(id, ct);
            return Results.Ok(await DescribeAsync(db, tenant, user, id, ct));
        }).Produces<RequestResponse>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).SignedIn();

        approvals.MapPost("/{id:guid}/decline", async (Guid id, DeclineRequest body, Approvals engine, WorkflowsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var user = SignedIn(tenant, http);
            await engine.DeclineAsync(id, body.Reason, ct);
            return Results.Ok(await DescribeAsync(db, tenant, user, id, ct));
        }).Produces<RequestResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).SignedIn();

        approvals.MapPost("/{id:guid}/cancel", async (Guid id, Approvals engine, WorkflowsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var user = SignedIn(tenant, http);
            await engine.CancelAsync(id, ct);
            return Results.Ok(await DescribeAsync(db, tenant, user, id, ct));
        }).Produces<RequestResponse>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).SignedIn();

        return app;
    }

    /// <summary>Records a workflow set up or switched off in the audit log (PLAT-03), named by its kind.</summary>
    private static async Task RecordAsync(WorkflowsDbContext db, string action, Workflow workflow, CancellationToken ct)
    {
        var name = await db.Kinds.Where(k => k.Key == workflow.Kind).Select(k => k.NameEn).SingleOrDefaultAsync(ct);
        await Audit.RecordAsync(db, action, workflow.CompanyId, new AuditSubject("workflow", workflow.Id, name ?? workflow.Kind),
            new Dictionary<string, object?> { ["kind"] = workflow.Kind }, ct);
    }

    private static void Require(TenantContext tenant, string permission, Guid companyId)
    {
        if (!tenant.Can(permission, companyId)) throw AccessErrors.NoPermission();
    }

    private static Guid SignedIn(TenantContext tenant, HttpContext http)
    {
        if (tenant.UserId is not Guid user) throw SessionService.RequireSignedIn(http);
        if (tenant.OrganizationId is null) throw AccessErrors.NoPermission();
        return user;
    }

    private static Task<List<Workflow>> ActiveAsync(WorkflowsDbContext db, CancellationToken ct) =>
        db.Workflows.AsNoTracking().Include(w => w.Steps).ThenInclude(s => s.People).Where(w => w.Active).ToListAsync(ct);

    private static async Task<WorkflowResponse> DescribeAsync(WorkflowsDbContext db, Workflow workflow, CancellationToken ct)
    {
        var memberships = workflow.Steps.SelectMany(s => s.People.Select(p => p.MembershipId)).Distinct().ToArray();
        var names = await (from m in db.Members where memberships.Contains(m.Id)
                           join u in db.Users on m.UserId equals u.Id
                           select new { m.Id, Name = u.FirstName + " " + u.LastName }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var setUpBy = await db.Users.Where(u => u.Id == workflow.SetUpBy).Select(u => u.FirstName + " " + u.LastName).SingleOrDefaultAsync(ct) ?? "";
        return new WorkflowResponse(workflow.Id, workflow.Kind, workflow.CompanyId, workflow.AllowSelfApproval, setUpBy, workflow.CreatedAt,
            workflow.Steps.OrderBy(s => s.Position).Select(s => new StepResponse(s.Position, s.Approvers, s.PermissionKey,
                s.People.Select(p => new PersonResponse(p.MembershipId, names.GetValueOrDefault(p.MembershipId, ""))).ToList(),
                s.FallbackPermissionKey)).ToList());
    }

    private static async Task<RequestResponse> DescribeAsync(WorkflowsDbContext db, TenantContext tenant, Guid user, Guid id, CancellationToken ct)
    {
        var request = await db.Requests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw WorkflowErrors.RequestNotFound();
        var approvers = await db.RequestApprovers.AsNoTracking().Where(a => a.RequestId == id).ToListAsync(ct);
        if (request.SentBy != user && approvers.All(a => a.UserId != user) && !tenant.Can(Permissions.WorkflowsView, request.CompanyId))
            throw WorkflowErrors.RequestNotFound();

        var actions = await db.Actions.AsNoTracking().Where(a => a.RequestId == id).OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync(ct);
        var people = actions.Select(a => a.ByUser).Concat(approvers.Select(a => a.UserId)).Append(request.SentBy).Distinct().ToArray();
        var names = await db.Users.Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
        var kind = await db.Kinds.SingleAsync(k => k.Key == request.Kind, ct);
        var company = await db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.LegalName).SingleAsync(ct);
        var steps = await db.Steps.CountAsync(s => s.WorkflowId == request.WorkflowId, ct);
        var waiting = request.Status == "submitted" ? approvers.Where(a => a.Step == request.CurrentStep).ToList() : [];

        return new RequestResponse(request.Id, request.Kind, new KindName(kind.NameEn, kind.NameSw), request.CompanyId, company, request.SubjectId,
            request.Status, request.CurrentStep, steps, names[request.SentBy], request.SentAt, request.DeclineReason,
            CanDecide: waiting.Any(a => a.UserId == user), CanCancel: request.Status == "submitted" && request.SentBy == user,
            waiting.Select(a => names[a.UserId]).ToList(),
            actions.Select(a => new ActionResponse(a.Action, a.Step, names[a.ByUser], a.At, a.Reason)).ToList());
    }
}
