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

    public static AppException MissingDetails() =>
        new("AUTH-1008", "Enter your name and choose your role.");
}

internal static class Passwords
{
    public const int MinimumLength = 10;
}
