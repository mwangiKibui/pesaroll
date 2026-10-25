using Microsoft.EntityFrameworkCore;

namespace Pesaroll.Payroll;

/// <summary>A pay run (database/schema/tenancy/pay_runs.sql): one company's pay for a schedule and period.</summary>
public sealed class PayRun
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayScheduleId { get; set; }
    /// <summary>regular or correction.</summary>
    public string Kind { get; set; } = "regular";
    public Guid? CorrectsPayRunId { get; set; }
    public Guid[]? EmployeeIds { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly PayDate { get; set; }
    /// <summary>draft, calculating, calculated, awaiting_approval, approved or cancelled.</summary>
    public string Status { get; set; } = "draft";
    public string? RulePackVersion { get; set; }
    public string? CalculationError { get; set; }
    public string? DeclineReason { get; set; }
    public int People { get; set; }
    public decimal GrossPay { get; set; }
    public decimal Paye { get; set; }
    public decimal NssfEmployee { get; set; }
    public decimal Shif { get; set; }
    public decimal AhlEmployee { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetPay { get; set; }
    public decimal NssfEmployer { get; set; }
    public decimal AhlEmployer { get; set; }
    public decimal Nita { get; set; }
    public decimal OtherEmployerCosts { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CalculatedBy { get; set; }
    public DateTimeOffset? CalculatedAt { get; set; }
    public Guid? SentBy { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

/// <summary>A person a run worked out, with their totals, or why they were left out.</summary>
public sealed class PayRunPerson
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string StaffNo { get; set; }
    public required string FullName { get; set; }
    public bool Included { get; set; }
    public string? ExcludedReason { get; set; }
    public string[] Notes { get; set; } = [];
    public Guid? EmploymentId { get; set; }
    public Guid? CompensationId { get; set; }
    public decimal? BasicPay { get; set; }
    public decimal? GrossPay { get; set; }
    public decimal? TaxablePay { get; set; }
    public decimal? TaxBeforeRelief { get; set; }
    public decimal? PersonalRelief { get; set; }
    public decimal? Paye { get; set; }
    public decimal? TotalDeductions { get; set; }
    public decimal? NetPay { get; set; }
    public decimal? EmployerCosts { get; set; }
    /// <summary>The statutory items made for them (paye, shif, nssf, housing_levy, nita).</summary>
    public string[]? DeductionsMade { get; set; }
    /// <summary>Who chose the deductions made for them, and when (null: every one, as monthly payroll makes).</summary>
    public Guid? DeductionsChosenBy { get; set; }
    public DateTimeOffset? DeductionsChosenAt { get; set; }
    public List<PayRunLine> Lines { get; set; } = [];
}

/// <summary>A line of a person's pay in a run, its code and name copied in.</summary>
public sealed class PayRunLine
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayRunId { get; set; }
    public Guid PersonId { get; set; }
    public required string Kind { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Guid? PayItemId { get; set; }
    public string? StatutoryKey { get; set; }
    public bool Taxable { get; set; }
    public bool BeforeTax { get; set; }
    public decimal? Base { get; set; }
    public decimal? Rate { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Statutory lines: the tax month's total, and what earlier approved runs in the month took.</summary>
    public decimal? MonthToDate { get; set; }
    public decimal Earlier { get; set; }
    public int Position { get; set; }
}

/// <summary>Which statutory deductions a weekly, fortnightly or daily schedule makes from a date (PAY-10).</summary>
public sealed class ScheduleDeductions
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PayScheduleId { get; set; }
    public DateOnly StartsOn { get; set; }
    public bool MakesDeductions { get; set; }
    public string[] Items { get; set; } = [];
    /// <summary>everyone, or chosen (the employees in EmployeeIds).</summary>
    public string AppliesTo { get; set; } = "everyone";
    public Guid[]? EmployeeIds { get; set; }
    public Guid SetBy { get; set; }
    public DateTimeOffset SetAt { get; set; }
}

/// <summary>A person's own statutory deductions from a date: the items listed, or their schedule's (null).</summary>
public sealed class EmployeeDeductions
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly StartsOn { get; set; }
    public string[]? Items { get; set; }
    public Guid SetBy { get; set; }
    public DateTimeOffset SetAt { get; set; }
}

/// <summary>
/// Pay runs (PAY-03) and statutory deduction choices (PAY-10). Reads and writes data only: the schema lives in database/schema, never here.
/// Employees, their pay and the organization's items are read through the Organizations context.
/// </summary>
public sealed class PayrollDbContext(DbContextOptions<PayrollDbContext> options) : DbContext(options)
{
    public DbSet<PayRun> PayRuns => Set<PayRun>();
    public DbSet<PayRunPerson> People => Set<PayRunPerson>();
    public DbSet<PayRunLine> Lines => Set<PayRunLine>();
    public DbSet<ScheduleDeductions> ScheduleDeductions => Set<ScheduleDeductions>();
    public DbSet<EmployeeDeductions> EmployeeDeductions => Set<EmployeeDeductions>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PayRun>().ToTable("pay_runs");
        model.Entity<PayRunPerson>().ToTable("pay_run_people");
        model.Entity<PayRunPerson>().HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.PersonId);
        model.Entity<PayRunLine>().ToTable("pay_run_lines");
        model.Entity<ScheduleDeductions>().ToTable("pay_schedule_deductions");
        model.Entity<EmployeeDeductions>().ToTable("employee_statutory_deductions");
    }
}
