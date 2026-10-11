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
import { TableCard } from '../components/TableCard'
import { TabPanel, Tabs } from '../components/Tabs'
import { useToast } from '../components/toast'
import { useCompanies, usePayItems, useSetupDone } from '../setup/hooks'
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
  const done = useSetupDone(true)
  const [tab, setTab] = useState<Kind>('earning')
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [editing, setEditing] = useState<PayItem | 'new' | null>(null)
  const [deleting, setDeleting] = useState<PayItem | null>(null)
  const [busy, setBusy] = useState(false)

  const ready = companies.data && items.data
  const visible = (items.data ?? []).filter(i => shown.all || i.allCompanies || i.companyIds.some(id => shown.ids.includes(id)))
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
                <TableCard title={t(`payItems.tabs.${k}`)} count={list.length}>
                  {list.length === 0 ? <EmptyState title={t('payItems.emptyTitle')}>{t('payItems.emptyText')}</EmptyState> : (
                    <table>
                      <thead>
                        <tr>
                          <th scope="col">{t('payItems.name')}</th>
                          <th scope="col">{t('payItems.worked')}</th>
                          <th scope="col">{t('payItems.tax')}</th>
                          <th scope="col">{t('payItems.defaultAmount')}</th>
                          <th scope="col">{t('payItems.appliesTo')}</th>
                          <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                        </tr>
                      </thead>
                      <tbody>
                        {list.map(i => (
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
                            <td className="whitespace-nowrap text-right">
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
        {/* Leave plans (ONB-08) is next; until it has a page, the dashboard. */}
        <Proceed label={t('organization.proceed')} onProceed={() => void navigate({ to: '/' })} />

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
      </ConfirmDialog>
    </SetupLayout>
  )
}

function PayItemDrawer({ item, kind, companies, defaultApplies, onClose }: {
  item: PayItem | null
  kind: Kind
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
      kind: (item?.kind as Kind) ?? kind,
      common: '',
      name: item?.name ?? '',
      code: item?.code ?? '',
      calcMethod: item?.calcMethod ?? 'fixed',
      defaultAmount: item?.defaultAmount != null ? String(item.defaultAmount) : '',
      rate: item?.rate != null ? String(item.rate) : '',
      taxable: item ? item.taxable : kind === 'earning',
      beforeTax: item?.beforeTax ?? false,
      applies: item ? { all: item.allCompanies, ids: item.allCompanies ? companies.map(c => c.id) : item.companyIds } : defaultApplies,
    },
  })
  const [chosenKind, calcMethod] = useWatch({ control, name: ['kind', 'calcMethod'] })

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
    if (!check(v)) return
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
      allCompanies: v.applies.all,
      companyIds: v.applies.all ? [] : v.applies.ids,
    }
    try {
      const { data, error } = item
        ? await api.PATCH('/api/pay-items/{id}', { params: { path: { id: item.id } }, body })
        : await api.POST('/api/pay-items', { body })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['payItems'] })
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
    if (error.code === 'ONB-3529') return setError('code', { message: errorMessage(t, error) })
    const fields: Record<string, FieldPath<Values>> = {
      name: 'name', kind: 'kind', code: 'code', calcMethod: 'calcMethod', rate: 'rate', defaultAmount: 'defaultAmount', companyIds: 'applies',
    }
    const known = Object.entries(error.fields ?? {}).filter(([name]) => fields[name])
    for (const [name, message] of known) setError(fields[name], { message })
    if (known.length === 0) setBanner(error)
  }

  return (
    <Drawer open size="wide" title={item ? t('payItems.editTitle') : t('payItems.addTitle')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{item ? t('payItems.save') : t('payItems.add')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        {!item && <>
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
        {companies.length > 1 && (
          <div className="flex flex-col gap-1.5">
            <Controller control={control} name="applies" render={({ field }) => (
              <CompanySelect label={t('payItems.appliesTo')} value={field.value} onChange={field.onChange}
                companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
            )} />
            {errors.applies?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.applies.message}</p>}
          </div>
        )}
      </form>
    </Drawer>
  )
}
