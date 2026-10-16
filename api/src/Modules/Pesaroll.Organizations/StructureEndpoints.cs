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

public sealed record DivisionRequest(string? Code, string? Name, bool AllCompanies, List<Guid>? CompanyIds);
public sealed record DivisionResponse(Guid Id, string Code, string Name, bool AllCompanies, IReadOnlyList<Guid> CompanyIds);

public sealed record DepartmentRequest(string? Code, string? Name, Guid? DivisionId, Guid? ParentId, bool AllCompanies, List<Guid>? CompanyIds);
public sealed record DepartmentResponse(Guid Id, string Code, string Name, Guid? DivisionId, Guid? ParentId, bool AllCompanies, IReadOnlyList<Guid> CompanyIds);

public sealed record PositionRequest(string? Code, string? Title, Guid? DepartmentId, bool AllCompanies, List<Guid>? CompanyIds);
public sealed record PositionResponse(Guid Id, string Code, string Title, Guid? DepartmentId, bool AllCompanies, IReadOnlyList<Guid> CompanyIds);

public sealed record UnitRequest(string? Code, string? Name, Guid? DepartmentId, bool AllCompanies, List<Guid>? CompanyIds);
public sealed record UnitResponse(Guid Id, string Code, string Name, Guid DepartmentId, bool AllCompanies, IReadOnlyList<Guid> CompanyIds);


/// <summary>
/// ONB-05, the Org structure step: divisions, departments, positions and units. They share one
/// shape (code, name, all or chosen companies) and so one set of endpoints, each level describing
/// only what is its own (<see cref="Level{TItem, TLink, TRequest, TResponse}"/>).
/// </summary>
public static class StructureEndpoints
{
    public static IEndpointRouteBuilder MapStructureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/structure").WithTags("Org structure");

        MapLevel(group, new Level<Division, DivisionCompany, DivisionRequest, DivisionResponse>
        {
            Path = "divisions",
            Items = db => db.Divisions,
            Links = db => db.DivisionCompanies,
            NewLink = (org, item, company, now) => new DivisionCompany { OrganizationId = org, DivisionId = item, CompanyId = company, CreatedAt = now },
            UniqueIndex = "divisions_unique_code",
            Common = r => new Common(r.Code, r.Name, "name", r.AllCompanies, r.CompanyIds),
            ToResponse = d => new DivisionResponse(d.Id, d.Code, d.Name, d.AllCompanies, d.Companies.Select(c => c.CompanyId).ToList()),
            InUse = async (db, d, ct) => Uses((await db.Departments.CountAsync(x => x.DivisionId == d.Id, ct), "department")),
        });

        MapLevel(group, new Level<Department, DepartmentCompany, DepartmentRequest, DepartmentResponse>
        {
            Path = "departments",
            Items = db => db.Departments,
            Links = db => db.DepartmentCompanies,
            NewLink = (org, item, company, now) => new DepartmentCompany { OrganizationId = org, DepartmentId = item, CompanyId = company, CreatedAt = now },
            UniqueIndex = "departments_unique_code",
            Common = r => new Common(r.Code, r.Name, "name", r.AllCompanies, r.CompanyIds),
            Check = async (r, existing, db, errors, ct) =>
            {
                if (r.DivisionId is Guid division && !await db.Divisions.AnyAsync(d => d.Id == division && d.Status == "active", ct))
                {
                    errors["divisionId"] = "Choose a division from the list.";
                }

                if (r.ParentId is Guid parent)
                {
                    var tree = await db.Departments.Where(d => d.Status == "active").Select(d => new { d.Id, d.ParentId }).ToDictionaryAsync(d => d.Id, d => d.ParentId, ct);
                    if (!tree.ContainsKey(parent))
                    {
                        errors["parentId"] = "Choose a department from the list.";
                    }
                    else if (existing is not null && Ancestors(tree, parent).Contains(existing.Id))
                    {
                        errors["parentId"] = "A department can't sit under itself or one of its own departments.";
                    }
                }
            },
            Apply = (r, d) =>
            {
                d.DivisionId = r.DivisionId;
                d.ParentId = r.ParentId;
            },
            ToResponse = d => new DepartmentResponse(d.Id, d.Code, d.Name, d.DivisionId, d.ParentId, d.AllCompanies, d.Companies.Select(c => c.CompanyId).ToList()),
            InUse = async (db, d, ct) => Uses(
                (await db.Departments.CountAsync(x => x.ParentId == d.Id, ct), "department"),
                (await db.Positions.CountAsync(x => x.DepartmentId == d.Id, ct), "position"),
                (await db.Units.CountAsync(x => x.DepartmentId == d.Id, ct), "unit")),
        });

