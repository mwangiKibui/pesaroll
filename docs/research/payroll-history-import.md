# Payroll history import (ONB-10): research

Status: research for review. Prepared 11 October 2026 for issue #54 ("Bring in past pay totals for reference and year-to-date figures").

**Question:** Customers switching to Pesaroll come from Kenyan payroll products, payroll bureaus or Excel. What payroll history can they export, which data points do we need, and how should our import work?

**Scope:** Kenya only. Competitor background is in `project-docs/hr-payroll-market-research-ecsa.md` (§3, §8.1, §17) and isn't repeated here.

**Confidence:** Vendor help-centre articles and KRA forms are cited where we found them. Anything we couldn't confirm from a public source is marked **unconfirmed**. Most vendors don't publish their export column headers, so ask early customers for sample exports before we build any preset.

---

## Summary

### Key findings

1. **Every Kenyan employer already has a common export: its KRA filings.** Since July 2025 every employer files a monthly simplified P10 PAYE return as Excel or CSV. It also issues a P9A tax deduction card per employee each year, with 12 monthly rows and fixed lettered columns A to O. Because these layouts are set by KRA, they work as a provider-neutral fallback whatever system the customer used. What they lack is net pay and NSSF. Those come from the payroll register, called the "muster roll" or "master roll" locally.
2. **The main products can all export a register and a P9 to Excel, but in different ways:**
   - **Workpay:** reports such as Muster Roll, PAYE, SHIF, NSSF and Housing Levy, through a "Queue for export". A month's payroll must be approved first.
   - **SeamlessHR:** a "Master Report" as an Excel file, plus YTD, P10 and P9 reports, from inside each payroll run.
   - **Sage VIP:** Kenya reports and Payroll Listing History reports, exported to Excel or CSV.
   - **Odoo:** Payslip Lines.
   - **PaySpace (Deel Local Payroll):** has a REST API. Its Kenya report names are unconfirmed.
   - **Workpay** says it has a REST API covering payroll runs, but documentation is only given during implementation.

   No vendor offers a documented "export everything for migration" file, and PDF payslips are the only per-employee artefact some customers will have.
3. **Year-to-date (YTD) figures matter for the P9, not for next month's PAYE.** As far as we found, Kenyan PAYE is worked out monthly on monthly bands, with no cumulative true-up. The tax year is January to December. So the history we must have is every month of the **current tax year** before go-live, with enough detail to fill a correct P9A in January. Older months are only for reference. That Kenyan PAYE is monthly, not cumulative, is our reading of KRA's monthly bands and the P9A layout; we found no source that says so outright, so a payroll accountant should confirm it.
4. **The rules changed a lot between 2023 and 2026, so old periods can't be checked against today's rules:**
   - new PAYE bands (July 2023)
   - the Housing Levy starting, stopping and restarting (2023–24)
   - NHIF replaced by SHIF (1 Oct 2024)
   - SHIF and the Housing Levy becoming pre-tax deductions, with higher pension and mortgage caps (Dec 2024)
   - NSSF phases every February (2023–26)
   - a new P10 template (July 2025)
   - a Finance Bill 2026 proposing new PAYE bands from 1 January 2027 (passed by the National Assembly; assent unconfirmed)

   Any recomputation check must use the rules in force for each row's period.
5. **Data quality, not file format, is the hard part.** Excel and bureau customers will have merged cells, a sheet per month, and NHIF and SHIF in the same column. They'll often be missing KRA PINs, employer contributions and separate NSSF tiers. Net pay checks have to leave out non-cash benefits, which are taxed but never paid in cash.

### Recommendations

1. **One canonical template, one row per employee per month.** Our existing import flow applies unchanged: download the .xlsx template, upload, review every row in an editable table, then import all or nothing, per company. Ten columns are required (see §5.1). Everything else is optional and recomputed or left blank.
2. **Smart header matching before presets.** A synonym table (§4.3) maps common headers, such as "Basic Salary", "NHIF/SHIF" or "H/Levy", onto our fields. That covers Excel, bureau files and most vendor registers without per-vendor code.
3. **Ship two KRA-shaped presets first, then vendor presets on real samples:**
   1. KRA P10 simplified return (CSV or Excel)
   2. KRA P9A card (Excel)
   3. Workpay Muster Roll
   4. SeamlessHR Master Report
   5. Sage VIP Payroll Listing History

   PaySpace, Odoo and the others come later, when a customer brings a file.
4. **Required range:** January of the current tax year up to the month before Pesaroll's first pay run. **Optional range:** up to 24 more months for reference. Imported history is stored as **imported, read-only and never re-filed**. It is kept apart from runs that Pesaroll computes.
5. **Checks warn rather than block,** except for identity, period and arithmetic errors (§4.5). The PAYE recomputation is a warning, with a tolerance, that uses the rules in force for the period.
6. **Defer:**
   - API connectors
   - reading PDF payslips
   - per-run (weekly or daily) history
   - line-by-line allowances
   - loan, leave and arrears balances (those are other steps)
7. **Treat uploads as high-risk personal data under the Data Protection Act 2019:**
   - only payroll admins can upload
   - the original file is deleted soon after the import
   - no bank account numbers are imported
   - every import is audit-logged
   - we confirm our basis for transferring data across borders (§5.5)

---

## 1. Providers used in Kenya

"In use" means we found Kenyan customers, Kenyan statutory features or Kenya-specific help content. The ranking is our judgement for **import priority**, not market share; no reliable market-share data exists.

