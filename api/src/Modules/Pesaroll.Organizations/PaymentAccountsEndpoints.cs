using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pesaroll.Identity;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Security;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

public sealed record BranchResponse(string Code, string Name);

public sealed record BankResponse(string Code, string Name, IReadOnlyList<BranchResponse> Branches);

/// <summary>
/// Adding or editing a paying account: a bank account (bank, branch, name, number) or an M-Pesa
/// shortcode. <see cref="IsDefault"/> makes it the company's default; the first account always is,
/// and the default stays the default until another account is made the default.
/// </summary>
public sealed record PaymentAccountRequest(
    string? Method, string? BankCode, string? BranchCode, string? AccountName, string? AccountNumber, string? MpesaShortcode,
    bool? IsDefault = null);

/// <summary>A paying account as people see it: the account number only ever masked ("•••• 8901").</summary>
public sealed record PaymentAccountResponse(
    Guid Id, string Method, string? BankCode, string? BankName, string? BranchCode, string? BranchName,
    string? AccountName, string? AccountNumberMasked, string? MpesaShortcode, bool IsDefault, DateTimeOffset CreatedAt);

/// <summary>
/// ONB-03, paying accounts: where each company's payroll is paid from, a bank account or an M-Pesa
/// B2C shortcode. Bank and M-Pesa files (PAY-06, PAY-07) take the debit account from here.
/// Account numbers are encrypted; nothing is overwritten: editing an account closes it and adds
/// the edited one in its place (replaces_id), and removing one closes it.
/// </summary>
public static class PaymentAccountsEndpoints
{
    /// <summary>What an account number's encryption is bound to, so it can't be copied onto another company.</summary>
    public static string Context(Guid companyId) => $"company_payment_accounts.account_no:{companyId}";

