using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pesaroll.Identity;
using Pesaroll.Shared.Errors;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

public sealed record CreateOrganizationRequest(string? Name);

public sealed record RenameOrganizationRequest(string? Name);

public sealed record OrganizationResponse(Guid Id, string Name);

/// <summary>The organization, and an access token that now carries it (the old one stops working).</summary>
public sealed record CreateOrganizationResponse(OrganizationResponse Organization, TokenResponse Token);

/// <summary>ONB-02, the organization step: name the organization and move into setup.</summary>
public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationsEndpoints(this IEndpointRouteBuilder app)
    {
        var organizations = app.MapGroup("/api/organizations").WithTags("Organizations");

        organizations.MapPost("/", async (CreateOrganizationRequest request, OrganizationsDbContext db, TenantContext tenant,
            AccessTokens tokens, HttpContext http, CancellationToken ct) =>
        {
            if (tenant.UserId is not Guid userId || SessionService.CurrentSessionId(http) is not Guid sessionId)
            {
                throw SessionService.RequireSignedIn(http);
            }

            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 120)
            {
                throw OnboardingErrors.NameInvalid();
            }

            Guid organizationId;
            try
            {
                // Creates the organization and the person's membership and switches their session,
                // in one transaction (database/schema/tenancy/onboarding_functions.sql).
                organizationId = await db.Database
                    .SqlQuery<Guid>($"select create_organization({name}, {sessionId}) as \"Value\"")
                    .SingleAsync(ct);
            }
            catch (PostgresException error) when (error.MessageText == "ONB-3502")
            {
                throw OnboardingErrors.AlreadyHasOrganization();
            }
            catch (PostgresException error) when (error.MessageText == "ONB-3504")
            {
                throw OnboardingErrors.NotSelfSignUp();
            }
            catch (PostgresException error) when (error.MessageText == "ONB-3501")
            {
                throw OnboardingErrors.NameInvalid();
            }
            catch (PostgresException error) when (error.MessageText is "AUTH-1007" or "AUTH-1010")
            {
                throw SessionService.RequireSignedIn(http);
            }

            var token = tokens.Issue(new AccessTokenClaims(userId, organizationId, sessionId));
            return Results.Created("/api/organizations/current",
                new CreateOrganizationResponse(new OrganizationResponse(organizationId, name), token));
        })
        .Produces<CreateOrganizationResponse>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict).SignedIn();

        organizations.MapGet("/current", async (OrganizationsDbContext db, TenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            if (tenant.UserId is null)
            {
                throw SessionService.RequireSignedIn(http);
            }

            // Row-level security limits this to the request's organization.
            var organization = tenant.OrganizationId is Guid organizationId
                ? await db.Organizations.Where(o => o.Id == organizationId)
                    .Select(o => new OrganizationResponse(o.Id, o.Name)).SingleOrDefaultAsync(ct)
                : null;
            return Results.Ok(organization ?? throw OnboardingErrors.NoOrganizationYet());
        })
        .Produces<OrganizationResponse>()
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound).SignedIn();

        // Fix the name, e.g. a typo right after the organization step: whoever may manage every company.
        organizations.MapPatch("/current", async (RenameOrganizationRequest request, OrganizationsDbContext db, TenantContext tenant,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            if (tenant.UserId is null)
            {
                throw SessionService.RequireSignedIn(http);
            }

            var organization = tenant.OrganizationId is Guid organizationId
                ? await db.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId, ct)
                : null;
            if (organization is null)
            {
                throw OnboardingErrors.NoOrganizationYet();
            }

            Access.Require(tenant, Permissions.Companies);

            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 120)
            {
                throw OnboardingErrors.NameInvalid();
            }

            organization.Name = name;
            organization.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new OrganizationResponse(organization.Id, organization.Name));
        })
        .Produces<OrganizationResponse>()
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound).RequirePermission(Permissions.Companies);

        return app;
    }
}
