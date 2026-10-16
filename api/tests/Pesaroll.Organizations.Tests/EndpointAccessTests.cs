using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations.Tests;

/// <summary>
/// ACC-04: every endpoint declares what it needs (a permission, any signed-in member, or nobody),
/// so a new endpoint can't ship without a check.
/// </summary>
public sealed class EndpointAccessTests(OnboardingTests.Api api) : IClassFixture<OnboardingTests.Api>
{
    [Fact]
    public void Every_endpoint_declares_what_it_needs()
    {
        using var _ = api.CreateClient();
        var missing = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<EndpointAccess>() is null)
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .Order()
            .ToList();
        Assert.Empty(missing);
    }
}
