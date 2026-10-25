using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Organizations;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Payroll;

/// <summary>A company's working day and month in hours; empty: the wage order's.</summary>
public sealed record WorkingHoursRequest(decimal? HoursPerDay, decimal? HoursPerMonth);

/// <summary>
/// What the company set (null: nothing), what payroll uses (its own, else the wage order's in force
/// today; null when its country has none), and who last set them and when.
/// </summary>
public sealed record WorkingHoursResponse(Guid CompanyId, decimal? HoursPerDay, decimal? HoursPerMonth, decimal? UsedPerDay, decimal? UsedPerMonth,
    Guid? SetBy, DateTimeOffset? SetAt);

/// <summary>
/// A company's working hours (PAY-11): how many hours make its working day and month, which turn a
/// daily rate or a salary into an hourly one for items paid by the hour (overtime). Each company sets
/// its own; until then the wage order's apply (8 a day, 225 a month). Reading needs View on pay at the
/// company, changing Manage on pay: they change what people are paid.
/// </summary>
public static class WorkingHoursEndpoints
{
    public static IEndpointRouteBuilder MapWorkingHoursEndpoints(this IEndpointRouteBuilder app)
    {
        var hours = app.MapGroup("/api/companies/{companyId:guid}/working-hours").WithTags("Pay runs");

        hours.MapGet("/", async (Guid companyId, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            PayRunsEndpoints.Who(tenant, http);
            var company = await CompanyAsync(org, tenant, companyId, Permissions.PayView, ct);
            return Results.Ok(await DescribeAsync(db, company, clock, ct));
        }).Produces<WorkingHoursResponse>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).RequirePermission(Permissions.PayView);

        hours.MapPut("/", async (Guid companyId, WorkingHoursRequest request, PayrollDbContext db, OrganizationsDbContext org, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = PayRunsEndpoints.Who(tenant, http);
            var company = await CompanyAsync(org, tenant, companyId, Permissions.Pay, ct);
            var errors = new Dictionary<string, string>();
            if (request.HoursPerDay is not (null or (> 0 and <= 24)))
                errors["hoursPerDay"] = "Enter the hours in a working day, more than 0 and up to 24, or leave it empty for the wage order's.";
            if (request.HoursPerMonth is not (null or (> 0 and <= 744)))
                errors["hoursPerMonth"] = "Enter the hours in a working month, more than 0 and up to 744, or leave it empty for the wage order's.";
            if (errors.Count > 0) throw PayRunErrors.HoursInvalid(errors);

            company.HoursPerDay = request.HoursPerDay;
            company.HoursPerMonth = request.HoursPerMonth;
            company.HoursSetBy = userId;
            company.HoursSetAt = clock.GetUtcNow();
            await org.SaveChangesAsync(ct);
            return Results.Ok(await DescribeAsync(db, company, clock, ct));
        }).Produces<WorkingHoursResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound).RequirePermission(Permissions.Pay);

        return app;
    }

    /// <summary>The wage order in force for a country on a date (null: none).</summary>
    internal static Task<WageOrderRow?> OrderInForceAsync(PayrollDbContext db, string country, DateOnly on, CancellationToken ct) =>
        db.WageOrders.AsNoTracking().Where(w => w.CountryCode == country && w.EffectiveFrom <= on)
            .OrderByDescending(w => w.EffectiveFrom).FirstOrDefaultAsync(ct);

    /// <summary>An active company this person has the permission at (one they can't see is not found).</summary>
    private static async Task<Company> CompanyAsync(OrganizationsDbContext org, TenantContext tenant, Guid id, string permission, CancellationToken ct)
    {
        var company = await org.Companies.SingleOrDefaultAsync(c => c.Id == id && c.Status == "active", ct) ?? throw PayRunErrors.CompanyNotFound();
        if (!tenant.Can(permission, company.Id)) throw AccessErrors.NoPermission();
        return company;
    }

    private static async Task<WorkingHoursResponse> DescribeAsync(PayrollDbContext db, Company company, TimeProvider clock, CancellationToken ct)
    {
        var order = await OrderInForceAsync(db, company.CountryCode, PayDates.TodayInKenya(clock.GetUtcNow()), ct);
        return new WorkingHoursResponse(company.Id, company.HoursPerDay, company.HoursPerMonth,
            company.HoursPerDay ?? order?.HoursPerDay, company.HoursPerMonth ?? order?.HoursPerMonth, company.HoursSetBy, company.HoursSetAt);
    }
}