        MapLevel(group, new Level<Position, PositionCompany, PositionRequest, PositionResponse>
        {
            Path = "positions",
            Items = db => db.Positions,
            Links = db => db.PositionCompanies,
            NewLink = (org, item, company, now) => new PositionCompany { OrganizationId = org, PositionId = item, CompanyId = company, CreatedAt = now },
            UniqueIndex = "positions_unique_code",
            Common = r => new Common(r.Code, r.Title, "title", r.AllCompanies, r.CompanyIds),
            Check = async (r, _, db, errors, ct) =>
            {
                if (r.DepartmentId is Guid department && !await db.Departments.AnyAsync(d => d.Id == department && d.Status == "active", ct))
                {
                    errors["departmentId"] = "Choose a department from the list.";
                }
            },
            Apply = (r, p) => p.DepartmentId = r.DepartmentId,
            ToResponse = p => new PositionResponse(p.Id, p.Code, p.Name, p.DepartmentId, p.AllCompanies, p.Companies.Select(c => c.CompanyId).ToList()),
        });

        MapLevel(group, new Level<Unit, UnitCompany, UnitRequest, UnitResponse>
        {
            Path = "units",
            Items = db => db.Units,
            Links = db => db.UnitCompanies,
            NewLink = (org, item, company, now) => new UnitCompany { OrganizationId = org, UnitId = item, CompanyId = company, CreatedAt = now },
            UniqueIndex = "units_unique_code",
            Common = r => new Common(r.Code, r.Name, "name", r.AllCompanies, r.CompanyIds),
            Check = async (r, _, db, errors, ct) =>
            {
                if (r.DepartmentId is not Guid department || !await db.Departments.AnyAsync(d => d.Id == department && d.Status == "active", ct))
                {
                    errors["departmentId"] = "Choose the department this unit is in.";
                }
            },
            Apply = (r, u) => u.DepartmentId = r.DepartmentId ?? Guid.Empty,
            ToResponse = u => new UnitResponse(u.Id, u.Code, u.Name, u.DepartmentId, u.AllCompanies, u.Companies.Select(c => c.CompanyId).ToList()),
        });

