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
import { RadioGroup, SelectField, Switch, TextField } from '../components/fields'
import { PlusIcon } from '../components/icons'
import { Money } from '../components/Money'
import { Pagination } from '../components/Pagination'
import { TableCard } from '../components/TableCard'
import { usePaged } from '../components/usePaged'
import { TabPanel, Tabs } from '../components/Tabs'
import { useToast } from '../components/toast'
import { useCompanies, usePayees, usePayItems, useSetupDone } from '../setup/hooks'
import { PayeeModal } from './PayeeModal'
import { PayItemView } from './PayItemView'
import { draftsFrom, requestFor, type CompanyDraft } from './whoDrafts'
import { WhoGetsIt } from './WhoGetsIt'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type PayItem = components['schemas']['PayItemResponse']

/** The three kinds, one tab each (same keys as the API). */
const kinds = ['earning', 'deduction', 'employer_cost'] as const
type Kind = (typeof kinds)[number]
/** How an own item is worked out; by_law is only for the law's items. */
const methods = ['fixed', 'percent_of_basic', 'each_run'] as const

interface Values {
  kind: Kind
  common: string
  name: string
  code: string
  calcMethod: string
  defaultAmount: string
  rate: string
  payeeId: string
  referenceSource: string
  taxable: boolean
  beforeTax: boolean
  applies: CompanySelection
}

/** Common items that fill in the form, per kind. */
const commons: { key: string; kind: Kind; calcMethod: string; taxable?: boolean; beforeTax?: boolean; rate?: number }[] = [
  { key: 'house', kind: 'earning', calcMethod: 'fixed', taxable: true },
  { key: 'commuter', kind: 'earning', calcMethod: 'fixed', taxable: true },
  { key: 'airtime', kind: 'earning', calcMethod: 'fixed', taxable: true },
  { key: 'overtime', kind: 'earning', calcMethod: 'each_run', taxable: true },
  { key: 'bonus', kind: 'earning', calcMethod: 'each_run', taxable: true },
  { key: 'commission', kind: 'earning', calcMethod: 'each_run', taxable: true },
  { key: 'advance', kind: 'deduction', calcMethod: 'each_run' },
  { key: 'loan', kind: 'deduction', calcMethod: 'fixed' },
  { key: 'sacco', kind: 'deduction', calcMethod: 'fixed' },
  { key: 'union', kind: 'deduction', calcMethod: 'fixed' },
  { key: 'welfare', kind: 'deduction', calcMethod: 'fixed' },
  { key: 'pension', kind: 'deduction', calcMethod: 'percent_of_basic', beforeTax: true, rate: 5 },
  { key: 'medical', kind: 'employer_cost', calcMethod: 'fixed' },
  { key: 'employerPension', kind: 'employer_cost', calcMethod: 'percent_of_basic', rate: 5 },
]

const none = <span className="text-muted">—</span>

/** Where each person's account number with the payee comes from (the API's keys). */
const referenceSources = ['typed', 'national_id', 'kra_pin', 'staff_no', 'phone', 'bank_account', 'mpesa_number'] as const

/** Who the law's items are paid to (their payee is the authority). */
const authorities: Record<string, string> = {
  paye: 'KRA', ahl_employee: 'KRA', ahl_employer: 'KRA', shif: 'SHA', nssf_employee: 'NSSF', nssf_employer: 'NSSF', nita: 'NITA',
}

/**
 * ONB-07, Earnings and deductions ("pay items" in code): Basic pay and the law's items (made with
 * the first company, locked, worked out by the rule pack) and the organization's own, for all or
 * chosen companies, on a tab per kind.
 */
