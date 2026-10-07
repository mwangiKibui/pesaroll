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
| AUTH | 1000–1999 | Sign-in, accounts, two-step sign-in |
| PAY | 2000–2999 | Payroll and statutory rules |
| IMP | 3000–3999 | Bulk imports (one code per row problem) |
| LV | 4000–4999 | Leave |
| WF | 5000–5999 | Workflows and approvals |
| BILL | 6000–6999 | Pricing and subscriptions |
| PRN | 7000–7999 | Partners and commissions |
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
| ACC-8001 | You don't have permission to do this. Ask your organization's owner. | Customer | Check the person's role and company scope in the back office. |
| IMP-3001 | Row {row}: {column} is required. | Customer | Point them to the template's required columns. |
| IMP-3002 | Row {row}: "{value}" isn't one of your {column} options. | Customer | Check the option exists in setup, or fix the spelling. |
| PAY-2001 | {employee} has no KRA PIN, so PAYE can't be filed. | Customer | Add the KRA PIN on the employee's profile, then rerun. |
| PAY-2002 | {employee}'s pay is below the minimum wage for {location}. | Customer | Confirm pay and location; the run can't be approved below the minimum. |
| BILL-6001 | This company has more than {limit} active employees. Choose a plan to approve this pay run. | Customer | Explain the plans; check for complimentary access first. |
| PLAT-9002 | This request came from another website and was blocked. | Engineering | Check App:AllowedOrigins includes the app's address; otherwise a cross-site request was stopped. |
| PLAT-9001 | Something went wrong on our side. Quote reference {reference} to support. | Engineering | Look up the reference; escalate with the request id. |

Add new codes here and in `database/post-deploy/0001_error_codes.sql` in the same change.
