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
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<DivisionCompany> DivisionCompanies => Set<DivisionCompany>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentCompany> DepartmentCompanies => Set<DepartmentCompany>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionCompany> PositionCompanies => Set<PositionCompany>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<UnitCompany> UnitCompanies => Set<UnitCompany>();
    public DbSet<IndustryStructure> IndustryStructures => Set<IndustryStructure>();

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
        model.Entity<IndustryStructure>().HasKey(s => new { s.IndustryCode, s.Kind, s.Name });
    }
}
