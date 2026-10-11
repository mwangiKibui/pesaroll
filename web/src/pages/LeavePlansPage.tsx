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
import { PlusIcon } from '../components/icons'
import { Modal } from '../components/Modal'
import { MultiSelectField } from '../components/MultiSelectField'
import { TableCard } from '../components/TableCard'
import { TabPanel, Tabs } from '../components/Tabs'
import { useToast } from '../components/toast'
import { useCompanies, useLeavePlans, useLeaveTypes, useSchedules, useSetupDone } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type LeaveType = components['schemas']['LeaveTypeResponse']
type LeavePlan = components['schemas']['LeavePlanResponse']
type Schedule = components['schemas']['PayScheduleResponse']
type Tab = 'plans' | 'types'

/** How leave is earned (same keys as the API). */
const earnMethods = ['yearly', 'monthly', 'weekly', 'per_day_worked', 'per_hour_worked', 'each_time', 'unlimited'] as const

/** A rule being edited in the plan drawer; saved with the plan. */
interface DraftRule {
  leaveTypeId: string
  earnMethod: string
  amount: number | null
  unit: 'days' | 'hours'
  halfPay: number
  prorate: boolean
  carryOverMax: number
  waitingMonths: number
}

/** Working days in a year, for leave earned per day or hour worked (as the API). */
const workingDaysAYear = 261

/** What a way of earning adds up to in a year, in days (as the API's LeaveMinimums.DaysAYear). */
function daysAYear(earn: string, amount: number | null, unit: string, hoursPerDay: number): number | null {
  if (amount == null || earn === 'unlimited') return null
  const inUnit = earn === 'monthly' ? amount * 12 : earn === 'weekly' ? amount * 52 : earn === 'per_day_worked' ? amount * workingDaysAYear
    : earn === 'per_hour_worked' ? amount * hoursPerDay * workingDaysAYear : amount
  return Math.round((unit === 'hours' ? inUnit / hoursPerDay : inUnit) * 10) / 10
}

/** Ready-made plans to start from, by statutory key (and UNPAID); other leave in the plan is kept. */
type Template = Record<string, Partial<DraftRule>>
const base: Template = {
  annual: { earnMethod: 'monthly', amount: 1.75 },
  sick: { earnMethod: 'yearly', amount: 7, halfPay: 7, waitingMonths: 2 },
  maternity: { earnMethod: 'each_time', amount: 90 },
  paternity: { earnMethod: 'each_time', amount: 14 },
  pre_adoptive: { earnMethod: 'each_time', amount: 30 },
  UNPAID: { earnMethod: 'unlimited', amount: null },
}
const templates: Record<string, Template> = {
  monthly: base,
  weekly: { ...base, annual: { earnMethod: 'weekly', amount: 0.4038 } },
  daily: { ...base, annual: { earnMethod: 'per_day_worked', amount: 0.0805 } },
  shift: {
    ...base,
    annual: { earnMethod: 'per_hour_worked', amount: 0.0805, unit: 'hours' },
    sick: { earnMethod: 'yearly', amount: 56, halfPay: 56, waitingMonths: 2, unit: 'hours' },
  },
}

const blankRule = (leaveTypeId: string): DraftRule => ({
  leaveTypeId, earnMethod: 'yearly', amount: null, unit: 'days', halfPay: 0, prorate: true, carryOverMax: 0, waitingMonths: 0,
})

/** The template's rules for the organization's types, plus the plan's other (own) leave. */
function fromTemplate(template: Template, types: LeaveType[], current: DraftRule[]): DraftRule[] {
  const key = (t: LeaveType) => t.lockedKey ?? t.code
  const made = types.filter(t => template[key(t)]).map(t => ({ ...blankRule(t.id), ...template[key(t)] }))
  return [...made, ...current.filter(r => !made.some(m => m.leaveTypeId === r.leaveTypeId))]
}

const none = <span className="text-muted">—</span>

/**
 * ONB-08: what leave people get, however they're paid. The law's leave types and a Standard plan
 * at the legal minimums come with the first company; plans give leave each year, month or week,
 * per day or hour worked, or each time, in days or hours, never below the law.
 */
