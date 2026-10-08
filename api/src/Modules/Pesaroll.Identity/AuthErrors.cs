using Pesaroll.Shared.Errors;

namespace Pesaroll.Identity;

/// <summary>Sign-up and sign-in errors. Keep in step with docs/standards/errors.md.</summary>
internal static class AuthErrors
{
    public static AppException WrongCredentials() =>
        new("AUTH-1001", "That email or phone number and password don't match.", 401);

    public static AppException Locked() =>
        new("AUTH-1002", "Your account is locked after too many attempts. Try again in 15 minutes or reset your password.", 423);

    public static AppException AlreadyExists() =>
        new("AUTH-1003", "An account with this email or phone number already exists. Sign in instead.", 409);

    public static AppException PasswordTooShort() =>
        new("AUTH-1004", $"Use a password of at least {Passwords.MinimumLength} characters.");

    public static AppException InvalidIdentifier() =>
        new("AUTH-1005", "Enter a valid email address or Kenyan phone number.");

    public static AppException NotSignedIn() =>
        new("AUTH-1007", "Sign in to continue.", 401);

    /// <summary>The access token or refresh cookie no longer works. The app refreshes once, then asks the person to sign in.</summary>
    public static AppException SessionEnded(bool invalidToken = false) =>
        new("AUTH-1010", "Your session has ended. Sign in again.", 401)
        {
            Headers = invalidToken
                ? new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer error=\"invalid_token\"" }
                : new Dictionary<string, string>(),
        };

    /// <summary>A change that moves money (a paying account) asks for the password again. 403, not 401: the session is fine.</summary>
    public static AppException PasswordNotConfirmed() =>
        new("AUTH-1011", "That password isn't right. Enter the password you sign in with to confirm.", 403);

    public static AppException MissingDetails() =>
        new("AUTH-1008", "Enter your first and last name and choose your role.");

    public static AppException OtherRoleNotTyped() =>
        new("AUTH-1009", "You chose Other: type your role (up to 80 characters).");
}

internal static class Passwords
{
    public const int MinimumLength = 6;
}
