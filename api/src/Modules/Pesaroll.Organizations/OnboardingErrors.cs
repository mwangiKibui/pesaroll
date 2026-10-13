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

    public static AppException PayItemDetailsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3528", "Some details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException PayItemCodeTaken() =>
        new("ONB-3529", "Another earning or deduction already has this code. Choose a different one or leave it empty.", 409);

    public static AppException PayItemNotFound() =>
        new("ONB-3530", "We couldn't find that earning or deduction. It may have been deleted.", 404);

    public static AppException PayItemLocked() =>
        new("ONB-3531", "This one is set by law (or is basic pay), so it can't be changed or deleted.", 409);

    public static AppException PayItemForEveryone() =>
        new("ONB-3545", "This one is set by law (or is basic pay): it applies to everyone, so there's no one to choose.", 409);

    public static AppException PayItemNotForCompany(string company) =>
        new("ONB-3546", $"This isn't used by {company}. Add the company to it first.", 409);

    public static AppException PayeeDetailsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3548", "Some payee details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException PayeeNotFound() =>
        new("ONB-3549", "We couldn't find that payee. It may have been deleted.", 404);

    public static AppException PayeeInUse(string name, int items) =>
        new("ONB-3550", $"{name} is the payee of {items} earnings and deductions. Choose another payee for them first.", 409);

    public static AppException PayeeNameTaken() =>
        new("ONB-3551", "Another payee already has this name.", 409);

    public static AppException HistoryBatchNotFound() =>
        new("ONB-3552", "We couldn't find that payroll history import. It may have been deleted.", 404);

    public static AppException AssignmentInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3547", "Some details of who gets it don't look right. Check the fields marked.") { Fields = fields };
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

    public static AppException EmployeeDetailsInvalid(IReadOnlyDictionary<string, string> fields) =>
        new("ONB-3538", "Some employee details don't look right. Check the fields marked.") { Fields = fields };

    public static AppException EmployeeNotFound() =>
        new("ONB-3539", "We couldn't find that employee. They may have been deleted.", 404);

    /// <summary>A number or contact another current employee has: holder is "Wanjiru Kamau (EMP-01)".</summary>
    public static AppException EmployeeNumberTaken(string field, string label, string holder) =>
        new("ONB-3540", $"{holder} already has this {label}. Each current employee has their own.", 409)
        { Fields = new Dictionary<string, string> { [field] = $"{holder} already has this {label}." } };

    public static AppException UsedByEmployees(string name, int count) =>
        new("ONB-3541", $"{name} is used by {count} {(count == 1 ? "employee" : "employees")}. Move them first.", 409);

    public static AppException StaffNoTaken() =>
        new("ONB-3542", "Another employee in this company has this staff number. Choose a different one or leave it empty.", 409)
        { Fields = new Dictionary<string, string> { ["staffNo"] = "Another employee in this company has this staff number." } };

    public static AppException NoOrganizationYet() =>
        new("ONB-3503", "You haven't set up your organization yet.", 404);
}
