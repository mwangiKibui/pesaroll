import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useId, useState, type FormEvent } from 'react'
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
import { Checkbox, RadioGroup, SelectField, Switch, TextField } from '../components/fields'
import { Modal } from '../components/Modal'
import { MultiSelectField } from '../components/MultiSelectField'
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
  usesCutoff: boolean
  cutoffAppliesTo: string[]
  applies: CompanySelection
  defaultFor: string[]
  reminders: DraftReminder[]
}

/** A reminder being edited in the drawer; saved with the schedule. id is set once it is saved. */
interface DraftReminder { key: string; id?: string; amount: number; channel: 'email' | 'sms'; recipient: string }

/** Reminders are in hours before the pay time for daily pay, days before the pay day otherwise, up to these limits (as the API). */
const reminderUnit = (frequency: string) => (frequency === 'daily' ? 'hours' : 'days')
const reminderMax = (frequency: string) => ({ daily: 12, weekly: 6, fortnightly: 13 } as Record<string, number>)[frequency] ?? 14
const maxReminders = 5

/** The same checks as the API: a plain email, or a Kenyan mobile number (07…, 01…, +254…). */
const isEmail = (text: string) => /^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/.test(text.trim())
const isKenyanMobile = (text: string) => /^(?:\+?254|0)?[17]\d{8}$/.test(text.replace(/[\s\-().]/g, ''))

const none = <span className="text-muted">—</span>

