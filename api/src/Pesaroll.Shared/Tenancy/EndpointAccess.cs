using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pesaroll.Shared.Errors;

namespace Pesaroll.Shared.Tenancy;

/// <summary>
/// What an endpoint needs (ACC-04): a permission, any signed-in member, or nobody. Every endpoint
/// declares one (EndpointAccessTests fails otherwise). A permission endpoint turns away someone who
/// has it for no company before any work; the handler still checks the company it touches
/// (<c>Access.Require(tenant, key, companyId)</c>) and narrows lists to <see cref="TenantContext.CompaniesFor"/>.
/// </summary>
public sealed record EndpointAccess(EndpointAccessKind Kind, string? Permission = null);

public enum EndpointAccessKind
{
    Public,
    SignedIn,
    Permission,
}

public static class EndpointAccessExtensions
{
    /// <summary>Only people with this permission, for at least one company.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission) where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new EndpointAccess(EndpointAccessKind.Permission, permission));
        builder.AddEndpointFilter(async (context, next) =>
        {
            // Signed-out requests and people without an organization get the handler's own sign-in or onboarding error.
            var tenant = context.HttpContext.RequestServices.GetRequiredService<TenantContext>();
            if (tenant.UserId is not null && tenant.OrganizationId is not null && !tenant.Grants.Any(g => g.Permission == permission))
            {
                throw AccessErrors.NoPermission();
            }

            return await next(context);
        });
        return builder;
    }

    /// <summary>Any signed-in person: what everyone needs, like the company selector, /me and reference lists.</summary>
    public static TBuilder SignedIn<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new EndpointAccess(EndpointAccessKind.SignedIn));

    /// <summary>Anyone, signed in or not: sign-up, sign-in, invitations, health.</summary>
    public static TBuilder Public<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new EndpointAccess(EndpointAccessKind.Public));
}
