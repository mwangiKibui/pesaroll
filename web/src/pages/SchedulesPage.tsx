import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useId, useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Drawer } from '../components/Drawer'
import { EmptyState, FormErrorBanner, Loading, Notice } from '../components/feedback'
import { Checkbox, RadioGroup, SelectField, TextField } from '../components/fields'
import { PlusIcon } from '../components/icons'
import { TableCard } from '../components/TableCard'
import { useToast } from '../components/toast'
import { useCompanies, useSchedules, useSetupDone } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type Schedule = components['schemas']['PayScheduleResponse']

interface Values {
  name: string
  frequency: string
  payDayRule: string
  payDay: string
  cutoffDay: string
  payWeekday: string
  firstPayDate: string
  cutoffDaysBefore: string
  payTime: string
  applies: CompanySelection
  defaultFor: string[]
}

const none = <span className="text-muted">—</span>

/** The frequencies and pay day rules (same lists as the API), and the days a fixed pay day or cut-off can be. */
const frequencies = ['monthly', 'weekly', 'fortnightly', 'daily'] as const
const payDayRules = ['last_working_day', 'last_day', 'fixed_day'] as const
const days = Array.from({ length: 28 }, (_, i) => String(i + 1))
/** Monday 1 to Friday 5, as the API counts them. */
const weekdays = ['1', '2', '3', '4', '5'] as const
/** Pay times for daily pay, every half hour from 6:00 am to 9:30 pm, as "17:30". */
const payTimes = Array.from({ length: 32 }, (_, i) => `${String(6 + Math.floor(i / 2)).padStart(2, '0')}:${i % 2 ? '30' : '00'}`)

/** "5:00 pm" from "17:00" (or "17:00:00"). */
const clockTime = (time: string) => {
  const [h, m] = time.split(':').map(Number)
  return `${h % 12 || 12}:${String(m).padStart(2, '0')} ${h < 12 ? 'am' : 'pm'}`
}

