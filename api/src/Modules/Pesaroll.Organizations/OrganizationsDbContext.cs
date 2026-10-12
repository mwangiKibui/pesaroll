using Microsoft.EntityFrameworkCore;

namespace Pesaroll.Organizations;

/// <summary>
/// Data access for organizations, companies and memberships (users belong to the Identity module). The schema itself lives in
/// database/schema/ (no migrations); this context only reads and writes rows, and row-level
/// security decides which rows it sees.
/// </summary>
public sealed class OrganizationsDbContext(DbContextOptions<OrganizationsDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<CompanyIdentifier> CompanyIdentifiers => Set<CompanyIdentifier>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<CountryCompanyField> CountryCompanyFields => Set<CountryCompanyField>();
    public DbSet<CountryRegion> CountryRegions => Set<CountryRegion>();
    public DbSet<Industry> Industries => Set<Industry>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<BankBranch> BankBranches => Set<BankBranch>();
    public DbSet<CompanyPaymentAccount> CompanyPaymentAccounts => Set<CompanyPaymentAccount>();
    public DbSet<CountryWageArea> CountryWageAreas => Set<CountryWageArea>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationCompany> LocationCompanies => Set<LocationCompany>();
    public DbSet<PaySchedule> PaySchedules => Set<PaySchedule>();
    public DbSet<PayScheduleCompany> PayScheduleCompanies => Set<PayScheduleCompany>();
    public DbSet<StatutoryItem> StatutoryItems => Set<StatutoryItem>();
    public DbSet<PayItem> PayItems => Set<PayItem>();
    public DbSet<PayItemCompany> PayItemCompanies => Set<PayItemCompany>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Employment> Employments => Set<Employment>();
    public DbSet<Compensation> Compensations => Set<Compensation>();
    public DbSet<EmployeePaymentAccount> EmployeePaymentAccounts => Set<EmployeePaymentAccount>();
    public DbSet<StatutoryLeave> StatutoryLeave => Set<StatutoryLeave>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeavePlan> LeavePlans => Set<LeavePlan>();
    public DbSet<LeavePlanCompany> LeavePlanCompanies => Set<LeavePlanCompany>();
    public DbSet<LeavePlanRule> LeavePlanRules => Set<LeavePlanRule>();
    public DbSet<PayScheduleReminder> PayScheduleReminders => Set<PayScheduleReminder>();
    public DbSet<ReminderSend> ReminderSends => Set<ReminderSend>();
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<DivisionCompany> DivisionCompanies => Set<DivisionCompany>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentCompany> DepartmentCompanies => Set<DepartmentCompany>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionCompany> PositionCompanies => Set<PositionCompany>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<UnitCompany> UnitCompanies => Set<UnitCompany>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Database defaults (now(), 'active') fill these when the app doesn't set them.
        model.Entity<Organization>().Property(o => o.CreatedAt).ValueGeneratedOnAdd();
        model.Entity<Organization>().Property(o => o.UpdatedAt).ValueGeneratedOnAdd();
        model.Entity<Company>().Property(c => c.CreatedAt).ValueGeneratedOnAdd();
        model.Entity<Company>().Property(c => c.UpdatedAt).ValueGeneratedOnAdd();
        model.Entity<Membership>().Property(m => m.CreatedAt).ValueGeneratedOnAdd();
        model.Entity<Membership>().Property(m => m.UpdatedAt).ValueGeneratedOnAdd();

        model.Entity<Company>().Property(c => c.CountryCode).HasColumnType("char(2)");
        model.Entity<Company>().Property(c => c.Currency).HasColumnType("char(3)");
        model.Entity<Company>().HasMany(c => c.Identifiers).WithOne().HasForeignKey(i => i.CompanyId);
        model.Entity<CompanyIdentifier>().Property(i => i.CreatedAt).ValueGeneratedOnAdd();
        model.Entity<CompanyIdentifier>().Property(i => i.UpdatedAt).ValueGeneratedOnAdd();

        model.Entity<Country>().HasKey(c => c.Code);
        model.Entity<Country>().Property(c => c.Code).HasColumnType("char(2)");
        model.Entity<Country>().Property(c => c.Currency).HasColumnType("char(3)");
        model.Entity<CountryCompanyField>().HasKey(f => new { f.CountryCode, f.Key });
        model.Entity<CountryCompanyField>().Property(f => f.CountryCode).HasColumnType("char(2)");
        model.Entity<CountryRegion>().HasKey(r => new { r.CountryCode, r.Code });
        model.Entity<CountryRegion>().Property(r => r.CountryCode).HasColumnType("char(2)");
        model.Entity<Industry>().HasKey(i => i.Code);
        model.Entity<Bank>().HasKey(b => new { b.CountryCode, b.Code });
        model.Entity<Bank>().Property(b => b.CountryCode).HasColumnType("char(2)");
        model.Entity<BankBranch>().HasKey(b => new { b.CountryCode, b.BankCode, b.Code });
        model.Entity<BankBranch>().Property(b => b.CountryCode).HasColumnType("char(2)");
        model.Entity<CompanyPaymentAccount>().Property(a => a.CountryCode).HasColumnType("char(2)");
        model.Entity<CompanyPaymentAccount>().Property(a => a.CreatedAt).ValueGeneratedOnAdd();
        model.Entity<CountryWageArea>().HasKey(a => new { a.CountryCode, a.Code });
        model.Entity<CountryWageArea>().Property(a => a.CountryCode).HasColumnType("char(2)");
        model.Entity<Location>().Property(l => l.CountryCode).HasColumnType("char(2)");
        model.Entity<Location>().HasMany(l => l.Companies).WithOne().HasForeignKey(c => c.LocationId);
        model.Entity<LocationCompany>().HasKey(c => new { c.LocationId, c.CompanyId });
        model.Entity<LocationCompany>().HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId);
        // Tells EF to write a new location before the company that points at it as main.
        model.Entity<Company>().HasOne<Location>().WithMany().HasForeignKey(c => c.MainLocationId);
        model.Entity<PaySchedule>().HasMany(s => s.Companies).WithOne().HasForeignKey(c => c.PayScheduleId);
        model.Entity<PayScheduleCompany>().HasKey(c => new { c.PayScheduleId, c.CompanyId });
        model.Entity<StatutoryItem>().HasKey(s => new { s.CountryCode, s.Key });
        model.Entity<StatutoryItem>().Property(s => s.CountryCode).HasColumnType("char(2)");
        model.Entity<PayItem>().Property(p => p.CountryCode).HasColumnType("char(2)");
        model.Entity<PayItem>().HasMany(p => p.Companies).WithOne().HasForeignKey(c => c.PayItemId);
        model.Entity<PayItemCompany>().HasKey(c => new { c.PayItemId, c.CompanyId });
        model.Entity<PayItemCompany>().HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId);
        // Employees: the app makes every id, so rows added to an employee are new, not updates.
        model.Entity<Employee>().HasMany(e => e.Employments).WithOne().HasForeignKey(x => x.EmployeeId);
        model.Entity<Employee>().HasMany(e => e.Compensations).WithOne().HasForeignKey(x => x.EmployeeId);
        model.Entity<Employee>().HasMany(e => e.Accounts).WithOne().HasForeignKey(x => x.EmployeeId);
        model.Entity<Employment>().Property(x => x.Id).ValueGeneratedNever();
        // A job's references, so EF writes a new department or position (from an upload) before the job.
        model.Entity<Employment>().HasOne<Position>().WithMany().HasForeignKey(x => x.PositionId);
        model.Entity<Employment>().HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId);
        model.Entity<Employment>().HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId);
        model.Entity<Employment>().HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId);
        model.Entity<Employment>().HasOne<PaySchedule>().WithMany().HasForeignKey(x => x.PayScheduleId);
        model.Entity<Employment>().HasOne<LeavePlan>().WithMany().HasForeignKey(x => x.LeavePlanId);
        model.Entity<Employment>().HasOne<Employee>().WithMany().HasForeignKey(x => x.LineManagerId).OnDelete(DeleteBehavior.ClientSetNull);
        model.Entity<Compensation>().Property(x => x.Id).ValueGeneratedNever();
        model.Entity<Compensation>().Property(x => x.Currency).HasColumnType("char(3)");
        model.Entity<EmployeePaymentAccount>().Property(x => x.Id).ValueGeneratedNever();
        model.Entity<EmployeePaymentAccount>().Property(x => x.CountryCode).HasColumnType("char(2)");
        model.Entity<StatutoryLeave>().ToTable("statutory_leave").HasKey(s => new { s.CountryCode, s.Key });
        model.Entity<StatutoryLeave>().Property(s => s.CountryCode).HasColumnType("char(2)");
        model.Entity<LeaveType>().Property(t => t.CountryCode).HasColumnType("char(2)");
        model.Entity<LeavePlan>().HasMany(p => p.Companies).WithOne().HasForeignKey(c => c.LeavePlanId);
        model.Entity<LeavePlan>().HasMany(p => p.Rules).WithOne().HasForeignKey(r => r.LeavePlanId);
        model.Entity<LeavePlanCompany>().HasKey(c => new { c.LeavePlanId, c.CompanyId });
        model.Entity<LeavePlanCompany>().HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId);
        // The app makes rule ids, so one added to an existing plan is a new row, not an update.
        model.Entity<LeavePlanRule>().Property(r => r.Id).ValueGeneratedNever();
        model.Entity<LeavePlanRule>().HasOne<LeaveType>().WithMany().HasForeignKey(r => r.LeaveTypeId);
        // Tells EF to write a new plan before the company or schedule that points at it as default.
        model.Entity<Company>().HasOne<LeavePlan>().WithMany().HasForeignKey(c => c.DefaultLeavePlanId);
        model.Entity<PaySchedule>().HasOne<LeavePlan>().WithMany().HasForeignKey(s => s.DefaultLeavePlanId);
        model.Entity<PaySchedule>().HasMany(s => s.Reminders).WithOne().HasForeignKey(r => r.PayScheduleId);
        // The app makes reminder ids, so one added to an existing schedule is a new row, not an update.
        model.Entity<PayScheduleReminder>().Property(r => r.Id).ValueGeneratedNever();
        model.Entity<ReminderSend>().ToTable("pay_schedule_reminder_sends").HasKey(s => new { s.ReminderId, s.PayDate });
        model.Entity<PayScheduleCompany>().HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId);
        // Tells EF to write a new schedule before the company that points at it as default.
        model.Entity<Company>().HasOne<PaySchedule>().WithMany().HasForeignKey(c => c.DefaultPayScheduleId);
        model.Entity<Division>().HasMany(d => d.Companies).WithOne().HasForeignKey(c => c.DivisionId);
        model.Entity<DivisionCompany>().HasKey(c => new { c.DivisionId, c.CompanyId });
        model.Entity<Department>().HasMany(d => d.Companies).WithOne().HasForeignKey(c => c.DepartmentId);
        model.Entity<Department>().HasOne<Division>().WithMany().HasForeignKey(d => d.DivisionId);
        model.Entity<Department>().HasOne<Department>().WithMany().HasForeignKey(d => d.ParentId);
        model.Entity<DepartmentCompany>().HasKey(c => new { c.DepartmentId, c.CompanyId });
        model.Entity<Position>().Property(p => p.Name).HasColumnName("title");
        model.Entity<Position>().HasMany(p => p.Companies).WithOne().HasForeignKey(c => c.PositionId);
        model.Entity<Position>().HasOne<Department>().WithMany().HasForeignKey(p => p.DepartmentId);
        model.Entity<PositionCompany>().HasKey(c => new { c.PositionId, c.CompanyId });
        model.Entity<Unit>().HasMany(u => u.Companies).WithOne().HasForeignKey(c => c.UnitId);
        model.Entity<Unit>().HasOne<Department>().WithMany().HasForeignKey(u => u.DepartmentId);
        model.Entity<UnitCompany>().HasKey(c => new { c.UnitId, c.CompanyId });
    }
}
