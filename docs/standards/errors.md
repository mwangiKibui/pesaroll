# Error codes (PLAT-14)

Status: draft for review.

Every error a user sees has three parts:

1. **Code**: a module prefix and a number, such as `PAY-2001`. A code never changes meaning once released; retired codes are marked, never reused.
2. **Message**: plain words in English or Swahili that say what went wrong and what to do next.
3. **Reference**: a short id for this one occurrence (for example `7F3K92QD`), stored in `error_events` with the organization, user and request id.

Customers quote the code or reference; customer service looks it up in the back office and follows the code's playbook.

## Number ranges

| Prefix | Range | Module |
| --- | --- | --- |
| NTF | 0500–0999 | Notifications (email and SMS) |
| AUTH | 1000–1999 | Sign-in, accounts, two-step sign-in |
| PAY | 2000–2999 | Payroll and statutory rules |
| IMP | 3000–3499 | Bulk imports (one code per row problem) |
| ONB | 3500–3999 | Onboarding (organization and setup steps) |
| LV | 4000–4499 | Leave |
| HOL | 4500–4999 | Holiday management |
| WF | 5000–5499 | Workflows and approvals |
| TASK | 5500–5999 | Task management |
| BILL | 6000–6999 | Pricing and subscriptions |
| PRN | 7000–7499 | Partners and commissions |
| PERF | 7500–7999 | Performance management |
| ACC | 8000–8999 | Access and permissions |
| PLAT | 9000–9999 | Platform and unexpected errors |

## Catalogue

Each entry also lives in the `error_codes` table (loaded by a post-deploy script).

