using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Organizations;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Payroll;

/// <summary>Basic pay from an earlier date, and why.</summary>
public sealed record PayChangeRequest(decimal? Amount, string? Basis, decimal? UsualQuantity, DateOnly? EffectiveFrom, string? Reason);

/// <summary>One row of a person's basic pay over time: who changed it and why (empty on older rows).</summary>
public sealed record PayChangeRow(Guid Id, DateOnly ValidFrom, DateOnly? ValidTo, decimal Amount, string Basis, decimal? UsualQuantity, string? Reason,
    Guid? ChangedBy, DateTimeOffset CreatedAt);

/// <summary>An approved run the change falls in: it keeps what it paid, and a correction pays the difference.</summary>
public sealed record RunToCorrect(Guid PayRunId, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly PayDate);

/// <summary>
/// The change made, the runs it sent back to draft to be worked out again and the approved runs it
/// falls in.
/// </summary>
public sealed record PayChangeResponse(PayChangeRow Change, IReadOnlyList<Guid> SentBackToDraft, IReadOnlyList<RunToCorrect> NeedCorrection);

/// <summary>
/// Pay changed from an earlier date (PAY-04), for a rate that was wrong or a rise agreed late: the
/// change runs from its date until the next change, the pay it interrupts ends the day before, and
/// nothing else is rewritten. Runs not yet sent that paid the person in those days go back to draft;
/// a run waiting for approval stops it (its figures would move under the approver); approved runs are
/// never changed, only listed for a correction. Needs Manage on pay at the company; reading the
/// history needs View on pay and is recorded.
/// </summary>
public static class PayChangesEndpoints
{
    private static readonly string[] Bases = ["monthly", "weekly", "fortnightly", "daily", "hourly"];

    public static IEndpointRouteBuilder MapPayChangesEndpoints(this IEndpointRouteBuilder app)
    {
        var changes = app.MapGroup("/api/companies/{companyId:guid}/employees/{employeeId:guid}/pay-changes").WithTags("Employees");

        changes.MapGet("/", async (Guid companyId, Guid employeeId, OrganizationsDbContext org, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            PayRunsEndpoints.Who(tenant, http);
            if (!tenant.Can(Permissions.PayView, companyId)) throw AccessErrors.NoPermission();
            var employee = await EmployeeAsync(org, companyId, employeeId, tracked: false, ct);
            await SensitiveViews.RecordAsync(org, tenant, http, "employee_pay", [employee.Id], ct);
            return Results.Ok(employee.Compensations.OrderByDescending(c => c.ValidFrom).Select(Row).ToList());
        }).Produces<List<PayChangeRow>>().Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).RequirePermission(Permissions.PayView);