/** "Fri 30 Oct 2026" from "2026-10-30": the date as it is, whatever the browser's time zone. */
const longDate = (iso: string) => new Intl.DateTimeFormat('en-GB', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
  .format(new Date(`${iso}T00:00:00Z`)).replace(',', '')

/** The companies a schedule serves: all of them, or the chosen ones. */
const served = (all: boolean, ids: string[], companies: Company[]) => (all ? companies : companies.filter(c => ids.includes(c.id)))

/**
 * ONB-06: how often (monthly, weekly, every two weeks or daily) and when people are paid. Each company already has a default schedule (the
 * suggested Monthly, last working day, made with the company); more schedules serve one, several or
 * all companies. Pay runs (PAY-03) take their periods and pay dates from these.
 */
export default function SchedulesPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const companies = useCompanies()
  const schedules = useSchedules()
  const done = useSetupDone(true)
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [editing, setEditing] = useState<Schedule | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Schedule | null>(null)
  const [busy, setBusy] = useState(false)

  const ready = companies.data && schedules.data
  const list = (schedules.data ?? []).filter(s => shown.all || s.allCompanies || s.companyIds.some(id => shown.ids.includes(id)))
  const companyNames = (ids: string[]) => (companies.data ?? []).filter(c => ids.includes(c.id)).map(c => c.legalName).join(', ')
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
    if (s.frequency === 'monthly') return t('schedules.day', { count: Number(s.cutoffDay), ordinal: true })
    return Number(s.cutoffDaysBefore) === 0 ? t('schedules.onPayDay') : t('schedules.daysBefore', { count: Number(s.cutoffDaysBefore) })
  }

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      const { error, response } = await api.DELETE('/api/pay-schedules/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        queryClient.setQueryData<Schedule[]>(['schedules'], old => old?.filter(s => s.id !== deleting.id))
        toast.show(t('schedules.deleted'))
      } else {
        toast.show(isApiError(error) ? errorMessage(t, error) : t('apiErrors.network'), 'danger')
      }
    } catch {
      toast.show(t('apiErrors.network'), 'danger')
    } finally {
      setBusy(false)
      setDeleting(null)
    }
  }

  return (
    <SetupLayout current="schedules" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('schedules.title')}</h1>
        <p className="m-0 text-muted">{t('schedules.intro')}</p>
      </div>
      {!ready ? <Loading rows={3} /> : <>
        {companies.data.length > 1 && (
          <div className="max-w-sm">
            <CompanySelect label={t('schedules.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <TableCard title={t('schedules.tableTitle')} count={list.length}
          actions={<Button onClick={() => setEditing('new')}><PlusIcon />{t('schedules.add')}</Button>}>
          {list.length === 0 ? <EmptyState title={t('schedules.emptyTitle')}>{t('schedules.emptyText')}</EmptyState> : (
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('schedules.schedule')}</th>
                  <th scope="col">{t('schedules.frequency')}</th>
                  <th scope="col">{t('schedules.payDay')}</th>
                  <th scope="col">{t('schedules.cutoff')}</th>
                  <th scope="col">{t('schedules.nextPayDates')}</th>
                  <th scope="col">{t('schedules.appliesTo')}</th>
                  <th scope="col">{t('schedules.defaultFor')}</th>
                  <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {list.map(s => (
                  <tr key={s.id}>
                    <td className="font-medium text-ink">{s.name}</td>
                    <td>{t(`schedules.frequencies.${s.frequency}`)}</td>
                    <td>{payDay(s)}</td>
                    <td>{cutoff(s)}</td>
                    <td className="whitespace-nowrap">{s.nextPayDates.map(d => <span key={d} className="block">{longDate(d)}</span>)}</td>
                    <td>{s.allCompanies ? t('schedules.allCompanies') : companyNames(s.companyIds) || none}</td>
                    <td>{companyNames(s.defaultFor) || none}</td>
                    <td className="whitespace-nowrap text-right">
                      <IconButton label={t('schedules.edit', { name: s.name })} onClick={() => setEditing(s)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                      </IconButton>
                      <IconButton tone="danger" label={t('schedules.delete', { name: s.name })} onClick={() => setDeleting(s)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TableCard>
        {/* Pay items (ONB-07) is next; until it has a page, the dashboard. */}
        <Proceed label={t('organization.proceed')} onProceed={() => void navigate({ to: '/' })} />

        {editing && (
          <ScheduleDrawer schedule={editing === 'new' ? null : editing} companies={companies.data}
            // A new schedule serves the companies being shown, as the selector suggests.
            defaultApplies={companies.data.length === 1 ? { all: false, ids: [companies.data[0].id] } : shown}
            onClose={() => setEditing(null)} />
        )}
      </>}
      <ConfirmDialog open={!!deleting} title={t('schedules.deleteTitle', { name: deleting?.name })}
        confirmLabel={t('schedules.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('schedules.deleteText')}
      </ConfirmDialog>
    </SetupLayout>
  )
}

function ScheduleDrawer({ schedule, companies, defaultApplies, onClose }: {
  schedule: Schedule | null
  companies: Company[]
  defaultApplies: CompanySelection
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)

  const { register, handleSubmit, control, setError, setValue, formState: { errors, isSubmitting } } = useForm<Values>({
    defaultValues: {
      name: schedule?.name ?? '',
      frequency: schedule?.frequency ?? 'monthly',
      payDayRule: schedule?.payDayRule ?? 'last_working_day',
      payDay: schedule?.payDay == null ? '' : String(schedule.payDay),
      cutoffDay: String(schedule?.cutoffDay ?? 25),
      payWeekday: String(schedule?.payWeekday ?? 5),
      firstPayDate: schedule?.firstPayDate ?? '',
      cutoffDaysBefore: String(schedule?.cutoffDaysBefore ?? 2),
      payTime: schedule?.payTime?.slice(0, 5) ?? '17:00',
      applies: schedule ? { all: schedule.allCompanies, ids: schedule.allCompanies ? companies.map(c => c.id) : schedule.companyIds } : defaultApplies,
      defaultFor: schedule?.defaultFor ?? [],
    },
  })
  const [applies, frequency, rule, day, weekday, firstPayDate] =
    useWatch({ control, name: ['applies', 'frequency', 'payDayRule', 'payDay', 'payWeekday', 'firstPayDate'] })
  const serves = served(applies.all, applies.ids, companies)
  const dayOptions = days.map(d => ({ value: d, label: t('schedules.day', { count: Number(d), ordinal: true }) }))
  const monthly = frequency === 'monthly'
  const byWeekday = frequency === 'weekly' || frequency === 'fortnightly'
  const fixed = monthly && rule === 'fixed_day'
  const beforeOptions = Array.from({ length: frequency === 'weekly' ? 7 : 14 }, (_, n) =>
    ({ value: String(n), label: n === 0 ? t('schedules.onPayDay') : t('schedules.daysBefore', { count: n }) }))

  const payDates = (query: Record<string, string | number>) => async () => {
    const { data } = await api.GET('/api/pay-schedules/pay-dates', { params: { query } })
    if (!data) throw new Error('pay-dates')
    return data
  }
  // The next pay dates for what is filled in so far, worked out by the API (the same rules pay runs use).
  const query: Record<string, string | number> =
    monthly ? (fixed ? { frequency, payDayRule: rule, payDay: Number(day) } : { frequency, payDayRule: rule })
      : frequency === 'weekly' ? { frequency, payWeekday: Number(weekday) }
        : frequency === 'fortnightly' ? { frequency, payWeekday: Number(weekday), firstPayDate }
          : { frequency }
  const preview = useQuery({
    queryKey: ['pay-dates', query],
    enabled: (!fixed || !!day) && (frequency !== 'fortnightly' || !!firstPayDate),
    queryFn: payDates(query),
  })
  // Every two weeks starts on one of the next two pay days (or where it already started).
  const starts = useQuery({
    queryKey: ['pay-dates', { frequency: 'weekly', payWeekday: Number(weekday) }],
    enabled: frequency === 'fortnightly',
    queryFn: payDates({ frequency: 'weekly', payWeekday: Number(weekday) }),
  })
  const startOptions = [...new Set([...(schedule?.firstPayDate && Number(schedule.payWeekday) === Number(weekday) ? [schedule.firstPayDate] : []),
    ...(starts.data ?? []).slice(0, 2)])].map(d => ({ value: d, label: longDate(d) }))

  // The same checks as the API, so most mistakes show before anything is sent.
  const check = (v: Values) => {
    const problems: [FieldPath<Values>, string][] = []
    if (v.name.trim().length < 2 || v.name.trim().length > 60) problems.push(['name', t('schedules.nameInvalid')])
    if (v.frequency === 'monthly' && v.payDayRule === 'fixed_day' && !v.payDay) problems.push(['payDay', t('schedules.payDayInvalid')])
    if (v.frequency === 'fortnightly' && !v.firstPayDate) problems.push(['firstPayDate', t('schedules.firstPayDateInvalid')])
    if (!v.applies.all && v.applies.ids.length === 0) problems.push(['applies', t('schedules.companiesRequired')])
    for (const [name, message] of problems) setError(name, { message })
    return problems.length === 0
  }

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!check(v)) return
    // Only the fields of its frequency; the others are sent empty.
    const isMonthly = v.frequency === 'monthly'
    const isByWeekday = v.frequency === 'weekly' || v.frequency === 'fortnightly'
    const body = {
      name: v.name.trim(),
      frequency: v.frequency,
      payDayRule: isMonthly ? v.payDayRule : null,
      payDay: isMonthly && v.payDayRule === 'fixed_day' ? Number(v.payDay) : null,
      cutoffDay: isMonthly ? Number(v.cutoffDay) : null,
      payWeekday: isByWeekday ? Number(v.payWeekday) : null,
      firstPayDate: v.frequency === 'fortnightly' ? v.firstPayDate : null,
      cutoffDaysBefore: isByWeekday ? Number(v.cutoffDaysBefore) : null,
      payTime: v.frequency === 'daily' ? v.payTime : null,
      allCompanies: v.applies.all,
      companyIds: v.applies.all ? [] : v.applies.ids,
      // Only companies it still serves can have it as default.
      defaultFor: v.defaultFor.filter(id => served(v.applies.all, v.applies.ids, companies).some(c => c.id === id)),
    }
    try {
      const { data, error } = schedule
        ? await api.PATCH('/api/pay-schedules/{id}', { params: { path: { id: schedule.id } }, body })
        : await api.POST('/api/pay-schedules', { body })
      if (data) {
        // Making it a default moves other companies' defaults, so the whole list is fetched again.
        void queryClient.invalidateQueries({ queryKey: ['schedules'] })
        toast.show(schedule ? t('schedules.saved') : t('schedules.added'))
        onClose()
        return
      }
      showError(error)
    } catch {
      setBanner('network')
    }
  }

  const showError = (error: unknown) => {
    if (!isApiError(error)) return setBanner('network')
    if (error.code === 'ONB-3525') return setError('name', { message: errorMessage(t, error) })
    const fields: Record<string, FieldPath<Values>> = {
      name: 'name', frequency: 'frequency', payDayRule: 'payDayRule', payDay: 'payDay', cutoffDay: 'cutoffDay', payWeekday: 'payWeekday',
      firstPayDate: 'firstPayDate', cutoffDaysBefore: 'cutoffDaysBefore', payTime: 'payTime', companyIds: 'applies', defaultFor: 'defaultFor',
    }
    const known = Object.entries(error.fields ?? {}).filter(([name]) => fields[name])
    for (const [name, message] of known) setError(fields[name], { message })
    if (known.length === 0) setBanner(error)
  }

  return (
    <Drawer open title={schedule ? t('schedules.editTitle') : t('schedules.addTitle')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{schedule ? t('schedules.save') : t('schedules.add')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <TextField label={t('schedules.name')} placeholder={t('schedules.namePlaceholder')} maxLength={60}
          error={errors.name?.message} {...register('name')} />
        <Controller control={control} name="frequency" render={({ field }) => (
          <SelectField label={t('schedules.frequency')} placeholder={t('schedules.frequencyPlaceholder')} error={errors.frequency?.message}
            options={frequencies.map(f => ({ value: f, label: t(`schedules.frequencies.${f}`) }))} {...field} />
        )} />
        {monthly && (
          <Controller control={control} name="payDayRule" render={({ field }) => (
            <RadioGroup label={t('schedules.payDayRule')} name={field.name} value={field.value} onChange={field.onChange}
              options={payDayRules.map(r => ({ value: r, label: t(`schedules.rules.${r}`), hint: t(`schedules.ruleHints.${r}`) }))} />
          )} />
        )}
        {byWeekday && (
          <Controller control={control} name="payWeekday" render={({ field }) => (
            <SelectField label={t('schedules.payWeekday')} placeholder={t('schedules.dayPlaceholder')} error={errors.payWeekday?.message}
              options={weekdays.map(d => ({ value: d, label: t(`schedules.weekdays.${d}`) }))} {...field}
              // A new weekday means a new first pay date.
              onChange={value => { field.onChange(value); setValue('firstPayDate', '') }} />
          )} />
        )}
        {frequency === 'fortnightly' && (
          <Controller control={control} name="firstPayDate" render={({ field }) => (
            <SelectField label={t('schedules.firstPayDate')} placeholder={t('schedules.firstPayDatePlaceholder')} hint={t('schedules.firstPayDateHint')}
              error={errors.firstPayDate?.message} options={startOptions} {...field} />
          )} />
        )}
        {frequency === 'daily' && (
          <Controller control={control} name="payTime" render={({ field }) => (
            <SelectField label={t('schedules.payTime')} placeholder={t('schedules.payTimePlaceholder')} hint={t('schedules.payTimeHint')}
              error={errors.payTime?.message} options={payTimes.map(time => ({ value: time, label: clockTime(time) }))} {...field} />
          )} />
        )}
        {fixed && (
          <Controller control={control} name="payDay" render={({ field }) => (
            <SelectField label={t('schedules.dayOfMonth')} placeholder={t('schedules.dayPlaceholder')} error={errors.payDay?.message}
              options={dayOptions} {...field} />
          )} />
        )}
        <section aria-labelledby={`${formId}-preview`} className="flex flex-col gap-1 rounded-control border border-line bg-raised px-3 py-2.5">
          <h3 id={`${formId}-preview`} className="m-0 text-label font-medium text-ink">{t('schedules.preview')}</h3>
          {preview.data
            ? <ul className="m-0 flex list-none flex-wrap gap-x-4 gap-y-1 p-0 text-body text-ink">{preview.data.map(d => <li key={d}>{longDate(d)}</li>)}</ul>
            : <p className="m-0 text-body text-muted">—</p>}
          <p className="m-0 text-hint text-muted">{t('schedules.previewHint')}</p>
        </section>
        {monthly && (
          <Controller control={control} name="cutoffDay" render={({ field }) => (
            <SelectField label={t('schedules.cutoffDay')} placeholder={t('schedules.cutoffPlaceholder')} hint={t('schedules.cutoffHint')}
              error={errors.cutoffDay?.message} options={dayOptions} {...field} />
          )} />
        )}
        {byWeekday && (
          <Controller control={control} name="cutoffDaysBefore" render={({ field }) => (
            <SelectField label={t('schedules.cutoffBefore')} placeholder={t('schedules.cutoffPlaceholder')} hint={t('schedules.cutoffBeforeHint')}
              error={errors.cutoffDaysBefore?.message} options={beforeOptions} {...field} />
          )} />
        )}

        {companies.length > 1 && (
          <div className="flex flex-col gap-1.5">
            <Controller control={control} name="applies" render={({ field }) => (
              <CompanySelect label={t('schedules.appliesTo')} value={field.value} onChange={field.onChange}
                companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
            )} />
            {errors.applies?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.applies.message}</p>}
          </div>
        )}
        <fieldset className="m-0 flex flex-col gap-1 border-0 p-0">
          <legend className="mb-1 p-0 text-label font-medium text-ink">{t('schedules.defaultFor')}</legend>
          <p className="m-0 text-hint text-muted">{t('schedules.defaultHint')}</p>
          <Controller control={control} name="defaultFor" render={({ field }) => <>
            {serves.map(c => (
              <Checkbox key={c.id} label={t('schedules.defaultForCompany', { name: c.legalName })} checked={field.value.includes(c.id)}
                onChange={e => field.onChange(e.target.checked ? [...field.value, c.id] : field.value.filter(id => id !== c.id))} />
            ))}
          </>} />
          {errors.defaultFor?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.defaultFor.message}</p>}
        </fieldset>
      </form>
    </Drawer>
  )
}
