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
| ACC-8001 | You don't have permission to do this. Ask your organization's owner. | Customer | Check the person's role and company scope in the back office. |
| IMP-3001 | Row {row}: {column} is required. | Customer | Point them to the template's required columns. |
| IMP-3002 | Row {row}: "{value}" isn't one of your {column} options. | Customer | Check the option exists in setup, or fix the spelling. |
| PAY-2001 | {employee} has no KRA PIN, so PAYE can't be filed. | Customer | Add the KRA PIN on the employee's profile, then rerun. |
| PAY-2002 | {employee}'s pay is below the minimum wage for {location}. | Customer | Confirm pay and location; the run can't be approved below the minimum. |
| BILL-6001 | This company has more than {limit} active employees. Choose a plan to approve this pay run. | Customer | Explain the plans; check for complimentary access first. |
| PLAT-9001 | Something went wrong on our side. Quote reference {reference} to support. | Engineering | Look up the reference; escalate with the request id. |

Add new codes here and in `database/post-deploy/error_codes.sql` in the same change.