| Code | Message (English) | Fixable by | Playbook |
| --- | --- | --- | --- |
| AUTH-1001 | That email or phone number and password don't match. | Customer | Offer a password reset. Accounts lock after 5 failed tries. |
| AUTH-1002 | Your account is locked after too many attempts. Try again in 15 minutes or reset your password. | Customer | Confirm identity before unlocking manually. |
| AUTH-1003 | An account with this email or phone number already exists. Sign in instead. | Customer | Point them to sign-in or password reset; never confirm details over the phone without verification. |
| AUTH-1004 | Use a password of at least 6 characters. | Customer | Ask them to choose a longer password. |
| AUTH-1005 | Enter a valid email address or Kenyan phone number. | Customer | Phone numbers: 07…, 01… or +254…. Other countries come later. |
| AUTH-1006 | Too many attempts. Wait a minute and try again. | Customer | Rate limit per IP address; repeated hits may be an attack, check the logs. |
| AUTH-1007 | Sign in to continue. | Customer | No access token or refresh cookie was sent: the person is not signed in. |
| AUTH-1008 | Enter your first and last name and choose your role. | Customer | Sign-up form incomplete. |
| AUTH-1009 | You chose Other: type your role (up to 80 characters). | Customer | Sign-up role "Other" needs the person's own role. |
| AUTH-1010 | Your session has ended. Sign in again. | Customer | Access token expired, changed or for an ended session, or a refresh failed. Sessions end after 12 hours idle, 7 days total, at sign-out, or when an old refresh token is reused (possible theft: suggest a password change). |
| AUTH-1011 | This invitation link isn't valid. Check you opened the whole link, or ask for a new invitation. | Customer | No invitation has this link's token (mistyped or cut-off link, or an old link replaced by a resend). |
| AUTH-1012 | This invitation has expired. Ask the person who invited you to send it again. | Customer | Invitation links last 7 days. An administrator resends it from Team access. |
| AUTH-1013 | This invitation has already been accepted. Sign in instead. | Customer | Links work once. |
| AUTH-1014 | This invitation was cancelled. Ask the person who invited you if you still need access. | Customer | An administrator revoked it in Team access. |
| AUTH-1015 | This invitation is for a different email or phone number. | Customer | Only the account with the invitation's email or phone can accept it. |
| AUTH-1016 | Enter your first and last name. | Customer | Accepting an invitation for a new account needs a first and last name (up to 60 characters each). |
| ACC-8001 | You don't have permission to do this. Ask an administrator in your organization. | Customer | Check the person's role and company scope in the back office. |
| ACC-8002 | {name} is already in your organization. | Customer | The email or phone belongs to an active member; change their group in Team access instead. |
| ACC-8003 | {contact} already has an invitation waiting. Resend it instead. | Customer | One open invitation per email and per phone in an organization. |
| ACC-8004 | Some invitation details don't look right. Check the fields marked. | Customer | Name, an email or Kenyan phone number, a group, and all companies or at least one. |
| ACC-8005 | We couldn't find that invitation. It may have been deleted. | Customer | The invitation doesn't exist in this organization. |
| ACC-8006 | This invitation was already accepted or cancelled. | Customer | Only open or expired invitations can be resent or cancelled. |
| ACC-8007 | Someone must stay an administrator. Make another person an administrator first. | Customer | The organization needs at least one active member who can manage the team for every company. |
| ACC-8008 | We couldn't find that person in your organization. They may have been removed. | Customer | The membership doesn't exist in this organization or isn't active. |
| ACC-8009 | Choose their group and companies. | Customer | A group (Administrators, HR, Payroll, Approvers) and all companies or at least one. |
| ACC-8010 | Choose No access, View or Manage for each area. | Customer | Changing what a group can do: a known group (administrators, hr, payroll, approvers), known areas, and a level of none, view or manage. |
| ACC-8011 | Give the group a name nobody else uses (2 to 60 characters). | Customer | Adding or renaming an organization's own group: 2 to 60 characters, not a standard group's name, not used by another group. |
| ACC-8012 | Move everyone out of this group before you delete it. | Customer | Someone is still in the group or an open invitation names it; change their access or cancel the invitation first. |
| ACC-8013 | This group is only for some of your companies. Choose from those. | Customer | Inviting someone into, or giving someone, a group the organization limited to chosen companies, for another company. |
| ACC-8014 | Someone in this group still covers a company you took away. Change their access first. | Customer | Narrowing a group's companies while a member, or an open invitation, covers a company being removed. |
| IMP-3001 | Row {row}: {column} is required. | Customer | Point them to the template's required columns. |
| IMP-3002 | Row {row}: "{value}" isn't one of your {column} options. | Customer | Check the option exists in setup, or fix the spelling. |
| ONB-3501 | Enter your organization's name (2 to 120 characters). | Customer | Organization step: the name is the one their team knows them by; legal company names come next. |
| ONB-3502 | You already have an organization. | Customer | One onboarded organization per person for now. Accountants and bureaus with several clients go through the partner program. |
| ONB-3503 | You haven't set up your organization yet. | Customer | They signed up but skipped or left the organization step; the app takes them back to it. |
| ONB-3504 | Your account was set up by your organization, so you can't create a new one. Ask an administrator in your organization. | Customer | Only people who signed up themselves onboard an organization; invited people, staff and partners come in another way. |
| ONB-3505 | Enter the company's legal name (2 to 160 characters). | Customer | The legal name is empty, too short or too long. |
| ONB-3506 | Pesaroll runs payroll for companies in Kenya. Choose Kenya. | Customer | The country isn't one Pesaroll runs payroll in yet (countries.available). |
| ONB-3507 | Some details don't look right. Check the fields marked. | Customer | A company detail failed its check; `fields` says which (statutory number formats come from the country's settings). |
| ONB-3508 | You already have a company with this name. | Customer | Another active company in the organization has the same legal name (ignoring case and spaces). |
| ONB-3509 | We couldn't find that company. | Customer | The company doesn't exist, was deleted, or belongs to another organization. |
| ONB-3510 | Some paying account details don't look right. Check the fields marked. | Customer | A paying account detail failed its check; `fields` says which (bank and branch must be from the list; account number 6 to 20 digits; M-Pesa shortcode 5 to 7 digits). |
| ONB-3511 | We couldn't find that paying account. | Customer | The account doesn't exist, was already removed, or belongs to another company or organization. |
| ONB-3512 | Some location details don't look right. Check the fields marked. | Customer | A location detail failed its check; fields says which (name, code format, county, minimum-wage area, companies, main location). |
| ONB-3513 | Another location already has this code. Choose a different one or leave it empty. | Customer | Location codes are unique in the organization; leaving it empty makes one (MOM-01). |
| ONB-3514 | We couldn't find that location. | Customer | The location doesn't exist, was deleted, or belongs to another organization. |
| ONB-3515 | This is the main location of {companies}. Make another location their main location first. | Customer | Every company keeps exactly one main location: it can't be deleted or unticked until another location is made main. |
| ONB-3516 | We couldn't read this file. Use the template (.xlsx) or a .csv file with the same columns. | Customer | The upload isn't an Excel or CSV file, or has no Location name and County columns. |
| ONB-3517 | The file is too big. Upload up to {max} {things} and {MB} MB at a time. | Customer | Locations: 1,000 rows, 2 MB. Divisions, departments, positions and units: 5,000 rows, 5 MB. Split the file. |
| ONB-3518 | Row {n} needs fixing. Check them and try again. | Customer | The rows sent to import failed the checks (they are checked again on import); the review shows each problem in its cell. |
| ONB-3519 | Some details don't look right. Check the fields marked. | Customer | An org structure detail failed its check; fields says which (name or title, code, division, part of, department, companies). |
| ONB-3520 | Another one already has this code. Choose a different one or leave it empty. | Customer | Codes are unique per level (division, department, position, unit) in the organization. |
| ONB-3521 | We couldn't find that. It may have been deleted. | Customer | The division, department, position or unit doesn't exist, was deleted, or belongs to another organization. |
| ONB-3522 | {name} still has {what uses it}. Move or delete them first. | Customer | A division with departments, or a department with sub-departments, positions or units, can't be deleted. |
| ONB-3523 | A department can't sit under itself or one of its own departments. | Customer | Raised by the database when a department's parent would make a loop; the API shows it under Part of. |
| ONB-3524 | Some schedule details don't look right. Check the fields marked. | Customer | A payroll schedule detail failed its check; fields says which (name, frequency, payDayRule, payDay, cutoffDay, payWeekday, firstPayDate, cutoffDaysBefore, cutoffAppliesTo, companyIds, defaultFor, reminders[n].amount, .channel or .recipient). Monthly pay and cut-off days are 1 to 28; weekly and every-two-weeks pay days are Monday to Friday. |
| ONB-3525 | You already have a schedule with this name. | Customer | Schedule names are unique in the organization, ignoring case and spaces. |
| ONB-3526 | We couldn't find that schedule. It may have been deleted. | Customer | The payroll schedule doesn't exist, was deleted, or belongs to another organization. |
| ONB-3527 | This is the default schedule of {companies}. Make another schedule their default first. | Customer | Every company keeps one default schedule; tick Default for on another schedule, then delete or untick this one. |
| ONB-3528 | Some details don't look right. Check the fields marked. | Customer | An earning, deduction or employer cost failed its check; fields says which (name, kind, code, calcMethod, rate, defaultAmount, companyIds). |
| ONB-3529 | Another earning or deduction already has this code. Choose a different one or leave it empty. | Customer | Pay item codes are unique in the organization (stored upper case). |
| ONB-3530 | We couldn't find that earning or deduction. It may have been deleted. | Customer | The pay item doesn't exist, was deleted, or belongs to another organization. |
| ONB-3531 | This one is set by law (or is basic pay), so it can't be changed or deleted. | Customer | Basic pay and statutory items (PAYE, SHIF, NSSF, Housing Levy, NITA) are locked; the database refuses changes too. Rates come from the rule pack (PAY-01). |
| ONB-3532 | Some leave details don't look right. Check the fields marked. | Customer | A leave type or plan failed its check; fields says which (name, code, counts, whenUsedUp, leaveYear, hoursPerDay, companyIds, defaultFor, defaultForSchedules, rules, rules[n].amount/earnMethod/unit/halfPay/carryOverMax/waitingMonths). Plans can't give less than the law (statutory_leave): each way of earning is turned into a yearly total. |
| ONB-3533 | You already have {a leave plan with this name / a leave type with this code}. Choose a different one. | Customer | Plan names and type codes are unique in the organization. |
| ONB-3534 | We couldn't find that leave plan or leave type. It may have been deleted. | Customer | It doesn't exist, was deleted, or belongs to another organization. |
| ONB-3535 | This leave is set by law, so it can't be deleted or renamed. | Customer | Statutory leave types (annual, sick, maternity, paternity, pre-adoptive) are locked; the database refuses too. Proof and when-used-up can change. |
| ONB-3536 | This is the default leave plan of {companies}. Make another plan their default first. | Customer | Every company keeps one default leave plan. |
| ONB-3537 | This leave is in {plans}. Take it out of those plans first. | Customer | A leave type in a plan can't be deleted. |
| ONB-3538 | Some employee details don't look right. Check the fields marked. | Customer | An employee detail failed its check; fields says which (firstName, lastName, staffNo, phone, email, nationalId, kraPin, nssfNo, shaNo, hireDate, exitDate, dateOfBirth, job.*, pay.*, account.*). |
| ONB-3539 | We couldn't find that employee. They may have been deleted. | Customer | The employee doesn't exist, was deleted, or is in a company or organization this person can't see. |
| ONB-3540 | {name (staff number)} already has this {KRA PIN / ID number / NSSF number / SHA number / phone number / email address}. Each current employee has their own. | Customer | Two current employees in an organization can't share an ID number or KRA PIN (checked through an encrypted lookup), NSSF or SHA number, phone number or email address; this catches a re-import. A former employee's details can come back on a rehire. |
| ONB-3541 | {name} is used by {n} employees. Move them first. | Customer | A department, position, unit, location, payroll schedule or leave plan in employees' jobs (current or past) can't be deleted. |
| ONB-3542 | Another employee in this company has this staff number. Choose a different one or leave it empty. | Customer | Staff numbers are unique per company (stored upper case). |
| ONB-3543 | Only .xlsx or .csv files can be imported. {file} is a {.ext} file. | Customer | Uploads are checked by file name first: the template (.xlsx) or a .csv with its columns. |
| ONB-3544 | Invalid file: it doesn't match the {things} template. Missing columns: … Columns the template doesn't have: … Download the template and copy your rows into it. | Customer | The first row must have every template column and no others (older date headers without (DD/MM/YYYY) still match). Checked before any row is read. |
| ONB-3545 | This one is set by law (or is basic pay): it applies to everyone, so there's no one to choose. | Customer | Basic pay and statutory items apply to every employee; only the organization's own items are given to people. |
| ONB-3546 | This isn't used by {company}. Add the company to it first. | Customer | An earning or deduction goes only to people at companies it serves. |
| ONB-3547 | Some details of who gets it don't look right. Check the fields marked. | Customer | People from another company, people who left, someone twice, an amount on a percentage or each-payroll item, or an end before the start. |
| ONB-3548 | Some payee details don't look right. Check the fields marked. | Customer | Name, the way they're paid and that way's details: bank, branch and account number (checked per bank), a 5 to 7 digit paybill, or who cheques are payable to. |
| ONB-3549 | We couldn't find that payee. It may have been deleted. | Customer | The payee doesn't exist in this organization. |
| ONB-3550 | {name} is the payee of {n} earnings and deductions. Choose another payee for them first. | Customer | A payee in use can't be deleted. |
| ONB-3551 | Another payee already has this name. | Customer | Payee names are unique in an organization (ignoring case). |
| ONB-3552 | We couldn't find that payroll history import. It may have been deleted. | Customer | The payroll history batch doesn't exist, or belongs to a company this person can't see. |
| NTF-0501 | Enter a valid email address, or a Kenyan mobile number like 0712 345 678. | Customer | A message's recipient isn't a Kenyan mobile number (07…, 01…, +254…) or a plain email address. |
| NTF-0502 | We couldn't deliver an email to {recipient}. | Support | Postmark kept refusing it (5 attempts); notification_deliveries.error has its reason (inactive recipient, bounced, bad address). Check the address with the customer. |
| NTF-0503 | We couldn't deliver an SMS to {recipient}. | Support | Africa's Talking kept refusing it (5 attempts); notification_deliveries.error has its status (blacklisted, invalid number, low balance). Check the number, or the account balance. |
| PAY-2001 | {employee} has no KRA PIN, so PAYE can't be filed. | Customer | Add the KRA PIN on the employee's profile, then rerun. |
| PAY-2002 | {employee}'s pay is below the minimum wage for {location}. | Customer | Confirm pay and location; the run can't be approved below the minimum. |
| BILL-6001 | This company has more than {limit} active employees. Choose a plan to approve this pay run. | Customer | Explain the plans; check for complimentary access first. |
| PLAT-9002 | This request came from another website and was blocked. | Engineering | Check App:AllowedOrigins includes the app's address; otherwise a cross-site request was stopped. |
| PLAT-9001 | Something went wrong on our side. Quote reference {reference} to support. | Engineering | Look up the reference; escalate with the request id. |

Add new codes here and in `database/post-deploy/0001_error_codes.sql` in the same change.
