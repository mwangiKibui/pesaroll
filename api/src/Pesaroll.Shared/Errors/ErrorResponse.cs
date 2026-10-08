namespace Pesaroll.Shared.Errors;

/// <summary>The one error shape every endpoint returns: <c>{ code, message, reference }</c> (docs/standards/errors.md).</summary>
public sealed record ErrorResponse(string Code, string Message, string Reference);