export default function EarningsPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const tabsId = useId()
  const companies = useCompanies()
  const items = usePayItems()
  const payees = usePayees()
  const done = useSetupDone(true)
  const [tab, setTab] = useState<Kind>('earning')
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [editing, setEditing] = useState<PayItem | 'new' | null>(null)
  const [viewing, setViewing] = useState<PayItem | null>(null)
  const [deleting, setDeleting] = useState<PayItem | null>(null)
  const [busy, setBusy] = useState(false)

  const ready = companies.data && items.data
  const visible = (items.data ?? []).filter(i => shown.all || i.allCompanies || i.companyIds.some(id => shown.ids.includes(id)))
  // Each tab's table pages on its own (every table has pagination).
  const paged: Record<Kind, ReturnType<typeof usePaged<PayItem>>> = {
    earning: usePaged(visible.filter(i => i.kind === 'earning')),
    deduction: usePaged(visible.filter(i => i.kind === 'deduction')),
    employer_cost: usePaged(visible.filter(i => i.kind === 'employer_cost')),
  }
  const companyNames = (ids: string[]) => (companies.data ?? []).filter(c => ids.includes(c.id)).map(c => c.legalName).join(', ')

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      const { error, response } = await api.DELETE('/api/pay-items/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        queryClient.setQueryData<PayItem[]>(['payItems'], old => old?.filter(i => i.id !== deleting.id))
        toast.show(t('payItems.deleted'))
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

  const method = (i: PayItem) => (i.calcMethod === 'percent_of_basic'
    ? t('payItems.percentOfBasic', { rate: Number(i.rate) })
    : t(`payItems.methodShort.${i.calcMethod}`))
  // "Everyone at Pesaroll Ltd · 3 people at Centrail Ltd"; the law's items and basic pay are everyone's.
  const whoGetsIt = (i: PayItem) => {
    if (i.locked) return t('who.everyoneShort')
    if (i.assignments.length === 0) return <span className="text-muted">{t('who.noOne')}</span>
    const name = (id: string) => companies.data?.find(c => c.id === id)?.legalName ?? ''
    return i.assignments.map(a => (a.everyone ? t('who.everyoneAt', { company: name(a.companyId) })
      : t('who.peopleAt', { count: Number(a.people), company: name(a.companyId) }))).join(' · ')
  }
  const tax = (i: PayItem) => {
    if (i.kind === 'earning') return t(i.taxable ? 'payItems.taxed' : 'payItems.notTaxed')
    if (i.kind === 'deduction') return t(i.beforeTax ? 'payItems.beforeTax' : 'payItems.afterTax')
    return none
  }

  return (
    <SetupLayout current="payItems" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('payItems.title')}</h1>
        <p className="m-0 text-muted">{t('payItems.intro')}</p>
      </div>
      {!ready ? <Loading rows={4} /> : <>
        {companies.data.length > 1 && (
          <div className="max-w-sm">
            <CompanySelect label={t('payItems.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <div className="flex flex-col gap-4">
          {/* One Add for every kind, on the tab row; it starts on the tab you're on. */}
          <div className="flex flex-wrap items-center gap-3">
            <Tabs id={tabsId} label={t('payItems.kinds')} current={tab} onChange={key => setTab(key as Kind)}
              tabs={kinds.map(k => ({ key: k, label: t(`payItems.tabs.${k}`) }))} />
            <div className="ml-auto flex gap-2">
              <Button onClick={() => setEditing('new')}><PlusIcon />{t('payItems.add')}</Button>
            </div>
          </div>
          {kinds.map(k => {
            const list = visible.filter(i => i.kind === k)
            return (
              <TabPanel key={k} id={tabsId} tab={k} current={tab}>
                <TableCard title={t(`payItems.tabs.${k}`)} count={list.length}
                  footer={<Pagination label={t('pagination.of', { what: t(`payItems.tabs.${k}`) })} {...paged[k].pagination} />}>
                  {list.length === 0 ? <EmptyState title={t('payItems.emptyTitle')}>{t('payItems.emptyText')}</EmptyState> : (
                    <table>
                      <thead>
                        <tr>
                          <th scope="col">{t('payItems.name')}</th>
                          <th scope="col">{t('payItems.worked')}</th>
                          <th scope="col">{t('payItems.tax')}</th>
                          <th scope="col">{t('payItems.defaultAmount')}</th>
                          <th scope="col">{t('payItems.appliesTo')}</th>
                          {k !== 'earning' && <th scope="col">{t('payees.paidTo')}</th>}
                          <th scope="col">{t('who.title')}</th>
                          <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                        </tr>
                      </thead>
                      <tbody>
                        {paged[k].items.map(i => (
                          <tr key={i.id}>
                            <td>
                              <span className="block font-medium text-ink">{i.name}</span>
                              {/* PAYE, SHIF, NITA: the code is the name, so once is enough. */}
                              {i.code.toLowerCase() !== i.name.toLowerCase() && <span className="block font-mono text-hint text-muted">{i.code}</span>}
                            </td>
                            <td>{method(i)}</td>
                            <td>{tax(i)}</td>
                            <td>{i.defaultAmount != null ? <Money amount={i.defaultAmount} /> : none}</td>
                            <td>{i.allCompanies ? t('payItems.allCompanies') : companyNames(i.companyIds) || none}</td>
                            {k !== 'earning' && <td>{i.lockedKey ? authorities[i.lockedKey] ?? none
                              : payees.data?.find(p => p.id === i.payeeId)?.name ?? none}</td>}
                            <td>{whoGetsIt(i)}</td>
                            <td className="whitespace-nowrap text-right">
                              <IconButton label={t('payItems.view', { name: i.name })} onClick={() => setViewing(i)}>
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></svg>
                              </IconButton>
                              {i.locked ? (
                                <span className="inline-flex items-center gap-1.5 rounded-full bg-raised px-2.5 py-1 text-tag text-muted">
                                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V7a4 4 0 018 0v4" /></svg>
                                  {t(i.lockedKey === 'basic' ? 'payItems.alwaysIncluded' : 'payItems.setByLaw')}
                                </span>
                              ) : <>
                                <IconButton label={t('payItems.edit', { name: i.name })} onClick={() => setEditing(i)}>
                                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                                </IconButton>
                                <IconButton tone="danger" label={t('payItems.delete', { name: i.name })} onClick={() => setDeleting(i)}>
                                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                                </IconButton>
                              </>}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </TableCard>
              </TabPanel>
            )
          })}
        </div>
        {/* Payroll history (ONB-10) is next; until it has a page, the dashboard. */}
        <Proceed label={t('organization.proceed')} onProceed={() => void navigate({ to: '/' })} />

        {viewing && (
          <PayItemView item={viewing} companies={companies.data} payee={payees.data?.find(p => p.id === viewing.payeeId)}
            authority={viewing.lockedKey ? authorities[viewing.lockedKey] : undefined} method={method(viewing)} tax={tax(viewing)}
            onClose={() => setViewing(null)} onEdit={viewing.locked ? undefined : () => { setEditing(viewing); setViewing(null) }} />
        )}
        {editing && (
          <PayItemDrawer item={editing === 'new' ? null : editing} kind={tab} companies={companies.data}
            // A new item serves the companies being shown, as the selector suggests.
            defaultApplies={companies.data.length === 1 ? { all: false, ids: [companies.data[0].id] } : shown}
            onClose={() => setEditing(null)} />
        )}
      </>}
      <ConfirmDialog open={!!deleting} title={t('payItems.deleteTitle', { name: deleting?.name })}
        confirmLabel={t('payItems.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('payItems.deleteText')}
        {deleting && deleting.assignments.length > 0 && ` ${t('who.deleteAlso', { who: whoGetsIt(deleting) })}`}
      </ConfirmDialog>
    </SetupLayout>
  )
}

/** An item's drawer: for an existing one, who gets it is read first. */
function PayItemDrawer(props: { item: PayItem | null; kind: Kind; companies: Company[]; defaultApplies: CompanySelection; onClose: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const id = props.item?.id
  const assignments = useQuery({
    queryKey: ['payItems', id, 'assignments'],
    enabled: !!id,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/pay-items/{id}/assignments', { params: { path: { id: id! } } })
      if (error || !data) throw new Error('assignments')
      return data
    },
  })
  if (id && !assignments.data) {
    return <Drawer open size="wide" title={t('payItems.editTitle')} onClose={props.onClose}><Loading rows={6} /></Drawer>
  }
  return <PayItemForm {...props} initial={draftsFrom(assignments.data ?? [])} />
}

const drawerTabs = ['details', 'who'] as const
type DrawerTab = (typeof drawerTabs)[number]

function PayItemForm({ item: opened, kind, companies, defaultApplies, initial, onClose }: {
  item: PayItem | null
  kind: Kind
  companies: Company[]
  defaultApplies: CompanySelection
  initial: Record<string, CompanyDraft>
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const tabsId = useId()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const [tab, setTab] = useState<DrawerTab>('details')
  // Who gets it, per company; saved after the item. A new item that's saved but whose people need
  // fixing stays open as the saved item, so the next save edits it.
  const [drafts, setDrafts] = useState(initial)
  const [whoErrors, setWhoErrors] = useState<Record<string, Record<string, string>>>({})
  const [item, setItem] = useState(opened)
  const adding = !opened
  const payees = usePayees()
  const [addingPayee, setAddingPayee] = useState(false)

  const { register, handleSubmit, control, setError, setValue, getValues, clearErrors, formState: { errors, isSubmitting } } = useForm<Values>({
    defaultValues: {
      kind: (item?.kind as Kind) ?? kind,
      common: '',
      name: item?.name ?? '',
      code: item?.code ?? '',
      calcMethod: item?.calcMethod ?? 'fixed',
      defaultAmount: item?.defaultAmount != null ? String(item.defaultAmount) : '',
      rate: item?.rate != null ? String(item.rate) : '',
      payeeId: item?.payeeId ?? '',
      referenceSource: item?.referenceSource ?? 'typed',
      taxable: item ? item.taxable : kind === 'earning',
      beforeTax: item?.beforeTax ?? false,
      applies: item ? { all: item.allCompanies, ids: item.allCompanies ? companies.map(c => c.id) : item.companyIds } : defaultApplies,
    },
  })
  const [chosenKind, calcMethod, applies, itemName, defaultAmount, rate, referenceSource] =
    useWatch({ control, name: ['kind', 'calcMethod', 'applies', 'name', 'defaultAmount', 'rate', 'referenceSource'] })
  const served = applies.all ? companies : companies.filter(c => applies.ids.includes(c.id))

  // A common item fills in the form; anything can still be changed.
  const startFrom = (key: string) => {
    const common = commons.find(c => c.key === key)
    if (!common) return
    setValue('name', t(`payItems.commons.${key}`))
    setValue('calcMethod', common.calcMethod)
    setValue('taxable', common.taxable ?? false)
    setValue('beforeTax', common.beforeTax ?? false)
    setValue('rate', common.rate ? String(common.rate) : '')
  }

  // The same checks as the API, so most mistakes show before anything is sent.
  const check = (v: Values) => {
    clearErrors()
    const problems: [FieldPath<Values>, string][] = []
    if (v.name.trim().length < 2 || v.name.trim().length > 80) problems.push(['name', t('payItems.nameInvalid')])
    if (v.code.trim() && !/^[A-Z0-9][A-Z0-9-]{1,19}$/.test(v.code.trim().toUpperCase())) problems.push(['code', t('payItems.codeInvalid')])
    const rate = Number(v.rate)
    if (v.calcMethod === 'percent_of_basic' && (!v.rate.trim() || !(rate > 0 && rate <= 100))) problems.push(['rate', t('payItems.rateInvalid')])
    if (v.calcMethod === 'fixed' && v.defaultAmount.trim() && !(Number(v.defaultAmount.replace(/,/g, '')) >= 0)) problems.push(['defaultAmount', t('payItems.defaultAmountInvalid')])
    if (!v.applies.all && v.applies.ids.length === 0) problems.push(['applies', t('payItems.companiesRequired')])
    for (const [name, message] of problems) setError(name, { message })
    return problems.length === 0
  }

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!check(v)) return setTab('details')
    // Only what fits its kind and way of working out is sent.
    const body = {
      code: v.code.trim() ? v.code.trim().toUpperCase() : null,
      name: v.name.trim(),
      kind: v.kind,
      taxable: v.kind === 'earning' && v.taxable,
      beforeTax: v.kind === 'deduction' && v.beforeTax,
      calcMethod: v.calcMethod,
      defaultAmount: v.calcMethod === 'fixed' && v.defaultAmount.trim() ? Number(v.defaultAmount.replace(/,/g, '')) : null,
      rate: v.calcMethod === 'percent_of_basic' ? Number(v.rate) : null,
      payeeId: v.kind !== 'earning' && v.payeeId ? v.payeeId : null,
      referenceSource: v.kind !== 'earning' ? v.referenceSource : 'typed',
      allCompanies: v.applies.all,
      companyIds: v.applies.all ? [] : v.applies.ids,
    }
    try {
      const { data, error } = item
        ? await api.PATCH('/api/pay-items/{id}', { params: { path: { id: item.id } }, body })
        : await api.POST('/api/pay-items', { body })
      if (data) {
        setItem(data)
        void queryClient.invalidateQueries({ queryKey: ['payItems'] })
        // People's own views list what they get.
        void queryClient.invalidateQueries({ queryKey: ['employee'] })
        // Then who gets it, for each company changed (among those the item serves).
        const failed: Record<string, Record<string, string>> = {}
        for (const c of served.filter(c => drafts[c.id]?.dirty)) {
          const { error: whoError } = await api.PUT('/api/pay-items/{id}/assignments', { params: { path: { id: data.id } }, body: requestFor(c.id, drafts[c.id]) })
          if (!whoError) {
            setDrafts(d => ({ ...d, [c.id]: { ...d[c.id], dirty: false } }))
          } else if (isApiError(whoError)) {
            failed[c.id] = whoError.fields ?? {}
            if (!whoError.fields) setBanner(whoError)
          } else {
            setBanner('network')
          }
        }
        setWhoErrors(failed)
        if (Object.keys(failed).length > 0) return setTab('who')
        toast.show(item ? t('payItems.saved') : t(`payItems.added.${v.kind}`))
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
    // The item's own problems are on Details.
    if (error.code === 'ONB-3529') {
      setTab('details')
      return setError('code', { message: errorMessage(t, error) })
    }
    const fields: Record<string, FieldPath<Values>> = {
      name: 'name', kind: 'kind', code: 'code', calcMethod: 'calcMethod', rate: 'rate', defaultAmount: 'defaultAmount', companyIds: 'applies', payeeId: 'payeeId',
      referenceSource: 'referenceSource',
    }
    const known = Object.entries(error.fields ?? {}).filter(([name]) => fields[name])
    for (const [name, message] of known) setError(fields[name], { message })
    if (known.length === 0) setBanner(error)
    else setTab('details')
  }

  return (
    <Drawer open size="wide" title={item ? t('payItems.editTitle') : t('payItems.addTitle')} onClose={onClose}
      tabs={<Tabs id={tabsId} label={t('who.sections')} current={tab} onChange={k => setTab(k as DrawerTab)}
        tabs={drawerTabs.map(k => ({ key: k, label: t(`who.tabs.${k}`), invalid: k === 'who' && Object.keys(whoErrors).length > 0 }))} />}
      // Adding: Details, Next (checks them), then Who gets it with Back and Add. Editing: Save changes on either tab.
      footer={<>
        <Button variant="secondary" className={adding && tab === 'who' ? 'mr-auto' : undefined} onClick={onClose}>{t('common.cancel')}</Button>
        {adding && tab === 'who' && <Button variant="secondary" onClick={() => setTab('details')}>{t('common.back')}</Button>}
        {/* Separate buttons (keys): if Next became Add in place, the browser would take the Next click as a submit. */}
        {adding && tab === 'details'
          ? <Button key="next" type="button" onClick={() => { if (check(getValues())) setTab('who') }}>{t('common.next')}</Button>
          : <Button key="submit" type="submit" form={formId} busy={isSubmitting}>{item ? t('payItems.save') : t('payItems.add')}</Button>}
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <TabPanel id={tabsId} tab="details" current={tab}>
        {!opened && <>
          <Controller control={control} name="kind" render={({ field }) => (
            <RadioGroup label={t('payItems.whatAdding')} name={field.name} value={field.value}
              onChange={value => { field.onChange(value); setValue('common', '') }}
              options={kinds.map(k => ({ value: k, label: t(`payItems.kindLabels.${k}`) }))} />
          )} />
          <Controller control={control} name="common" render={({ field }) => (
            <SelectField label={t('payItems.common')} placeholder={t('payItems.commonPlaceholder')} hint={t('payItems.commonHint')} optional
              options={commons.filter(c => c.kind === chosenKind).map(c => ({ value: c.key, label: t(`payItems.commons.${c.key}`) }))}
              {...field} onChange={value => { field.onChange(value); startFrom(value) }} />
          )} />
        </>}
        <TextField label={t('payItems.name')} placeholder={t('payItems.namePlaceholder')} maxLength={80}
          error={errors.name?.message} {...register('name')} />
        <TextField label={t('payItems.code')} placeholder={t('payItems.codePlaceholder')} optional hint={t('payItems.codeHint')}
          maxLength={20} autoCapitalize="characters" error={errors.code?.message} {...register('code')} />
        <Controller control={control} name="calcMethod" render={({ field }) => (
          <SelectField label={t('payItems.worked')} placeholder={t('payItems.workedPlaceholder')} error={errors.calcMethod?.message}
            options={methods.map(m => ({ value: m, label: t(`payItems.methods.${m}`) }))} {...field} />
        )} />
        {calcMethod === 'fixed' && (
          <TextField label={t('payItems.defaultAmountLabel')} placeholder={t('payItems.defaultAmountPlaceholder')} optional
            hint={t('payItems.defaultAmountHint')} inputMode="decimal" error={errors.defaultAmount?.message} {...register('defaultAmount')} />
        )}
        {calcMethod === 'percent_of_basic' && (
          <TextField label={t('payItems.rate')} placeholder={t('payItems.ratePlaceholder')} inputMode="decimal" end="%"
            error={errors.rate?.message} {...register('rate')} />
        )}
        {chosenKind === 'earning' && (
          <Controller control={control} name="taxable" render={({ field }) => (
            <Switch label={t('payItems.taxed')} hint={t('payItems.taxedHint')} checked={field.value} onChange={field.onChange} />
          )} />
        )}
        {chosenKind === 'deduction' && (
          <Controller control={control} name="beforeTax" render={({ field }) => (
            <Switch label={t('payItems.beforeTaxLabel')} hint={t('payItems.beforeTaxHint')} checked={field.value} onChange={field.onChange} />
          )} />
        )}
        {chosenKind !== 'earning' && (
          // Who the money goes to: a SACCO, union, insurer or lender; Add a payee opens a small form on top.
          // The field fills the row, the button on the right, level with the field (its hint goes under both).
          <div className="flex flex-col gap-1.5">
            <div className="flex items-end gap-3">
              <div className="min-w-0 flex-1">
                <Controller control={control} name="payeeId" render={({ field }) => (
                  <SelectField label={t('payees.paidTo')} placeholder={t('payees.paidToPlaceholder')} optional
                    error={errors.payeeId?.message} options={(payees.data ?? []).map(p => ({ value: p.id, label: p.name }))} {...field} />
                )} />
              </div>
              <Button variant="secondary" className="shrink-0" onClick={() => setAddingPayee(true)}><PlusIcon />{t('payees.add')}</Button>
            </div>
            <p className="m-0 text-hint text-muted">{t('payees.paidToHint')}</p>
          </div>
        )}
        {companies.length > 1 && (
          <div className="flex flex-col gap-1.5">
            <Controller control={control} name="applies" render={({ field }) => (
              <CompanySelect label={t('payItems.appliesTo')} value={field.value} onChange={field.onChange}
                companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
            )} />
            {errors.applies?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.applies.message}</p>}
          </div>
        )}
        </TabPanel>
        <TabPanel id={tabsId} tab="who" current={tab}>
          {chosenKind !== 'earning' && (
            // Each person's number with the payee: typed, or from their profile (HELB uses the ID number).
            <div className="max-w-md">
              <Controller control={control} name="referenceSource" render={({ field }) => (
                <SelectField label={t('payItems.referenceSource')} placeholder={t('payItems.referenceSourcePlaceholder')} hint={t('payItems.referenceSourceHint')}
                  error={errors.referenceSource?.message} options={referenceSources.map(k => ({ value: k, label: t(`payItems.sources.${k}`) }))} {...field} />
              )} />
            </div>
          )}
          <WhoGetsIt referenceSource={chosenKind !== 'earning' ? referenceSource : 'typed'} companies={served} itemName={itemName} method={calcMethod} defaultAmount={defaultAmount} rate={rate}
            drafts={drafts} errors={whoErrors} onChange={(id, draft) => setDrafts(d => ({ ...d, [id]: draft }))} />
        </TabPanel>
      </form>
      {addingPayee && (
        <PayeeModal onCancel={() => setAddingPayee(false)}
          onSaved={payee => { setValue('payeeId', payee.id); setAddingPayee(false) }} />
      )}
    </Drawer>
  )
}
