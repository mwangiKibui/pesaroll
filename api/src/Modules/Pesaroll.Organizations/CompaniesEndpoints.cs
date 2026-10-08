using System.Net.Mail;
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

public sealed record CompanyRequest(
    string? LegalName, string? TradingName, string? CountryCode, string? IndustryCode,
    string? AddressLine, string? Town, string? RegionCode, string? PostalAddress, string? PostalCode,
    string? Phone, string? Email, Dictionary<string, string?>? Identifiers);

public sealed record CompanyFieldResponse(string Key, LocalizedText Label, LocalizedText? Hint, string? Example, string? Pattern, bool RequiredForPayroll);

public sealed record RegionResponse(string Code, string Name);

public sealed record CountryResponse(
    string Code, LocalizedText Name, string Currency, LocalizedText RegionLabel,
    IReadOnlyList<CompanyFieldResponse> Fields, IReadOnlyList<RegionResponse> Regions);

public sealed record IndustryResponse(string Code, LocalizedText Name);

/// <summary>One thing that stops this company's payroll from running (PAY-03 enforces it).</summary>
public sealed record MissingItem(string Kind, string Key, LocalizedText Label);

public sealed record ReadinessResponse(bool Ready, IReadOnlyList<MissingItem> Missing);

public sealed record CompanyResponse(
    Guid Id, string LegalName, string? TradingName, string CountryCode, string Currency, string? IndustryCode,
    string? AddressLine, string? Town, string? RegionCode, string? PostalAddress, string? PostalCode, string? Phone, string? Email,
    IReadOnlyDictionary<string, string> Identifiers, ReadinessResponse Readiness);