| Provider | In use in Kenya? | Who uses it | Import priority |
| --- | --- | --- | --- |
| **Excel / Google Sheets** (own workbook, often with an accountant) | Yes, the largest group in our wedge (see the market research, §3.1) | Micro and small firms, restaurants, casual-heavy | **1** |
| **Payroll bureaus / accounting firms**: Grant Thornton, PwC, KPMG (APOS team) and many small audit firms | Yes. Grant Thornton's 2026 brochure covers PAYE, NSSF, SHIF, the Housing Levy and NITA | SMEs to corporates that outsource | **1** (they deliver Excel registers, P10s and P9 PDFs) |
| **Workpay** | Yes (Kenya-born incumbent) | SMEs, NGOs, startups | **2** |
| **SeamlessHR** | Yes (via partners; Kenya help content for P9 and P10) | Medium and large companies | **3** |
| **Sage VIP (Classic / Premier)**, **Sage Pastel Payroll**, **Sage Payroll Professional** | Yes. Sage publishes Kenya P9 and P10 patches and Housing Levy notices for these products | Corporates, long-standing SMEs | **3** |
| **Sage 300 People** | Yes. Kenya P9 statutory updates for the 2025 tax year | Large corporates | 4 |
| **PaySpace / Deel Local Payroll** | Yes (Kenya page; SHIF and NSSF notices in its help centre) | Multi-country mid-market | 4 |
| **Odoo** (`l10n_ke_hr_payroll` plus partner apps) | Yes (official Kenya localisation) | SMEs already on Odoo | 5 |
| **FaidiHR, KaziQuest, Wagemaster, Crystal HR, ZuriHR, Aren, Lenvica HRMS** | Yes (Kenyan local tools; Wagemaster sells a desktop lifetime licence) | Kenyan SMEs | 5 (use the generic mapping) |
| **ERPNext** Kenya payroll apps | Yes (third-party listings) | A few SMEs on ERPNext | 6 |
| **QuickBooks** | For accounting, yes. **No native Kenyan payroll found.** "QuickBooks Payroll (Kenya)" in competitor blogs is unconfirmed; payroll usually happens in Excel or another tool, then a journal is posted | SMEs | Treat as Excel |
| **Zoho People / Zoho Payroll** | Zoho People yes; **Zoho Payroll doesn't cover Kenya** (India, Saudi Arabia and the UAE only). A marketplace add-on, "Payroll Master KE", exists but is unverified | Zoho-ecosystem SMEs | Treat as Excel |
| **BambooHR** | HR only; no Kenyan payroll. Workpay lists a BambooHR connector | Startups, NGOs | Not a payroll source |
| **Banks** (e.g. NCBA "Salo Xpress", launched June 2026 with Mwalimu Sacco) | Bank products we found handle **salary payment** and reconciliation, not PAYE or statutory computation | Institutions, SACCOs | Not a payroll source; bank files give net pay only |
| **"PayKenya"**, **"KenPayroll" / Bitrix-style tools** | **Unconfirmed.** We found no product by these names | — | — |

Added to the list: Sage Pastel Payroll and Payroll Professional, ERPNext, Lenvica and Wagemaster (desktop). We also confirmed that Zoho and QuickBooks don't run Kenyan payroll natively.

---

## 2. What each provider can export

### 2.1 Capability matrix

| Provider | Payroll register (all employees, one month) | P9A (annual, per employee) | P10 / statutory returns | Payslips | API | Known limits |
| --- | --- | --- | --- | --- | --- | --- |
| **Workpay** | **Muster Roll Report**, plus PAYE, NSSF, SHIF, Housing Levy, Deductions, Allowances, Gross Salary, Net Pay Bank or Mobile and HELB reports | **P9 report**: Reports → P9 card → year → Export | PAYE, SHIF, NSSF and Housing Levy reports (per month) | In app; bulk export unconfirmed | REST API "covering payroll runs, employee records, statutory submissions"; docs and sandbox "provided during implementation" | The month's payroll **must be approved** before any report. Exports are queued (wait for "completed", then Download). The help article says "select format" without naming formats; Excel is assumed but **unconfirmed**. API access is likely partner- or plan-gated (unconfirmed) |
| **SeamlessHR** | **Master Report**: Run Payroll → view run → Preview Report → Master report → "Master Report 1" (an **Excel** file with "all the payroll reports"). Custom reports under Payroll Setting → Report → Add Report | Run Payroll → month → view breakdown → Download → "Download all P9 Report" (**zip**); per employee **Excel or PDF** | **P10 report**: Preview Report → Other Report → P10 Report. P10 updated to KRA v30.0.1 (late 2025: PWD and exemption-certificate columns, separate voluntary pension) | Bulk PDF zip per closed period | Unconfirmed | Reports live **inside each payroll run**, so 12 months means 12 downloads. The **YTD report** (Other report → YTD report) is useful for opening balances. Enterprise clients may use different menus |
| **Sage VIP / Pastel / Payroll Professional** | **Payroll Listing History** and **History Reports** export to Excel; the report selection screen offers CSV | Main Menu → Reports → Kenya Reports → **P9A/P9A HOSP/P9B Tax Ded Cards** | Main Menu → Reports → Kenya Reports → **New iTax PAYE Online – New P10** | Printed or PDF | No | On-premise: the customer or their reseller runs the reports. "Report Setup" linking must be right, or the SHIF and AHL columns come out empty. Community threads report Excel-export glitches on history reports |
| **Sage 300 People** | Reports → Financial Analysis → Payroll Analysis → "Employee Detail" → Preview → export to Excel (community answer; a pattern, not confirmed for every report) | **Year-specific menu items**: "P9AP9B_2024 – Kenya P9A P9B Tax Deduction Cards" for 2024. Printing the 2025 card for earlier years "can result in errors and incorrect values" | Kenya tax file `KEN-genericTax.xml`; P10 path unconfirmed | Yes | No public API found | Needs version ≥ 25.1.3.0 for the 2025 P9. Enterprise installs are usually run by a consultant |
| **PaySpace / Deel Local Payroll** | Report names for Kenya **unconfirmed** | Marketing says "Annual P9 employee tax certificates generated automatically" | P10 (marketing pages); SHIF and NSSF notices in its help centre | Employee self-service | **Yes**: REST API v2.0 (`api.payspace.com`, OAuth client credentials from the account manager); unified-API vendors list "Payslip" as a supported model | PaySpace has its own **YTD take-on** (Bulk Actions template) for mid-year moves *into* PaySpace. That confirms the pattern but doesn't help exports |
| **Odoo** (`l10n_ke_hr_payroll`) | **Master payroll**: Payroll → Reporting → **Payslip Lines**, filtered by month, exported with Odoo's standard list export (XLSX or CSV) | Official docs describe the P9; generation needs a partner app (e.g. `p9_report`, paid, PDF) | NSSF and SHIF reports (need ID, NSSF and SHIF numbers on the employee). P10 via a partner app (`l10n_ke_p10_paye_return`, paid: CSV or styled Excel, Sheet A and B; also P9A cards A–M, zip) | PDF | Odoo's XML-RPC/JSON-RPC API exposes payslips and lines | Salary rule codes differ per database, so mapping always needs a review step |
| **FaidiHR** | Reports listed: P9A, P12, Net Pay, NHIF, NSSF, PAYE | Yes (P9A) | Yes; guides for PAYE, SHIF and NSSF filing | Yes | Unconfirmed | Export formats **unconfirmed** |
| **KaziQuest, Wagemaster, Crystal HR, ZuriHR, Aren, Lenvica** | Some form of payroll summary is assumed; **formats and menus unconfirmed** | Claimed P9 support (ZuriHR: "year-end certificates") | "KRA-ready reports" (ZuriHR) | Yes | Unconfirmed | Desktop tools (Wagemaster) export from a local machine |
| **Payroll bureau** | Monthly Excel register, sent by email | P9 PDFs at year end | Copies of P10 and the SHA and NSSF files they filed | PDF | No | Layout varies by firm; formulas and merged headers are common |
| **Excel (in-house)** | One sheet per month, or one long sheet | Rarely; many small employers never issue P9s (unconfirmed, anecdotal) | The P10 CSV they uploaded to iTax, if they kept it | Word or PDF | No | Often missing employer contributions and KRA PINs; old months still say "NHIF" |

