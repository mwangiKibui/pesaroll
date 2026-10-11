import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useDeferredValue, useEffect, useId, useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { BulkUpload, type BulkUploadRow } from '../components/BulkUpload'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Drawer } from '../components/Drawer'
import { EmptyState, FormErrorBanner, Loading, Notice, Tag } from '../components/feedback'
import { RadioGroup, SelectField, TextField } from '../components/fields'
import { PlusIcon, UploadIcon } from '../components/icons'
import { TableCard } from '../components/TableCard'
import { TabPanel, Tabs } from '../components/Tabs'
import { useToast } from '../components/toast'
import { formatMoney } from '../lib/money'
import { useCompanies, useLeavePlans, useLocations, useSchedules, useSetupDone, useStructure } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type Row = components['schemas']['EmployeeRow']
type Detail = components['schemas']['EmployeeResponse']
type ImportRow = components['schemas']['ImportRow']

const pageSize = 100
const statuses = ['current', 'former', 'all'] as const
const types = ['permanent', 'contract', 'casual', 'intern'] as const
const bases = ['monthly', 'daily', 'hourly'] as const
const tabs = ['personal', 'ids', 'job', 'pay', 'account'] as const
type TabKey = (typeof tabs)[number]

/** The upload's columns, in the template's order (same keys as the API). */
const columnKeys = ['company', 'staffNo', 'firstName', 'middleName', 'lastName', 'dateOfBirth', 'phone', 'email', 'nationalId', 'kraPin', 'nssfNo',
  'shaNo', 'hireDate', 'exitDate', 'employmentType', 'contractEnd', 'division', 'department', 'unit', 'position', 'location', 'lineManager',
  'payrollSchedule', 'leavePlan', 'basicPay', 'payBasis', 'bank', 'branch', 'accountNumber', 'mpesa'] as const

const none = <span className="text-muted">—</span>

/**
 * ONB-09: the organization's people. A server-searched, paged list (companies can have thousands);
 * adding one in a drawer with tabs (personal, IDs and numbers, job, pay, paid into), several in a
 * sheet of empty rows, or many from a spreadsheet, all held to the same checks by the API.
 */
