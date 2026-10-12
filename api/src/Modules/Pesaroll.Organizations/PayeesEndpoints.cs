using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// Adding or editing a payee. Only the chosen way's details are kept: bank (bank, branch, the
/// payee's account number), mpesa_paybill (paybill, account), cheque (payable to) or cash.
/// </summary>
public sealed record PayeeRequest(string? Name, string? Method, string? BankCode, string? BranchCode, string? AccountNumber,
    string? Paybill, string? PaybillAccount, string? ChequePayableTo, string? OurReference);

/// <summary>A payee, with how many earnings and deductions name it.</summary>
public sealed record PayeeResponse(Guid Id, string Name, string Method, string? BankCode, string? BankName, string? BranchCode, string? BranchName,
    string? AccountNumber, string? Paybill, string? PaybillAccount, string? ChequePayableTo, string? OurReference, int UsedBy);

/// <summary>
/// ONB-07: who a deduction's or employer cost's money is paid to (a SACCO, union, insurer, lender),
/// the organization's, shared by its companies and items. Remittances (PAY-09) are paid to these.
/// </summary>
public static class PayeesEndpoints
{
    public static readonly string[] Methods = ["bank", "mpesa_paybill", "cheque", "cash"];

    public static IEndpointRouteBuilder MapPayeesEndpoints(this IEndpointRouteBuilder app)
    {
        var payees = app.MapGroup("/api/payees").WithTags("Earnings and deductions");

        payees.MapGet("/", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var list = await db.Payees.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
            return Results.Ok(await ResponsesAsync(db, list, ct));
        }).Produces<List<PayeeResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        payees.MapPost("/", async (PayeeRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var now = clock.GetUtcNow();
            var payee = new Payee { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = "", Method = "", CreatedAt = now };
            await ApplyAsync(payee, request, db, now, ct);
            db.Payees.Add(payee);
            await SaveAsync(db, ct);
            return Results.Created($"/api/payees/{payee.Id}", (await ResponsesAsync(db, [payee], ct))[0]);
        }).Produces<PayeeResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        payees.MapPatch("/{id:guid}", async (Guid id, PayeeRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var payee = await db.Payees.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw OnboardingErrors.PayeeNotFound();
            await ApplyAsync(payee, request, db, clock.GetUtcNow(), ct);
            await SaveAsync(db, ct);
            return Results.Ok((await ResponsesAsync(db, [payee], ct))[0]);
        }).Produces<PayeeResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        payees.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var payee = await db.Payees.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw OnboardingErrors.PayeeNotFound();
            var used = await db.PayItems.CountAsync(i => i.PayeeId == id, ct);
            if (used > 0)
            {
                throw OnboardingErrors.PayeeInUse(payee.Name, used);
            }

            db.Payees.Remove(payee);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>Checks the request and keeps only the chosen way's details (ONB-3548 with the fields marked).</summary>
    private static async Task ApplyAsync(Payee payee, PayeeRequest request, OrganizationsDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var name = Text(request.Name) ?? "";
        if (name.Length is < 2 or > 120)
        {
            errors["name"] = "Enter the payee's name (2 to 120 characters).";
        }

        var method = Methods.FirstOrDefault(m => m == request.Method);
        if (method is null)
        {
            errors["method"] = "Choose bank, M-Pesa paybill, cheque or cash.";
        }

        string? bankCode = null, branchCode = null, account = null, paybill = null, paybillAccount = null, payableTo = null;
        if (method == "bank")
        {
            var bank = await db.Banks.AsNoTracking().SingleOrDefaultAsync(b => b.CountryCode == "KE" && b.Code == request.BankCode && b.Active, ct);
            if (bank is null)
            {
                errors["bankCode"] = "Choose the payee's bank.";
            }
            else if (!await db.BankBranches.AnyAsync(b => b.CountryCode == "KE" && b.BankCode == bank.Code && b.Code == request.BranchCode && b.Active, ct))
            {
                errors["branchCode"] = $"Choose a branch of {bank.Name}.";
            }

            account = (request.AccountNumber ?? "").Replace(" ", "").Replace("-", "");
            if (account.Length == 0)
            {
                errors["accountNumber"] = "Enter the payee's account number.";
            }
            else if (bank is not null && EmployeesEndpoints.AccountNumberProblem(bank, account) is { } problem)
            {
                errors["accountNumber"] = problem;
            }

            (bankCode, branchCode) = (bank?.Code, request.BranchCode);
        }
        else if (method == "mpesa_paybill")
        {
            paybill = (request.Paybill ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(paybill, "^[0-9]{5,7}$"))
            {
                errors["paybill"] = "Enter the paybill number: 5 to 7 digits.";
            }

            paybillAccount = Text(request.PaybillAccount);
            if (paybillAccount?.Length > 40)
            {
                errors["paybillAccount"] = "Keep the account to 40 characters.";
            }
        }
        else if (method == "cheque")
        {
            payableTo = Text(request.ChequePayableTo) ?? name;
            if (payableTo.Length is < 2 or > 120)
            {
                errors["chequePayableTo"] = "Enter who cheques are payable to (2 to 120 characters).";
            }
        }

        var reference = Text(request.OurReference);
        if (reference?.Length > 40)
        {
            errors["ourReference"] = "Keep your reference to 40 characters.";
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.PayeeDetailsInvalid(errors);
        }

        payee.Name = name;
        payee.Method = method!;
        payee.CountryCode = method == "bank" ? "KE" : null;
        (payee.BankCode, payee.BranchCode, payee.AccountNumber) = (bankCode, branchCode, account);
        (payee.Paybill, payee.PaybillAccount, payee.ChequePayableTo, payee.OurReference) = (paybill, paybillAccount, payableTo, reference);
        payee.UpdatedAt = now;
    }

    private static async Task SaveAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "payees_unique_name" })
        {
            throw OnboardingErrors.PayeeNameTaken();
        }
    }

    private static async Task<List<PayeeResponse>> ResponsesAsync(OrganizationsDbContext db, List<Payee> payees, CancellationToken ct)
    {
        var ids = payees.Select(p => p.Id).ToList();
        var used = await db.PayItems.AsNoTracking().Where(i => i.PayeeId != null && ids.Contains(i.PayeeId.Value))
            .GroupBy(i => i.PayeeId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var banks = await db.Banks.AsNoTracking().Where(b => b.CountryCode == "KE").ToDictionaryAsync(b => b.Code, b => b.Name, ct);
        var branches = await db.BankBranches.AsNoTracking().Where(b => b.CountryCode == "KE").ToListAsync(ct);
        return payees.Select(p => new PayeeResponse(p.Id, p.Name, p.Method, p.BankCode, p.BankCode is null ? null : banks.GetValueOrDefault(p.BankCode),
            p.BranchCode, branches.FirstOrDefault(b => b.BankCode == p.BankCode && b.Code == p.BranchCode)?.Name, p.AccountNumber,
            p.Paybill, p.PaybillAccount, p.ChequePayableTo, p.OurReference, used.GetValueOrDefault(p.Id))).ToList();
    }
}