/** The frequencies and pay day rules (same lists as the API), and the days a fixed pay day or cut-off can be. */
const frequencies = ['monthly', 'weekly', 'fortnightly', 'daily'] as const
const payDayRules = ['last_working_day', 'last_day', 'fixed_day'] as const
const days = Array.from({ length: 28 }, (_, i) => String(i + 1))
/** What a cut-off can cover (same keys as the API); anything else is the organization's own item. */
const cutoffItems = ['overtime', 'new_employees', 'leavers', 'unpaid_leave', 'pay_changes', 'one_off'] as const
const isCutoffItem = (item: string): item is (typeof cutoffItems)[number] => (cutoffItems as readonly string[]).includes(item)

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
    if (!s.usesCutoff) return t('schedules.noCutoff')
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
                  <th scope="col">{t('schedules.reminders')}</th>
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
                    <td>{s.reminders.length || none}</td>
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
        <Proceed label={t('setup.proceed', { step: t('setup.steps.leavePlans') })} onProceed={() => void navigate({ to: '/setup/leave-plans' })} />

        {editing && (
          <ScheduleDrawer schedule={editing === 'new' ? null : editing} companies={companies.data} schedules={schedules.data}
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

function ScheduleDrawer({ schedule, companies, schedules, defaultApplies, onClose }: {
  schedule: Schedule | null
  companies: Company[]
  /** Every schedule, for the cut-off items the organization added before. */
  schedules: Schedule[]
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
      usesCutoff: schedule ? schedule.usesCutoff : true,
      cutoffAppliesTo: schedule?.usesCutoff ? schedule.cutoffAppliesTo : [...cutoffItems],
      reminders: (schedule?.reminders ?? []).map(r => ({ key: r.id, id: r.id, amount: Number(r.amount), channel: r.channel as 'email' | 'sms', recipient: r.recipient })),
      applies: schedule ? { all: schedule.allCompanies, ids: schedule.allCompanies ? companies.map(c => c.id) : schedule.companyIds } : defaultApplies,
      defaultFor: schedule?.defaultFor ?? [],
    },
  })
  const [applies, frequency, rule, day, weekday, firstPayDate, usesCutoff, appliesTo] =
    useWatch({ control, name: ['applies', 'frequency', 'payDayRule', 'payDay', 'payWeekday', 'firstPayDate', 'usesCutoff', 'cutoffAppliesTo'] })
  const [addingItem, setAddingItem] = useState(false)
  const reminders = useWatch({ control, name: 'reminders' })
  const [editingReminder, setEditingReminder] = useState<DraftReminder | 'new' | null>(null)
  const [reminderErrors, setReminderErrors] = useState<Record<string, string>>({})
  const unit = reminderUnit(frequency)
  const max = reminderMax(frequency)
  const whenText = (amount: number) => t(unit === 'hours' ? 'schedules.hoursBefore' : 'schedules.daysBefore', { count: amount })
  const fits = (r: DraftReminder) => r.amount >= 1 && r.amount <= max
  // The known items, then the organization's own (from any schedule, or just added here).
  const ownItems = [...new Set([...schedules.flatMap(s => s.cutoffAppliesTo), ...appliesTo].filter(i => !isCutoffItem(i)))]
  const itemOptions = [
    ...cutoffItems.map(i => ({ value: i as string, label: t(`schedules.cutoffItems.${i}`) })),
    ...ownItems.map(i => ({ value: i, label: i })),
  ]
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
    if (v.frequency !== 'daily' && v.usesCutoff && v.cutoffAppliesTo.length === 0) problems.push(['cutoffAppliesTo', t('schedules.appliesToRequired')])
    if (v.reminders.some(r => r.amount < 1 || r.amount > reminderMax(v.frequency))) problems.push(['reminders', t('schedules.remindersDontFit')])
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
    const uses = v.frequency !== 'daily' && v.usesCutoff
    const body = {
      name: v.name.trim(),
      frequency: v.frequency,
      payDayRule: isMonthly ? v.payDayRule : null,
      payDay: isMonthly && v.payDayRule === 'fixed_day' ? Number(v.payDay) : null,
      cutoffDay: isMonthly && uses ? Number(v.cutoffDay) : null,
      payWeekday: isByWeekday ? Number(v.payWeekday) : null,
      firstPayDate: v.frequency === 'fortnightly' ? v.firstPayDate : null,
      cutoffDaysBefore: isByWeekday && uses ? Number(v.cutoffDaysBefore) : null,
      payTime: v.frequency === 'daily' ? v.payTime : null,
      usesCutoff: uses,
      cutoffAppliesTo: uses ? v.cutoffAppliesTo : [],
      reminders: v.reminders.map(r => ({ id: r.id ?? null, amount: r.amount, channel: r.channel, recipient: r.recipient.trim() })),
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
    // Reminder problems ("reminders[1].recipient") show on their reminder.
    const onReminders = Object.entries(error.fields ?? {}).filter(([name]) => name.startsWith('reminders'))
    setReminderErrors(Object.fromEntries(onReminders.map(([name, message]) => [reminders[Number(/\[(\d+)\]/.exec(name)?.[1])]?.key ?? 'all', message])))
    if (error.code === 'ONB-3525') return setError('name', { message: errorMessage(t, error) })
    const fields: Record<string, FieldPath<Values>> = {
      name: 'name', frequency: 'frequency', payDayRule: 'payDayRule', payDay: 'payDay', cutoffDay: 'cutoffDay', payWeekday: 'payWeekday',
      firstPayDate: 'firstPayDate', cutoffDaysBefore: 'cutoffDaysBefore', payTime: 'payTime', cutoffAppliesTo: 'cutoffAppliesTo', companyIds: 'applies', defaultFor: 'defaultFor',
    }
    const known = Object.entries(error.fields ?? {}).filter(([name]) => fields[name])
    for (const [name, message] of known) setError(fields[name], { message })
    if (known.length === 0 && onReminders.length === 0) setBanner(error)
  }

  return (
    <Drawer open size="wide" title={schedule ? t('schedules.editTitle') : t('schedules.addTitle')} onClose={onClose}
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
        {frequency !== 'daily' && (
          <Controller control={control} name="usesCutoff" render={({ field }) => (
            <Switch label={t('schedules.useCutoff')} hint={t('schedules.cutoffHelp')} checked={field.value} onChange={field.onChange} />
          )} />
        )}
        {monthly && usesCutoff && (
          <Controller control={control} name="cutoffDay" render={({ field }) => (
            <SelectField label={t('schedules.cutoffDay')} placeholder={t('schedules.cutoffPlaceholder')} hint={t('schedules.cutoffHint')}
              error={errors.cutoffDay?.message} options={dayOptions} {...field} />
          )} />
        )}
        {byWeekday && usesCutoff && (
          <Controller control={control} name="cutoffDaysBefore" render={({ field }) => (
            <SelectField label={t('schedules.cutoffBefore')} placeholder={t('schedules.cutoffPlaceholder')} hint={t('schedules.cutoffBeforeHint')}
              error={errors.cutoffDaysBefore?.message} options={beforeOptions} {...field} />
          )} />
        )}
        {frequency !== 'daily' && usesCutoff && (
          <Controller control={control} name="cutoffAppliesTo" render={({ field }) => (
            <MultiSelectField label={t('schedules.appliesTo2')} hint={t('schedules.appliesToHint')} placeholder={t('schedules.appliesToPlaceholder')}
              error={errors.cutoffAppliesTo?.message} options={itemOptions} value={field.value} onChange={field.onChange}
              addLabel={t('schedules.addOwn')} onAdd={() => setAddingItem(true)} />
          )} />
        )}

        <section aria-labelledby={`${formId}-reminders`} className="flex flex-col gap-2">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between sm:gap-6">
            <div className="flex min-w-0 flex-col gap-0.5">
              <h3 id={`${formId}-reminders`} className="m-0 text-label font-medium text-ink">{t('schedules.reminders')}</h3>
              <p className="m-0 text-hint text-muted">{t('schedules.remindersHint')}</p>
            </div>
            <Button variant="secondary" className="flex-none self-end whitespace-nowrap sm:self-start" disabled={reminders.length >= maxReminders}
              onClick={() => setEditingReminder('new')}>
              <PlusIcon />{t('schedules.addReminder')}
            </Button>
          </div>
          {reminders.length >= maxReminders && <p className="m-0 text-hint text-muted">{t('schedules.maxReminders')}</p>}
          {reminders.length > 0 && (
            <ul aria-label={t('schedules.reminders')} className="m-0 flex list-none flex-col divide-y divide-line-soft rounded-control border border-line p-0">
              {reminders.map(r => {
                const problem = !fits(r) ? t('schedules.reminderRange', { max, unit: t(`schedules.units.${unit}`) }) : reminderErrors[r.key]
                return (
                  <li key={r.key} className="flex items-center gap-2 px-3 py-2">
                    <span className="flex min-w-0 flex-1 flex-col">
                      <span className="truncate text-body text-ink">{[whenText(r.amount), t(`schedules.channels.${r.channel}`), r.recipient].join(' · ')}</span>
                      {problem && <span className="text-hint font-medium text-danger-ink">{problem}</span>}
                    </span>
                    <IconButton label={t('schedules.editReminder', { when: whenText(r.amount) })} onClick={() => setEditingReminder(r)}>
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                    </IconButton>
                    <IconButton tone="danger" label={t('schedules.removeReminder', { when: whenText(r.amount) })}
                      onClick={() => setValue('reminders', reminders.filter(x => x.key !== r.key))}>
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                    </IconButton>
                  </li>
                )
              })}
            </ul>
          )}
          {errors.reminders?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.reminders.message}</p>}
        </section>

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
      {editingReminder && (
        <ReminderModal reminder={editingReminder === 'new' ? null : editingReminder} unit={unit} max={max}
          onCancel={() => setEditingReminder(null)}
          onSave={saved => {
            setValue('reminders', editingReminder === 'new' ? [...reminders, saved] : reminders.map(r => (r.key === saved.key ? saved : r)))
            setReminderErrors(e => ({ ...e, [saved.key]: '' }))
            setEditingReminder(null)
          }} />
      )}
      {addingItem && (
        <AddCutoffItem taken={itemOptions.map(o => o.label)} onCancel={() => setAddingItem(false)}
          onAdd={item => { setValue('cutoffAppliesTo', [...appliesTo, item]); setAddingItem(false) }} />
      )}
    </Drawer>
  )
}

