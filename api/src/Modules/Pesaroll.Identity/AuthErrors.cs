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

    /// <summary>AUTH-05: not one of your active sessions (ended already, or someone else's).</summary>
    public static AppException SessionNotFound() =>
        new("AUTH-1017", "We couldn't find that sign-in. It may have ended already.", 404);

    /// <summary>The access token or refresh cookie no longer works. The app refreshes once, then asks the person to sign in.</summary>
    public static AppException SessionEnded(bool invalidToken = false) =>
        new("AUTH-1010", "Your session has ended. Sign in again.", 401)
        {
            Headers = invalidToken
                ? new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer error=\"invalid_token\"" }
                : new Dictionary<string, string>(),
        };

    public static AppException MissingDetails() =>
        new("AUTH-1008", "Enter your first and last name and choose your role.");

    public static AppException MissingName() =>
        new("AUTH-1016", "Enter your first and last name.");

    public static AppException InvitationNotFound() =>
        new("AUTH-1011", "This invitation link isn't valid. Check you opened the whole link, or ask for a new invitation.", 404);

    public static AppException InvitationExpired() =>
        new("AUTH-1012", "This invitation has expired. Ask the person who invited you to send it again.", 410);

    public static AppException InvitationUsed() =>
        new("AUTH-1013", "This invitation has already been accepted. Sign in instead.", 409);

    public static AppException InvitationRevoked() =>
        new("AUTH-1014", "This invitation was cancelled. Ask the person who invited you if you still need access.", 410);

    public static AppException InvitationForSomeoneElse() =>
        new("AUTH-1015", "This invitation is for a different email or phone number.", 403);

    public static AppException OtherRoleNotTyped() =>
        new("AUTH-1009", "You chose Other: type your role (up to 80 characters).");
}

internal static class Passwords
{
    public const int MinimumLength = 6;
}
