/**
 * The checks on an employee's details (ONB-09), the same as the API's (EmployeeNumbers,
 * EmployeesEndpoints), so mistakes show as soon as someone leaves a field. Each returns a message
 * key under employees.rules, or null when the value is fine (an empty value is fine unless required).
 */
const compact = (value: string) => value.replace(/[\s\-().]/g, '')

/** A Kenyan mobile number on any network: 07…, 01…, 254… or +254…, 9 digits after the 0. */
export const kenyanMobile = (value: string) => {
  const match = /^(?:\+?254|0)?([17]\d{8})$/.exec(compact(value))
  return match ? match[1] : null
}

/** Safaricom's ranges (the three digits after 0), for M-Pesa: 070-072, 074(0-3,5,6,8), 0757-9, 0768-9, 079, 0110-5, 0118-9, 014(0-3), 018(0-2). */
const safaricom = (p: number) =>
  (p >= 700 && p <= 729) || (p >= 740 && p <= 743) || p === 745 || p === 746 || p === 748 || (p >= 757 && p <= 759) || p === 768 || p === 769
  || (p >= 790 && p <= 799) || (p >= 110 && p <= 115) || p === 118 || p === 119 || (p >= 140 && p <= 143) || (p >= 180 && p <= 182)

export const isMpesa = (value: string) => {
  const local = kenyanMobile(value)
  return !!local && safaricom(Number(local.slice(0, 3)))
}

export interface BankRule { code: string; name: string; accountDigitsMin: number | string; accountDigitsMax: number | string }

type Check = (value: string) => string | null
const pattern = (re: RegExp, key: string, upper = false): Check => value => {
  const v = compact(value.trim())
  return !v || re.test(upper ? v.toUpperCase() : v) ? null : key
}

export const rules = {
  required: (value: string) => (value.trim() ? null : 'required'),
  name: (value: string) => (!value.trim() ? 'required' : value.trim().length > 60 ? 'nameTooLong' : null),
  kraPin: pattern(/^A\d{9}[A-Z]$/, 'kraPin', true),
  nationalId: pattern(/^\d{7,9}$/, 'nationalId'),
  nssfNo: pattern(/^\d{7,10}$/, 'nssfNo'),
  shaNo: pattern(/^[A-Z0-9-]{6,20}$/, 'shaNo', true),
  phone: (value: string) => (!value.trim() || kenyanMobile(value) ? null : 'phone'),
  email: (value: string) => (!value.trim() || /^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/.test(value.trim()) ? null : 'email'),
  mpesa: (value: string) => (!value.trim() || isMpesa(value) ? null : 'mpesa'),
  amount: (value: string) => {
    const v = value.replace(/kes|ksh|[\s,]/gi, '')
    return !v || (Number(v) > 0 && /^\d+(\.\d{1,2})?$/.test(v)) ? null : 'amount'
  },
  staffNo: pattern(/^[A-Z0-9][A-Z0-9/-]{0,19}$/, 'staffNo', true),
}

/** An account number for this bank: digits only, as long as its account numbers are. */
export const accountProblem = (bank: BankRule | undefined, value: string): { key: string; values?: Record<string, unknown> } | null => {
  const v = value.replace(/[\s-]/g, '')
  if (!v) return null
  if (!bank) return /^\d{5,20}$/.test(v) ? null : { key: 'accountDigits' }
  const min = Number(bank.accountDigitsMin)
  const max = Number(bank.accountDigitsMax)
  if (/^\d+$/.test(v) && v.length >= min && v.length <= max) return null
  return min === max ? { key: 'accountExactly', values: { bank: bank.name, count: min } } : { key: 'accountBetween', values: { bank: bank.name, min, max } }
}