/** A reminder's when, how and to whom, in a small form on top of the drawer. */
function ReminderModal({ reminder, unit, max, onSave, onCancel }: {
  reminder: DraftReminder | null
  unit: 'days' | 'hours'
  max: number
  onSave: (reminder: DraftReminder) => void
  onCancel: () => void
}) {
  const { t } = useTranslation()
  const formId = useId()
  const [amount, setAmount] = useState(String(reminder?.amount ?? 1))
  const [channel, setChannel] = useState<'email' | 'sms'>(reminder?.channel ?? 'email')
  const [recipient, setRecipient] = useState(reminder?.recipient ?? '')
  const [error, setError] = useState<string>()
  const options = Array.from({ length: max }, (_, i) => ({
    value: String(i + 1), label: t(unit === 'hours' ? 'schedules.hoursBefore' : 'schedules.daysBefore', { count: i + 1 }),
  }))

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (channel === 'email' && !isEmail(recipient)) return setError(t('schedules.emailInvalid'))
    if (channel === 'sms' && !isKenyanMobile(recipient)) return setError(t('schedules.phoneInvalid'))
    onSave({ key: reminder?.key ?? crypto.randomUUID(), id: reminder?.id, amount: Number(amount), channel, recipient: recipient.trim() })
  }

  return (
    <Modal open title={reminder ? t('schedules.editReminderTitle') : t('schedules.addReminder')} onClose={onCancel}
      footer={<>
        <Button variant="secondary" onClick={onCancel}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId}>{reminder ? t('schedules.saveReminder') : t('schedules.addReminder')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={submit} className="flex flex-col gap-4">
        <SelectField label={t('schedules.when')} placeholder={t('schedules.whenPlaceholder')} options={options} value={amount} onChange={setAmount} />
        <RadioGroup label={t('schedules.sendBy')} name={`${formId}-channel`} value={channel}
          onChange={value => { setChannel(value as 'email' | 'sms'); setError(undefined) }}
          options={[{ value: 'email', label: t('schedules.channels.email') }, { value: 'sms', label: t('schedules.channels.sms') }]} />
        {channel === 'email'
          ? <TextField key="email" label={t('schedules.email')} placeholder={t('schedules.emailPlaceholder')} type="email" inputMode="email" maxLength={254}
              error={error} value={recipient} onChange={e => { setRecipient(e.target.value); setError(undefined) }} />
          : <TextField key="sms" label={t('schedules.phone')} placeholder={t('schedules.phonePlaceholder')} type="tel" inputMode="tel" maxLength={20}
              error={error} value={recipient} onChange={e => { setRecipient(e.target.value); setError(undefined) }} />}
      </form>
    </Modal>
  )
}

/** Names one of the organization's own cut-off items, which is then ticked. */
function AddCutoffItem({ taken, onAdd, onCancel }: { taken: string[]; onAdd: (item: string) => void; onCancel: () => void }) {
  const { t } = useTranslation()
  const formId = useId()
  const [item, setItem] = useState('')
  const [error, setError] = useState<string>()

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const name = item.trim()
    if (name.length < 2 || name.length > 60) return setError(t('schedules.itemInvalid'))
    if (taken.some(x => x.toLowerCase() === name.toLowerCase())) return setError(t('schedules.itemTaken'))
    onAdd(name)
  }

  return (
    <Modal open title={t('schedules.addItemTitle')} onClose={onCancel}
      footer={<>
        <Button variant="secondary" onClick={onCancel}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId}>{t('schedules.addItem')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={submit}>
        <TextField label={t('schedules.item')} placeholder={t('schedules.itemPlaceholder')} maxLength={60} error={error}
          value={item} onChange={e => { setItem(e.target.value); setError(undefined) }} />
      </form>
    </Modal>
  )
}
