using Pesaroll.Shared.Errors;

namespace Pesaroll.Organizations;

/// <summary>Onboarding errors (ONB 3500–3999). Keep in step with docs/standards/errors.md.</summary>
internal static class OnboardingErrors
{
    public static AppException NameInvalid() =>
        new("ONB-3501", "Enter your organization's name (2 to 120 characters).");

    public static AppException AlreadyHasOrganization() =>
        new("ONB-3502", "You already have an organization.", 409);

    public static AppException NotSelfSignUp() =>
        new("ONB-3504", "Your account was set up by your organization, so you can't create a new one. Ask an administrator in your organization.", 403);

    /// <summary>ACC-8001. For now only the person who onboarded the organization renames it; ACC-04 turns this into a permission.</summary>
    public static AppException NoPermission() =>
        new("ACC-8001", "You don't have permission to do this. Ask an administrator in your organization.", 403);

    public static AppException CompanyNameInvalid() =>
        new("ONB-3505", "Enter the company's legal name (2 to 160 characters).");

    public static AppException CountryNotSupported() =>
        new("ONB-3506", "Pesaroll runs payroll for companies in Kenya. Choose Kenya.");

    public static AppException DetailsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3507", "Some details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException CompanyNameTaken() =>
        new("ONB-3508", "You already have a company with this name.", 409);

    public static AppException CompanyNotFound() =>
        new("ONB-3509", "We couldn't find that company.", 404);

    public static AppException PaymentAccountInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3510", "Some paying account details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException PaymentAccountNotFound() =>
        new("ONB-3511", "We couldn't find that paying account.", 404);

    public static AppException LocationDetailsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3512", "Some location details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException LocationCodeTaken() =>
        new("ONB-3513", "Another location already has this code. Choose a different one or leave it empty.", 409);

    public static AppException LocationNotFound() =>
        new("ONB-3514", "We couldn't find that location.", 404);

    public static AppException MainLocationNeeded(string companies) =>
        new("ONB-3515", $"This is the main location of {companies}. Make another location their main location first.", 409);

    public static AppException StructureDetailsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3519", "Some details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException StructureCodeTaken() =>
        new("ONB-3520", "Another one already has this code. Choose a different one or leave it empty.", 409);

    public static AppException StructureNotFound() =>
        new("ONB-3521", "We couldn't find that. It may have been deleted.", 404);

    public static AppException StructureInUse(string what, string uses) =>
        new("ONB-3522", $"{what} still has {uses}. Move or delete them first.", 409);

    public static AppException ScheduleDetailsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3524", "Some schedule details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException ScheduleNameTaken() =>
        new("ONB-3525", "You already have a schedule with this name.", 409);

    public static AppException ScheduleNotFound() =>
        new("ONB-3526", "We couldn't find that schedule. It may have been deleted.", 404);

    public static AppException DefaultScheduleNeeded(string companies) =>
        new("ONB-3527", $"This is the default schedule of {companies}. Make another schedule their default first.", 409);

    public static AppException LeaveDetailsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3532", "Some leave details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException LeaveNameTaken(string what) =>
        new("ONB-3533", $"You already have {what}. Choose a different one.", 409);

    public static AppException LeaveNotFound() =>
        new("ONB-3534", "We couldn't find that leave plan or leave type. It may have been deleted.", 404);

    public static AppException LeaveTypeLocked() =>
        new("ONB-3535", "This leave is set by law, so it can't be deleted or renamed.", 409);

    public static AppException DefaultLeavePlanNeeded(string what) =>
        new("ONB-3536", $"This is the default leave plan of {what}. Make another plan their default first.", 409);

    public static AppException LeaveTypeInUse(string plans) =>
        new("ONB-3537", $"This leave is in {plans}. Take it out of those plans first.", 409);

    public static AppException NoOrganizationYet() =>
        new("ONB-3503", "You haven't set up your organization yet.", 404);
}
