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
    /// <summary>The schedule's cut-off the last calculation used and what it applied to (PAY-04; null: none).</summary>
    public DateOnly? CutoffOn { get; set; }
    public string[]? CutoffAppliesTo { get; set; }
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
    /// <summary>Lines paid by the day or hour (PAY-11): amount = quantity x unit rate.</summary>
    public decimal? Quantity { get; set; }
    public decimal? UnitRate { get; set; }
    /// <summary>Why a line was set by hand for the run (PAY-04).</summary>
    public string? Reason { get; set; }
    /// <summary>A line owed from the last period, held back by its cut-off (PAY-04): that period's days.</summary>
    public DateOnly? OwedFrom { get; set; }
    public DateOnly? OwedTo { get; set; }
    public int Position { get; set; }
}

/// <summary>
/// What is entered for a person in a run: days or hours (PAY-11) for basic pay (no item) or an item
/// paid by the hour or day, or an amount (PAY-04) for an item, basic pay or a statutory line, with why.
/// </summary>
public sealed class PayRunInput
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? PayItemId { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Amount { get; set; }
    public string? Reason { get; set; }
    /// <summary>entered, or copied from the last approved run on the schedule.</summary>
    public string Source { get; set; } = "entered";
    public Guid EnteredBy { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
}

/// <summary>A country's wage order from a date (PAY-11): the hours that turn a daily rate or a salary into an hourly one.</summary>
public sealed class WageOrderRow
{
    public required string CountryCode { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public required string Source { get; set; }
    public decimal HoursPerDay { get; set; }
    public decimal HoursPerMonth { get; set; }
}

/// <summary>The lowest minimum wage in a wage area under a wage order.</summary>
public sealed class MinimumWageRow
{
    public required string CountryCode { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public required string WageAreaCode { get; set; }
    public decimal Monthly { get; set; }
    public decimal Daily { get; set; }
    public decimal Hourly { get; set; }
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

/// <summary>The statutory deductions made for a person in one run (PAY-04), replacing theirs and their schedule's.</summary>
public sealed class PayRunDeductionChoice
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public string[] Deductions { get; set; } = [];
    public required string Reason { get; set; }
    public Guid SetBy { get; set; }
    public DateTimeOffset SetAt { get; set; }
}

/// <summary>
/// Pay runs (PAY-03), statutory deduction choices (PAY-10), days and hours worked and wage orders (PAY-11). Reads and writes data only: the schema lives in database/schema, never here.
/// Employees, their pay and the organization's items are read through the Organizations context.
/// </summary>
public sealed class PayrollDbContext(DbContextOptions<PayrollDbContext> options) : DbContext(options)
{
    public DbSet<PayRun> PayRuns => Set<PayRun>();
    public DbSet<PayRunPerson> People => Set<PayRunPerson>();
    public DbSet<PayRunLine> Lines => Set<PayRunLine>();
    public DbSet<ScheduleDeductions> ScheduleDeductions => Set<ScheduleDeductions>();
    public DbSet<EmployeeDeductions> EmployeeDeductions => Set<EmployeeDeductions>();
    public DbSet<PayRunInput> Inputs => Set<PayRunInput>();
    public DbSet<PayRunDeductionChoice> RunDeductions => Set<PayRunDeductionChoice>();
    public DbSet<WageOrderRow> WageOrders => Set<WageOrderRow>();
    public DbSet<MinimumWageRow> MinimumWages => Set<MinimumWageRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PayRun>().ToTable("pay_runs");
        model.Entity<PayRunPerson>().ToTable("pay_run_people");
        model.Entity<PayRunPerson>().HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.PersonId);
        model.Entity<PayRunLine>().ToTable("pay_run_lines");
        model.Entity<ScheduleDeductions>().ToTable("pay_schedule_deductions");
        model.Entity<EmployeeDeductions>().ToTable("employee_statutory_deductions");
        model.Entity<PayRunInput>().ToTable("pay_run_inputs");
        model.Entity<PayRunDeductionChoice>().ToTable("pay_run_deduction_choices");
        model.Entity<WageOrderRow>().ToTable("wage_orders").HasKey(w => new { w.CountryCode, w.EffectiveFrom });
        model.Entity<WageOrderRow>().Property(w => w.CountryCode).HasColumnType("char(2)");
        model.Entity<MinimumWageRow>().ToTable("minimum_wages").HasKey(m => new { m.CountryCode, m.EffectiveFrom, m.WageAreaCode });
        model.Entity<MinimumWageRow>().Property(m => m.CountryCode).HasColumnType("char(2)");
    }
}
