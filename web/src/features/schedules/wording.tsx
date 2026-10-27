import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'

type Schedule = components['schemas']['PayScheduleResponse']

/** "5:00 pm" from "17:00" (or "17:00:00"). */
export const clockTime = (time: string) => {
  const [h, m] = time.split(':').map(Number)
  return `${h % 12 || 12}:${String(m).padStart(2, '0')} ${h < 12 ? 'am' : 'pm'}`
}

/** "Fri 30 Oct 2026" from "2026-10-30": the date as it is, whatever the browser's time zone. */
export const longDate = (iso: string) => new Intl.DateTimeFormat('en-GB', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
  .format(new Date(`${iso}T00:00:00Z`)).replace(',', '')

export const none = <span className="text-muted">—</span>

/** A schedule's pay day ("Last working day", "Every Friday") and cut-off ("25th", "2 days before"), as the lists show them. */
export function useScheduleWording() {
  const { t } = useTranslation()
  const payDay = (s: Schedule) => {
    const weekday = t(`schedules.weekdays.${s.payWeekday}`)
    switch (s.frequency) {
      case 'weekly': return t('schedules.everyWeekday', { day: weekday })
      case 'fortnightly': return t('schedules.everyOtherWeekday', { day: weekday })
      case 'daily': return t('schedules.workingDaysAt', { time: clockTime(s.payTime ?? '17:00') })
      default: return s.payDayRule === 'fixed_day' ? t('schedules.day', { count: Number(s.payDay), ordinal: true }) : t(`schedules.rules.${s.payDayRule}`)
    }
  }
  const cutoff = (s: Schedule) => {
    if (s.frequency === 'daily') return none
    if (!s.usesCutoff) return t('schedules.noCutoff')
    if (s.frequency === 'monthly') return t('schedules.day', { count: Number(s.cutoffDay), ordinal: true })
    return Number(s.cutoffDaysBefore) === 0 ? t('schedules.onPayDay') : t('schedules.daysBefore', { count: Number(s.cutoffDaysBefore) })
  }
  return { payDay, cutoff }
}
