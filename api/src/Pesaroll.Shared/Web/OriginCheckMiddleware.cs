using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Pesaroll.Shared.Errors;

namespace Pesaroll.Shared.Web;

/// <summary>
/// Blocks data-changing requests (POST, PUT, PATCH, DELETE) that a browser sends from another
/// website, so a page elsewhere can't act with a signed-in person's session (CSRF). Requests
/// without an Origin header come from non-browser clients and are allowed; the session cookie
/// is also SameSite=Lax.
/// </summary>
public sealed class OriginCheckMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly HashSet<string> allowed = new(
        configuration.GetSection("App:AllowedOrigins").Get<string[]>() ?? [],
        StringComparer.OrdinalIgnoreCase);

    public Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var changesData = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
        var origin = context.Request.Headers.Origin.ToString();

        if (changesData && origin.Length > 0 && !allowed.Contains(origin))
        {
            throw new AppException("PLAT-9002", "This request came from another website and was blocked.", 403);
        }

        return next(context);
    }
}
