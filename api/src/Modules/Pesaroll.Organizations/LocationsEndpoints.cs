using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Identity;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// Adding or editing a location. <see cref="Code"/> is made up when empty; <see cref="WageAreaCode"/> comes from the town when empty. It serves all companies
/// (<see cref="AllCompanies"/>) or <see cref="CompanyIds"/>, and is the main location of <see cref="MainFor"/>.
/// </summary>
public sealed record LocationRequest(
    string? Code, string? Name, string? Kind, string? RegionCode, string? Town, string? AddressLine, string? WageAreaCode,
    bool AllCompanies, List<Guid>? CompanyIds, List<Guid>? MainFor);

public sealed record LocationResponse(
    Guid Id, string Code, string Name, string Kind, string CountryCode, string? RegionCode, string? Town, string? AddressLine,
    string? WageAreaCode, bool AllCompanies, IReadOnlyList<Guid> CompanyIds, IReadOnlyList<Guid> MainFor);

public sealed record WageAreaResponse(string Code, LocalizedText Name, LocalizedText Description, IReadOnlyList<string> Towns);

/// <summary>
/// ONB-04, the Locations step: where people work. Each company has exactly one main location
/// (created as "Head office" with the company); a location serves chosen companies or all of them.
/// Its county and minimum-wage area drive minimum-wage checks and public holidays later.
/// </summary>
public static class LocationsEndpoints
{
    /// <summary>The types a location can be; a new one is a branch unless said otherwise.</summary>
    public static readonly string[] Kinds = ["head_office", "branch", "office", "warehouse", "site"];

    public static IEndpointRouteBuilder MapLocationsEndpoints(this IEndpointRouteBuilder app)
    {
        var locations = app.MapGroup("/api/locations").WithTags("Locations");

        locations.MapGet("/", async (Guid? companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var query = db.Locations.AsNoTracking().Include(l => l.Companies).Where(l => l.Status == "active");
            if (companyId is Guid id)
            {
                query = query.Where(l => l.AllCompanies || l.Companies.Any(c => c.CompanyId == id));
            }

            var list = await query.OrderBy(l => l.CreatedAt).ThenBy(l => l.Code).ToListAsync(ct);
            var mains = await MainsAsync(db, ct);
            return Results.Ok(list.Select(l => ToResponse(l, mains)).ToList());
        }).Produces<List<LocationResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        locations.MapPost("/", async (LocationRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var valid = await ValidateAsync(request, db, ct);

            var now = clock.GetUtcNow();
            var location = new Location
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = organizationId,
                Code = valid.Code ?? await NextCodeAsync(db, Prefix(valid.Town ?? valid.Name), ct),
                Name = valid.Name,
                CountryCode = valid.CountryCode,
                CreatedAt = now,
            };
            Apply(location, valid, now);
            db.Locations.Add(location);
            await SetMainsAsync(db, location, valid.MainFor, ct);
            await SaveAsync(db, ct);
            return Results.Created($"/api/locations/{location.Id}", ToResponse(location, await MainsAsync(db, ct)));
        }).Produces<LocationResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        locations.MapPatch("/{id:guid}", async (Guid id, LocationRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var location = await db.Locations.Include(l => l.Companies).SingleOrDefaultAsync(l => l.Id == id && l.Status == "active", ct)
                ?? throw OnboardingErrors.LocationNotFound();
            var valid = await ValidateAsync(request, db, ct);

            // Every company keeps a main location: this one stays main where it is, until another takes over.
            var keeping = await db.Companies.Where(c => c.MainLocationId == location.Id && !valid.MainFor.Contains(c.Id))
                .Select(c => c.LegalName).ToListAsync(ct);
            if (keeping.Count > 0)
            {
                throw OnboardingErrors.MainLocationNeeded(string.Join(", ", keeping));
            }

            location.Code = valid.Code ?? location.Code;
            location.Name = valid.Name;
            location.CountryCode = valid.CountryCode;
            var dropped = location.Companies.Where(c => !valid.CompanyIds.Contains(c.CompanyId)).ToList();
            Apply(location, valid, clock.GetUtcNow());
            await SetMainsAsync(db, location, valid.MainFor, ct);

            // New links before old ones go: a location is visible (row-level security) through its links,
            // so it must never be without one mid-save.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SaveAsync(db, ct);
            db.LocationCompanies.RemoveRange(dropped);
            await SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
            var saved = await db.Locations.AsNoTracking().Include(l => l.Companies).SingleAsync(l => l.Id == id, ct);
            return Results.Ok(ToResponse(saved, await MainsAsync(db, ct)));
        }).Produces<LocationResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        locations.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var location = await db.Locations.SingleOrDefaultAsync(l => l.Id == id && l.Status == "active", ct)
                ?? throw OnboardingErrors.LocationNotFound();
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            await EmployeesEndpoints.RequireUnusedAsync(db, location.Name, e => e.LocationId == id, ct);
            var mainOf = await db.Companies.Where(c => c.MainLocationId == id).Select(c => c.LegalName).ToListAsync(ct);
            if (mainOf.Count > 0)
            {
                throw OnboardingErrors.MainLocationNeeded(string.Join(", ", mainOf));
            }

