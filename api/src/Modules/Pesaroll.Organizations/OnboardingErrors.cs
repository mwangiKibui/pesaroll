using Pesaroll.Shared.Errors;

namespace Pesaroll.Organizations;

/// <summary>Onboarding errors (ONB 3500–3999). Keep in step with docs/standards/errors.md.</summary>
internal static class OnboardingErrors
{
    public static AppException NameInvalid() =>
        new("ONB-3501", "Enter your organization's name (2 to 120 characters).");

    public static AppException AlreadyHasOrganization() =>
        new("ONB-3502", "You already have an organization.", 409);

    public static AppException NoOrganizationYet() =>
        new("ONB-3503", "You haven't set up your organization yet.", 404);
}