    public static IEndpointRouteBuilder MapPaymentAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/banks", async (string? country, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireSignedIn(tenant, http);
            var code = country?.Trim().ToUpperInvariant() ?? "KE";
            var banks = await db.Banks.Where(b => b.CountryCode == code && b.Active).OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ToListAsync(ct);
            var branches = await db.BankBranches.Where(b => b.CountryCode == code && b.Active).OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ToListAsync(ct);
            return Results.Ok(banks.Select(b => new BankResponse(b.Code, b.Name,
                branches.Where(r => r.BankCode == b.Code).Select(r => new BranchResponse(r.Code, r.Name)).ToList())).ToList());
        }).WithTags("Companies").Produces<List<BankResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        var accounts = app.MapGroup("/api/companies/{companyId:guid}/payment-accounts").WithTags("Companies");

        accounts.MapGet("/", async (Guid companyId, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            CompaniesEndpoints.RequireOrganization(tenant, http);
            var company = await FindCompany(db, companyId, ct);
            return Results.Ok(await ListAsync(db, company, ct));
        }).Produces<List<PaymentAccountResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        accounts.MapPost("/", async (Guid companyId, PaymentAccountRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, FieldEncryptor encryptor, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var company = await FindCompany(db, companyId, ct);
            var (account, number) = await Validate(request, company, db, ct);

            account.Id = Guid.CreateVersion7();
            account.OrganizationId = organizationId;
            account.CreatedBy = userId;
            if (number is not null)
            {
                account.AccountNoEnc = encryptor.Encrypt(number, Context(company.Id));
            }

            // The first open account is the default; later ones only when asked.
            var open = await db.CompanyPaymentAccounts.Where(a => a.CompanyId == company.Id && a.ClosedAt == null).ToListAsync(ct);
            account.IsDefault = open.Count == 0 || request.IsDefault == true;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (account.IsDefault)
            {
                await ClearDefaultAsync(db, open, ct);
            }

            db.CompanyPaymentAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var saved = (await ListAsync(db, company, ct)).Single(a => a.Id == account.Id);
            return Results.Created($"/api/companies/{company.Id}/payment-accounts/{account.Id}", saved);
        }).Produces<PaymentAccountResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        accounts.MapPut("/{accountId:guid}", async (Guid companyId, Guid accountId, PaymentAccountRequest request, OrganizationsDbContext db,
            TenantContext tenant, HttpContext http, FieldEncryptor encryptor, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var company = await FindCompany(db, companyId, ct);
            var old = await db.CompanyPaymentAccounts.SingleOrDefaultAsync(a => a.Id == accountId && a.CompanyId == company.Id && a.ClosedAt == null, ct)
                ?? throw OnboardingErrors.PaymentAccountNotFound();
            var (account, number) = await Validate(request, company, db, ct, replacing: old);

            account.Id = Guid.CreateVersion7();
            account.OrganizationId = organizationId;
            account.CreatedBy = userId;
            account.ReplacesId = old.Id;
            if (number is not null)
            {
                account.AccountNoEnc = encryptor.Encrypt(number, Context(company.Id));
            }
            else if (account.Method == "bank")
            {
                // Left empty: the same number, already encrypted for this company.
                account.AccountNoEnc = old.AccountNoEnc;
                account.AccountLast4 = old.AccountLast4;
            }

            // The default stays the default; another becomes it only when asked. Close the old row and
            // clear the old default first: only one open account may be the default at a time.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            account.IsDefault = old.IsDefault || request.IsDefault == true;
            old.IsDefault = false;
            old.ClosedAt = clock.GetUtcNow();
            old.ClosedBy = userId;
            if (account.IsDefault)
            {
                await ClearDefaultAsync(db, await db.CompanyPaymentAccounts.Where(a => a.CompanyId == company.Id && a.ClosedAt == null).ToListAsync(ct), ct);
            }

            await db.SaveChangesAsync(ct);
            db.CompanyPaymentAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok((await ListAsync(db, company, ct)).Single(a => a.Id == account.Id));
        }).Produces<PaymentAccountResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        accounts.MapPost("/{accountId:guid}/default", async (Guid companyId, Guid accountId, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var company = await FindCompany(db, companyId, ct);
            var open = await db.CompanyPaymentAccounts.Where(a => a.CompanyId == company.Id && a.ClosedAt == null).ToListAsync(ct);
            var chosen = open.SingleOrDefault(a => a.Id == accountId) ?? throw OnboardingErrors.PaymentAccountNotFound();
            await MakeDefaultAsync(db, open, chosen, ct);
            return Results.Ok(await ListAsync(db, company, ct));
        }).Produces<List<PaymentAccountResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        accounts.MapDelete("/{accountId:guid}", async (Guid companyId, Guid accountId, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = CompaniesEndpoints.RequireOrganization(tenant, http);
            await CompaniesEndpoints.RequireOnboarder(db, userId, organizationId, ct);
            var company = await FindCompany(db, companyId, ct);
            var open = await db.CompanyPaymentAccounts.Where(a => a.CompanyId == company.Id && a.ClosedAt == null)
                .OrderBy(a => a.CreatedAt).ToListAsync(ct);
            var account = open.SingleOrDefault(a => a.Id == accountId) ?? throw OnboardingErrors.PaymentAccountNotFound();

            // Closed, not deleted: what past payrolls were paid from stays on record.
            account.ClosedAt = clock.GetUtcNow();
            account.ClosedBy = userId;
            var wasDefault = account.IsDefault;
            account.IsDefault = false;
            await db.SaveChangesAsync(ct);
            if (wasDefault && open.FirstOrDefault(a => a.Id != account.Id) is { } next)
            {
                await MakeDefaultAsync(db, open.Where(a => a.Id != account.Id).ToList(), next, ct);
            }

            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Company> FindCompany(OrganizationsDbContext db, Guid companyId, CancellationToken ct) =>
        await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == companyId && c.Status == "active", ct)
            ?? throw OnboardingErrors.CompanyNotFound();

    private static async Task ClearDefaultAsync(OrganizationsDbContext db, List<CompanyPaymentAccount> open, CancellationToken ct)
    {
        foreach (var other in open.Where(a => a.IsDefault))
        {
            other.IsDefault = false;
        }

        await db.SaveChangesAsync(ct);
    }

    // Two steps, because only one open account may be the default at any moment (a unique index).
    private static async Task MakeDefaultAsync(OrganizationsDbContext db, List<CompanyPaymentAccount> open, CompanyPaymentAccount chosen, CancellationToken ct)
    {
        await ClearDefaultAsync(db, open.Where(a => a.Id != chosen.Id).ToList(), ct);
        chosen.IsDefault = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Checks the request. A bank account's number comes back separately, for the caller to encrypt;
    /// when editing a bank account (<paramref name="replacing"/>) it may be left empty to keep the old one.
    /// </summary>
    private static async Task<(CompanyPaymentAccount Account, string? Number)> Validate(PaymentAccountRequest request, Company company,
        OrganizationsDbContext db, CancellationToken ct, CompanyPaymentAccount? replacing = null)
    {
        var errors = new Dictionary<string, string>();
        var method = request.Method?.Trim().ToLowerInvariant();
        if (method == "mpesa")
        {
            var shortcode = request.MpesaShortcode?.Trim() ?? "";
            if (!Regex.IsMatch(shortcode, "^[0-9]{5,7}$"))
            {
                errors["mpesaShortcode"] = "Enter the M-Pesa shortcode your payroll is paid from (5 to 7 digits).";
            }

            return errors.Count > 0
                ? throw OnboardingErrors.PaymentAccountInvalid(errors)
                : (new CompanyPaymentAccount { Method = "mpesa", CompanyId = company.Id, MpesaShortcode = shortcode }, null);
        }

        if (method != "bank")
        {
            throw OnboardingErrors.PaymentAccountInvalid(new Dictionary<string, string> { ["method"] = "Choose a bank account or M-Pesa." });
        }

        var bankCode = request.BankCode?.Trim() ?? "";
        var branchCode = request.BranchCode?.Trim() ?? "";
        if (!await db.Banks.AnyAsync(b => b.CountryCode == company.CountryCode && b.Code == bankCode && b.Active, ct))
        {
            errors["bankCode"] = "Choose a bank from the list.";
        }
        else if (!await db.BankBranches.AnyAsync(b => b.CountryCode == company.CountryCode && b.BankCode == bankCode && b.Code == branchCode && b.Active, ct))
        {
            errors["branchCode"] = "Choose a branch of this bank from the list.";
        }

        var name = request.AccountName?.Trim() ?? "";
        if (name.Length is < 2 or > 120)
        {
            errors["accountName"] = "Enter the name on the account (2 to 120 characters).";
        }

        // People paste numbers with spaces or dashes; banks only want the digits.
        var number = Regex.Replace(request.AccountNumber ?? "", @"[\s-]", "");
        var keepNumber = number.Length == 0 && replacing?.Method == "bank";
        if (!keepNumber && !Regex.IsMatch(number, "^[0-9]{6,20}$"))
        {
            errors["accountNumber"] = "Enter the account number: 6 to 20 digits.";
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.PaymentAccountInvalid(errors);
        }

        return (new CompanyPaymentAccount
        {
            Method = "bank",
            CompanyId = company.Id,
            CountryCode = company.CountryCode,
            BankCode = bankCode,
            BranchCode = branchCode,
            AccountName = name,
            AccountLast4 = keepNumber ? null : number[^4..],
        }, keepNumber ? null : number);
    }

    private static async Task<List<PaymentAccountResponse>> ListAsync(OrganizationsDbContext db, Company company, CancellationToken ct)
    {
        var open = await db.CompanyPaymentAccounts.AsNoTracking().Where(a => a.CompanyId == company.Id && a.ClosedAt == null)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
        var banks = await db.Banks.AsNoTracking().Where(b => b.CountryCode == company.CountryCode).ToListAsync(ct);
        var branches = await db.BankBranches.AsNoTracking().Where(b => b.CountryCode == company.CountryCode).ToListAsync(ct);
        return open.Select(a => new PaymentAccountResponse(
            a.Id, a.Method, a.BankCode, banks.SingleOrDefault(b => b.Code == a.BankCode)?.Name,
            a.BranchCode, branches.SingleOrDefault(b => b.BankCode == a.BankCode && b.Code == a.BranchCode)?.Name,
            a.AccountName, a.AccountLast4 is null ? null : $"•••• {a.AccountLast4}", a.MpesaShortcode, a.IsDefault, a.CreatedAt)).ToList();
    }
}