export default function LeavePlansPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const tabsId = useId()
  const companies = useCompanies()
  const types = useLeaveTypes()
  const plans = useLeavePlans()
  const schedules = useSchedules()
  const done = useSetupDone(true)
  const [tab, setTab] = useState<Tab>('plans')
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [editing, setEditing] = useState<{ kind: 'plan'; item: LeavePlan | null } | { kind: 'type'; item: LeaveType | null } | null>(null)
  const [deleting, setDeleting] = useState<{ kind: Tab; id: string; name: string } | null>(null)
  const [busy, setBusy] = useState(false)

  const ready = companies.data && types.data && plans.data && schedules.data
  const visible = (plans.data ?? []).filter(p => shown.all || p.allCompanies || p.companyIds.some(id => shown.ids.includes(id)))
  const companyNames = (ids: string[]) => (companies.data ?? []).filter(c => ids.includes(c.id)).map(c => c.legalName)
  const ruleText = useRuleText()

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      const { error, response } = deleting.kind === 'plans'
        ? await api.DELETE('/api/leave-plans/{id}', { params: { path: { id: deleting.id } } })
        : await api.DELETE('/api/leave-types/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        void queryClient.invalidateQueries({ queryKey: [deleting.kind === 'plans' ? 'leavePlans' : 'leaveTypes'] })
        toast.show(t('leavePlans.deleted'))
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

  const editIcon = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
  const deleteIcon = <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>

  return (
    <SetupLayout current="leavePlans" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('leavePlans.title')}</h1>
        <p className="m-0 text-muted">{t('leavePlans.intro')}</p>
      </div>
      {!ready ? <Loading rows={4} /> : <>
        {companies.data.length > 1 && (
          <div className="max-w-sm">
            <CompanySelect label={t('leavePlans.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <div className="flex flex-col gap-4">
          {/* One Add for plans and types, on the tab row; it asks which, starting with the tab you're on. */}
          <div className="flex flex-wrap items-center gap-3">
            <Tabs id={tabsId} label={t('leavePlans.kinds')} current={tab} onChange={key => setTab(key as Tab)}
              tabs={(['plans', 'types'] as const).map(k => ({ key: k, label: t(`leavePlans.tabs.${k}`) }))} />
            <div className="ml-auto flex gap-2">
              <Button onClick={() => setEditing(tab === 'plans' ? { kind: 'plan', item: null } : { kind: 'type', item: null })}>
                <PlusIcon />{t('leavePlans.add')}
              </Button>
            </div>
          </div>
          <TabPanel id={tabsId} tab="plans" current={tab}>
            <TableCard title={t('leavePlans.tabs.plans')} count={visible.length}>
              {visible.length === 0 ? <EmptyState title={t('leavePlans.emptyTitle')}>{t('leavePlans.emptyText')}</EmptyState> : (
                <table>
                  <thead>
                    <tr>
                      <th scope="col">{t('leavePlans.plan')}</th>
                      <th scope="col">{t('leavePlans.leave')}</th>
                      <th scope="col">{t('leavePlans.appliesTo')}</th>
                      <th scope="col">{t('leavePlans.defaultFor')}</th>
                      <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    {visible.map(p => (
                      <tr key={p.id}>
                        <td className="font-medium text-ink">{p.name}</td>
                        <td className="text-hint">
                          {p.rules.map(r => {
                            const type = types.data.find(x => x.id === r.leaveTypeId)
                            return <span key={r.id} className="block">{type?.name}: {ruleText(r)}</span>
                          })}
                        </td>
                        <td>{p.allCompanies ? t('leavePlans.allCompanies') : companyNames(p.companyIds).join(', ') || none}</td>
                        <td>{[...companyNames(p.defaultFor), ...schedules.data.filter(s => p.defaultForSchedules.includes(s.id)).map(s => s.name)].join(', ') || none}</td>
                        <td className="whitespace-nowrap text-right">
                          <IconButton label={t('leavePlans.edit', { name: p.name })} onClick={() => setEditing({ kind: 'plan', item: p })}>{editIcon}</IconButton>
                          <IconButton tone="danger" label={t('leavePlans.delete', { name: p.name })} onClick={() => setDeleting({ kind: 'plans', id: p.id, name: p.name })}>{deleteIcon}</IconButton>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </TableCard>
          </TabPanel>
          <TabPanel id={tabsId} tab="types" current={tab}>
            <TableCard title={t('leavePlans.tabs.types')} count={types.data.length}>
              <table>
                <thead>
                  <tr>
                    <th scope="col">{t('leavePlans.leaveType')}</th>
                    <th scope="col">{t('leavePlans.countedIn')}</th>
                    <th scope="col">{t('leavePlans.paid')}</th>
                    <th scope="col">{t('leavePlans.proof')}</th>
                    <th scope="col">{t('leavePlans.whenUsedUp')}</th>
                    <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                  </tr>
                </thead>
                <tbody>
                  {types.data.map(lt => (
                    <tr key={lt.id}>
                      <td>
                        <span className="block font-medium text-ink">{lt.name}</span>
                        <span className="block font-mono text-hint text-muted">{lt.code}</span>
                      </td>
                      <td>{t(`leavePlans.counts.${lt.counts}`)}</td>
                      <td>{t(lt.paid ? 'leavePlans.paid' : 'leavePlans.unpaid')}</td>
                      <td>{lt.needsProof ? t('leavePlans.needed') : none}</td>
                      <td>{t(`leavePlans.usedUpShort.${lt.whenUsedUp}`)}</td>
                      <td className="whitespace-nowrap text-right">
                        {lt.locked && (
                          <span className="mr-1 inline-flex items-center gap-1.5 rounded-full bg-raised px-2.5 py-1 text-tag text-muted">
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V7a4 4 0 018 0v4" /></svg>
                            {t('leavePlans.setByLaw')}
                          </span>
                        )}
                        <IconButton label={t('leavePlans.edit', { name: lt.name })} onClick={() => setEditing({ kind: 'type', item: lt })}>{editIcon}</IconButton>
                        {!lt.locked && (
                          <IconButton tone="danger" label={t('leavePlans.delete', { name: lt.name })} onClick={() => setDeleting({ kind: 'types', id: lt.id, name: lt.name })}>{deleteIcon}</IconButton>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableCard>
          </TabPanel>
        </div>
        <Proceed label={t('setup.proceed', { step: t('setup.steps.employees') })} onProceed={() => void navigate({ to: '/setup/employees' })} />

        {editing?.kind === 'plan' && (
          <PlanDrawer plan={editing.item} types={types.data} companies={companies.data} schedules={schedules.data}
            defaultApplies={companies.data.length === 1 ? { all: false, ids: [companies.data[0].id] } : shown}
            onSwitch={() => setEditing({ kind: 'type', item: null })} onClose={() => setEditing(null)} />
        )}
        {editing?.kind === 'type' && (
          <TypeDrawer type={editing.item} onSwitch={() => setEditing({ kind: 'plan', item: null })} onClose={() => setEditing(null)} />
        )}
      </>}
      <ConfirmDialog open={!!deleting} title={t('leavePlans.deleteTitle', { name: deleting?.name })}
        confirmLabel={t('leavePlans.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {deleting?.kind === 'plans' ? t('leavePlans.deletePlanText') : t('leavePlans.deleteTypeText')}
      </ConfirmDialog>
    </SetupLayout>
  )
}

/** "1.75 days a month", "7 days a year + 7 on half pay", "no limit". */
function useRuleText() {
  const { t } = useTranslation()
  return (r: { earnMethod: string; amount?: number | string | null; unit: string; halfPay: number | string }) => {
    const text = t(`leavePlans.ruleText.${r.earnMethod}`, { amount: r.amount == null ? '' : Number(r.amount), unit: t(`leavePlans.units.${r.unit}`) })
    return Number(r.halfPay) > 0 ? text + t('leavePlans.halfPayText', { amount: Number(r.halfPay) }) : text
  }
}

/** The legal problems with a rule, by field, and a warning for leave earned by what's worked (as the API). */
function useLawCheck() {
  const { t } = useTranslation()
  return (rule: DraftRule, type: LeaveType | undefined, hoursPerDay: number) => {
    const problems: Partial<Record<'amount' | 'waitingMonths', string>> = {}
    let warning: string | undefined
    if (!type?.locked || type.minFullPayDays == null) return { problems, warning }
    const full = Number(type.minFullPayDays)
    const half = Number(type.minHalfPayDays ?? 0)
    const waiting = Number(type.minWaitingMonths ?? 0)
    if (['yearly', 'monthly', 'weekly', 'each_time'].includes(rule.earnMethod)) {
      const got = daysAYear(rule.earnMethod, rule.amount, rule.unit, hoursPerDay) ?? 0
      const gotHalf = daysAYear(rule.earnMethod, rule.halfPay, rule.unit, hoursPerDay) ?? 0
      if (got < full || got + gotHalf < full + half) {
        problems.amount = half > 0 ? t('leavePlans.lawFullHalf', { name: type.name, full, half })
          : t(rule.earnMethod === 'each_time' ? 'leavePlans.lawEach' : 'leavePlans.lawYear', { name: type.name, full })
      }
    } else if (rule.earnMethod !== 'unlimited') {
      const year = daysAYear(rule.earnMethod, rule.amount, rule.unit, hoursPerDay) ?? 0
      if (year < full) warning = t('leavePlans.lawWorkedWarning', { days: year, full })
    }
    if (rule.waitingMonths > waiting) {
      problems.waitingMonths = waiting === 0 ? t('leavePlans.lawWaitingNone', { name: type.name }) : t('leavePlans.lawWaiting', { name: type.name, months: waiting })
    }
    return { problems, warning }
  }
}

interface PlanValues {
  template: string
  name: string
  leaveYear: string
  hoursPerDay: string
  applies: CompanySelection
  defaultFor: string[]
  defaultForSchedules: string[]
  rules: DraftRule[]
}

function PlanDrawer({ plan, types, companies, schedules, defaultApplies, onSwitch, onClose }: {
  plan: LeavePlan | null
  types: LeaveType[]
  companies: Company[]
  schedules: Schedule[]
  defaultApplies: CompanySelection
  onSwitch: () => void
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const ruleText = useRuleText()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const [ruleErrors, setRuleErrors] = useState<Record<string, string>>({})
  const [editingRule, setEditingRule] = useState<DraftRule | 'new' | null>(null)

  const { register, handleSubmit, control, setError, setValue, formState: { errors, isSubmitting } } = useForm<PlanValues>({
    defaultValues: {
      template: '',
      name: plan?.name ?? '',
      leaveYear: plan?.leaveYear ?? 'calendar',
      hoursPerDay: String(plan?.hoursPerDay ?? 8),
      applies: plan ? { all: plan.allCompanies, ids: plan.allCompanies ? companies.map(c => c.id) : plan.companyIds } : defaultApplies,
      defaultFor: plan?.defaultFor ?? [],
      defaultForSchedules: plan?.defaultForSchedules ?? [],
      rules: plan
        ? plan.rules.map(r => ({
          leaveTypeId: r.leaveTypeId, earnMethod: r.earnMethod, amount: r.amount == null ? null : Number(r.amount), unit: r.unit as 'days' | 'hours',
          halfPay: Number(r.halfPay), prorate: r.prorate, carryOverMax: Number(r.carryOverMax), waitingMonths: Number(r.waitingMonths),
        }))
        : fromTemplate(templates.monthly, types, []),
    },
  })
  const [applies, rules, hoursText] = useWatch({ control, name: ['applies', 'rules', 'hoursPerDay'] })
  const hoursPerDay = Number(hoursText) || 8
  const serves = applies.all ? companies : companies.filter(c => applies.ids.includes(c.id))
  const typeOf = (id: string) => types.find(x => x.id === id)

  const equivalence = (r: DraftRule) => {
    const days = daysAYear(r.earnMethod, r.amount, r.unit, hoursPerDay)
    if (days == null || r.earnMethod === 'each_time' || (r.earnMethod === 'yearly' && r.unit === 'days')) return ''
    return ` (${t(r.earnMethod.startsWith('per_') ? 'leavePlans.yearTotalWorked' : 'leavePlans.yearTotal', { days })})`
  }

  const startFrom = (key: string) => {
    const template = templates[key]
    if (!template) return
    setValue('name', t(`leavePlans.templates.${key}`))
    setValue('rules', fromTemplate(template, types, rules))
  }

  const check = (v: PlanValues) => {
    const problems: [FieldPath<PlanValues>, string][] = []
    if (v.name.trim().length < 2 || v.name.trim().length > 80) problems.push(['name', t('leavePlans.planNameInvalid')])
    const hours = Number(v.hoursPerDay)
    if (!(hours >= 1 && hours <= 24)) problems.push(['hoursPerDay', t('leavePlans.hoursPerDayInvalid')])
    if (!v.applies.all && v.applies.ids.length === 0) problems.push(['applies', t('leavePlans.companiesRequired')])
    for (const [name, message] of problems) setError(name, { message })
    return problems.length === 0
  }

  const onSubmit = async (v: PlanValues) => {
    setBanner(null)
    setRuleErrors({})
    if (!check(v)) return
    const body = {
      name: v.name.trim(),
      leaveYear: v.leaveYear,
      hoursPerDay: Number(v.hoursPerDay),
      allCompanies: v.applies.all,
      companyIds: v.applies.all ? [] : v.applies.ids,
      // Only companies it still serves can have it as default.
      defaultFor: v.defaultFor.filter(id => v.applies.all || v.applies.ids.includes(id)),
      defaultForSchedules: v.defaultForSchedules,
      rules: v.rules.map(r => ({ ...r, amount: r.earnMethod === 'unlimited' ? null : r.amount })),
    }
    try {
      const { data, error } = plan
        ? await api.PATCH('/api/leave-plans/{id}', { params: { path: { id: plan.id } }, body })
        : await api.POST('/api/leave-plans', { body })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['leavePlans'] })
        void queryClient.invalidateQueries({ queryKey: ['schedules'] })
        toast.show(plan ? t('leavePlans.planSaved') : t('leavePlans.planAdded'))
        onClose()
        return
      }
      showError(error, v.rules)
    } catch {
      setBanner('network')
    }
  }

  const showError = (error: unknown, sent: DraftRule[]) => {
    if (!isApiError(error)) return setBanner('network')
    if (error.code === 'ONB-3533') return setError('name', { message: errorMessage(t, error) })
    const fields: Record<string, FieldPath<PlanValues>> = {
      name: 'name', leaveYear: 'leaveYear', hoursPerDay: 'hoursPerDay', companyIds: 'applies', defaultFor: 'defaultFor', defaultForSchedules: 'defaultForSchedules', rules: 'rules',
    }
    // Rule problems ("rules[1].amount") show on their rule.
    const onRules = Object.entries(error.fields ?? {}).filter(([name]) => name.startsWith('rules['))
    setRuleErrors(Object.fromEntries(onRules.map(([name, message]) => [sent[Number(/\[(\d+)\]/.exec(name)?.[1])]?.leaveTypeId ?? '', message])))
    const known = Object.entries(error.fields ?? {}).filter(([name]) => fields[name])
    for (const [name, message] of known) setError(fields[name], { message })
    if (known.length === 0 && onRules.length === 0) setBanner(error)
  }

  const free = types.filter(x => !rules.some(r => r.leaveTypeId === x.id))

  return (
    <Drawer open size="wide" title={plan ? t('leavePlans.editPlanTitle') : t('leavePlans.planTitle')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{plan ? t('leavePlans.save') : t('leavePlans.addPlan')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        {!plan && <>
          <RadioGroup label={t('leavePlans.whatAdding')} name={`${formId}-kind`} value="plan" onChange={value => { if (value === 'type') onSwitch() }}
            options={[{ value: 'plan', label: t('leavePlans.adding.plan') }, { value: 'type', label: t('leavePlans.adding.type') }]} />
          <Controller control={control} name="template" render={({ field }) => (
            <SelectField label={t('leavePlans.startFrom')} placeholder={t('leavePlans.startFromPlaceholder')} hint={t('leavePlans.startFromHint')} optional
              options={Object.keys(templates).map(k => ({ value: k, label: t(`leavePlans.templates.${k}`) }))}
              {...field} onChange={value => { field.onChange(value); startFrom(value) }} />
          )} />
        </>}
        <TextField label={t('leavePlans.planName')} placeholder={t('leavePlans.planNamePlaceholder')} maxLength={80} error={errors.name?.message} {...register('name')} />
        <Controller control={control} name="leaveYear" render={({ field }) => (
          <RadioGroup label={t('leavePlans.leaveYear')} name={field.name} value={field.value} onChange={field.onChange}
            options={(['calendar', 'anniversary'] as const).map(k => ({ value: k, label: t(`leavePlans.leaveYears.${k}`) }))} />
        )} />
        <TextField label={t('leavePlans.hoursPerDay')} placeholder={t('leavePlans.hoursPerDayPlaceholder')} hint={t('leavePlans.hoursPerDayHint')}
          inputMode="decimal" error={errors.hoursPerDay?.message} {...register('hoursPerDay')} />

        <section aria-labelledby={`${formId}-rules`} className="flex flex-col gap-2">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between sm:gap-6">
            <h3 id={`${formId}-rules`} className="m-0 text-label font-medium text-ink">{t('leavePlans.rules')}</h3>
            <Button variant="secondary" className="flex-none self-end whitespace-nowrap sm:self-start" disabled={free.length === 0}
              onClick={() => setEditingRule('new')}><PlusIcon />{t('leavePlans.addRule')}</Button>
          </div>
          <ul aria-label={t('leavePlans.rules')} className="m-0 flex list-none flex-col divide-y divide-line-soft rounded-control border border-line p-0">
            {rules.map(r => {
              const type = typeOf(r.leaveTypeId)
              return (
                <li key={r.leaveTypeId} className="flex items-center gap-2 px-3 py-2">
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span className="text-body text-ink">{type?.name}: {ruleText(r)}{equivalence(r)}</span>
                    {ruleErrors[r.leaveTypeId] && <span className="text-hint font-medium text-danger-ink">{ruleErrors[r.leaveTypeId]}</span>}
                  </span>
                  <IconButton label={t('leavePlans.editRule', { name: type?.name })} onClick={() => setEditingRule(r)}>
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                  </IconButton>
                  {!type?.locked && (
                    <IconButton tone="danger" label={t('leavePlans.removeRule', { name: type?.name })}
                      onClick={() => setValue('rules', rules.filter(x => x.leaveTypeId !== r.leaveTypeId))}>
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                    </IconButton>
                  )}
                </li>
              )
            })}
          </ul>
          {errors.rules?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.rules.message}</p>}
        </section>

        {companies.length > 1 && (
          <div className="flex flex-col gap-1.5">
            <Controller control={control} name="applies" render={({ field }) => (
              <CompanySelect label={t('leavePlans.appliesTo')} value={field.value} onChange={field.onChange}
                companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
            )} />
            {errors.applies?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.applies.message}</p>}
          </div>
        )}
        <fieldset className="m-0 flex flex-col gap-1 border-0 p-0">
          <legend className="mb-1 p-0 text-label font-medium text-ink">{t('leavePlans.defaultFor')}</legend>
          <p className="m-0 text-hint text-muted">{t('leavePlans.defaultHint')}</p>
          <Controller control={control} name="defaultFor" render={({ field }) => <>
            {serves.map(c => (
              <Checkbox key={c.id} label={t('leavePlans.defaultForCompany', { name: c.legalName })} checked={field.value.includes(c.id)}
                onChange={e => field.onChange(e.target.checked ? [...field.value, c.id] : field.value.filter(id => id !== c.id))} />
            ))}
          </>} />
          {errors.defaultFor?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.defaultFor.message}</p>}
        </fieldset>
        <Controller control={control} name="defaultForSchedules" render={({ field }) => (
          <MultiSelectField label={t('leavePlans.defaultForSchedules')} hint={t('leavePlans.defaultForSchedulesHint')} placeholder={t('leavePlans.schedulesPlaceholder')}
            error={errors.defaultForSchedules?.message} options={schedules.map(s => ({ value: s.id, label: s.name }))} value={field.value} onChange={field.onChange} />
        )} />
      </form>
      {editingRule && (
        <RuleModal rule={editingRule === 'new' ? null : editingRule} types={editingRule === 'new' ? free : types} hoursPerDay={hoursPerDay}
          onCancel={() => setEditingRule(null)}
          onSave={saved => {
            setValue('rules', editingRule === 'new' ? [...rules, saved] : rules.map(r => (r.leaveTypeId === saved.leaveTypeId ? saved : r)))
            setRuleErrors(e => ({ ...e, [saved.leaveTypeId]: '' }))
            setEditingRule(null)
          }} />
      )}
    </Drawer>
  )
}

/** How a plan gives one leave type, in a small form on top of the drawer, checked against the law. */
function RuleModal({ rule, types, hoursPerDay, onSave, onCancel }: {
  rule: DraftRule | null
  types: LeaveType[]
  hoursPerDay: number
  onSave: (rule: DraftRule) => void
  onCancel: () => void
}) {
  const { t } = useTranslation()
  const formId = useId()
  const lawCheck = useLawCheck()
  const [typeId, setTypeId] = useState(rule?.leaveTypeId ?? '')
  const [earn, setEarn] = useState(rule?.earnMethod ?? 'yearly')
  const [unit, setUnit] = useState<'days' | 'hours'>(rule?.unit ?? 'days')
  const [amount, setAmount] = useState(rule?.amount == null ? '' : String(rule.amount))
  const [halfPay, setHalfPay] = useState(rule?.halfPay ? String(rule.halfPay) : '')
  const [prorate, setProrate] = useState(rule?.prorate ?? true)
  const [carry, setCarry] = useState(rule?.carryOverMax ? String(rule.carryOverMax) : '')
  const [waiting, setWaiting] = useState(rule?.waitingMonths ? String(rule.waitingMonths) : '')
  const [errors, setErrors] = useState<Partial<Record<'type' | 'amount' | 'waitingMonths' | 'number', string>>>({})
  const type = types.find(x => x.id === typeId)
  const unitWord = t(`leavePlans.unitWords.${unit}`)

  const draft = (): DraftRule => ({
    leaveTypeId: typeId, earnMethod: earn, unit, amount: earn === 'unlimited' || amount.trim() === '' ? null : Number(amount),
    halfPay: earn === 'yearly' || earn === 'each_time' ? Number(halfPay || 0) : 0, prorate, carryOverMax: Number(carry || 0), waitingMonths: Number(waiting || 0),
  })
  const current = draft()
  const year = daysAYear(earn, current.amount, unit, hoursPerDay)
  const { warning } = lawCheck(current, type, hoursPerDay)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const r = draft()
    const found: typeof errors = {}
    if (!typeId) found.type = t('leavePlans.leaveTypePlaceholder')
    if (earn !== 'unlimited' && !(r.amount != null && r.amount > 0)) found.amount = t('leavePlans.amountInvalid')
    if ([r.halfPay, r.carryOverMax, r.waitingMonths].some(n => !(n >= 0))) found.number = t('leavePlans.numberInvalid')
    const { problems } = lawCheck(r, type, hoursPerDay)
    Object.assign(found, { ...problems, ...found })
    setErrors(found)
    if (Object.keys(found).length === 0) onSave(r)
  }

  return (
    <Modal open title={rule ? type?.name ?? '' : t('leavePlans.addRule')} onClose={onCancel}
      footer={<>
        <Button variant="secondary" onClick={onCancel}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId}>{t('leavePlans.saveRule')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={submit} className="flex flex-col gap-4">
        {!rule && (
          <SelectField label={t('leavePlans.leaveType')} placeholder={t('leavePlans.leaveTypePlaceholder')} error={errors.type}
            options={types.map(x => ({ value: x.id, label: x.name }))} value={typeId} onChange={setTypeId} />
        )}
        <SelectField label={t('leavePlans.earned')} placeholder={t('leavePlans.earnedPlaceholder')}
          options={earnMethods.map(m => ({ value: m, label: t(`leavePlans.earnMethods.${m}`) }))} value={earn} onChange={setEarn} />
        {earn !== 'unlimited' && <>
          <RadioGroup label={t('leavePlans.countedIn')} name={`${formId}-unit`} value={unit} onChange={v => setUnit(v as 'days' | 'hours')}
            options={(['days', 'hours'] as const).map(u => ({ value: u, label: t(`leavePlans.unitOptions.${u}`) }))} />
          <div className="flex flex-col gap-1">
            <TextField label={t(`leavePlans.amountLabels.${earn}`, { unit: unitWord })} placeholder={t('leavePlans.amountPlaceholder')} inputMode="decimal"
              error={errors.amount} value={amount} onChange={e => { setAmount(e.target.value); setErrors({}) }} />
            {year != null && earn !== 'each_time' && !(earn === 'yearly' && unit === 'days') && (
              <p className="m-0 text-hint text-muted">{t(earn.startsWith('per_') ? 'leavePlans.yearTotalWorked' : 'leavePlans.yearTotal', { days: year })}</p>
            )}
            {warning && <p className="m-0 text-hint font-medium text-warning-ink">{warning}</p>}
          </div>
        </>}
        {(earn === 'yearly' || earn === 'each_time') && (
          <TextField label={t('leavePlans.halfPay', { unit: unitWord })} placeholder={t('leavePlans.numberPlaceholder')} hint={t('leavePlans.halfPayHint')} optional
            inputMode="decimal" value={halfPay} onChange={e => { setHalfPay(e.target.value); setErrors({}) }} />
        )}
        {earn === 'yearly' && <Switch label={t('leavePlans.prorate')} hint={t('leavePlans.prorateHint')} checked={prorate} onChange={setProrate} />}
        {earn !== 'each_time' && earn !== 'unlimited' && (
          <TextField label={t('leavePlans.carryOver')} placeholder={t('leavePlans.numberPlaceholder')} hint={t('leavePlans.carryOverHint')} optional
            inputMode="decimal" value={carry} onChange={e => setCarry(e.target.value)} />
        )}
        <TextField label={t('leavePlans.waiting')} placeholder={t('leavePlans.numberPlaceholder')} optional inputMode="numeric"
          error={errors.waitingMonths ?? errors.number} value={waiting} onChange={e => { setWaiting(e.target.value); setErrors({}) }} />
      </form>
    </Modal>
  )
}

interface TypeValues {
  common: string
  name: string
  code: string
  counts: string
  paid: boolean
  needsProof: boolean
  whenUsedUp: string
}

/** Common leave types that fill in the form. */
const commonTypes: Record<string, Omit<TypeValues, 'common' | 'name' | 'code'>> = {
  compassionate: { counts: 'working_days', paid: true, needsProof: false, whenUsedUp: 'unpaid' },
  study: { counts: 'working_days', paid: true, needsProof: true, whenUsedUp: 'stop' },
  pto: { counts: 'working_days', paid: true, needsProof: false, whenUsedUp: 'stop' },
}

function TypeDrawer({ type, onSwitch, onClose }: { type: LeaveType | null; onSwitch: () => void; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { register, handleSubmit, control, setError, setValue, formState: { errors, isSubmitting } } = useForm<TypeValues>({
    defaultValues: {
      common: '', name: type?.name ?? '', code: type?.code ?? '', counts: type?.counts ?? 'working_days', paid: type?.paid ?? true,
      needsProof: type?.needsProof ?? false, whenUsedUp: type?.whenUsedUp ?? 'stop',
    },
  })
  const locked = !!type?.locked

  const startFrom = (key: string) => {
    const common = commonTypes[key]
    if (!common) return
    setValue('name', t(`leavePlans.commons.${key}`))
    for (const [field, value] of Object.entries(common)) setValue(field as keyof TypeValues, value as never)
  }

  const onSubmit = async (v: TypeValues) => {
    setBanner(null)
    const problems: [FieldPath<TypeValues>, string][] = []
    if (v.name.trim().length < 2 || v.name.trim().length > 80) problems.push(['name', t('leavePlans.nameInvalid')])
    if (v.code.trim() && !/^[A-Z0-9][A-Z0-9-]{1,19}$/.test(v.code.trim().toUpperCase())) problems.push(['code', t('leavePlans.codeInvalid')])
    for (const [name, message] of problems) setError(name, { message })
    if (problems.length) return
    const body = {
      code: v.code.trim() ? v.code.trim().toUpperCase() : null, name: v.name.trim(), counts: v.counts, paid: v.paid, needsProof: v.needsProof, whenUsedUp: v.whenUsedUp,
    }
    try {
      const { data, error } = type
        ? await api.PATCH('/api/leave-types/{id}', { params: { path: { id: type.id } }, body })
        : await api.POST('/api/leave-types', { body })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['leaveTypes'] })
        toast.show(type ? t('leavePlans.typeSaved') : t('leavePlans.typeAdded'))
        onClose()
        return
      }
      if (!isApiError(error)) return setBanner('network')
      if (error.code === 'ONB-3533') return setError('code', { message: errorMessage(t, error) })
      const known = Object.entries(error.fields ?? {}).filter(([name]) => ['name', 'code'].includes(name))
      for (const [name, message] of known) setError(name as FieldPath<TypeValues>, { message })
      if (known.length === 0) setBanner(error)
    } catch {
      setBanner('network')
    }
  }

  return (
    <Drawer open size="wide" title={type ? t('leavePlans.editTypeTitle') : t('leavePlans.typeTitle')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{type ? t('leavePlans.save') : t('leavePlans.addType')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        {!type && <>
          <RadioGroup label={t('leavePlans.whatAdding')} name={`${formId}-kind`} value="type" onChange={value => { if (value === 'plan') onSwitch() }}
            options={[{ value: 'plan', label: t('leavePlans.adding.plan') }, { value: 'type', label: t('leavePlans.adding.type') }]} />
          <Controller control={control} name="common" render={({ field }) => (
            <SelectField label={t('leavePlans.common')} placeholder={t('leavePlans.commonPlaceholder')} hint={t('leavePlans.commonHint')} optional
              options={Object.keys(commonTypes).map(k => ({ value: k, label: t(`leavePlans.commons.${k}`) }))}
              {...field} onChange={value => { field.onChange(value); startFrom(value) }} />
          )} />
        </>}
        {locked ? <Notice tone="brand">{t('leavePlans.lockedNote')}</Notice> : <>
          <TextField label={t('leavePlans.name')} placeholder={t('leavePlans.namePlaceholder')} maxLength={80} error={errors.name?.message} {...register('name')} />
          <TextField label={t('leavePlans.code')} placeholder={t('leavePlans.codePlaceholder')} hint={t('leavePlans.codeHint')} optional maxLength={20}
            autoCapitalize="characters" error={errors.code?.message} {...register('code')} />
          <Controller control={control} name="counts" render={({ field }) => (
            <RadioGroup label={t('leavePlans.countedIn')} name={field.name} value={field.value} onChange={field.onChange}
              options={(['working_days', 'calendar_days'] as const).map(c => ({ value: c, label: t(`leavePlans.counts.${c}`) }))} />
          )} />
          <Controller control={control} name="paid" render={({ field }) => (
            <Switch label={t('leavePlans.paid')} hint={t('leavePlans.paidHint')} checked={field.value} onChange={field.onChange} />
          )} />
        </>}
        <Controller control={control} name="needsProof" render={({ field }) => (
          <Switch label={t('leavePlans.needsProof')} hint={t('leavePlans.needsProofHint')} checked={field.value} onChange={field.onChange} />
        )} />
        <Controller control={control} name="whenUsedUp" render={({ field }) => (
          <RadioGroup label={t('leavePlans.whenUsedUp')} name={field.name} value={field.value} onChange={field.onChange}
            options={(['stop', 'unpaid'] as const).map(k => ({ value: k, label: t(`leavePlans.usedUp.${k}`) }))} />
        )} />
      </form>
    </Drawer>
  )
}
