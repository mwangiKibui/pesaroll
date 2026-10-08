using System.Text.Json.Serialization;

namespace Pesaroll.Shared.Errors;

/// <summary>
/// The one error shape every endpoint returns: <c>{ code, message, reference }</c> (docs/standards/errors.md),
/// plus <c>fields</c> when particular inputs are wrong: field name → what's wrong with it.
/// </summary>
public sealed record ErrorResponse(
    string Code, string Message, string Reference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Fields = null);
