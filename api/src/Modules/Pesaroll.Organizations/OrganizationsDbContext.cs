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
    }
}
