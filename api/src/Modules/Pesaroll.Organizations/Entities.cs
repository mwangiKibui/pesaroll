namespace Pesaroll.Organizations;

// Mapped to the tables in database/schema/. EF Core never creates or changes them.

public sealed class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Who onboarded the organization (ONB-02). Not an owner: roles come from security groups.</summary>
    public Guid OnboardedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Company
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string LegalName { get; set; }
    public string? TradingName { get; set; }
    public required string CountryCode { get; set; }
    public required string Currency { get; set; }
    public string? IndustryCode { get; set; }
    public string? AddressLine { get; set; }
    public string? Town { get; set; }
    public string? RegionCode { get; set; }
    public string? PostalAddress { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>Its one main location (ONB-04), created as "Head office" with the company.</summary>
    public Guid? MainLocationId { get; set; }
    /// <summary>The schedule its employees get unless told otherwise (ONB-06); the suggested "Monthly" to start.</summary>
    public Guid? DefaultPayScheduleId { get; set; }
    /// <summary>The leave plan its employees get (ONB-08) unless their pay schedule names one; "Standard" to start.</summary>
    public Guid? DefaultLeavePlanId { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CompanyIdentifier> Identifiers { get; set; } = [];
}

/// <summary>A company's statutory or registration number for one of its country's fields.</summary>
public sealed class CompanyIdentifier
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public required string FieldKey { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// Reference data (database/schema/reference/countries.sql): read here, edited by the back office (BO-11).

public sealed class Country
{
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public required string Currency { get; set; }
    public required string PhoneCode { get; set; }
    public required string RegionLabelEn { get; set; }
    public required string RegionLabelSw { get; set; }
    public bool Available { get; set; }
    public int SortOrder { get; set; }
}

public sealed class CountryCompanyField
{
    public required string CountryCode { get; set; }
    public required string Key { get; set; }
    public required string LabelEn { get; set; }
    public required string LabelSw { get; set; }
    public string? HintEn { get; set; }
    public string? HintSw { get; set; }
    public string? Example { get; set; }
    public string? Pattern { get; set; }
    public bool RequiredForPayroll { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; }
}

public sealed class CountryRegion
{
    public required string CountryCode { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Industry
{
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; }
}

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string? JobRole { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Bank
{
    public required string CountryCode { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string LegalName { get; set; }
    /// <summary>How many digits its account numbers have (ONB-09).</summary>
    public int AccountDigitsMin { get; set; }
    public int AccountDigitsMax { get; set; }
    public bool Active { get; set; }
    public int SortOrder { get; set; }
}

public sealed class BankBranch
{
    public required string CountryCode { get; set; }
    public required string BankCode { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Where a company's payroll is paid from. Rows are never rewritten: the app may only move the
/// default or close one (database/schema/tenancy/companies.sql grants nothing else).
/// </summary>
public sealed class CompanyPaymentAccount
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>bank or mpesa.</summary>
    public required string Method { get; set; }
    public string? CountryCode { get; set; }
    public string? BankCode { get; set; }
    public string? BranchCode { get; set; }
    public string? AccountName { get; set; }
    /// <summary>Encrypted with <see cref="Pesaroll.Shared.Security.FieldEncryptor"/>; see PaymentAccountsEndpoints.Context.</summary>
    public byte[]? AccountNoEnc { get; set; }
    public string? AccountLast4 { get; set; }
    public string? MpesaShortcode { get; set; }
    public bool IsDefault { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    /// <summary>Set when this row came from editing another (which was closed).</summary>
    public Guid? ReplacesId { get; set; }
}

/// <summary>A minimum-wage area of a country (ONB-04). Its towns suggest it; rates live in rule packs.</summary>
public sealed class CountryWageArea
{
    public required string CountryCode { get; set; }
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public required string DescriptionEn { get; set; }
    public required string DescriptionSw { get; set; }
    public List<string> Towns { get; set; } = [];
    public int SortOrder { get; set; }
    public bool Active { get; set; }
}

/// <summary>
/// Where people work (ONB-04). Serves all the organization's companies (<see cref="AllCompanies"/>)
/// or those in <see cref="Companies"/>. Timestamps are set by the app: the database's read policy
/// needs the company links, which don't exist yet when the row is first written.
/// </summary>
public sealed class Location
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>head_office, branch, office, warehouse or site.</summary>
    public string Kind { get; set; } = "branch";
    public required string CountryCode { get; set; }
    public string? RegionCode { get; set; }
    public string? Town { get; set; }
    public string? AddressLine { get; set; }
    public string? WageAreaCode { get; set; }
    public bool AllCompanies { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<LocationCompany> Companies { get; set; } = [];
}

public sealed class LocationCompany
{
    public Guid OrganizationId { get; set; }
    public Guid LocationId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// How often and when people are paid (ONB-06): monthly, weekly, every two weeks or daily, on the
/// days <see cref="PayDates"/> gives, for all companies or the ones in <see cref="Companies"/>.
/// </summary>
public sealed class PaySchedule
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    /// <summary>monthly, weekly, fortnightly or daily; each has its own fields below and none of the others'.</summary>
    public string Frequency { get; set; } = "monthly";
    /// <summary>Monthly: last_working_day, last_day or fixed_day.</summary>
    public string? PayDayRule { get; set; } = PayDates.LastWorkingDay;
    /// <summary>Monthly: the day of the month (1 to 28) for a fixed day; empty otherwise.</summary>
    public int? PayDay { get; set; }
    /// <summary>Monthly: the day of the month (1 to 28) after which changes go into next month.</summary>
    public int? CutoffDay { get; set; } = 25;
    /// <summary>Weekly and fortnightly: the weekday paid, Monday 1 to Friday 5.</summary>
    public int? PayWeekday { get; set; }
    /// <summary>Fortnightly: the first pay date, from which every other week counts.</summary>
    public DateOnly? FirstPayDate { get; set; }
    /// <summary>Weekly and fortnightly: how many days before the pay day changes close.</summary>
    public int? CutoffDaysBefore { get; set; }
    /// <summary>Daily: when in the day people are paid (reminders count hours back from it).</summary>
    public TimeOnly? PayTime { get; set; }
    /// <summary>Whether changes after a cut-off wait for the next payroll; daily pay has none.</summary>
    public bool UsesCutoff { get; set; } = true;
    /// <summary>What the cut-off covers: <see cref="PaySchedulesEndpoints.CutoffItems"/> keys or the organization's own items.</summary>
    public List<string> CutoffAppliesTo { get; set; } = [.. PaySchedulesEndpoints.CutoffItems];
    public bool AllCompanies { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<PayScheduleCompany> Companies { get; set; } = [];
    public List<PayScheduleReminder> Reminders { get; set; } = [];
    /// <summary>The leave plan for people on this schedule (ONB-08), before their company's default.</summary>
    public Guid? DefaultLeavePlanId { get; set; }
}

/// <summary>
/// An employee of one company (ONB-09). The ID number and KRA PIN are encrypted
/// (<see cref="Pesaroll.Shared.Security.FieldEncryptor"/>) with a lookup for duplicates and the last
/// four kept for screens. An exit date means they've left.
/// </summary>
public sealed class Employee
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public required string StaffNo { get; set; }
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public byte[]? NationalIdEnc { get; set; }
    public string? NationalIdLookup { get; set; }
    public string? NationalIdLast4 { get; set; }
    public byte[]? KraPinEnc { get; set; }
    public string? KraPinLookup { get; set; }
    public string? KraPinLast4 { get; set; }
    public string? NssfNo { get; set; }
    public string? ShaNo { get; set; }
    public DateOnly HireDate { get; set; }
    public DateOnly? ExitDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<Employment> Employments { get; set; } = [];
    public List<Compensation> Compensations { get; set; } = [];
    public List<EmployeePaymentAccount> Accounts { get; set; } = [];
}

/// <summary>An employee's job from <see cref="ValidFrom"/> (to <see cref="ValidTo"/>; empty while current).</summary>
public sealed class Employment
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? LineManagerId { get; set; }
    public Guid PayScheduleId { get; set; }
    public Guid LeavePlanId { get; set; }
    /// <summary>permanent, contract, casual or intern.</summary>
    public required string EmploymentType { get; set; }
    public DateOnly? ContractEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An employee's basic pay from <see cref="ValidFrom"/>: monthly, daily or hourly.</summary>
public sealed class Compensation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal BasicAmount { get; set; }
    public required string PayBasis { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Where an employee's pay goes from <see cref="ValidFrom"/>: a bank account or M-Pesa (encrypted).</summary>
public sealed class EmployeePaymentAccount
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string Method { get; set; }
    public string? CountryCode { get; set; }
    public string? BankCode { get; set; }
    public string? BranchCode { get; set; }
    public byte[]? AccountNoEnc { get; set; }
    public string? AccountLast4 { get; set; }
    public byte[]? MpesaPhoneEnc { get; set; }
    public string? MpesaLast4 { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The leave the law gives in a country (reference data, ONB-08).</summary>
public sealed class StatutoryLeave
{
    public required string CountryCode { get; set; }
    public required string Key { get; set; }
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public decimal FullPayDays { get; set; }
    public decimal HalfPayDays { get; set; }
    public required string Counts { get; set; }
    public required string EarnMethod { get; set; }
    public decimal EarnAmount { get; set; }
    public int WaitingMonths { get; set; }
    public bool NeedsProof { get; set; }
    public bool Active { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A kind of leave in an organization (ONB-08); statutory ones (<see cref="LockedKey"/>) can't be deleted or renamed.</summary>
public sealed class LeaveType
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>working_days or calendar_days.</summary>
    public string Counts { get; set; } = "working_days";
    public bool Paid { get; set; } = true;
    public bool NeedsProof { get; set; }
    /// <summary>stop, or unpaid: what happens when it runs out.</summary>
    public string WhenUsedUp { get; set; } = "stop";
    public string? LockedKey { get; set; }
    public string? CountryCode { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A bundle of leave rules (ONB-08) for all companies or the ones in <see cref="Companies"/>.</summary>
public sealed class LeavePlan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    /// <summary>calendar (January to December) or anniversary (each person's start date).</summary>
    public string LeaveYear { get; set; } = "calendar";
    public decimal HoursPerDay { get; set; } = 8;
    public bool AllCompanies { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<LeavePlanCompany> Companies { get; set; } = [];
    public List<LeavePlanRule> Rules { get; set; } = [];
}

public sealed class LeavePlanCompany
{
    public Guid OrganizationId { get; set; }
    public Guid LeavePlanId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>How a plan gives one leave type: see database/schema/tenancy/leave_plan_rules.sql.</summary>
public sealed class LeavePlanRule
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid LeavePlanId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public required string EarnMethod { get; set; }
    public decimal? Amount { get; set; }
    public string Unit { get; set; } = "days";
    public decimal HalfPay { get; set; }
    public bool Prorate { get; set; } = true;
    public decimal CarryOverMax { get; set; }
    public int WaitingMonths { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A reminder to run payroll: <see cref="Amount"/> days (or hours, for daily pay) before each pay day.</summary>
public sealed class PayScheduleReminder
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PayScheduleId { get; set; }
    public int Amount { get; set; }
    /// <summary>days, or hours for daily pay.</summary>
    public required string Unit { get; set; }
    /// <summary>email or sms.</summary>
    public required string Channel { get; set; }
    /// <summary>An email address, or a Kenyan mobile number as +254….</summary>
    public required string Recipient { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A reminder sent for one pay date (claimed before queuing, so it is never sent twice).</summary>
public sealed class ReminderSend
{
    public Guid OrganizationId { get; set; }
    public Guid ReminderId { get; set; }
    public DateOnly PayDate { get; set; }
    public Guid? DeliveryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PayScheduleCompany
{
    public Guid OrganizationId { get; set; }
    public Guid PayScheduleId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// The org structure (ONB-05): divisions, departments, positions and units share this shape: a code
/// unique in their level, a name, and all companies or the ones in <see cref="Companies"/>.
/// Timestamps are set by the app (the read policy needs the company links, written after the row).
/// </summary>
public interface IStructureItem<TLink> : IHasCode where TLink : IStructureLink
{
    Guid Id { get; set; }
    Guid OrganizationId { get; set; }
    string Name { get; set; }
    bool AllCompanies { get; set; }
    string Status { get; set; }
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    List<TLink> Companies { get; }
}

/// <summary>Something with a code that is unique in its kind (PREFIX-01, PREFIX-02… when made up).</summary>
public interface IHasCode
{
    string Code { get; set; }
}

/// <summary>A company using a structure item that isn't for all companies.</summary>
public interface IStructureLink
{
    Guid OrganizationId { get; set; }
    Guid CompanyId { get; set; }
    DateTimeOffset CreatedAt { get; set; }
}

public sealed class Division : IStructureItem<DivisionCompany>
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool AllCompanies { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<DivisionCompany> Companies { get; set; } = [];
}

public sealed class DivisionCompany : IStructureLink
{
    public Guid OrganizationId { get; set; }
    public Guid DivisionId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Department : IStructureItem<DepartmentCompany>
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? DivisionId { get; set; }
    /// <summary>The department this one sits under (Kitchen under Operations); the tree has no loops.</summary>
    public Guid? ParentId { get; set; }
    public bool AllCompanies { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<DepartmentCompany> Companies { get; set; } = [];
}

public sealed class DepartmentCompany : IStructureLink
{
    public Guid OrganizationId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A job people are hired into. <see cref="Name"/> is its title; no department means shared across departments.</summary>
public sealed class Position : IStructureItem<PositionCompany>
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? DepartmentId { get; set; }
    public bool AllCompanies { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<PositionCompany> Companies { get; set; } = [];
}

public sealed class PositionCompany : IStructureLink
{
    public Guid OrganizationId { get; set; }
    public Guid PositionId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Unit : IStructureItem<UnitCompany>
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid DepartmentId { get; set; }
    public bool AllCompanies { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<UnitCompany> Companies { get; set; } = [];
}

public sealed class UnitCompany : IStructureLink
{
    public Guid OrganizationId { get; set; }
    public Guid UnitId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
