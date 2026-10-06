namespace Pesaroll.Shared.Errors;

/// <summary>
/// An error the user should see, with a code from the catalogue (docs/standards/errors.md).
/// <see cref="ErrorResponseMiddleware"/> turns it into <c>{ code, message, reference }</c>.
/// </summary>
public sealed class AppException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}
