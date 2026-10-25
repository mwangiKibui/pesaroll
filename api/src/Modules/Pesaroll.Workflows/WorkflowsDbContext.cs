using Microsoft.EntityFrameworkCore;

namespace Pesaroll.Workflows;

/// <summary>A workflow an organization set up for a kind (database/schema/tenancy/approvals.sql); changing it makes a new one.</summary>
public sealed class Workflow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Kind { get; set; }
    /// <summary>Null: every company.</summary>
    public Guid? CompanyId { get; set; }
    public bool AllowSelfApproval { get; set; }
    public bool Active { get; set; } = true;
    public Guid SetUpBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SwitchedOffAt { get; set; }
    public List<WorkflowStep> Steps { get; set; } = [];
}

public sealed class WorkflowStep
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkflowId { get; set; }
    public int Position { get; set; }
    /// <summary>permission, people or line_manager.</summary>
    public required string Approvers { get; set; }
    public string? PermissionKey { get; set; }
    public string? FallbackPermissionKey { get; set; }
    public List<WorkflowStepPerson> People { get; set; } = [];
}

public sealed class WorkflowStepPerson
{
    public Guid OrganizationId { get; set; }
    public Guid StepId { get; set; }
    public Guid MembershipId { get; set; }
}

public sealed class ApprovalRequest
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public required string Kind { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid WorkflowId { get; set; }
    /// <summary>submitted, approved, declined or cancelled.</summary>
    public string Status { get; set; } = "submitted";
    public int CurrentStep { get; set; } = 1;
    public Guid SentBy { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DeclineReason { get; set; }
}

public sealed class ApprovalRequestApprover
{
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RequestId { get; set; }
    public int Step { get; set; }
    public Guid UserId { get; set; }
    public bool ViaFallback { get; set; }
}

public sealed class ApprovalAction
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RequestId { get; set; }
    public int? Step { get; set; }
    /// <summary>sent, approved, self_approved, declined or cancelled.</summary>
    public required string Action { get; set; }
    public Guid ByUser { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Reason { get; set; }
    public string? Reference { get; set; }
}

/// <summary>What can be sent for approval (reference data).</summary>
public sealed class WorkflowKindRow
{
    public required string Key { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public bool Required { get; set; }
    public required string SuggestedSteps { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Read only: who people are and where requests are, for messages and screens.</summary>
public sealed class MemberRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public required string Status { get; set; }
}

public sealed class UserRow
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
}

public sealed class CompanyRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string LegalName { get; set; }
}

public sealed class PermissionRow
{
    public required string Key { get; set; }
}

/// <summary>
/// Data access for workflows and approval requests. The schema lives in database/schema/ (no
/// migrations); row-level security limits every read to the organization and the companies in scope.
/// Members, users, companies and permissions are read only.
/// </summary>
public sealed class WorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options) : DbContext(options)
{
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowStep> Steps => Set<WorkflowStep>();
    public DbSet<WorkflowStepPerson> StepPeople => Set<WorkflowStepPerson>();
    public DbSet<ApprovalRequest> Requests => Set<ApprovalRequest>();
    public DbSet<ApprovalRequestApprover> RequestApprovers => Set<ApprovalRequestApprover>();
    public DbSet<ApprovalAction> Actions => Set<ApprovalAction>();
    public DbSet<WorkflowKindRow> Kinds => Set<WorkflowKindRow>();
    public DbSet<MemberRow> Members => Set<MemberRow>();
    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<CompanyRow> Companies => Set<CompanyRow>();
    public DbSet<PermissionRow> Permissions => Set<PermissionRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Workflow>().Property(w => w.Id).ValueGeneratedNever();
        model.Entity<Workflow>().HasMany(w => w.Steps).WithOne().HasForeignKey(s => s.WorkflowId);
        model.Entity<WorkflowStep>().ToTable("workflow_steps").Property(s => s.Id).ValueGeneratedNever();
        model.Entity<WorkflowStep>().HasMany(s => s.People).WithOne().HasForeignKey(p => p.StepId);
        model.Entity<WorkflowStepPerson>().ToTable("workflow_step_people").HasKey(p => new { p.StepId, p.MembershipId });
        model.Entity<ApprovalRequest>().ToTable("approval_requests").Property(r => r.Id).ValueGeneratedNever();
        model.Entity<ApprovalRequestApprover>().ToTable("approval_request_approvers").HasKey(a => new { a.RequestId, a.Step, a.UserId });
        model.Entity<ApprovalAction>().ToTable("approval_actions").Property(a => a.Id).ValueGeneratedNever();
        model.Entity<WorkflowKindRow>().ToTable("workflow_kinds").HasKey(k => k.Key);
        model.Entity<WorkflowKindRow>().Property(k => k.SuggestedSteps).HasColumnType("jsonb");
        model.Entity<MemberRow>().ToTable("memberships");
        model.Entity<UserRow>().ToTable("users");
        model.Entity<CompanyRow>().ToTable("companies");
        model.Entity<PermissionRow>().ToTable("permissions").HasKey(p => p.Key);
    }
}
