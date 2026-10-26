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
    /// <summary>
    /// Its working day and month in hours (PAY-11), which turn a daily rate or a salary into an hourly
    /// one for overtime; null: the wage order's (8 and 225). Set through Payroll, with who and when.
    /// </summary>
    public decimal? HoursPerDay { get; set; }
    public decimal? HoursPerMonth { get; set; }
    public Guid? HoursSetBy { get; set; }
    public DateTimeOffset? HoursSetAt { get; set; }
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

/// <summary>A SACCO payees (and later employees) can be paid into; the back office maintains them (BO-11).</summary>
public sealed class Sacco
{
    public required string CountryCode { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string LegalName { get; set; }
    public bool Active { get; set; }
    public int SortOrder { get; set; }
}

public sealed class SaccoBranch
{
    public required string CountryCode { get; set; }
    public required string SaccoCode { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
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

/// <summary>Something the law takes from pay or adds to the employer's cost, per country (reference data).</summary>
public sealed class StatutoryItem
{
    public required string CountryCode { get; set; }
    public required string Key { get; set; }
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    /// <summary>deduction or employer_cost.</summary>
    public required string Kind { get; set; }
    public bool ReducesTaxablePay { get; set; }
    public bool Active { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// An earning, deduction or employer cost (ONB-07; "Earnings and deductions" on screen), for all
/// companies or the ones in <see cref="Companies"/>. Locked ones (<see cref="LockedKey"/>: basic or a
/// statutory item) are made with the first company and never changed by the app.
/// </summary>
/// <summary>Who a deduction's or employer cost's money is paid to: by bank, M-Pesa paybill, cheque or cash.</summary>
public sealed class Payee
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    /// <summary>bank, mpesa_paybill, cheque or cash.</summary>
    public required string Method { get; set; }
    /// <summary>For a transfer: bank or sacco.</summary>
    public string? Institution { get; set; }
    public string? SaccoCode { get; set; }
    public string? SaccoBranchCode { get; set; }
    public string? CountryCode { get; set; }
    public string? BankCode { get; set; }
    public string? BranchCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? Paybill { get; set; }
    public string? PaybillAccount { get; set; }
    public string? ChequePayableTo { get; set; }
    public string? OurReference { get; set; }
    /// <summary>How they identify each person: typed, or the person's profile (national_id, kra_pin, staff_no, phone, bank_account, mpesa_number).</summary>
    public string ReferenceSource { get; set; } = "typed";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Who gets an earning or deduction at a company: everyone (no employee) or one person, from a date.</summary>
public sealed class EmployeePayItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PayItemId { get; set; }
    public Guid? EmployeeId { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Rate { get; set; }
    /// <summary>Items paid by the hour or day: their own multiplier of their hourly or daily rate (PAY-11).</summary>
    public decimal? Multiplier { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    /// <summary>Their member or account number with the item's payee.</summary>
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PayItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>earning, deduction or employer_cost.</summary>
    public required string Kind { get; set; }
    public bool Taxable { get; set; }
    public bool BeforeTax { get; set; }
    /// <summary>fixed, percent_of_basic, each_run, by_law or per_unit (PAY-11).</summary>
    public required string CalcMethod { get; set; }
    public decimal? DefaultAmount { get; set; }
    public decimal? Rate { get; set; }
    /// <summary>per_unit items: hour or day.</summary>
    public string? Unit { get; set; }
    /// <summary>per_unit items: times the person's own hourly or daily rate (or <see cref="DefaultAmount"/> a unit).</summary>
    public decimal? Multiplier { get; set; }
    public string? LockedKey { get; set; }
    public string? CountryCode { get; set; }
    public bool AllCompanies { get; set; }
    public string Status { get; set; } = "active";
    /// <summary>Who the money goes to (deductions and employer costs).</summary>
    public Guid? PayeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<PayItemCompany> Companies { get; set; } = [];
}

public sealed class PayItemCompany
{
    public Guid OrganizationId { get; set; }
    public Guid PayItemId { get; set; }
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
    public int? CutoffDay { get; set; }
    /// <summary>Weekly and fortnightly: the weekday paid, Monday 1 to Friday 5.</summary>
    public int? PayWeekday { get; set; }
    /// <summary>Fortnightly: the first pay date, from which every other week counts.</summary>
    public DateOnly? FirstPayDate { get; set; }
    /// <summary>Weekly and fortnightly: how many days before the pay day changes close.</summary>
    public int? CutoffDaysBefore { get; set; }
    /// <summary>Daily: when in the day people are paid (reminders count hours back from it).</summary>
    public TimeOnly? PayTime { get; set; }
    /// <summary>Whether changes after a cut-off wait for the next payroll: off until chosen; daily pay has none.</summary>
    public bool UsesCutoff { get; set; }
    /// <summary>What the cut-off covers: <see cref="PaySchedulesEndpoints.CutoffItems"/> keys or the organization's own items.</summary>
    public List<string> CutoffAppliesTo { get; set; } = [];
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
    /// <summary>Daily or hourly rates: the days or hours usually worked in a pay period (PAY-11).</summary>
    public decimal? UsualQuantity { get; set; }
    /// <summary>Who made the change and why (PAY-04): a reason is required for a change from an earlier date.</summary>
    public Guid? ChangedBy { get; set; }
    public string? Reason { get; set; }
    /// <summary>A wrong rate replaced by a change on the same date: kept, never paid (hidden by a query filter).</summary>
    public Guid? ReplacedBy { get; set; }
    public DateTimeOffset? ReplacedAt { get; set; }
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

/// <summary>One upload of payroll history from the previous system (ONB-10): deleting it takes its lines.</summary>
public sealed class PayrollHistoryBatch
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public required string FileName { get; set; }
    public int RowCount { get; set; }
    public DateOnly FirstPeriod { get; set; }
    public DateOnly LastPeriod { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal PayeTotal { get; set; }
    public Guid ImportedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A person's month in the previous system: the figures a P9A tax card and year-to-date totals need.</summary>
public sealed class PayrollHistoryLine
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BatchId { get; set; }
    public Guid EmployeeId { get; set; }
    /// <summary>The first of the month.</summary>
    public DateOnly Period { get; set; }
    public DateOnly? PayDate { get; set; }
    public decimal BasicPay { get; set; }
    public decimal? CashAllowances { get; set; }
    public decimal? NonCashBenefits { get; set; }
    public decimal? ValueOfQuarters { get; set; }
    public decimal GrossPay { get; set; }
    public decimal? Pension { get; set; }
    public decimal? NssfTier1 { get; set; }
    public decimal? NssfTier2 { get; set; }
    public decimal NssfEmployee { get; set; }
    public decimal? NssfEmployer { get; set; }
    public decimal Shif { get; set; }
    public decimal? HousingLevyEmployee { get; set; }
    public decimal? HousingLevyEmployer { get; set; }
    public decimal? PostRetirementMedical { get; set; }
    public decimal? MortgageInterest { get; set; }
    public decimal? TaxablePay { get; set; }
    public decimal? PersonalRelief { get; set; }
    public decimal? InsuranceRelief { get; set; }
    public decimal Paye { get; set; }
    public decimal? OtherDeductions { get; set; }
    public decimal? NetPay { get; set; }
    public decimal? Nita { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An invitation to join the organization "as HR, for these companies" (ACC-03).</summary>
public sealed class Invitation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>One or more of administrators, hr, payroll, approvers: the starting groups whose roles they get.</summary>
    public required string[] GroupKeys { get; set; }
    public bool AllCompanies { get; set; }
    public Guid InvitedBy { get; set; }
    /// <summary>SHA-256 of the link's token; the token itself is only in the message.</summary>
    public required byte[] TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public int SendCount { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<InvitationCompany> Companies { get; } = [];
}

public sealed class InvitationCompany
{
    public Guid OrganizationId { get; set; }
    public Guid InvitationId { get; set; }
    public Guid CompanyId { get; set; }
}
