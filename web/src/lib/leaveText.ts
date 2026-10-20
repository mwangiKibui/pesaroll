import { useTranslation } from 'react-i18next'

/** A leave plan's rule in words: "1.75 days a month", "7 days a year + 7 on half pay", "no limit". */
export function useRuleText() {
  const { t } = useTranslation()
  return (r: { earnMethod: string; amount?: number | string | null; unit: string; halfPay: number | string }) => {
    const text = t(`leavePlans.ruleText.${r.earnMethod}`, { amount: r.amount == null ? '' : Number(r.amount), unit: t(`leavePlans.units.${r.unit}`) })
    return Number(r.halfPay) > 0 ? text + t('leavePlans.halfPayText', { amount: Number(r.halfPay) }) : text
  }
}