            db.Locations.Remove(location);   // its company links go with it (on delete cascade)
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>The main location made with a new company: "Head office" at the company's address.</summary>
    internal static async Task<Location> HeadOfficeAsync(OrganizationsDbContext db, Company company, DateTimeOffset now, CancellationToken ct)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = company.OrganizationId,
            Code = await NextCodeAsync(db, "HQ", ct),
            Name = "Head office",
            Kind = "head_office",
            CountryCode = company.CountryCode,
            RegionCode = company.RegionCode,
            Town = company.Town,
            AddressLine = company.AddressLine,
            WageAreaCode = await SuggestWageAreaAsync(db, company.CountryCode, company.Town, ct),
            CreatedAt = now,
            UpdatedAt = now,
        };
        location.Companies.Add(new LocationCompany { OrganizationId = company.OrganizationId, LocationId = location.Id, CompanyId = company.Id, CreatedAt = now });
        return location;
    }

    /// <summary>The wage area whose towns include this town, if any; the person can always change it.</summary>
    internal static async Task<string?> SuggestWageAreaAsync(OrganizationsDbContext db, string countryCode, string? town, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(town))
        {
            return null;
        }

        var areas = await db.CountryWageAreas.AsNoTracking().Where(a => a.CountryCode == countryCode && a.Active).ToListAsync(ct);
        return areas.FirstOrDefault(a => a.Towns.Any(t => string.Equals(t, town.Trim(), StringComparison.OrdinalIgnoreCase)))?.Code;
    }

    /// <summary>"MOM" from Mombasa: the first three letters of the town (or name); "LOC" when there are none.</summary>
    internal static string Prefix(string text)
    {
        var letters = new string(text.Where(char.IsAsciiLetter).Take(3).ToArray()).ToUpperInvariant();
        return letters.Length >= 2 ? letters : "LOC";
    }

    /// <summary>The first free PREFIX-01, PREFIX-02… in the organization.</summary>
    internal static async Task<string> NextCodeAsync(OrganizationsDbContext db, string prefix, CancellationToken ct)
    {
        var taken = (await db.Locations.Where(l => l.Code.StartsWith(prefix + "-")).Select(l => l.Code).ToListAsync(ct))
            .Concat(db.Locations.Local.Select(l => l.Code)).ToHashSet();
        for (var n = 1; ; n++)
        {
            var code = $"{prefix}-{n:00}";
            if (!taken.Contains(code))
            {
                return code;
            }
        }
    }

    private sealed record ValidLocation(
        string? Code, string Name, string Kind, string CountryCode, string? RegionCode, string? Town, string? AddressLine, string? WageAreaCode,
        bool AllCompanies, List<Guid> CompanyIds, List<Guid> MainFor);

    private static async Task<ValidLocation> ValidateAsync(LocationRequest request, OrganizationsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 120)
        {
            errors["name"] = "Enter the location's name (2 to 120 characters).";
        }

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "branch" : request.Kind.Trim();
        if (!Kinds.Contains(kind))
        {
            errors["kind"] = "Choose the type from the list.";
        }

        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant();
        if (code is not null && !Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9-]{1,19}$"))
        {
            errors["code"] = "Use 2 to 20 letters, numbers or dashes, or leave it empty for us to make one.";
        }

        // The companies it serves, all in one country: the location's country.
        var companies = await db.Companies.AsNoTracking().Where(c => c.Status == "active").ToListAsync(ct);
        var chosen = (request.CompanyIds ?? []).Distinct().ToList();
        var serves = request.AllCompanies ? companies : companies.Where(c => chosen.Contains(c.Id)).ToList();
        if (!request.AllCompanies && chosen.Count == 0)
        {
            errors["companyIds"] = "Choose at least one company, or all companies.";
        }
        else if (!request.AllCompanies && serves.Count != chosen.Count)
        {
            errors["companyIds"] = "Choose companies from your organization.";
        }
        else if (serves.Select(c => c.CountryCode).Distinct().Count() > 1)
        {
            errors["companyIds"] = "A location serves companies in one country. Choose companies in the same country.";
        }

        var countryCode = serves.FirstOrDefault()?.CountryCode ?? companies.FirstOrDefault()?.CountryCode ?? "KE";
        var country = await db.Countries.AsNoTracking().SingleAsync(c => c.Code == countryCode, ct);
        var region = request.RegionCode?.Trim();
        if (string.IsNullOrEmpty(region) || !await db.CountryRegions.AnyAsync(r => r.CountryCode == countryCode && r.Code == region, ct))
        {
            errors["regionCode"] = $"Choose a {country.RegionLabelEn.ToLowerInvariant()} from the list.";
        }

        // Nobody is asked for the minimum-wage area: it comes from the town (a city's name) unless given.
        var wageArea = string.IsNullOrWhiteSpace(request.WageAreaCode)
            ? await SuggestWageAreaAsync(db, countryCode, request.Town, ct)
            : request.WageAreaCode.Trim();
        if (wageArea is not null && !await db.CountryWageAreas.AnyAsync(a => a.CountryCode == countryCode && a.Code == wageArea && a.Active, ct))
        {
            errors["wageAreaCode"] = "Choose the minimum-wage area from the list.";
        }

        var mainFor = (request.MainFor ?? []).Distinct().ToList();
        if (mainFor.Any(id => !serves.Any(c => c.Id == id)))
        {
            errors["mainFor"] = "A location can only be the main location of a company it serves.";
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.LocationDetailsInvalid(errors);
        }

        return new ValidLocation(code, name, kind, countryCode, region, Limit(request.Town, 100), Limit(request.AddressLine, 200), wageArea,
            request.AllCompanies, request.AllCompanies ? [] : chosen, mainFor);
    }

    private static void Apply(Location location, ValidLocation valid, DateTimeOffset now)
    {
        location.Kind = valid.Kind;
        location.RegionCode = valid.RegionCode;
        location.Town = valid.Town;
        location.AddressLine = valid.AddressLine;
        location.WageAreaCode = valid.WageAreaCode;
        location.AllCompanies = valid.AllCompanies;
        location.UpdatedAt = now;
        foreach (var companyId in valid.CompanyIds.Where(id => !location.Companies.Any(c => c.CompanyId == id)))
        {
            location.Companies.Add(new LocationCompany { OrganizationId = location.OrganizationId, LocationId = location.Id, CompanyId = companyId, CreatedAt = now });
        }
    }

    // A company has one main location, so making this one main moves it from wherever it was.
    private static async Task SetMainsAsync(OrganizationsDbContext db, Location location, List<Guid> mainFor, CancellationToken ct)
    {
        foreach (var company in await db.Companies.Where(c => mainFor.Contains(c.Id)).ToListAsync(ct))
        {
            company.MainLocationId = location.Id;
        }
    }

    private static async Task SaveAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "locations_unique_code" })
        {
            throw OnboardingErrors.LocationCodeTaken();
        }
    }

    private static async Task<Dictionary<Guid, List<Guid>>> MainsAsync(OrganizationsDbContext db, CancellationToken ct) =>
        (await db.Companies.AsNoTracking().Where(c => c.MainLocationId != null && c.Status == "active")
            .Select(c => new { c.Id, Main = c.MainLocationId!.Value }).ToListAsync(ct))
        .GroupBy(c => c.Main).ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

    private static LocationResponse ToResponse(Location l, Dictionary<Guid, List<Guid>> mains) => new(
        l.Id, l.Code, l.Name, l.Kind, l.CountryCode, l.RegionCode, l.Town, l.AddressLine, l.WageAreaCode, l.AllCompanies,
        l.Companies.Select(c => c.CompanyId).ToList(), mains.GetValueOrDefault(l.Id) ?? []);

    private static string? Limit(string? value, int max)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return trimmed is null ? null : trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
