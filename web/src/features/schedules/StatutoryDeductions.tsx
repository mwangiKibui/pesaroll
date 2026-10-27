import { useQuery } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { useServices } from '../../app/services'
import { Notice } from '../../components/feedback'
import { Checkbox, RadioGroup, Switch, TextField } from '../../components/fields'
import { MultiSelectField } from '../../components/MultiSelectField'
import { formatMoney } from '../../lib/money'
import { statutoryItems, type DeductionErrors, type DeductionsDraft } from './deductions'
import { dayMonthYear, shortDate } from './wording'

type Choice = components['schemas']['DeductionChoiceResponse']
type Described = components['schemas']['ScheduleDeductionsResponse']

/** "28 Aug 2026": the day in Kenya a choice was made. */
const setOn = (at: string) => dayMonthYear(new Date(at), 'Africa/Nairobi')

/** A sample wage for the example: a week, two weeks or a day of a modest wage. */
const sampleWage: Record<string, string> = { weekly: '5000', fortnightly: '10000', daily: '1000' }

/**
 * PAY-10's choice for a weekly, fortnightly or daily schedule, in the schedule drawer (OP-05):
 * whether it makes statutory deductions, which, for everyone on it or chosen people, from when.
 * A month of a sample wage, worked out by the API, shows what it means before saving; earlier
 * choices are kept and listed. Monthly payroll makes every one, so it has no section.
 */
