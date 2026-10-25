# Golden payroll cases (PAY-02)

Worked examples the payroll engine must match to the cent on every change. `GoldenTests` runs each file in `cases/` through the engine with the rule pack it names (the same files in `database/rule-packs/` that the database publishes) and fails naming the case and the line.

## A case

```json
{
  "description": "KES 100,000 a month: the headline example",
  "payDate": "2026-02-27",
  "rulePack": "KE-2026.02",
  "inputs": { "grossCashPay": 100000 },
  "expected": {
    "nssf_employee": 6000, "shif": 2750, "ahl_employee": 1500, "paye": 19308.35,
    "nssf_employer": 6000, "ahl_employer": 1500, "nita": 50,
    "taxablePay": 89750, "netPay": 70441.65
  },
  "checked": { "by": "computed from sources", "on": "2026-10-25" }
}
```

- `rulePack` is the pack in force on `payDate` (packs start on the date in `post-deploy/0016_rule_packs.sql`).
- `inputs`: `grossCashPay`, and when they apply `nonCashBenefits`, `pensionContributions`, `insurancePremiums`, `mortgageInterest`, `postRetirementMedical`.
- `expected` lists every line the engine gives (keys as in `statutory_items`), plus `taxablePay` and `netPay`. `netPay` is cash pay less statutory deductions; other deductions, such as the pension contribution itself, come with pay runs (PAY-03).
- `checked.by` is `computed from sources` until the payroll accountant (BIZ-08) reviews the case; then their name, with the date.

## Adding a case

Work the figures out by hand or with an independent calculation from the sources in `docs/research/kenya-statutory-2026.md`, never by copying what the engine gives. Name the file by number and what it shows (`23-…json`). A new rule pack gets cases for what changed, including a pay date either side of its start.