        return app;
    }

    /// <summary>The fields every level has, as its request names them.</summary>
    internal sealed record Common(string? Code, string? Name, string NameField, bool AllCompanies, List<Guid>? CompanyIds);

    /// <summary>What one level of the structure adds to the shared endpoints.</summary>
    internal sealed class Level<TItem, TLink, TRequest, TResponse>
        where TItem : class, IStructureItem<TLink>, new()
        where TLink : class, IStructureLink
    {
        public required string Path { get; init; }
        public required Func<OrganizationsDbContext, DbSet<TItem>> Items { get; init; }
        public required Func<OrganizationsDbContext, DbSet<TLink>> Links { get; init; }
        public required Func<Guid, Guid, Guid, DateTimeOffset, TLink> NewLink { get; init; }
        public required string UniqueIndex { get; init; }
        public required Func<TRequest, Common> Common { get; init; }
        public required Func<TItem, TResponse> ToResponse { get; init; }
        /// <summary>Checks of the level's own fields; the item is null when adding.</summary>
        public Func<TRequest, TItem?, OrganizationsDbContext, Dictionary<string, string>, CancellationToken, Task>? Check { get; init; }
        public Action<TRequest, TItem>? Apply { get; init; }
        /// <summary>What still uses the item ("1 position and 1 unit"), or null when it can go.</summary>
        public Func<OrganizationsDbContext, TItem, CancellationToken, Task<string?>>? InUse { get; init; }
    }

    private static void MapLevel<TItem, TLink, TRequest, TResponse>(RouteGroupBuilder group, Level<TItem, TLink, TRequest, TResponse> level)
        where TItem : class, IStructureItem<TLink>, new()
        where TLink : class, IStructureLink
    {
        var path = $"/{level.Path}";

        group.MapGet(path, async (Guid? companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var items = await level.Items(db).AsNoTracking().Include(i => i.Companies).Where(i => i.Status == "active")
                .OrderBy(i => i.CreatedAt).ThenBy(i => i.Code).ToListAsync(ct);
            if (companyId is Guid id)
            {
                items = items.Where(i => i.AllCompanies || i.Companies.Any(c => c.CompanyId == id)).ToList();
            }

            return Results.Ok(items.Select(level.ToResponse).ToList());
        }).Produces<List<TResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).RequirePermission(Permissions.StructureView);

        group.MapPost(path, async (TRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Structure);
            var (common, companies) = await ValidateAsync(level, request, null, db, ct);

            var item = New<TItem, TLink>(organizationId, common.Name!.Trim(),
                common.Code ?? await NextCodeAsync(level.Items(db), LocationsEndpoints.Prefix(common.Name!), ct),
                common.AllCompanies, companies, clock.GetUtcNow(), level.NewLink);
            level.Apply?.Invoke(request, item);
            level.Items(db).Add(item);
            await SaveAsync(db, level.UniqueIndex, ct);
            return Results.Created($"/api/structure/{level.Path}/{item.Id}", level.ToResponse(item));
        }).Produces<TResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Structure);

        group.MapPatch($"{path}/{{id:guid}}", async (Guid id, TRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            Access.Require(tenant, Permissions.Structure);
            var item = await level.Items(db).Include(i => i.Companies).SingleOrDefaultAsync(i => i.Id == id && i.Status == "active", ct)
                ?? throw OnboardingErrors.StructureNotFound();
            var (common, companies) = await ValidateAsync(level, request, item, db, ct);

            var now = clock.GetUtcNow();
            item.Code = common.Code ?? item.Code;
            item.Name = common.Name!.Trim();
            item.AllCompanies = common.AllCompanies;
            item.UpdatedAt = now;
            level.Apply?.Invoke(request, item);
            var keep = common.AllCompanies ? [] : companies.Select(c => c.Id).ToHashSet();
            var dropped = item.Companies.Where(c => !keep.Contains(c.CompanyId)).ToList();
            foreach (var companyId in keep.Where(k => !item.Companies.Any(c => c.CompanyId == k)))
            {
                item.Companies.Add(level.NewLink(organizationId, item.Id, companyId, now));
            }

            // New links before old ones go: the item is visible (row-level security) through its links.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SaveAsync(db, level.UniqueIndex, ct);
            level.Links(db).RemoveRange(dropped);
            await SaveAsync(db, level.UniqueIndex, ct);
            await transaction.CommitAsync(ct);
            var saved = await level.Items(db).AsNoTracking().Include(i => i.Companies).SingleAsync(i => i.Id == id, ct);
            return Results.Ok(level.ToResponse(saved));
        }).Produces<TResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Structure);

        group.MapDelete($"{path}/{{id:guid}}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            var item = await level.Items(db).SingleOrDefaultAsync(i => i.Id == id && i.Status == "active", ct)
                ?? throw OnboardingErrors.StructureNotFound();
            Access.Require(tenant, Permissions.Structure);
            // Employees' jobs first (ids are unique across the levels, so one check covers them all).
            await EmployeesEndpoints.RequireUnusedAsync(db, item.Name, e => e.PositionId == id || e.DepartmentId == id || e.UnitId == id, ct);
            if (level.InUse is not null && await level.InUse(db, item, ct) is { } uses)
            {
                throw OnboardingErrors.StructureInUse(item.Name, uses);
            }

            level.Items(db).Remove(item);   // its company links go with it (on delete cascade)
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                // Used by something this person can't see (limited to some companies).
                throw OnboardingErrors.StructureInUse(item.Name, "things that use it");
            }

            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Structure);
    }

    private static async Task<(Common Common, List<Company> Companies)> ValidateAsync<TItem, TLink, TRequest, TResponse>(
        Level<TItem, TLink, TRequest, TResponse> level, TRequest request, TItem? existing, OrganizationsDbContext db, CancellationToken ct)
        where TItem : class, IStructureItem<TLink>, new()
        where TLink : class, IStructureLink
    {
        var common = level.Common(request);
        var errors = new Dictionary<string, string>();
        var name = common.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 120)
        {
            errors[common.NameField] = $"Enter the {common.NameField} (2 to 120 characters).";
        }

        var code = string.IsNullOrWhiteSpace(common.Code) ? null : common.Code.Trim().ToUpperInvariant();
        if (code is not null && !Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9-]{1,19}$"))
        {
            errors["code"] = "Use 2 to 20 letters, numbers or dashes, or leave it empty for us to make one.";
        }

        var companies = await CompaniesAsync(common.AllCompanies, common.CompanyIds, db, errors, ct);
        if (level.Check is not null)
        {
            await level.Check(request, existing, db, errors, ct);
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.StructureDetailsInvalid(errors);
        }

        return (common with { Code = code, Name = name }, companies);
    }

    /// <summary>The chosen companies, or all of them; at least one, all from this organization.</summary>
    private static async Task<List<Company>> CompaniesAsync(bool all, List<Guid>? ids, OrganizationsDbContext db, Dictionary<string, string> errors, CancellationToken ct)
    {
        var companies = await db.Companies.AsNoTracking().Where(c => c.Status == "active").ToListAsync(ct);
        var chosen = (ids ?? []).Distinct().ToList();
        var serves = all ? companies : companies.Where(c => chosen.Contains(c.Id)).ToList();
        if (!all && (chosen.Count == 0 || serves.Count != chosen.Count))
        {
            errors["companyIds"] = "Choose at least one company from your organization, or all companies.";
        }

        return serves;
    }

    private static TItem New<TItem, TLink>(Guid organizationId, string name, string code, bool all, List<Company> companies, DateTimeOffset now,
        Func<Guid, Guid, Guid, DateTimeOffset, TLink> link)
        where TItem : class, IStructureItem<TLink>, new()
        where TLink : class, IStructureLink
    {
        var item = new TItem
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, Code = code, Name = name, AllCompanies = all, CreatedAt = now, UpdatedAt = now,
        };
        if (!all)
        {
            item.Companies.AddRange(companies.Select(c => link(organizationId, item.Id, c.Id, now)));
        }

        return item;
    }

    /// <summary>The first free PREFIX-01, PREFIX-02… in the level, counting rows not saved yet.</summary>
    private static async Task<string> NextCodeAsync<TItem>(DbSet<TItem> items, string prefix, CancellationToken ct) where TItem : class, IHasCode
    {
        var stored = await items.Where(i => i.Code.StartsWith(prefix + "-")).Select(i => i.Code).ToListAsync(ct);
        var taken = stored.Concat(items.Local.Select(i => i.Code)).ToHashSet();
        for (var n = 1; ; n++)
        {
            var code = $"{prefix}-{n:00}";
            if (!taken.Contains(code))
            {
                return code;
            }
        }
    }

    private static async Task SaveAsync(OrganizationsDbContext db, string uniqueIndex, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique && unique.ConstraintName == uniqueIndex)
        {
            throw OnboardingErrors.StructureCodeTaken();
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation, MessageText: "ONB-3523" })
        {
            throw OnboardingErrors.StructureDetailsInvalid(new Dictionary<string, string> { ["parentId"] = "A department can't sit under itself or one of its own departments." });
        }
    }

    /// <summary>"1 position and 2 units", or null when nothing uses it.</summary>
    private static string? Uses(params (int Count, string Thing)[] uses)
    {
        var parts = uses.Where(u => u.Count > 0).Select(u => $"{u.Count} {u.Thing}{(u.Count == 1 ? "" : "s")}").ToList();
        return parts.Count switch { 0 => null, 1 => parts[0], _ => $"{string.Join(", ", parts[..^1])} and {parts[^1]}" };
    }

    private static IEnumerable<Guid> Ancestors(Dictionary<Guid, Guid?> tree, Guid start)
    {
        Guid? at = start;
        for (var steps = 0; at is Guid id && steps < 100; steps++)
        {
            yield return id;
            at = tree.GetValueOrDefault(id);
        }
    }
}
