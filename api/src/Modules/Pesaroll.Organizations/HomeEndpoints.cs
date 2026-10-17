using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Identity;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>Someone who started this month: name and start date only.</summary>
public sealed record HomeJoiner(Guid Id, string Name, DateOnly HireDate);

/// <summary>One detail payroll needs, how many people lack it, and its catalogue code when it has one (PAY-2001).</summary>
public sealed record HomeMissing(string Key, LocalizedText Label, string? Code, int People);

/// <summary>Who can be paid in the next pay run of the company's main payroll schedule, and what the rest lack.</summary>
public sealed record HomeReadiness(DateOnly? NextPayDate, int Ready, int Total, IReadOnlyList<HomeMissing> Missing);

/// <summary>Home's people numbers for one company (OP-30). No pay, no ID numbers.</summary>
public sealed record HomePeople(Guid CompanyId, int Employees, int JoinedThisMonth, IReadOnlyList<HomeJoiner> Joined, HomeReadiness Readiness);

/// <summary>
/// OP-30: the numbers on HR's Home for the company chosen in the top bar. Everything is counted in
/// the database (never loading every employee) with the same rules as the Employees readiness
/// (<see cref="EmployeeReadiness"/>); at most <see cref="JoinedShown"/> names come back.
/// </summary>
public static class HomeEndpoints
{
    public const int JoinedShown = 3;

    public static IEndpointRouteBuilder MapHomeEndpoints(this IEndpointRouteBuilder app)
    {
        var home = app.MapGroup("/api/home").WithTags("Home");

        home.MapGet("/people", async (Guid companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            // Only a company of theirs where they may view employees (ACC-04): another company, or another organization's, is refused.
            if (!tenant.CompaniesFor(Permissions.EmployeesView).Contains(companyId))
            {
                throw AccessErrors.NoPermission();
            }

            var today = PayDates.TodayInKenya(clock.GetUtcNow());
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            // Current employees, as the Employees list counts them.
            var current = db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && (e.ExitDate == null || e.ExitDate >= today));
            var employees = await current.CountAsync(ct);
            var joinedQuery = current.Where(e => e.HireDate >= monthStart && e.HireDate <= today);
            var joinedThisMonth = await joinedQuery.CountAsync(ct);
            var joined = await joinedQuery
                .OrderByDescending(e => e.HireDate).ThenBy(e => e.FirstName).ThenBy(e => e.LastName)
                .Take(JoinedShown)
                .Select(e => new HomeJoiner(e.Id, e.FirstName + " " + e.LastName, e.HireDate))
                .ToListAsync(ct);

            var ready = await current.CountAsync(EmployeeReadiness.Ready, ct);
            var missing = new List<HomeMissing>();
            foreach (var rule in EmployeeReadiness.Rules)
            {
                var people = await current.CountAsync(rule.IsMissing, ct);
                if (people > 0)
                {
                    missing.Add(new HomeMissing(rule.Item.Key, rule.Item.Label, rule.Code, people));
                }
            }

            // The next pay date of the company's main payroll schedule (its default), if it has one.
            var schedule = await db.Companies.AsNoTracking().Where(c => c.Id == companyId)
                .Join(db.PaySchedules.AsNoTracking(), c => c.DefaultPayScheduleId, s => s.Id, (c, s) => s)
                .SingleOrDefaultAsync(ct);
            DateOnly? next = schedule is null ? null
                : PayDates.For(schedule.Frequency, schedule.PayDayRule, schedule.PayDay, schedule.PayWeekday, schedule.FirstPayDate, today, count: 1)[0];

            return Results.Ok(new HomePeople(companyId, employees, joinedThisMonth, joined,
                new HomeReadiness(next, ready, employees, missing.OrderByDescending(m => m.People).ToList())));
        }).Produces<HomePeople>().Produces<ErrorResponse>(StatusCodes.Status403Forbidden).RequirePermission(Permissions.EmployeesView);

        return app;
    }
}