        changes.MapPost("/", async (Guid companyId, Guid employeeId, PayChangeRequest request, PayrollDbContext db, OrganizationsDbContext org,
            TenantContext tenant, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, _) = PayRunsEndpoints.Who(tenant, http);
            if (!tenant.Can(Permissions.Pay, companyId)) throw AccessErrors.NoPermission();
            var employee = await EmployeeAsync(org, companyId, employeeId, tracked: true, ct);
            var now = clock.GetUtcNow();
            var from = Validate(request, employee, PayDates.TodayInKenya(now));

            var next = employee.Compensations.Where(c => c.ValidFrom > from).Select(c => (DateOnly?)c.ValidFrom).Min();
            var to = next?.AddDays(-1);
            var runs = await db.PayRuns.Where(r => r.CompanyId == companyId && r.PeriodEnd >= from && (to == null || r.PeriodStart <= to)
                && r.Status != "draft" && r.Status != "cancelled" && db.People.Any(p => p.PayRunId == r.Id && p.EmployeeId == employeeId))
                .OrderBy(r => r.PayDate).ToListAsync(ct);
            if (runs.Any(r => r.Status == "awaiting_approval")) throw PayRunErrors.WaitingForApproval();

            var company = await org.Companies.AsNoTracking().SingleAsync(c => c.Id == companyId, ct);
            var change = new Compensation
            {
                Id = Guid.CreateVersion7(), OrganizationId = employee.OrganizationId, CompanyId = companyId, EmployeeId = employeeId, ValidFrom = from, ValidTo = to,
                BasicAmount = request.Amount!.Value, PayBasis = request.Basis!, Currency = company.Currency, UsualQuantity = request.UsualQuantity,
                ChangedBy = userId, Reason = request.Reason!.Trim(), CreatedAt = now,
            };
            var interrupted = employee.Compensations.SingleOrDefault(c => c.ValidFrom < from && (c.ValidTo is null || c.ValidTo >= from));
            // Runs go back to draft first: on its own that only means working them out again.
            var sentBack = runs.Where(r => r.Status is "calculated" or "calculating").ToList();
            foreach (var run in sentBack) run.Status = "draft";
            await db.SaveChangesAsync(ct);

            // The interrupted pay ends before the change goes in: one current row at a time.
            await using var transaction = await org.Database.BeginTransactionAsync(ct);
            if (interrupted is not null) interrupted.ValidTo = from.AddDays(-1);
            await org.SaveChangesAsync(ct);
            org.Compensations.Add(change);
            await org.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(new PayChangeResponse(Row(change), sentBack.Select(r => r.Id).ToList(),
                runs.Where(r => r.Status == "approved").Select(r => new RunToCorrect(r.Id, r.PeriodStart, r.PeriodEnd, r.PayDate)).ToList()));
        }).Produces<PayChangeResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict).RequirePermission(Permissions.Pay);

        return app;
    }

    /// <summary>The date the change starts, once every field is right (PAY-2028 otherwise).</summary>
    private static DateOnly Validate(PayChangeRequest request, Employee employee, DateOnly today)
    {
        var fields = new Dictionary<string, string>();
        if (request.Amount is not > 0) fields["amount"] = "Enter the basic pay, more than 0.";
        if (request.Basis is null || !Bases.Contains(request.Basis)) fields["basis"] = "Choose how the pay is given: monthly, weekly, fortnightly, daily or hourly.";
        if (request.UsualQuantity is { } usual && (request.Basis is not ("daily" or "hourly") || usual is not (> 0 and <= 744)))
            fields["usualQuantity"] = "Usual days or hours are only for a daily or hourly rate, more than 0 and up to 744.";
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) fields["reason"] = "Say why the pay changes from this date, in up to 500 characters.";
        if (request.EffectiveFrom is not { } from) fields["effectiveFrom"] = "Choose the date the pay changes from.";
        else if (from < employee.HireDate) fields["effectiveFrom"] = $"Choose a date on or after they started ({employee.HireDate:d MMM yyyy}).";
        else if (from > today) fields["effectiveFrom"] = "Choose today or an earlier date. A change from today can be made on their details.";
        else if (employee.ExitDate is { } exit && from > exit) fields["effectiveFrom"] = $"Choose a date on or before their last day ({exit:d MMM yyyy}).";
        else if (employee.Compensations.Any(c => c.ValidFrom == from)) fields["effectiveFrom"] = "Their pay already changes on this date. Choose another date.";
        if (fields.Count > 0) throw PayRunErrors.PayChangeInvalid(fields);
        return request.EffectiveFrom!.Value;
    }

    private static async Task<Employee> EmployeeAsync(OrganizationsDbContext org, Guid companyId, Guid employeeId, bool tracked, CancellationToken ct)
    {
        var query = org.Employees.Include(e => e.Compensations).Where(e => e.Id == employeeId && e.CompanyId == companyId);
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct) ?? throw PayRunErrors.EmployeeNotFound();
    }

    private static PayChangeRow Row(Compensation c) =>
        new(c.Id, c.ValidFrom, c.ValidTo, c.BasicAmount, c.PayBasis, c.UsualQuantity, c.Reason, c.ChangedBy, c.CreatedAt);
}