export default function EmployeesPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const companies = useCompanies()
  const schedules = useSchedules()
  const done = useSetupDone(true)
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<string>('current')
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Row | null>(null)
  const [busy, setBusy] = useState(false)
  const [bulk, setBulk] = useState<'upload' | 'several' | null>(null)
  const term = useDeferredValue(search.trim())


  const query = { search: term || undefined, status, page, pageSize, companyIds: shown.all ? undefined : shown.ids }
  const employees = useQuery({
    queryKey: ['employees', query],
    placeholderData: previous => previous,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees', { params: { query } })
      if (error || !data) throw new Error('employees')
      return data
    },
  })

  const ready = companies.data && schedules.data && employees.data
  const pages = Math.max(1, Math.ceil(Number(employees.data?.total ?? 0) / pageSize))

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      const { error, response } = await api.DELETE('/api/employees/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        void queryClient.invalidateQueries({ queryKey: ['employees'] })
        toast.show(t('employees.deleted'))
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

  const job = (r: Row) => [r.positionTitle, r.departmentName].filter(Boolean).join(' · ')
  const readiness = (r: Row) => r.readiness.ready
    ? <Tag tone="brand">{t('employees.readyTag')}</Tag>
    : <span className="text-hint text-warning-ink">{t('employees.needs', { items: r.readiness.missing.map(m => m.label.en).join(', ') })}</span>

  const actions = <>
    <Button onClick={() => setEditing('new')}><PlusIcon />{t('employees.add')}</Button>
    <Button variant="secondary" onClick={() => setBulk('several')}><PlusIcon />{t('employees.addSeveral')}</Button>
    <Button variant="secondary" onClick={() => setBulk('upload')}><UploadIcon />{t('employees.upload')}</Button>
  </>

  return (
    <SetupLayout current="employees" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('employees.title')}</h1>
        <p className="m-0 text-muted">{t('employees.intro')}</p>
      </div>
      {!ready ? <Loading rows={4} /> : <>
        <div className="flex flex-wrap items-end gap-3">
          {companies.data.length > 1 && (
            <div className="w-full max-w-sm">
              <CompanySelect label={t('employees.show')} value={shown} onChange={value => { setShown(value); setPage(1) }}
                companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
            </div>
          )}
          <div className="w-full max-w-sm">
            <TextField type="search" label={t('employees.search')} placeholder={t('employees.searchPlaceholder')} value={search}
              onChange={e => { setSearch(e.target.value); setPage(1) }} />
          </div>
          <div className="w-full max-w-56">
            <SelectField label={t('employees.status')} placeholder={t('employees.statusPlaceholder')} value={status}
              onChange={value => { setStatus(value); setPage(1) }}
              options={statuses.map(s => ({ value: s, label: t(`employees.statuses.${s}`) }))} />
          </div>
        </div>
        <TableCard title={t('employees.tableTitle')} count={Number(employees.data.total)} actions={actions}>
          {employees.data.items.length === 0 ? <EmptyState title={t('employees.emptyTitle')}>{t('employees.emptyText')}</EmptyState> : (
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('employees.employee')}</th>
                  <th scope="col">{t('employees.job')}</th>
                  <th scope="col">{t('employees.schedule')}</th>
                  <th scope="col">{t('employees.basicPay')}</th>
                  <th scope="col">{t('employees.ready')}</th>
                  <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {employees.data.items.map(r => (
                  <tr key={r.id}>
                    <td>
                      <span className="block font-medium text-ink">{r.name}</span>
                      <span className="block font-mono text-hint text-muted">{r.staffNo}</span>
                      {r.exitDate && <span className="block text-hint text-muted">{t('employees.left', { date: r.exitDate })}</span>}
                    </td>
                    <td>{job(r) || none}</td>
                    <td>{schedules.data.find(s => s.id === r.payScheduleId)?.name ?? none}</td>
                    <td className="whitespace-nowrap">
                      {r.basicAmount != null && r.payBasis
                        ? <span className="tabular-nums">{t(`employees.per.${r.payBasis}`, { amount: formatMoney(r.basicAmount, { currency: r.currency ?? 'KES' }) })}</span>
                        : none}
                    </td>
                    <td>{readiness(r)}</td>
                    <td className="whitespace-nowrap text-right">
                      <IconButton label={t('employees.edit', { name: r.name })} onClick={() => setEditing(r.id)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                      </IconButton>
                      <IconButton tone="danger" label={t('employees.delete', { name: r.name })} onClick={() => setDeleting(r)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TableCard>
        {pages > 1 && (
          <nav aria-label={t('employees.tableTitle')} className="flex items-center justify-end gap-3">
            <Button variant="secondary" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>{t('employees.previous')}</Button>
            <span className="text-label text-muted">{t('employees.page', { page, pages })}</span>
            <Button variant="secondary" disabled={page >= pages} onClick={() => setPage(p => p + 1)}>{t('employees.next')}</Button>
          </nav>
        )}
        {/* Earnings and deductions (ONB-07) is next; until it has a page, the dashboard. */}
        <Proceed label={t('organization.proceed')} onProceed={() => void navigate({ to: '/' })} />

        {editing && (
          <EmployeeDrawer id={editing === 'new' ? null : editing} companies={companies.data}
            defaultCompany={shown.all ? companies.data[0].id : shown.ids[0] ?? companies.data[0].id} onClose={() => setEditing(null)} />
        )}
        {bulk && <UploadEmployees startRows={bulk === 'several' ? 10 : undefined} onClose={() => setBulk(null)} />}
      </>}
      <ConfirmDialog open={!!deleting} title={t('employees.deleteTitle', { name: deleting?.name })}
        confirmLabel={t('employees.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('employees.deleteText')}
      </ConfirmDialog>
    </SetupLayout>
  )
}

interface Values {
  firstName: string; middleName: string; lastName: string; dateOfBirth: string; phone: string; email: string; staffNo: string; hireDate: string; exitDate: string
  nationalId: string; kraPin: string; nssfNo: string; shaNo: string
  companyId: string; departmentId: string; positionId: string; unitId: string; locationId: string; lineManagerId: string; payScheduleId: string; leavePlanId: string
  employmentType: string; contractEnd: string
  payBasis: string; basicAmount: string
  method: string; bankCode: string; branchCode: string; accountNumber: string; mpesaPhone: string
}

/** Which tab each field is on, to mark the tab when the server finds a problem there. */
const tabOf: Record<string, TabKey> = {
  firstName: 'personal', middleName: 'personal', lastName: 'personal', dateOfBirth: 'personal', phone: 'personal', email: 'personal',
  staffNo: 'personal', hireDate: 'personal', exitDate: 'personal', nationalId: 'ids', kraPin: 'ids', nssfNo: 'ids', shaNo: 'ids',
  companyId: 'job', departmentId: 'job', positionId: 'job', unitId: 'job', locationId: 'job', lineManagerId: 'job', payScheduleId: 'job',
  leavePlanId: 'job', employmentType: 'job', contractEnd: 'job', payBasis: 'pay', basicAmount: 'pay',
  method: 'account', bankCode: 'account', branchCode: 'account', accountNumber: 'account', mpesaPhone: 'account',
}

/** The API's field names ("job.payScheduleId", "account.mpesaPhone") to the form's. */
const fieldOf = (name: string): FieldPath<Values> | null => {
  const last = name.split('.').pop()!
  const map: Record<string, FieldPath<Values>> = { basicAmount: 'basicAmount', payBasis: 'payBasis' }
  return (map[last] ?? (last in tabOf ? last : null)) as FieldPath<Values> | null
}

function EmployeeDrawer({ id, companies, defaultCompany, onClose }: {
  id: string | null
  companies: Company[]
  defaultCompany: string
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api } = useServices()
  const detail = useQuery({
    queryKey: ['employee', id],
    enabled: !!id,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/{id}', { params: { path: { id: id! } } })
      if (error || !data) throw new Error('employee')
      return data
    },
  })
  if (id && !detail.data) {
    return <Drawer open size="wide" title={t('employees.editTitle')} onClose={onClose}><Loading rows={6} /></Drawer>
  }

  return <EmployeeForm employee={detail.data ?? null} companies={companies} defaultCompany={defaultCompany} onClose={onClose} />
}

function EmployeeForm({ employee, companies, defaultCompany, onClose }: {
  employee: Detail | null
  companies: Company[]
  defaultCompany: string
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const tabsId = useId()
  const [tab, setTab] = useState<TabKey>('personal')
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const schedules = useSchedules()
  const plans = useLeavePlans()
  const locations = useLocations()
  const departments = useStructure('departments')
  const positions = useStructure('positions')
  const units = useStructure('units')

  const companyStart = employee?.companyId ?? defaultCompany
  const defaultSchedule = (company: string) => schedules.data?.find(s => s.defaultFor.includes(company))?.id ?? ''
  const defaultPlan = (company: string, schedule: string) =>
    plans.data?.find(p => p.defaultForSchedules.includes(schedule))?.id ?? plans.data?.find(p => p.defaultFor.includes(company))?.id ?? ''

  const { register, handleSubmit, control, setError, setValue, getValues, formState: { errors, isSubmitting, dirtyFields } } = useForm<Values>({
    defaultValues: {
      firstName: employee?.firstName ?? '', middleName: employee?.middleName ?? '', lastName: employee?.lastName ?? '',
      dateOfBirth: employee?.dateOfBirth ?? '', phone: employee?.phone ?? '', email: employee?.email ?? '', staffNo: employee?.staffNo ?? '',
      hireDate: employee?.hireDate ?? '', exitDate: employee?.exitDate ?? '',
      nationalId: '', kraPin: '', nssfNo: employee?.nssfNo ?? '', shaNo: employee?.shaNo ?? '',
      companyId: companyStart,
      departmentId: employee?.job.departmentId ?? '', positionId: employee?.job.positionId ?? '', unitId: employee?.job.unitId ?? '',
      locationId: employee?.job.locationId ?? '', lineManagerId: employee?.job.lineManagerId ?? '',
      payScheduleId: employee?.job.payScheduleId ?? '', leavePlanId: employee?.job.leavePlanId ?? '',
      employmentType: employee?.job.employmentType ?? 'permanent', contractEnd: employee?.job.contractEnd ?? '',
      payBasis: employee?.pay?.payBasis ?? 'monthly', basicAmount: employee?.pay ? String(employee.pay.basicAmount) : '',
      method: employee?.account?.method ?? 'none', bankCode: employee?.account?.bankCode ?? '', branchCode: employee?.account?.branchCode ?? '',
      accountNumber: '', mpesaPhone: '',
    },
  })
  const [companyId, scheduleId, basis, method, bankCode, type, departmentId] =
    useWatch({ control, name: ['companyId', 'payScheduleId', 'payBasis', 'method', 'bankCode', 'employmentType', 'departmentId'] })
  const company = companies.find(c => c.id === companyId) ?? companies[0]

  // A new employee starts with the company's schedule, leave plan and main location, once they're loaded.
  useEffect(() => {
    if (employee || !schedules.data || !plans.data || !locations.data) return
    const schedule = getValues('payScheduleId') || defaultSchedule(companyId)
    setValue('payScheduleId', schedule)
    setValue('leavePlanId', defaultPlan(companyId, schedule))
    if (!getValues('locationId')) setValue('locationId', locations.data.find(l => l.mainFor.includes(companyId))?.id ?? '')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [employee, schedules.data, plans.data, locations.data, companyId])

  const banks = useQuery({
    queryKey: ['banks', company.countryCode],
    staleTime: Infinity,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/banks', { params: { query: { country: company.countryCode } } })
      if (error || !data) throw new Error('banks')
      return data
    },
  })
  const managers = useQuery({
    queryKey: ['employees', 'choices', companyId],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/choices', { params: { query: { companyId } } })
      if (error || !data) throw new Error('choices')
      return data
    },
  })

  type Scoped = { id: string; allCompanies: boolean; companyIds: string[] }
  const serves = <T extends Scoped>(items: T[] | undefined) => (items ?? []).filter(i => i.allCompanies || i.companyIds.includes(companyId))
  const schedule = schedules.data?.find(s => s.id === scheduleId)
  const mismatch = schedule && ((basis === 'monthly') !== (schedule.frequency === 'monthly'))
  const sameDepartmentFirst = [...(managers.data ?? [])].filter(m => m.id !== employee?.id)
    .sort((a, b) => Number(b.departmentId === departmentId && !!departmentId) - Number(a.departmentId === departmentId && !!departmentId))

  const invalidTabs = new Set(Object.keys(errors).map(name => tabOf[name]).filter(Boolean))

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!v.hireDate) {
      setError('hireDate', { message: t('employees.hireDateRequired') })
      return setTab('personal')
    }
    const accountTouched = !employee || ['method', 'bankCode', 'branchCode', 'accountNumber', 'mpesaPhone'].some(k => dirtyFields[k as keyof Values])
    const body = {
      companyId: v.companyId, staffNo: v.staffNo.trim() || null, firstName: v.firstName, middleName: v.middleName || null, lastName: v.lastName,
      dateOfBirth: v.dateOfBirth || null, phone: v.phone, email: v.email,
      // Saved numbers stay unless retyped (they're never sent back to the screen).
      nationalId: v.nationalId.trim() || null, kraPin: v.kraPin.trim() || null, nssfNo: v.nssfNo, shaNo: v.shaNo,
      hireDate: v.hireDate, exitDate: v.exitDate || null,
      job: {
        positionId: v.positionId || null, departmentId: v.departmentId || null, unitId: v.unitId || null, locationId: v.locationId || null,
        lineManagerId: v.lineManagerId || null, payScheduleId: v.payScheduleId || null, leavePlanId: v.leavePlanId || null,
        employmentType: v.employmentType, contractEnd: v.employmentType === 'contract' && v.contractEnd ? v.contractEnd : null,
      },
      pay: v.basicAmount.trim() ? { basicAmount: Number(v.basicAmount.replace(/,/g, '')), payBasis: v.payBasis } : null,
      account: !accountTouched || v.method === 'none' ? null
        : v.method === 'bank' ? { method: 'bank', bankCode: v.bankCode, branchCode: v.branchCode, accountNumber: v.accountNumber.trim() || null }
          : { method: 'mpesa', mpesaPhone: v.mpesaPhone.trim() || null },
    }
    try {
      const { data, error } = employee
        ? await api.PATCH('/api/employees/{id}', { params: { path: { id: employee.id } }, body: body as never })
        : await api.POST('/api/employees', { body: body as never })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['employees'] })
        void queryClient.invalidateQueries({ queryKey: ['employee', employee?.id] })
        toast.show(employee ? t('employees.saved') : t('employees.added'))
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
    const known = Object.entries(error.fields ?? {}).map(([name, message]) => [fieldOf(name), message] as const).filter(([field]) => field)
    for (const [field, message] of known) setError(field!, { message })
    if (known.length === 0) setBanner(error)
    else setTab(tabOf[known[0][0]!])
  }

  const saved = (masked: string | null | undefined, hint: string) => (employee && masked ? t('employees.saved_', { masked }) : hint)
  const select = (name: FieldPath<Values>, label: string, placeholder: string, options: { value: string; label: string }[], extra: { optional?: boolean; hint?: string } = {}) => (
    <Controller control={control} name={name} render={({ field }) => (
      <SelectField label={label} placeholder={placeholder} error={errors[name]?.message} options={options} {...extra}
        value={String(field.value ?? '')} onChange={field.onChange} onBlur={field.onBlur} name={field.name} />
    )} />
  )
  const bank = banks.data?.find(b => b.code === bankCode)

  return (
    <Drawer open size="wide" title={employee ? t('employees.editTitle') : t('employees.addTitle')} onClose={onClose}
      tabs={<Tabs id={tabsId} label={t('employees.sections')} current={tab} onChange={k => setTab(k as TabKey)}
        tabs={tabs.map(k => ({ key: k, label: t(`employees.tabs.${k}`), invalid: invalidTabs.has(k) }))} />}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{employee ? t('employees.save') : t('employees.add')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}

        <TabPanel id={tabsId} tab="personal" current={tab}>
          <div className="grid gap-4 sm:grid-cols-3">
            <TextField label={t('employees.firstName')} placeholder={t('employees.firstNamePlaceholder')} maxLength={60} error={errors.firstName?.message} {...register('firstName')} />
            <TextField label={t('employees.middleName')} placeholder={t('employees.middleNamePlaceholder')} optional maxLength={60} error={errors.middleName?.message} {...register('middleName')} />
            <TextField label={t('employees.lastName')} placeholder={t('employees.lastNamePlaceholder')} maxLength={60} error={errors.lastName?.message} {...register('lastName')} />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField type="date" label={t('employees.hireDate')} placeholder={t('employees.hireDatePlaceholder')} error={errors.hireDate?.message} {...register('hireDate')} />
            <TextField type="date" label={t('employees.exitDate')} placeholder={t('employees.exitDatePlaceholder')} optional hint={t('employees.exitDateHint')} error={errors.exitDate?.message} {...register('exitDate')} />
            <TextField type="date" label={t('employees.dateOfBirth')} placeholder={t('employees.dateOfBirthPlaceholder')} optional error={errors.dateOfBirth?.message} {...register('dateOfBirth')} />
            <TextField label={t('employees.staffNo')} placeholder={t('employees.staffNoPlaceholder')} optional hint={t('employees.staffNoHint')} maxLength={20}
              error={errors.staffNo?.message} {...register('staffNo')} />
            <TextField type="tel" label={t('employees.phone')} placeholder={t('employees.phonePlaceholder')} optional error={errors.phone?.message} {...register('phone')} />
            <TextField type="email" label={t('employees.email')} placeholder={t('employees.emailPlaceholder')} optional error={errors.email?.message} {...register('email')} />
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="ids" current={tab}>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={t('employees.nationalId')} placeholder={t('employees.nationalIdPlaceholder')} optional inputMode="numeric"
              hint={saved(employee?.nationalIdMasked, t('employees.nationalIdHint'))} error={errors.nationalId?.message} {...register('nationalId')} />
            <TextField label={t('employees.kraPin')} placeholder={t('employees.kraPinPlaceholder')} optional autoCapitalize="characters"
              hint={saved(employee?.kraPinMasked, t('employees.kraPinHint'))} error={errors.kraPin?.message} {...register('kraPin')} />
            <TextField label={t('employees.nssfNo')} placeholder={t('employees.nssfNoPlaceholder')} optional inputMode="numeric" hint={t('employees.nssfNoHint')}
              error={errors.nssfNo?.message} {...register('nssfNo')} />
            <TextField label={t('employees.shaNo')} placeholder={t('employees.shaNoPlaceholder')} optional hint={t('employees.shaNoHint')}
              error={errors.shaNo?.message} {...register('shaNo')} />
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="job" current={tab}>
          {companies.length > 1 && !employee && select('companyId', t('employees.company'), t('employees.companyPlaceholder'),
            companies.map(c => ({ value: c.id, label: c.legalName })))}
          <div className="grid gap-4 sm:grid-cols-2">
            {select('departmentId', t('employees.department'), t('employees.departmentPlaceholder'), serves(departments.data).map(d => ({ value: d.id, label: d.name ?? '' })), { optional: true })}
            {select('positionId', t('employees.position'), t('employees.positionPlaceholder'), serves(positions.data).map(p => ({ value: p.id, label: p.title ?? '' })), { optional: true })}
            {select('unitId', t('employees.unit'), t('employees.unitPlaceholder'), serves(units.data).map(u => ({ value: u.id, label: u.name ?? '' })), { optional: true })}
            {select('locationId', t('employees.location'), t('employees.locationPlaceholder'), serves(locations.data).map(l => ({ value: l.id, label: l.name })), { optional: true })}
            {select('lineManagerId', t('employees.lineManager'), t('employees.lineManagerPlaceholder'),
              sameDepartmentFirst.map(m => ({ value: m.id, label: `${m.name} (${m.staffNo})` })), { optional: true, hint: t('employees.lineManagerHint') })}
            <Controller control={control} name="payScheduleId" render={({ field }) => (
              <SelectField label={t('employees.payrollSchedule')} placeholder={t('employees.payrollSchedulePlaceholder')} error={errors.payScheduleId?.message}
                options={serves(schedules.data).map(s => ({ value: s.id, label: s.name }))} value={field.value} name={field.name} onBlur={field.onBlur}
                // A schedule with its own leave plan brings it along.
                onChange={value => { field.onChange(value); setValue('leavePlanId', defaultPlan(companyId, value)) }} />
            )} />
            {select('leavePlanId', t('employees.leavePlan'), t('employees.leavePlanPlaceholder'), serves(plans.data).map(p => ({ value: p.id, label: p.name })),
              { hint: t('employees.leavePlanHint') })}
          </div>
          <Controller control={control} name="employmentType" render={({ field }) => (
            <RadioGroup label={t('employees.employmentType')} name={field.name} value={field.value} onChange={field.onChange}
              options={types.map(k => ({ value: k, label: t(`employees.types.${k}`) }))} />
          )} />
          {type === 'contract' && (
            <TextField type="date" label={t('employees.contractEnd')} placeholder={t('employees.contractEndPlaceholder')} optional error={errors.contractEnd?.message} {...register('contractEnd')} />
          )}
        </TabPanel>

        <TabPanel id={tabsId} tab="pay" current={tab}>
          <Controller control={control} name="payBasis" render={({ field }) => (
            <RadioGroup label={t('employees.payBasis')} name={field.name} value={field.value} onChange={field.onChange}
              options={bases.map(b => ({ value: b, label: t(`employees.bases.${b}`) }))} />
          )} />
          <div className="max-w-sm">
            <TextField label={t(`employees.amountLabels.${basis}`)} placeholder={t('employees.amountPlaceholder')} optional inputMode="decimal"
              error={errors.basicAmount?.message} {...register('basicAmount')} />
          </div>
          {mismatch && schedule && (
            <Notice>{t('employees.basisMismatch', { schedule: schedule.name, frequency: t(`employees.frequencies.${schedule.frequency}`), basis: t(`employees.bases.${basis}`).toLowerCase() })}</Notice>
          )}
        </TabPanel>

        <TabPanel id={tabsId} tab="account" current={tab}>
          <Controller control={control} name="method" render={({ field }) => (
            <RadioGroup label={t('employees.method')} name={field.name} value={field.value} onChange={field.onChange}
              options={(['bank', 'mpesa', 'none'] as const).map(m => ({ value: m, label: t(`employees.methods.${m}`) }))} />
          )} />
          {method === 'bank' && (
            <div className="grid gap-4 sm:grid-cols-2">
              {select('bankCode', t('employees.bank'), t('employees.bankPlaceholder'), (banks.data ?? []).map(b => ({ value: b.code, label: b.name })))}
              {select('branchCode', t('employees.branch'), t('employees.branchPlaceholder'), (bank?.branches ?? []).map(b => ({ value: b.code, label: `${b.name} (${b.code})` })))}
              <TextField label={t('employees.accountNumber')} placeholder={t('employees.accountNumberPlaceholder')} inputMode="numeric"
                hint={employee?.account?.accountMasked ? t('employees.keepAccount', { masked: employee.account.accountMasked }) : undefined}
                error={errors.accountNumber?.message} {...register('accountNumber')} />
            </div>
          )}
          {method === 'mpesa' && (
            <div className="max-w-sm">
              <TextField type="tel" label={t('employees.mpesa')} placeholder={t('employees.mpesaPlaceholder')}
                hint={employee?.account?.mpesaMasked ? t('employees.keepAccount', { masked: employee.account.mpesaMasked }) : undefined}
                error={errors.mpesaPhone?.message} {...register('mpesaPhone')} />
            </div>
          )}
        </TabPanel>
      </form>
    </Drawer>
  )
}

/** Uploading employees (template, then the review), or adding several in a sheet of empty rows. */
function UploadEmployees({ startRows, onClose }: { startRows?: number; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const columns = columnKeys.map(key => ({
    key, label: t(`employees.columns.${key}`),
    ...(key === 'employmentType' ? { options: types.map(k => ({ value: t(`employees.types.${k}`, { lng: 'en' }), label: t(`employees.types.${k}`) })) } : {}),
    ...(key === 'payBasis' ? { options: bases.map(k => ({ value: t(`employees.bases.${k}`, { lng: 'en' }), label: t(`employees.bases.${k}`) })) } : {}),
  }))

  const downloadTemplate = async () => {
    const { data } = await api.GET('/api/employees/template', { parseAs: 'blob' })
    if (!data) throw new Error('template')
    const url = URL.createObjectURL(data)
    const link = document.createElement('a')
    link.href = url
    link.download = 'pesaroll-employees-template.xlsx'
    link.click()
    URL.revokeObjectURL(url)
  }

  const read = async (file: File) => {
    const { data, error } = await api.POST('/api/employees/import/read', {
      body: { file } as never,
      bodySerializer: () => {
        const form = new FormData()
        form.append('file', file)
        return form
      },
    })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('read')
  }

  const check = async (rows: BulkUploadRow[]) => {
    const { data, error } = await api.POST('/api/employees/import/check', { body: { rows: rows as ImportRow[] } })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('check')
  }

  const importRows = async (rows: BulkUploadRow[]) => {
    const { data, error } = await api.POST('/api/employees/import', { body: { rows: rows as ImportRow[], allCompanies: false, companyIds: [] } })
    if (data) {
      for (const key of [['employees'], ['structure', 'departments'], ['structure', 'positions'], ['structure', 'divisions']]) {
        void queryClient.invalidateQueries({ queryKey: key })
      }
      toast.show(t('employees.imported', { count: Number(data.imported) }))
      return null
    }
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('import')
  }

  return (
    <BulkUpload open startRows={startRows} title={startRows ? t('employees.addSeveral') : t('employees.upload')} columns={columns} onClose={onClose}
      onDownloadTemplate={downloadTemplate} onRead={read} onCheck={check} onImport={importRows}
      labels={{
        review: count => (startRows ? t('employees.several', { count }) : t('employees.review', { count })),
        importButton: count => (startRows ? t('employees.several', { count }) : t('employees.importButton', { count })),
        templateHint: t('employees.templateHint'),
      }} />
  )
}
