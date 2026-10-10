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
  applies: CompanySelection
  defaultFor: string[]
}

const none = <span className="text-muted">—</span>

/** The pay day rules (same list as the API) and the days a fixed pay day or cut-off can be. */
const payDayRules = ['last_working_day', 'last_day', 'fixed_day'] as const
const days = Array.from({ length: 28 }, (_, i) => String(i + 1))

/** "Fri 30 Oct 2026" from "2026-10-30": the date as it is, whatever the browser's time zone. */
const longDate = (iso: string) => new Intl.DateTimeFormat('en-GB', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
  .format(new Date(`${iso}T00:00:00Z`)).replace(',', '')

/** The companies a schedule serves: all of them, or the chosen ones. */
const served = (all: boolean, ids: string[], companies: Company[]) => (all ? companies : companies.filter(c => ids.includes(c.id)))

/**
 * ONB-06: how often and when people are paid. Each company already has a default schedule (the
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
  const payDay = (s: Schedule) => (s.payDayRule === 'fixed_day' ? t('schedules.day', { count: Number(s.payDay), ordinal: true }) : t(`schedules.rules.${s.payDayRule}`))

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
                    <td>{t('schedules.day', { count: Number(s.cutoffDay), ordinal: true })}</td>
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

  const { register, handleSubmit, control, setError, formState: { errors, isSubmitting } } = useForm<Values>({
    defaultValues: {
      name: schedule?.name ?? '',
      frequency: schedule?.frequency ?? 'monthly',
      payDayRule: schedule?.payDayRule ?? 'last_working_day',
      payDay: schedule?.payDay == null ? '' : String(schedule.payDay),
      cutoffDay: String(schedule?.cutoffDay ?? 25),
      applies: schedule ? { all: schedule.allCompanies, ids: schedule.allCompanies ? companies.map(c => c.id) : schedule.companyIds } : defaultApplies,
      defaultFor: schedule?.defaultFor ?? [],
    },
  })
  const [applies, rule, day] = useWatch({ control, name: ['applies', 'payDayRule', 'payDay'] })
  const serves = served(applies.all, applies.ids, companies)
  const dayOptions = days.map(d => ({ value: d, label: t('schedules.day', { count: Number(d), ordinal: true }) }))

  // The next pay dates for what is filled in so far, worked out by the API (the same rules pay runs use).
  const fixed = rule === 'fixed_day'
  const preview = useQuery({
    queryKey: ['pay-dates', rule, fixed ? day : null],
    enabled: !fixed || !!day,
    queryFn: async () => {
      const { data } = await api.GET('/api/pay-schedules/pay-dates', {
        params: { query: fixed ? { payDayRule: rule, payDay: Number(day) } : { payDayRule: rule } },
      })
      if (!data) throw new Error('pay-dates')
      return data
    },
  })

  // The same checks as the API, so most mistakes show before anything is sent.
  const check = (v: Values) => {
    const problems: [FieldPath<Values>, string][] = []
    if (v.name.trim().length < 2 || v.name.trim().length > 60) problems.push(['name', t('schedules.nameInvalid')])
    if (v.payDayRule === 'fixed_day' && !v.payDay) problems.push(['payDay', t('schedules.payDayInvalid')])
    if (!v.applies.all && v.applies.ids.length === 0) problems.push(['applies', t('schedules.companiesRequired')])
    for (const [name, message] of problems) setError(name, { message })
    return problems.length === 0
  }

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!check(v)) return
    const body = {
      name: v.name.trim(),
      frequency: v.frequency,
      payDayRule: v.payDayRule,
      payDay: v.payDayRule === 'fixed_day' ? Number(v.payDay) : null,
      cutoffDay: Number(v.cutoffDay),
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
      name: 'name', frequency: 'frequency', payDayRule: 'payDayRule', payDay: 'payDay', cutoffDay: 'cutoffDay', companyIds: 'applies', defaultFor: 'defaultFor',
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
            options={[{ value: 'monthly', label: t('schedules.frequencies.monthly') }]} {...field} />
        )} />
        <Controller control={control} name="payDayRule" render={({ field }) => (
          <RadioGroup label={t('schedules.payDayRule')} name={field.name} value={field.value} onChange={field.onChange}
            options={payDayRules.map(r => ({ value: r, label: t(`schedules.rules.${r}`), hint: t(`schedules.ruleHints.${r}`) }))} />
        )} />
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
        <Controller control={control} name="cutoffDay" render={({ field }) => (
          <SelectField label={t('schedules.cutoffDay')} placeholder={t('schedules.cutoffPlaceholder')} hint={t('schedules.cutoffHint')}
            error={errors.cutoffDay?.message} options={dayOptions} {...field} />
        )} />

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
