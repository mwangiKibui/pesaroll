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
    }
}
