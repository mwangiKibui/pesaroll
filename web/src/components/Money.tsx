import { formatMoney, type MoneyOptions } from '../lib/money'

/** Money in text: figures line up in columns. */
export function Money({ amount, currency, className = '' }: { amount: number | string; className?: string } & MoneyOptions) {
  return <span className={`tabular-nums ${className}`}>{formatMoney(amount, { currency })}</span>
}