/// <summary>
/// ONB-03, the Companies step. Countries are data (database/schema/reference/countries.sql): the
/// fields a company records, their format checks and which ones payroll needs all come from the
/// company's country, so a new country is configuration (BO-11), not code. Missing numbers don't
/// block setup; they appear in the company's payroll readiness.
/// </summary>
public static class CompaniesEndpoints
{
    public static IEndpointRouteBuilder MapCompaniesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/countries", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            RequireSignedIn(tenant, http);
            var countries = await db.Countries.Where(c => c.Available).OrderBy(c => c.SortOrder).ToListAsync(ct);
            var codes = countries.Select(c => c.Code).ToList();
            var fields = await db.CountryCompanyFields.Where(f => codes.Contains(f.CountryCode) && f.Active).OrderBy(f => f.SortOrder).ToListAsync(ct);
            var regions = await db.CountryRegions.Where(r => codes.Contains(r.CountryCode)).OrderBy(r => r.SortOrder).ToListAsync(ct);
            return Results.Ok(countries.Select(c => new CountryResponse(
                c.Code, new LocalizedText(c.NameEn, c.NameSw), c.Currency, new LocalizedText(c.RegionLabelEn, c.RegionLabelSw),
                fields.Where(f => f.CountryCode == c.Code).Select(ToResponse).ToList(),
                regions.Where(r => r.CountryCode == c.Code).Select(r => new RegionResponse(r.Code, r.Name)).ToList())).ToList());
        }).WithTags("Companies").Produces<List<CountryResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/industries", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            RequireSignedIn(tenant, http);
            return Results.Ok(await db.Industries.Where(i => i.Active).OrderBy(i => i.SortOrder)
                .Select(i => new IndustryResponse(i.Code, new LocalizedText(i.NameEn, i.NameSw))).ToListAsync(ct));
        }).WithTags("Companies").Produces<List<IndustryResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        var companies = app.MapGroup("/api/companies").WithTags("Companies");

        companies.MapGet("/", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            RequireOrganization(tenant, http);
            var list = await db.Companies.Include(c => c.Identifiers).Where(c => c.Status == "active")
                .OrderBy(c => c.CreatedAt).ToListAsync(ct);
            var fields = await FieldsFor(db, list.Select(c => c.CountryCode).Distinct().ToList(), ct);
            var paying = await WithPayingAccount(db, ct);
            return Results.Ok(list.Select(c => ToResponse(c, fields, paying.Contains(c.Id))).ToList());
        }).Produces<List<CompanyResponse>>().Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        companies.MapPost("/", async (CompanyRequest request, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = RequireOrganization(tenant, http);
            await RequireOnboarder(db, userId, organizationId, ct);
            var valid = await Validate(request, db, ct);

            var company = new Company
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = organizationId,
                LegalName = valid.LegalName,
                CountryCode = valid.Country.Code,
                Currency = valid.Country.Currency,
            };
            Apply(company, valid);
            foreach (var (key, value) in valid.Identifiers.Where(i => i.Value is not null))
            {
                company.Identifiers.Add(NewIdentifier(company, key, value!));
            }

            // The new company joins the companies this request may see, so row-level security lets
            // it (and its numbers) be written. Its organization is still checked by the tenant policy.
            tenant.Set(organizationId, [.. tenant.CompanyIds, company.Id]);
            db.Companies.Add(company);
            await SaveAsync(db, ct);

            var fields = await FieldsFor(db, [company.CountryCode], ct);
            return Results.Created($"/api/companies/{company.Id}", ToResponse(company, fields, hasPayingAccount: false));
        }).Produces<CompanyResponse>(StatusCodes.Status201Created).Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        companies.MapPatch("/{id:guid}", async (Guid id, CompanyRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var (userId, organizationId) = RequireOrganization(tenant, http);
            var company = await db.Companies.Include(c => c.Identifiers).SingleOrDefaultAsync(c => c.Id == id && c.Status == "active", ct)
                ?? throw OnboardingErrors.CompanyNotFound();
            await RequireOnboarder(db, userId, organizationId, ct);
            var valid = await Validate(request, db, ct);

            company.LegalName = valid.LegalName;
            company.CountryCode = valid.Country.Code;
            company.Currency = valid.Country.Currency;
            Apply(company, valid);
            company.UpdatedAt = clock.GetUtcNow();

            // Numbers for fields the (possibly new) country doesn't have go; the rest follow the request:
            // a value sets the number, an empty value clears it, a field left out is kept.
            var countryKeys = valid.CountryFields.Select(f => f.Key).ToHashSet();
            db.CompanyIdentifiers.RemoveRange(company.Identifiers.Where(i => !countryKeys.Contains(i.FieldKey)));
            foreach (var (key, value) in valid.Identifiers)
            {
                var existing = company.Identifiers.SingleOrDefault(i => i.FieldKey == key);
                if (value is null && existing is not null)
                {
                    db.CompanyIdentifiers.Remove(existing);
                }
                else if (value is not null && existing is not null)
                {
                    existing.Value = value;
                    existing.UpdatedAt = clock.GetUtcNow();
                }
                else if (value is not null)
                {
                    db.CompanyIdentifiers.Add(NewIdentifier(company, key, value));
                }
            }

            await SaveAsync(db, ct);
            var saved = await db.Companies.Include(c => c.Identifiers).AsNoTracking().SingleAsync(c => c.Id == id, ct);
            var paying = await WithPayingAccount(db, ct);
            return Results.Ok(ToResponse(saved, await FieldsFor(db, [saved.CountryCode], ct), paying.Contains(saved.Id)));
        }).Produces<CompanyResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
          .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized).Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
          .Produces<ErrorResponse>(StatusCodes.Status404NotFound).Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        companies.MapDelete("/{id:guid}", async (Guid id, OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var (userId, organizationId) = RequireOrganization(tenant, http);
            var company = await db.Companies.SingleOrDefaultAsync(c => c.Id == id && c.Status == "active", ct)
                ?? throw OnboardingErrors.CompanyNotFound();
            await RequireOnboarder(db, userId, organizationId, ct);
            db.Companies.Remove(company);   // its numbers go with it (on delete cascade)
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
          .Produces<ErrorResponse>(StatusCodes.Status403Forbidden).Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    private sealed record ValidCompany(
        string LegalName, Country Country, List<CountryCompanyField> CountryFields,
        string? TradingName, string? IndustryCode, string? AddressLine, string? Town, string? RegionCode,
        string? PostalAddress, string? PostalCode, string? Phone, string? Email,
        Dictionary<string, string?> Identifiers);

    private static async Task<ValidCompany> Validate(CompanyRequest request, OrganizationsDbContext db, CancellationToken ct)
    {
        var legalName = request.LegalName?.Trim() ?? "";
        if (legalName.Length is < 2 or > 160)
        {
            throw OnboardingErrors.CompanyNameInvalid();
        }

        var code = request.CountryCode?.Trim().ToUpperInvariant() ?? "";
        var country = await db.Countries.SingleOrDefaultAsync(c => c.Code == code && c.Available, ct)
            ?? throw OnboardingErrors.CountryNotSupported();
        var countryFields = await db.CountryCompanyFields.Where(f => f.CountryCode == country.Code && f.Active).ToListAsync(ct);

        var errors = new Dictionary<string, string>();
        var industry = Blank(request.IndustryCode);
        if (industry is not null && !await db.Industries.AnyAsync(i => i.Code == industry && i.Active, ct))
        {
            errors["industryCode"] = "Choose an industry from the list.";
        }

        var region = Blank(request.RegionCode);
        if (region is not null && !await db.CountryRegions.AnyAsync(r => r.CountryCode == country.Code && r.Code == region, ct))
        {
            errors["regionCode"] = $"Choose a {country.RegionLabelEn.ToLowerInvariant()} from the list.";
        }

        var email = Blank(request.Email)?.ToLowerInvariant();
        if (email is not null && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || email.Length > 254))
        {
            errors["email"] = "Enter a valid email address.";
        }

        var phone = Blank(request.Phone);
        if (phone is not null && !Regex.IsMatch(phone, @"^\+?[0-9 ()-]{7,20}$"))
        {
            errors["phone"] = "Enter a valid phone number.";
        }

        var identifiers = new Dictionary<string, string?>();
        foreach (var (key, raw) in request.Identifiers ?? [])
        {
            var field = countryFields.SingleOrDefault(f => f.Key == key);
            var value = Blank(raw)?.ToUpperInvariant();
            if (field is null)
            {
                errors[$"identifiers.{key}"] = $"Companies in {country.NameEn} don't have this number.";
            }
            else if (value is not null && (value.Length > 60 || (field.Pattern is not null
                && !Regex.IsMatch(value, field.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)))))
            {
                errors[$"identifiers.{key}"] = field.Example is null
                    ? $"{field.LabelEn} doesn't look right."
                    : $"{field.LabelEn} doesn't look right. For example {field.Example}.";
            }
            else
            {
                identifiers[key] = value;
            }
        }

        if (errors.Count > 0)
        {
            throw OnboardingErrors.DetailsInvalid(errors);
        }

        return new ValidCompany(legalName, country, countryFields,
            Limit(request.TradingName, 160), industry, Limit(request.AddressLine, 200), Limit(request.Town, 100), region,
            Limit(request.PostalAddress, 100), Limit(request.PostalCode, 20), phone, email, identifiers);
    }

    private static void Apply(Company company, ValidCompany valid)
    {
        company.TradingName = valid.TradingName;
        company.IndustryCode = valid.IndustryCode;
        company.AddressLine = valid.AddressLine;
        company.Town = valid.Town;
        company.RegionCode = valid.RegionCode;
        company.PostalAddress = valid.PostalAddress;
        company.PostalCode = valid.PostalCode;
        company.Phone = valid.Phone;
        company.Email = valid.Email;
    }

    private static CompanyIdentifier NewIdentifier(Company company, string key, string value) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = company.OrganizationId,
        CompanyId = company.Id,
        FieldKey = key,
        Value = value,
    };

    private static async Task SaveAsync(OrganizationsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "companies_unique_name" })
        {
            throw OnboardingErrors.CompanyNameTaken();
        }
    }

    private static async Task<List<CountryCompanyField>> FieldsFor(OrganizationsDbContext db, List<string> countryCodes, CancellationToken ct) =>
        await db.CountryCompanyFields.Where(f => countryCodes.Contains(f.CountryCode) && f.Active).OrderBy(f => f.SortOrder).ToListAsync(ct);

    /// <summary>Companies (that this request can see) with at least one open paying account.</summary>
    private static async Task<HashSet<Guid>> WithPayingAccount(OrganizationsDbContext db, CancellationToken ct) =>
        [.. await db.CompanyPaymentAccounts.Where(a => a.ClosedAt == null).Select(a => a.CompanyId).Distinct().ToListAsync(ct)];

    private static readonly MissingItem PayingAccountMissing =
        new("payment_account", "payment_account", new LocalizedText("A paying account", "Akaunti ya kulipa"));

    private static CompanyResponse ToResponse(Company c, List<CountryCompanyField> fields, bool hasPayingAccount)
    {
        var identifiers = c.Identifiers.ToDictionary(i => i.FieldKey, i => i.Value);
        var missing = fields
            .Where(f => f.CountryCode == c.CountryCode && f.RequiredForPayroll && !identifiers.ContainsKey(f.Key))
            .Select(f => new MissingItem("identifier", f.Key, new LocalizedText(f.LabelEn, f.LabelSw)))
            .ToList();
        if (!hasPayingAccount)
        {
            missing.Add(PayingAccountMissing);   // payroll needs an account to pay from (PAY-03 enforces it)
        }

        return new CompanyResponse(c.Id, c.LegalName, c.TradingName, c.CountryCode, c.Currency, c.IndustryCode,
            c.AddressLine, c.Town, c.RegionCode, c.PostalAddress, c.PostalCode, c.Phone, c.Email,
            identifiers, new ReadinessResponse(missing.Count == 0, missing));
    }

    private static CompanyFieldResponse ToResponse(CountryCompanyField f) => new(
        f.Key, new LocalizedText(f.LabelEn, f.LabelSw),
        f.HintEn is null ? null : new LocalizedText(f.HintEn, f.HintSw ?? f.HintEn),
        f.Example, f.Pattern, f.RequiredForPayroll);

    internal static void RequireSignedIn(TenantContext tenant, HttpContext http)
    {
        if (tenant.UserId is null)
        {
            throw SessionService.RequireSignedIn(http);
        }
    }

    internal static (Guid UserId, Guid OrganizationId) RequireOrganization(TenantContext tenant, HttpContext http)
    {
        if (tenant.UserId is not Guid userId)
        {
            throw SessionService.RequireSignedIn(http);
        }

        return tenant.OrganizationId is Guid organizationId ? (userId, organizationId) : throw OnboardingErrors.NoOrganizationYet();
    }

    // For now only the person who onboarded the organization sets up its companies; ACC-04 makes this a permission.
    internal static async Task RequireOnboarder(OrganizationsDbContext db, Guid userId, Guid organizationId, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(o => o.Id == organizationId && o.OnboardedBy == userId, ct))
        {
            throw OnboardingErrors.NoPermission();
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Limit(string? value, int max)
    {
        var trimmed = Blank(value);
        return trimmed is null ? null : trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