export function StatutoryDeductions({ scheduleId, frequency, value, onChange, errors, canChange, history, loading, loadFailed }: {
  /** The saved schedule, for choosing people on it; none while adding. */
  scheduleId: string | null
  frequency: string
  value: DeductionsDraft
  onChange: (value: DeductionsDraft) => void
  errors: DeductionErrors
  /** Only someone who manages schedules for every company can change it (they serve the organization). */
  canChange: boolean
  history: Described['history']
  /** The choice in force is still being read: nothing can change until it is. */
  loading: boolean
  loadFailed: boolean
}) {
  const { t } = useTranslation()
  const { api } = useServices()
  const id = useId()
  const [wage, setWage] = useState(sampleWage[frequency] ?? '5000')
  const [sample, setSample] = useState(Number(wage))
  const set = (change: Partial<DeductionsDraft>) => onChange({ ...value, ...change })
  const itemName = (item: string) => t(`schedules.statutory.items.${item}`)

  const people = useQuery({
    queryKey: ['employees', 'on-schedule', scheduleId],
    enabled: !!scheduleId && value.makes && value.appliesTo === 'chosen',
    queryFn: async () => {
      const { data } = await api.GET('/api/employees', { params: { query: { scheduleIds: [scheduleId!], status: 'current', pageSize: 100 } } })
      if (!data) throw new Error('employees')
      return data.items
    },
  })
  const items = statutoryItems.filter(i => value.items.includes(i))
  const example = useQuery({
    queryKey: ['deductions-example', frequency, sample, items],
    enabled: value.makes && items.length > 0 && sample > 0,
    queryFn: async () => {
      const { data } = await api.POST('/api/pay-schedules/deductions-example', { body: { frequency, wage: sample, items, countryCode: null } })
      if (!data) throw new Error('example')
      return data
    },
  })
  const lines = (list: { key: string; amount: number | string }[]) =>
    list.map(l => `${t(`schedules.statutory.lines.${l.key}`)} ${formatMoney(l.amount)}`).join(' · ')
  const describe = (c: Choice) => [
    t('schedules.statutory.from', { date: shortDate(c.startsOn!) }) + ': '
      + (c.makesDeductions ? c.items.map(itemName).join(', ') : t('schedules.statutory.noneChosen')),
    ...(c.makesDeductions ? [c.appliesTo === 'chosen' ? t('schedules.statutory.forChosen', { count: c.employeeIds.length }) : t('schedules.statutory.forEveryone')] : []),
    ...(c.setAt ? [t('schedules.statutory.setOn', { date: setOn(c.setAt) })] : []),
  ].join(' · ')

  return (
    <section aria-labelledby={`${id}-title`} className="flex flex-col gap-3 rounded-control border border-line px-3 py-3">
      <div className="flex flex-col gap-0.5">
        <h3 id={`${id}-title`} className="m-0 text-label font-medium text-ink">{t('schedules.statutory.title')}</h3>
        <p className="m-0 text-hint text-muted">{t('schedules.statutory.hint')}</p>
      </div>
      {loadFailed && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
      {!canChange && <p className="m-0 text-hint text-muted">{t('schedules.statutory.readOnly')}</p>}
      <Switch label={t('schedules.statutory.makes')} checked={value.makes} disabled={!canChange || loading || loadFailed}
        // Switched on, every deduction is ticked to start with.
        onChange={makes => set(makes && value.items.length === 0 ? { makes, items: [...statutoryItems] } : { makes })} />
      {value.makes && <>
        <fieldset className="m-0 flex flex-col gap-1 border-0 p-0" aria-describedby={errors.items ? `${id}-items` : undefined}>
          <legend className="mb-1 p-0 text-label font-medium text-ink">{t('schedules.statutory.which')}</legend>
          <div className="flex flex-wrap gap-x-5 gap-y-1">
            {statutoryItems.map(item => (
              <Checkbox key={item} label={itemName(item)} checked={value.items.includes(item)} disabled={!canChange}
                onChange={e => set({ items: e.target.checked ? [...value.items, item] : value.items.filter(i => i !== item) })} />
            ))}
          </div>
          {errors.items && <p id={`${id}-items`} className="m-0 text-hint font-medium text-danger-ink">{errors.items}</p>}
        </fieldset>
        {scheduleId && (
          <RadioGroup label={t('schedules.statutory.whoFor')} name={`${id}-who`} value={value.appliesTo}
            onChange={appliesTo => canChange && set({ appliesTo: appliesTo as DeductionsDraft['appliesTo'] })}
            options={[{ value: 'everyone', label: t('schedules.statutory.everyone') }, { value: 'chosen', label: t('schedules.statutory.chosen') }]} />
        )}
        {scheduleId && value.appliesTo === 'chosen' && (
          <MultiSelectField label={t('schedules.statutory.employees')} placeholder={t('schedules.statutory.employeesPlaceholder')}
            hint={t('schedules.statutory.employeesHint')} error={errors.employeeIds}
            options={(people.data ?? []).map(p => ({ value: p.id, label: p.name }))} value={value.employeeIds}
            onChange={employeeIds => canChange && set({ employeeIds })} />
        )}
      </>}
      <TextField type="date" label={t('schedules.statutory.startsOn')} placeholder={t('schedules.statutory.startsOnPlaceholder')}
        hint={t('schedules.statutory.startsOnHint')} error={errors.startsOn} disabled={!canChange}
        value={value.startsOn} onChange={e => set({ startsOn: e.target.value })} />

      {value.makes && items.length > 0 && (
        <fieldset className="m-0 flex flex-col gap-2 rounded-control border-0 bg-raised p-3">
          <legend className="float-left mb-1 p-0 text-label font-medium text-ink">{t('schedules.statutory.example')}</legend>
          <TextField label={t(`schedules.statutory.wage_${frequency}`)} placeholder={t('schedules.statutory.wagePlaceholder')} inputMode="decimal" size="sm"
            value={wage} onChange={e => setWage(e.target.value)} onBlur={() => setSample(Number(wage.replace(/,/g, '')) || 0)}
            onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); setSample(Number(wage.replace(/,/g, '')) || 0) } }} />
          {example.data && <>
            <p className="m-0 text-body text-ink">{t('schedules.statutory.month', { count: example.data.runs, frequency: t(`schedules.statutory.runOf_${frequency}`), pay: formatMoney(example.data.wage) })}</p>
            <dl className="m-0 flex flex-col gap-1 text-body text-ink">
              <div><dt className="inline font-medium">{t('schedules.statutory.deducted')} </dt><dd className="inline m-0 tabular-nums">{lines(example.data.deductions)}</dd></div>
              <div><dt className="inline font-medium">{t('schedules.statutory.takeHome')} </dt><dd className="inline m-0 tabular-nums">{formatMoney(example.data.takeHome)}</dd></div>
              {example.data.employerCosts.length > 0 && (
                <div><dt className="inline font-medium">{t('schedules.statutory.employerPays')} </dt><dd className="inline m-0 tabular-nums">{lines(example.data.employerCosts)}</dd></div>
              )}
            </dl>
          </>}
          <p className="m-0 text-hint text-muted">{t('schedules.statutory.exampleHint')}</p>
        </fieldset>
      )}

      {history.length > 0 && (
        <div className="flex flex-col gap-1">
          <h4 id={`${id}-history`} className="m-0 text-label font-medium text-ink">{t('schedules.statutory.history')}</h4>
          <ul aria-labelledby={`${id}-history`} className="m-0 flex list-none flex-col gap-0.5 p-0 text-hint text-muted">
            {history.map(c => <li key={`${c.startsOn}-${c.setAt}`}>{describe(c)}</li>)}
          </ul>
        </div>
      )}
    </section>
  )
}
