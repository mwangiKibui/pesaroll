import i18n from '../../i18n'
import { formatMoney } from '../../lib/money'
import { periodLabel } from './wording'

export interface Line {
  kind: string; name: string; amount: number | string; base?: number | string | null; rate?: number | string | null; quantity?: number | string | null
  unitRate?: number | string | null; reason?: string | null; owedFrom?: string | null; owedTo?: string | null; monthToDate?: number | string | null; earlier?: number | string
}

/** "6%" from 0.06. */
const percent = (rate: number | string) => `${Number((Number(rate) * 100).toFixed(4))}%`

/**
 * How a line was worked out, in the order people read it: owed from the last period, changed for
 * this run and why, days or hours × rate, a rate of a base, and what earlier runs in the month took.
 */
export function lineDetails(l: Line): string[] {
  const t = i18n.t.bind(i18n)
  const details: string[] = []
  if (l.owedFrom && l.owedTo) details.push(t('payRun.owed', { period: periodLabel(l.owedFrom, l.owedTo) }))
  if (l.reason) details.push(t('payRun.changed', { reason: l.reason }))
  if (l.quantity != null && l.unitRate != null) details.push(`${Number(l.quantity)} × ${formatMoney(l.unitRate)}`)
  if (l.rate != null && l.base != null) details.push(t('payRun.rateOf', { rate: percent(l.rate), base: formatMoney(l.base) }))
  if (Number(l.earlier ?? 0) > 0 && l.monthToDate != null)
    details.push(t('payRun.monthToDate', { total: formatMoney(l.monthToDate), earlier: formatMoney(l.earlier!) }))
  return details
}


/** Notes the app knows how to say; anything newer from the API is left out rather than shown as a key. */
export const knownNotes = (notes: string[]) => notes.filter(n => i18n.exists(`payRun.notes.${n}`))
