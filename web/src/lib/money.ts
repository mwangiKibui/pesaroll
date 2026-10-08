export interface MoneyOptions {
  /** false leaves the currency out, for a column of figures. Default KES. */
  currency?: string | false
}

/**
 * "KES 17,501": currency first, thousand separators, cents only when there are some, and a true
 * minus sign (−) for deductions. Amounts come from the API as decimals (numeric(18,2)).
 */
export function formatMoney(amount: number | string, { currency = 'KES' }: MoneyOptions = {}) {
  const value = Number(amount)
  const abs = Math.abs(value)
  const hasCents = Math.round(abs * 100) % 100 !== 0
  const digits = new Intl.NumberFormat('en-KE', { minimumFractionDigits: hasCents ? 2 : 0, maximumFractionDigits: 2 }).format(abs)
  const text = currency ? `${currency} ${digits}` : digits
  return value < 0 ? `−${text}` : text
}