### 2.2 Exports every Kenyan employer has, whatever the provider

These come from government systems, so their layout is fixed. That makes them the most reliable import sources.

| Artefact | Where the employer gets it | Grain | What's in it | What's missing |
| --- | --- | --- | --- | --- |
| **Simplified P10 PAYE return** (from the July 2025 period) | Their own filed Excel or CSV; acknowledgement on iTax | Employee × month | Sheet A: employer PIN, return type, period, gratuity, lump sum and arrears flags. Sheet B: employee PIN, name, resident status, PWD, **Total Cash Pay** (basic and all cash allowances collapsed into one figure), car benefit, meals, non-cash benefits, housing, other benefits, **SHIF**, AHL, pension, PAYE and reliefs (exact column list unconfirmed: the KRA guide and sample CSV couldn't be fully read) | Net pay, NSSF split, employer contributions, basic vs allowances |
| **Old P10 (macro workbook)**, before July 2025 | Their own saved workbook or CSV | Employee × month | Sheet B employee details (basic, allowances, benefits, deductions, PAYE) | Same as above |
| **P9A tax deduction card** (KRA 2025 template) | From their payroll system or bureau | Employee × 12 months | Columns A–O (see §3.2) | Net pay, NSSF split, employer contributions, ID number |
| **NSSF return (SF24 source file)** | NSSF self-service, now on eCitizen | Employee × month | NSSF number, ID, KRA PIN, **gross salary** (the new format works from gross, not amounts), contribution type (one row per type) | Everything else |
| **SHA return** | SHA employer portal (CSV template after sign-in) | Employee × month | National ID, name, gross pay, 2.75% contribution (exact headers unconfirmed) | Everything else |
| **Bank / M-Pesa bulk file** | Bank portal | Employee × run | Net pay, account or phone | Everything else (and bank details we shouldn't import) |

---

## 3. Data points a history import needs

### 3.1 Per employee, per pay period

The availability column says how often each data point appears in a typical register, P10 or Excel sheet. It's our assessment, not a measured figure.

| Field | Why we need it | Availability |
| --- | --- | --- |
| Employee number (payroll number) | Match to the employee in Pesaroll | Always in registers; not on P10 or P9 |
| Full name | Human check during review | Always |
| **KRA PIN** | P9A, P10 and match key; format `A` + 9 digits + letter (individuals) | Always on P10 and P9; **often missing in Excel** |
| National ID / passport number | SHA and NSSF match; second match key | Usually in registers; not on P10 or P9 |
| **Pay period** (year and month) | P9 row; P10 month | Always |
| Pay date | Reports; which tax period a payment falls in | Often missing; default to the last day of the period |
| **Basic pay** | P9A column A; pension cap E1 (30% of A) | Always in registers. **Not separate on the simplified P10** (collapsed into Total Cash Pay) |
| Cash allowances (total, optionally house, transport, overtime, leave pay and others) | Gross; reference | Usually; names vary |
| Non-cash benefits (car, meals, other) | P9A column B; taxable but not paid in cash | Rare outside proper payroll software |
| Value of quarters (housing benefit) | P9A column C | Rare |
| **Gross pay** | P9A column D; SHIF and AHL base | Always |
| Defined contribution pension (employee) | P9A column E; capped deduction | Sometimes; often merged into "Other deductions" |
| **NSSF employee**: Tier I, Tier II (or one total) | Deduction (counts toward pension in E); NSSF return | Total always; **tiers often missing** |
| NSSF employer: Tier I, Tier II | Employer cost; NSSF return | Often missing in Excel |
| **SHIF** (or **NHIF** before Oct 2024) | P9A column G (pre-tax from Dec 2024); SHA return | Always (column name changes over time) |
| **Housing Levy, employee** | P9A column F (pre-tax from Dec 2024); P10 | Always from 2024 |
| Housing Levy, employer | Employer cost; P10 | Often missing |
| Post-retirement medical fund (PRMF) | P9A column H (from Dec 2024) | Rare |
| Owner-occupied (mortgage) interest | P9A column I | Rare |
| **Taxable (chargeable) pay** | P9A column K | Usually in registers; on P10 and P9 |
| Tax charged (before reliefs) | P9A column L | Sometimes |
| Personal relief | P9A column M (KES 2,400 per month) | Usually; can be derived |
| Insurance relief | P9A column N (15% of premiums, max KES 5,000 per month) | Sometimes |
| **PAYE** | P9A column O; P10 | Always |
| NITA (employer) | KES 50 per employee per month; P10 | Rarely per employee; derivable |
| Other deductions (HELB, SACCO, loans, union dues, advances) | Net pay check; reference | Usually as a total or several columns |
| **Net pay** | Reconciliation; employee history | Always in registers; **not on P10 or P9** |

### 3.2 Year-to-date: the P9A card

The KRA P9A 2025 template (Appendix 2A) has these columns. Each holds 12 monthly rows and a TOTAL:

| Col | KRA heading | Our field |
| --- | --- | --- |
| A | Basic Salary | `basic_pay` |
| B | Benefits – Non-Cash | `non_cash_benefits` |
| C | Value of Quarters | `value_of_quarters` |
| D | Total Gross Pay | `gross_pay_taxable` (A+B+C plus cash allowances) |
| E1 / E2 / E3 | Defined Contribution Retirement Scheme: 30% of A / Actual / Fixed 30,000 p.m. | `pension_employee` (we derive E1 and E3; E = the lowest of the three) |
| F | Affordable Housing Levy (AHL) | `ahl_employee` |
| G | Social Health Insurance Fund (SHIF) | `shif_employee` |
| H | Post Retirement Medical Fund (PRMF) | `prmf` |
| I | Owner-Occupied Interest | `mortgage_interest` |
| J | Total Deductions (lower of E + F + G + H + I) | derived |
| K | Chargeable Pay (D − J) | `taxable_pay` |
| L | Tax Charged | `tax_charged` |
| M | Personal Relief | `personal_relief` |
| N | Insurance Relief | `insurance_relief` |
| O | PAYE Tax (L − M − N) | `paye` |

The template's notes also hold dated caps that our checks need:
- pension: KES 20,000 per month before December 2024, KES 30,000 from then on
- mortgage interest: KES 25,000 before December 2024, KES 30,000 from then on
- PRMF: up to KES 15,000 from December 2024
- SHIF and AHL: deductible from December 2024
- personal relief: KES 2,400 per month (KES 28,800 a year)
- insurance relief: 15%, up to KES 5,000 per month (KES 60,000 a year)

**What YTD is for.** Kenya's tax year is January to December. PAYE appears to be worked out monthly, so mid-year switchers don't need YTD to get next month's PAYE right (this needs confirming; see the summary). They do need the January to go-live months so that we can:

1. issue a full **P9A** to every employee by the end of February, including leavers who were paid that year;
2. reconcile the year's P10s against payroll;
3. show employees their payslip history and year totals;
4. run reports (labour cost, statutory totals) that span the switch.

A customer who only has year totals (for example from SeamlessHR's YTD report) can't produce the monthly P9A rows. Accept it as a fallback, flagged as "totals only".

### 3.3 Statutory filings that tie history to returns

| Filing | Due | Ties to history by | Import? |
| --- | --- | --- | --- |
| P10 (PAYE, AHL, NITA) on iTax | 9th of the next month | Employee PIN × month: PAYE, gross, AHL | **Yes, as a source** (preset). Also store the acknowledgement number per month (optional) |
| SHA (SHIF) return; NHIF return before Oct 2024 | 9th | ID × month: gross, SHIF | Optional reference only |
| NSSF return (SF24) | 9th | NSSF number × month: gross, tier amounts | Optional reference only |
| P9A | Employees, by the end of February | PIN × month | **Yes, as a source** (preset) |
| Payment slips and receipts | — | Month totals | Defer (it's the filing tracker's job) |

### 3.4 Rule changes that make older periods different

A recomputation check must look up the rules in force **for the row's period**. Our rules service already keeps dated rates.

| Effective | Change | Effect on history |
| --- | --- | --- |
| Before Jul 2023 | PAYE bands 10% (≤24,000), 25% (≤32,333), 30% above; personal relief 2,400 | Three bands only |
| **1 Jul 2023** (Finance Act 2023) | Adds 32.5% (500,000–800,000) and 35% (>800,000) | KRA's circular cites several dates in the Act. Reports suggest many payrolls applied it from **August 2023**, possibly backdated to July (unconfirmed). Allow a tolerance for July and August 2023 |
| **Jul 2023** | Housing Levy 1.5% + 1.5% of gross, with **AHL relief** (15%, max KES 9,000 per month per Sage notes) | AHL column appears; relief lowers PAYE |
| 28 Nov 2023 → 26 Jan 2024 | High Court rules the levy unconstitutional. Stay to 10 Jan 2024, then Court of Appeal confirms; deductions stop | **Expect zero AHL for some or all of late Jan to early Mar 2024.** Don't flag it as missing |
| **19 Mar 2024** | Affordable Housing Act 2024 assented; deductions resume (March 2024 payroll onward) | AHL returns |
| **1 Feb 2023 / 2024 / 2025 / 2026** | NSSF Act 2013 phases at 6% + 6%. LEL/UEL: Y1 6,000/18,000; Y2 7,000/36,000; Y3 8,000/72,000; **Y4 9,000/108,000** (max KES 6,480 each side). Y2's UEL is disputed in one press report (29,000) | Tier amounts change each February. Some systems kept the old KES 200 flat rate (one source) |
| **Up to 30 Sep 2024** | NHIF graduated flat amounts (KES 150–1,700) with insurance relief (date introduced unconfirmed) | Old rows say "NHIF" |
| **1 Oct 2024** | **SHIF** 2.75% of gross, no cap (minimum KES 300) replaces NHIF | Column renamed. Whether SHIF gets insurance relief for **Oct–Nov 2024** is unconfirmed (KRA notice said no; later guidance conflicts) |
| **27 Dec 2024** (Tax Laws (Amendment) Act 2024; applies to December 2024 payroll) | SHIF and AHL employee shares become **deductions before PAYE**; **AHL relief removed**; pension cap 20,000 → 30,000; mortgage 25,000 → 30,000; PRMF deductible up to 15,000 (PRMF relief removed); non-cash benefit and meals thresholds rise to KES 5,000 per month | Taxable pay drops from December 2024. The P9A layout changed (E1–E3, new F, G and H columns) |
| **Jul 2025** | Simplified P10 (Excel or CSV) mandatory; allowances collapsed into "Total Cash Pay" | P10 files before and after July 2025 have different layouts |
| Late 2025 | P10 v30.0.1: PWD column, exemption-certificate number, voluntary pension separated (per SeamlessHR) | Minor layout change |
| **1 Jan 2027 (proposed)** | Finance Bill 2026 bands, as reported: 10% to 360,000 a year, then 17.5%, 25%, 27.5%, and 30% above about 9.6m. Passed by the National Assembly on 18 June 2026; **assent and final text unconfirmed** | Not history yet. Our rules service needs it before January 2027 |

---

## 4. Practical import design

### 4.1 Always there vs often missing

- **Almost always there:**
  - name
  - period
  - basic or gross pay
  - PAYE
  - NSSF (total)
  - NHIF or SHIF
  - Housing Levy (2024 on)
  - net pay (registers only)
- **Usually there:**
  - KRA PIN
  - ID number
  - taxable pay
  - personal relief
  - other deductions (as a total)
- **Often missing:**
  - NSSF tier split
  - **employer** NSSF, AHL and NITA
  - insurance relief
  - non-cash benefits
  - pension and PRMF
  - mortgage interest
  - pay date
- **Design consequence:** require only what a P9A row and identity need. Derive what's derivable (personal relief, NITA, NSSF tiers from gross and the phase table, employer AHL = employee AHL). Mark derived values as derived in the review.

### 4.2 Granularity

- **Store monthly totals per employee**, which is what the P9A and P10 use.
- If a source has several runs in a month (weekly casuals, a bonus run, arrears), **sum them into the month** at import and keep a "number of runs" note.
- Per-run history is deferred. It only matters if we later show weekly payslips from before the switch.
- Arrears and lump sums: the P10 has flags for them. Keep an optional `arrears_note` text column rather than modelling arrears allocation.

### 4.3 Header mapping (common headers → canonical fields)

Matching ignores case, spaces, dots and slashes. The synonyms come from KRA form headings and common Kenyan payroll wording. **Vendor-specific headers are unconfirmed until we see real exports.** The reviewer can always remap a column.

| Canonical field | Common headers seen or expected |
| --- | --- |
| `employee_number` | Staff No, Emp No, Employee No, Payroll No, PF No, Employee Code, Emp Code, Staff ID |
| `employee_name` | Name, Employee Name, Full Name, Employee's Main Name + Other Names (P9A), Surname + Other Names |
| `kra_pin` | KRA PIN, PIN, PIN No, Employee's PIN, Tax PIN, Personal Identification Number |
| `national_id` | ID No, ID Number, National ID, ID/Passport, Identification No (Odoo) |
| `period` | Month, Period, Pay Period, Payroll Month, Month/Year (P9A rows are month names; the year comes from the card) |
| `pay_date` | Pay Date, Payment Date, Date Paid |
| `basic_pay` | Basic, Basic Pay, Basic Salary, Basic Salary (A), Monthly Basic |
| `allowances_total` | Allowances, Total Allowances, House Allowance + Transport + … (summed), Total Cash Pay *minus basic* (P10, when basic is known) |
| `non_cash_benefits` | Benefits Non-Cash, Benefits (B), Car Benefit, Value of Meals, Non Cash Benefits |
| `value_of_quarters` | Value of Quarters, Housing Benefit, Value of Housing |
| `gross_pay` | Gross, Gross Pay, Gross Salary, Total Gross Pay (D), Total Earnings, Total Cash Pay (P10, cash only) |
| `pension_employee` | Pension, Pension (Employee), Provident Fund, Defined Contribution, E2 Actual, Retirement Contribution |
| `nssf_employee` | NSSF, N.S.S.F, NSSF Employee, NSSF Contribution |
| `nssf_tier1_employee` / `nssf_tier2_employee` | NSSF Tier I / Tier 1 / T1, NSSF Tier II / Tier 2 / T2 |
| `nssf_employer` (and tiers) | NSSF Employer, NSSF (ER), Employer NSSF, Company NSSF |
| `shif_employee` | SHIF, SHA, SHIF Deduction, Social Health Insurance Fund (G); **NHIF** (before Oct 2024: map to `nhif_employee` by period) |
| `ahl_employee` | Housing Levy, H/Levy, AHL, Affordable Housing Levy (F), Housing Levy (EE) |
| `ahl_employer` | Housing Levy Employer, AHL (ER), Employer Housing Levy |
| `prmf` | PRMF, Post Retirement Medical Fund (H) |
| `mortgage_interest` | Owner Occupied Interest (I), Mortgage Interest, Interest |
| `taxable_pay` | Taxable Pay, Taxable Income, Chargeable Pay (K) |
| `tax_charged` | Tax Charged (L), Tax on Taxable Pay, Gross Tax |
| `personal_relief` | Personal Relief (M), Relief, Monthly Relief |
| `insurance_relief` | Insurance Relief (N), Ins. Relief, NHIF Relief, SHIF Relief |
| `paye` | PAYE, PAYE Tax (O), Tax Payable, Net Tax, Income Tax, Tax Deducted |
| `nita` | NITA, NITA Levy, Industrial Training Levy |
| `other_deductions` | Other Deductions, HELB, SACCO, Loan, Advance, Union Dues, Total Deductions *minus statutory* |
| `net_pay` | Net, Net Pay, Net Salary, Take Home, Amount Payable, Net Pay Bank or Mobile (Workpay) |

Ambiguous headers need a decision in the review, not a guess:
- "Deductions" (statutory only, or everything)
- "Relief" (personal relief or insurance relief)
- "NHIF/SHIF" (one column across Oct 2024; split it by period)
- "Gross" (with or without non-cash benefits)

### 4.4 How many months

| Range | Required? | Why |
| --- | --- | --- |
| January of the current tax year → the month before the first Pesaroll run | **Required for a P9 that is right first time** (the step itself stays optional) | P9A, annual reconciliation, YTD on payslips |
| The previous full tax year | Optional, recommended | P9 reprints and employee queries (filing season ends 30 June); year-on-year reports |
| Up to 24 months in all | Optional | Reference: trends, leave-pay averages, disputes |
| Older | Not offered | The Tax Procedures Act requires keeping tax records **5 years**, but that duty stays with the employer and the old system or bureau. We don't need to absorb it. Revisit if customers ask |

### 4.5 Validation checks

The cell shows the problem and an IMP error code (3000–3499 range).

**Block the import** (fix or remove the row):
1. **Identity:** no `employee_number`, `kra_pin` or `national_id` that resolves to one employee. Unknown employees follow the "create it on the fly" rule (§4.6).
2. **KRA PIN format:** `^[AP]\d{9}[A-Z]$` after trimming and upper-casing. Individuals start with `A`; a `P` (non-individual) PIN on an employee row is a warning. The format check doesn't prove the PIN exists. Optionally check it against KRA's GavaConnect PIN-validation API later.
3. **Period:** must parse to a month, can't be in the future, and can't be on or after the company's first Pesaroll pay run.
4. **Duplicates:** two rows for the same employee and month in one file are a block unless the customer chooses "sum" (several runs). A row that duplicates history **already imported** blocks; offer "replace that month" as a separate, explicit action.
5. **Arithmetic:** **net pay = cash gross − (PAYE + NSSF employee + SHIF/NHIF + AHL employee + pension employee + PRMF + other deductions)**, within KES 1 per row for rounding. Use **cash** gross (gross minus non-cash benefits and value of quarters), because benefits are taxed but never paid. Only applies when net pay is given.
6. **Negatives** (except a clearly labelled refund or adjustment column).

**Warn** (the import can go ahead; the warning is saved with the row):
1. **PAYE recomputation:** recompute with the rules in force for the period (§3.4) from gross, benefits and deductions. Warn if the difference is more than **KES 10, or 1% of PAYE, whichever is larger**. Common real causes: arrears spread over months, a missing PRMF or mortgage column, or the 2023 band date.
2. Taxable pay ≠ gross − allowable deductions (the same tolerance).
3. SHIF ≠ max(2.75% × gross, 300) from Oct 2024; NHIF not on the graduated table before then.
4. AHL ≠ 1.5% × gross, except a zero during the January–March 2024 suspension.
5. NSSF outside the phase's Tier I and II limits for the period (also catches the old KES 200 flat rate).
6. Personal relief ≠ 2,400 (resident employees). Missing KRA PIN or ID (needed later for P9 and SHA).
7. **Gaps:** a month missing between an employee's first and last imported month. Possible reasons: unpaid leave, a wrong file, or an employee who left and came back.
8. The P10 total for a month (if a P10 is also imported) ≠ the sum of history rows.

### 4.6 Edge cases

- **Employees who left during the year:** they still need a P9A. Following our "higher levels are never a precondition" rule, an unmatched row creates a **minimal former-employee record** (name, KRA PIN, ID, employee number, a status of "Left", and an exit date set to the last imported month, which can be edited). The review notes it under the cell: "No employee with PIN A0… yet: they will be added as a former employee."
- **Joined mid-year:** fine. History starts in their first month. The gap check ignores months before their first row.
- **Joined this year from another employer:** their earlier employer's pay is **not** ours to import. The employee files their own return with both P9s. Say so in the help text.
- **Several companies:** history is per company (per KRA PIN). The company selector sits above the review table, as in every import. Employees on two companies' payrolls get one row per company per month. A single-company preset (P10 or P9A) reads the employer PIN in the file and warns if it doesn't match the chosen company.
- **Bureau customers:** ask the bureau for (1) the monthly register as Excel and (2) the year's P10 files. Never accept PDFs as the import itself. Bureaus often send one workbook with one sheet per month, so the uploader should **read every sheet** and use the sheet name as the period when there's no period column.
- **Excel customers:** expect merged title rows above the header. Detect the header row (the first row with at least three known headers). Ignore TOTAL rows. Expect formulas (read the cached values) and a sheet per month. Many won't have PINs; let them import with a warning, and let them add PINs in the review table.
- **Casual workers paid daily or weekly:** sum to the month. Casuals often have no PIN or NSSF number, so warn rather than block.
- **Currency:** KES only. A non-KES value blocks the row (until the multi-currency work).
- **Amended P10s:** if both an original and an amended return are imported for a month, the amended one wins (Sheet A shows the return type).

---

## 5. Recommendations for Pesaroll

### 5.1 Proposed template: "Payroll history" (.xlsx, one sheet, one row per employee per month)

The company is chosen with the company selector, not a column. Reference columns are plain text, following our import pattern.

| # | Column (template header) | Required | Notes |
| --- | --- | --- | --- |
| 1 | Employee number | One of 1–3 | Match key |
| 2 | KRA PIN | **Yes** | Needed for P9; warn-only for casuals (see §4.6) |
| 3 | ID or passport number | One of 1–3 | Match key for SHA and NSSF |
| 4 | Employee name | **Yes** | Used when creating a former employee |
| 5 | Pay month | **Yes** | `2026-03` or "March 2026" |
| 6 | Pay date | No | Defaults to the last day of the month |
| 7 | Basic pay | **Yes** | P9A A |
| 8 | Cash allowances | No | Total of all cash allowances |
| 9 | Non-cash benefits | No | P9A B |
| 10 | Value of quarters | No | P9A C |
| 11 | Gross pay | **Yes** | Taxable gross (P9A D) |
| 12 | Pension (employee) | No | P9A E (actual) |
| 13 | NSSF employee Tier I | No* | *Give the tiers **or** column 15 |
| 14 | NSSF employee Tier II | No* | |
| 15 | NSSF employee total | **Yes** (if tiers aren't given) | |
| 16 | NSSF employer total | No | Derived if blank |
| 17 | SHIF / NHIF | **Yes** | One column; the period decides which. P9A G |
| 18 | Housing Levy (employee) | **Yes** from Mar 2024 | P9A F |
| 19 | Housing Levy (employer) | No | Derived if blank |
| 20 | Post-retirement medical fund | No | P9A H |
| 21 | Mortgage interest | No | P9A I |
| 22 | Taxable pay | No | Recomputed and compared if given (P9A K) |
| 23 | Personal relief | No | Derived (2,400) if blank |
| 24 | Insurance relief | No | P9A N |
| 25 | PAYE | **Yes** | P9A O |
| 26 | Other deductions | No | HELB, SACCO, loans and similar, as a total |
| 27 | Net pay | **Yes** for registers; No for P10 and P9 presets | Turns on the arithmetic check |
| 28 | NITA | No | Derived (50) if blank |
| 29 | Notes | No | For example "includes leave arrears" |

**Required set:** an identity key, KRA PIN, name, month, basic pay, gross pay, NSSF, SHIF/NHIF, Housing Levy (from March 2024) and PAYE. Net pay is required unless the file comes from a P10 or P9A preset.

### 5.2 Presets: which first

| Order | Preset | Why first | Needs |
| --- | --- | --- | --- |
| 1 | **Generic (any Excel)** with header auto-matching and a manual "map this column" step | Covers Excel, bureaus, QuickBooks and Zoho users and unknown vendors | The §4.3 synonym table; multi-sheet reading |
| 2 | **KRA simplified P10** (July 2025 on; CSV or Excel) | Every employer has it; fixed layout | The KRA sample CSV (download from kra.go.ke publications) to pin exact columns. **No net pay, so arithmetic checks are off** |
| 3 | **KRA P9A** (2025 layout; pre-December 2024 layout second) | Fixed layout; gives the YTD months in one file per employee | Read many files or one zip; the year comes from the card |
| 4 | **Workpay Muster Roll** | Largest Kenyan incumbent | A real export sample |
| 5 | **SeamlessHR Master Report** | Excel by design; corporates | A real export sample |
| 6 | **Sage VIP Payroll Listing History** | Large installed base | A real export sample (layout depends on the customer's setup) |
| Later | PaySpace, Odoo Payslip Lines, Sage 300 People, FaidiHR | When a customer brings one | Sample per customer |

A preset is just a saved header map plus fixed rules (for example "the P10 has no net pay"). Keep presets as data, not code, so support can add one without a release.

### 5.3 What to defer

- **API connectors** (Workpay REST, PaySpace API v2, Odoo RPC): access is gated through account managers, and a file covers the one-off migration.
- **Reading PDF payslips or P9 PDFs (OCR):** high error rate. Ask for Excel from the old system or bureau instead.
- **Per-run history** (weekly, daily, off-cycle) and **line-by-line allowances or deductions:** monthly totals plus "Cash allowances" and "Other deductions" are enough for the P9 and reports.
- **Opening balances for loans, advances, leave and gratuity:** separate onboarding steps.
- **Importing SHA and NSSF returns** as sources: keep them as reference uploads only.
- **"Replace a month" and history editing after import:** first version is import, view and delete a whole batch.

### 5.4 How imported history behaves

- Stored as **imported pay history**, separate from Pesaroll pay runs, labelled "Imported" on payslips and reports. It never feeds a statutory file for a past month; those were already filed.
- **Feeds:** the January P9A (imported months plus Pesaroll months), YTD totals on payslips, the employee portal's history (as a summary row, not a reconstructed payslip), and labour-cost reports.
- Each batch keeps: who imported it, when, the source (preset name), the file name, the row count, and the warnings accepted. A whole batch can be deleted until the year's P9s are issued.

### 5.5 Risks

| Risk | Mitigation |
| --- | --- |
| **Bad source data** (wrong months, NHIF/SHIF mix-ups, missing PINs) giving wrong P9As | Period-aware checks (§4.5); a summary before import ("12 months, 43 employees, 3 former employees, KES 4.1m PAYE"); compare against an uploaded P10 total where possible |
| **False alarms** from the recomputation check (arrears, the 2023 band date, unrecorded reliefs) | Warnings, not blocks; tolerance; a "why might this differ?" help line |
| **Personal data (Data Protection Act 2019):** names, ID numbers, PINs and salaries for a whole workforce in one file | Upload limited to payroll admins (an access-grant role). Original file kept only until the import ends, then deleted (suggest ≤ 7 days, encrypted at rest). No bank or M-Pesa numbers in the template. Every import and deletion audit-logged. Salaries only shown to roles that can see pay. Our processor terms with the customer cover migration data. Note that the Act lists "property details" among sensitive data; whether salary counts is unconfirmed, so treat it as sensitive anyway |
| **Cross-border transfer:** DigitalOcean hosting is outside Kenya | Confirm our transfer basis under the Act (sections 48–50) and the 2021 Regulations, and state it in the DPA template. Needs legal review (unconfirmed) |
| **Salaries left on laptops and in email** after the bureau or old vendor sends them | Help text: delete the file after import. We don't email files back |
| **Duplicate or overlapping history** (imported March plus a Pesaroll March run) | Block periods on or after the first Pesaroll run |
| **A customer importing history for the wrong company** in a group | The company selector is required. P10 and P9A presets compare the employer PIN in the file |
| **Rule uncertainty** (Finance Act 2026 assent; SHIF insurance relief Oct–Nov 2024; the effective date of the July 2023 bands) | Keep these as dated, editable rules. Confirm with a payroll accountant before the recomputation check ships |

### 5.6 Open questions for the team

1. Should ONB-10 require the current-year months when the company goes live after January, or only nudge? (We recommend a strong nudge plus a P9 warning in December.)
2. Do we show imported months as payslips in the employee portal, or only as year totals?
3. Who confirms the PAYE "monthly, non-cumulative" reading and the Oct–Nov 2024 SHIF relief: our payroll accountant, or a partner firm?
4. Can we get one real export from each of Workpay, SeamlessHR and Sage VIP from design-partner customers before building presets 4–6?

---

## Sources

**KRA and statutory**
- [KRA P9A form template 2025 (Appendix 2A)](https://www.kra.go.ke/images/publications/P9-FORM-Template-2025.pdf)
- [KRA step-by-step guide for the simplified PAYE return](https://www.kra.go.ke/images/publications/STEP-BY-STEP-GUIDE-FOR-THE-SIMPLIFIED-PAYE-RETURN.pdf) · [iTax enhancements, June 2025](https://www.kra.go.ke/images/publications/iTax-Enhancements---June-2025.pdf) · [KRA publications (P10 template and sample CSV)](https://www.kra.go.ke/publications)
- [KRA simplifies PAYE return filing (public notice)](https://www.kra.go.ke/news-center/public-notices/2230-kra-simplifies-the-paye-return-filing-process) · [People Daily on the simplified PAYE return](https://peopledaily.digital/business/kra-announces-new-simplified-paye-return-filing-process)
- [Business Daily: KRA tax relief on Housing Levy and SHIF (Dec 2024)](https://www.businessdailyafrica.com/bd/economy/workers-gain-as-kra-grants-tax-relief-on-housing-levy-shif-4863782) · [PKF: Tax Laws (Amendment) Act 2024](https://pkfea.com/media/w2ebfrii/tax-alert-tax-laws-amendment-tax-procedures-amendment-acts-2024.pdf)
- [Andersen: SHIF contributions eligible for 15% relief](https://ke.andersen.com/news/shif-contributions-eligible-for-15-tax-relief) · [PaySpace: KRA notice on insurance relief on SHIF](https://support.payspace.com/portal/en/kb/articles/kenya-public-notice-insurance-relief-on-shif-contributions)
- [KPMG: Finance Act 2023 analysis](https://assets.kpmg.com/content/dam/kpmg/ke/pdf/tax/2023/KPMG%20Finance%20Act%202023%20Analysis.pdf) · [Business Daily: KRA updates iTax for Finance Act 2023](https://www.businessdailyafrica.com/bd/economy/kra-updates-itax-portal-to-effect-new-taxes--4323304)
- [The Standard: MPs pass Finance Bill 2026](https://www.standardmedia.co.ke/business/national/article/2001550701/heated-debate-as-mps-pass-finance-bill-2026) · [Business Daily: personal tax growth at a 4-year low](https://www.businessdailyafrica.com/bd/economy/personal-tax-growth-at-a-4-year-low-on-strained-households-5506584)
- [PwC: Year 4 NSSF rates 2026](https://www.pwc.com/ke/en/assets/pdf/tax-alert-year-4nssf-rates-2026.pdf) · [KPMG: Phase 4 NSSF rates](https://assets.kpmg.com/content/dam/kpmgsites/ke/pdf/thought_leaderships/tax/2026/Phase-4-of-NSSF-Contribution-Rates-effective-February-2026.pdf.coredownload.inline.pdf) · [PwC: NSSF rates Feb 2025](https://pwc.com/ke/en/assets/pdf/tax-alert-updated-nssf-rates-feb-2025.pdf) · [BDO: NSSF rates Feb 2025](https://www.bdo-ea.com/getmedia/2490456c-e13a-471f-88fb-8f618d196028/_NSSF-New-Rates-February-2025-(1).pdf)
- [NSSF Employer Self Service guide](https://selfservice.nssf.or.ke/wp-content/uploads/download-manager-files/NSSF%20Employer%20Self%20Service%20Guide.pdf) · [FaidiHR: NSSF returns file structure](https://blog.faidihr.com/our-latest-news/Specifications+for+new+NSSF+Returns+file+structure) (couldn't be fetched; cited from its search summary)
- [SHA](https://sha.go.ke) · [Paybill KE: SHA employer monthly reporting](https://paybillke.com/guides/sha-employer-monthly-reporting-kenya) (secondary)
- [KPMG: Court of Appeal maintains Housing Levy suspension](https://assets.kpmg.com/content/dam/kpmg/ke/pdf/tax/2024/Court%20of%20Appeal%20Maintains%20Housing%20Levy%20Suspension%20by%20the%20High%20Court.pdf) · [Sage: Court of Appeal suspends the Housing Levy](https://communityhub.sage.com/za/sage-pastel-payroll/f/announcements/217067/kenya-court-of-appeal-suspends-the-affordable-housing-levy) · [Rödl: Newsflash Kenya 1/2024](https://www.roedl.com/en-gb/de/media/publications/newsletters/newsflash-kenya/Documents/newsflash-kenya-1-2024.pdf)
- [Grant Thornton: Tax Procedures Act 2015 (record keeping)](https://www.grantthornton.co.ke/globalassets/pdf/report/tax-alert---tax-procedures-act-2015.pdf) · [Oraro: record keeping (2026)](https://www.oraro.co.ke/wp-content/uploads/2026/04/No-Longer-Negotiable-Record-Keeping-Taxation.pdf) · [Employment Act s.74](https://www.sheriaplex.com/kenya-acts/1488-records-to-be-kept-by-employer)
- [python-stdnum: Kenyan KRA PIN format](https://arthurdejong.org/python-stdnum/doc/stdnum.ke.pin)

**Providers**
- Workpay: [View payroll and HR reports](https://learn.myworkpay.com/en/articles/11998834-view-payroll-and-hr-reports) · [Reports help (P9, Net Pay, Muster Roll)](https://learn.myworkpay.com/knowledge/view-payroll-and-hr-reports-workpay-help-center) · [Payroll journal](https://learn.myworkpay.com/en/articles/11998825-how-to-generate-payroll-journal) · [Integrations and API](https://myworkpay.com/integrations) · [HRIS integration guide](https://learn.myworkpay.com/en/articles/16540421-connecting-an-hr-system-to-workpay-hris-integration-guide)
- SeamlessHR: [P9 download](https://support.seamlesshr.com/how-to-download-p9-report-on-seamlesspayroll) · [P10 download](https://support.seamlesshr.com/how-to-download-p10-report-on-seamlesspayroll) · [YTD report](https://support.seamlesshr.com/how-to-download-year-to-date-ytd-report) · [Master Report](https://support.seamlesshr.com/how-to-download-a-report-that-shows-employee-tax-numbers-id) · [Custom reports](https://support.seamlesshr.com/how-to-create-custom-report) · [P10 release note (v30.0.1)](https://support.seamlesshr.com/november-release-note-on-the-p10-report) · [New features (bulk payslips)](https://support.seamlesshr.com/new-features-on-seamlesspayroll)
- Sage: [VIP Classic/Premier R63b patch notes (Kenya P10 and P9)](https://communityhub.sage.com/cfs-file/__key/communityserver-discussions-components-files/384/Additional-Notes-R63b-Patch-20250227.pdf) · [VIP: Kenya changes to P9 cards](https://communityhub.sage.com/za/sage-vip-payroll-hr/f/announcements/242471/kenya-changes-to-p9-tax-deduction-cards-report) · [Sage 300 People: Kenya P9 for 2025](https://communityhub.sage.com/za/sage-300-people/f/announcements/260074/statutory-update-kenya-p9-tax-deduction-card-for-2025-tax-year) · [300 People: Payroll Analysis export](https://communityhub.sage.com/za/sage-300-people/f/general-discussion/274180/fixed-earing-report/658783) · [VIP: history data export](https://communityhub.sage.com/za/sage-vip-payroll-hr/f/general-discussion/146342/increase-screen-history-data-export) · [VIP: NHIF to SHI transition](https://communityhub.sage.com/za/sage-vip-payroll-hr/f/announcements/233952/kenya-transition-from-nhif-to-shi-contributions---october-2024---implementation-important-notice)
- PaySpace / Deel: [Kenya page](https://payspace.com/kenya) · [NSSF Phase 4](https://support.payspace.com/portal/en/kb/articles/kenya-nssf-rates-increase-phase-4) · [Simplified PAYE](https://support.payspace.com/portal/en/kb/articles/kenya-simplified-pay-as-you-earn-paye-filing-process) · [Flowgear PaySpace API connector](https://help.flowgear.net/v2/nodes/payspace) · [Unified.to PaySpace](https://unified.to/integrations/payspace.md)
- Odoo: [Kenya payroll localisation](https://www.odoo.com/documentation/master/applications/hr/payroll/payroll_localizations/kenya.html) · [P9 report app](https://apps.odoo.com/apps/modules/18.0/p9_report) · [P10 PAYE return app](https://apps.odoo.com/apps/modules/18.0/l10n_ke_p10_paye_return)
- Zoho: [Zoho Payroll integration countries](https://help.zoho.com/portal/en/kb/people/administrator-guide/payroll-integrations/articles/zoho-payroll-integration-with-zoho-people) · [Payroll Master KE](https://marketplace.zoho.com/app/creator/payroll-master-ke)
- Local tools and lists (vendor-written, used only to show which products exist): [FaidiHR payroll](https://faidihr.com/payroll) · [SeamlessHR: top HR and payroll software in Kenya 2026](https://seamlesshr.com/blog/top-hr-payroll-software-in-kenya-for-2026/) · [FaidiHR: best payroll software in Kenya 2026](https://faidihr.com/blog/best-payroll-software-in-kenya-2026-guide) · [Mercans DSR (QuickBooks Kenya, Sage Pastel Kenya)](https://dsr.mercans.com/?p=904)
- Bureaus and banks: [Grant Thornton: payroll outsourcing in Kenya](https://www.grantthornton.co.ke/globalassets/1.-member-firms/kenya/insights/pdf/payroll-outsourcing-in-kenya.pdf) · [PwC Kenya tax capabilities](https://pwc.com/ke/en/assets/pdf/pwc-kenya-capabilities-tax-services.pdf) · [PwC managed payroll (Africa)](https://www.pwc.com/mu/en/services/tax/workforce/payroll.html) · [NCBA and Mwalimu Sacco Salo Xpress](https://citizen.digital/article/mwalimu-sacco-ncba-bank-launch-new-system-to-simplify-salary-processing-n384721)
- Migration practice: [PaySpace: YTD take-on figures](https://support.payspace.com/portal/en/kb/articles/how-do-i-process-year-to-date-take-on-figures-on-employee-level) (returned 404 when fetched; cited from its search summary) · [SimplePay: Excel import for take-on balances](https://simplepay.co.za/help/bulk-actions/excel-import-for-take-on-balances)

**Internal**
- `project-docs/hr-payroll-market-research-ecsa.md` (§3.1 players, §7.1 Kenya statutory, §8.1 incumbents, §17 filing)
- `pesaroll/docs/standards/ui.md` (BulkUpload pattern) and `errors.md` (IMP 3000–3499)
