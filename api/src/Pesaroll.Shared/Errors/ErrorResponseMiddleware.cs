using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pesaroll.Shared.Errors;

/// <summary>
/// Turns every error into the one shape the apps understand:
/// <c>{ "code": "AUTH-1001", "message": "…", "reference": "7F3K92QD" }</c>.
/// The reference is logged with the error so support can find it.
/// </summary>
public sealed class ErrorResponseMiddleware(RequestDelegate next, ILogger<ErrorResponseMiddleware> logger)
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32: no I, L, O, U

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException error)
        {
            var reference = NewReference();
            logger.LogInformation("{Code} {Reference} on {Method} {Path}", error.Code, reference,
                context.Request.Method, context.Request.Path);
            await WriteAsync(context, error.StatusCode, error.Code, error.Message, reference, error.Headers);
        }
        catch (Exception error)
        {
            var reference = NewReference();
            logger.LogError(error, "PLAT-9001 {Reference} on {Method} {Path}", reference,
                context.Request.Method, context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError, "PLAT-9001",
                $"Something went wrong on our side. Quote reference {reference} to support.", reference);
        }
    }

    public static Task WriteAsync(HttpContext context, int statusCode, string code, string message, string? reference = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            context.Response.Headers[name] = value;
        }

        return context.Response.WriteAsJsonAsync(new ErrorResponse(code, message, reference ?? NewReference()));
    }

    public static string NewReference() =>
        string.Create(8, 0, (chars, _) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
        });
}
