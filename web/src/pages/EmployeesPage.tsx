import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useDeferredValue, useEffect, useId, useState, type ReactNode } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { BulkUpload, type BulkUploadColumn, type BulkUploadRow } from '../components/BulkUpload'
import { Button, IconButton } from '../components/Button'
import { Checklist, FilterButton, SortButton } from '../components/ColumnControls'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Drawer } from '../components/Drawer'
import { EmptyState, FormErrorBanner, Loading, Notice, Tag } from '../components/feedback'
import { Checkbox, RadioGroup, SelectField, TextField } from '../components/fields'
import { PlusIcon, UploadIcon } from '../components/icons'
import { Pagination } from '../components/Pagination'
import { TableCard } from '../components/TableCard'
import { TabPanel, Tabs } from '../components/Tabs'
import { useToast } from '../components/toast'
import { accountProblem, rules } from '../lib/employeeRules'
import { matches } from '../components/search'
import { formatMoney } from '../lib/money'
import { useCompanies, useLeavePlans, useLocations, useSchedules, useSetupDone, useStructure, type StructureItem } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type Row = components['schemas']['EmployeeRow']
type Detail = components['schemas']['EmployeeResponse']
type ImportRow = components['schemas']['ImportRow']

const statuses = ['current', 'former', 'all'] as const
const types = ['permanent', 'contract', 'casual', 'intern'] as const
const tabs = ['personal', 'ids', 'job', 'pay', 'account'] as const
type TabKey = (typeof tabs)[number]

/** The sheet's columns, in the template's order (same keys as the API; the company is chosen above, and pay follows the schedule). */
const columnKeys = ['staffNo', 'firstName', 'middleName', 'lastName', 'dateOfBirth', 'phone', 'email', 'nationalId', 'kraPin', 'nssfNo',
  'address', 'shaNo', 'hireDate', 'stillHere', 'exitDate', 'employmentType', 'contractEnd', 'division', 'department', 'unit', 'position', 'lineManager',
  'payrollSchedule', 'leavePlan', 'basicPay', 'paidInto', 'bank', 'branch', 'accountNumber', 'mpesa'] as const

/** The banks of a country, with their branches and how long their account numbers are. */
function useBanks(country: string) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['banks', country],
    staleTime: Infinity,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/banks', { params: { query: { country } } })
      if (error || !data) throw new Error('banks')
      return data
    },
  })
}

/** How the company pays people: the methods of its paying accounts (none before one is set), and its default's. */
function usePaysBy(companyId: string) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['payment-accounts', companyId],
    enabled: !!companyId,
    // Another company's answer stays until this one's arrives, so screens don't fall back to loading.
    placeholderData: previous => previous,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/companies/{companyId}/payment-accounts', { params: { path: { companyId } } })
      if (error || !data) throw new Error('payment-accounts')
      return data
    },
    select: accounts => ({
      methods: [...new Set(accounts.map(a => a.method))] as ('bank' | 'mpesa')[],
      usual: (accounts.find(a => a.isDefault)?.method ?? null) as 'bank' | 'mpesa' | null,
    }),
  })
}

const none = <span className="text-muted">—</span>

/** The columns the employees list sorts by (the API's sort keys). */
type SortKey = 'company' | 'name' | 'job' | 'schedule' | 'basicPay' | 'hireDate'

/** The column filters: ids ticked (none ticked: no filter), amounts and dates as typed. */
interface ColumnFilters { departments: string[]; positions: string[]; schedules: string[]; payFrom: string; payTo: string; hiredFrom: string; hiredTo: string }
const noFilters: ColumnFilters = { departments: [], positions: [], schedules: [], payFrom: '', payTo: '', hiredFrom: '', hiredTo: '' }
const activeFilters = (f: ColumnFilters) => [
  ...(f.departments.length || f.positions.length ? ['job'] : []), ...(f.schedules.length ? ['schedule'] : []),
  ...(f.payFrom || f.payTo ? ['basicPay'] : []), ...(f.hiredFrom || f.hiredTo ? ['hireDate'] : []),
]

/** "15 Jan 2024". */
const shortDate = (iso: string | null | undefined) => !iso ? none : new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${iso}T00:00:00Z`))

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
  // Each column sorts and filters on the server, across every page; any change goes back to page 1.
  const [sort, setSort] = useState<{ key: SortKey; direction: 'ascending' | 'descending' } | null>(null)
  const [filters, setFilters] = useState<ColumnFilters>(noFilters)
  const filter = (change: Partial<ColumnFilters>) => { setFilters(f => ({ ...f, ...change })); setPage(1) }
  const sortBy = (key: SortKey) => {
    setSort(s => (s?.key !== key ? { key, direction: 'ascending' } : s.direction === 'ascending' ? { key, direction: 'descending' } : null))
    setPage(1)
  }
  const departments = useStructure('departments')
  const positions = useStructure('positions')
  const [jobSearch, setJobSearch] = useState('')
  const active = activeFilters(filters)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const [viewing, setViewing] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<Row | null>(null)
  const [busy, setBusy] = useState(false)
  const [bulk, setBulk] = useState<'upload' | 'add' | null>(null)
  const term = useDeferredValue(search.trim())


  const amount = (text: string) => (text.trim() && Number(text.replace(/[\s,]/g, '')) >= 0 ? Number(text.replace(/[\s,]/g, '')) : undefined)
  const query = {
    search: term || undefined, status, page, pageSize, companyIds: shown.all ? undefined : shown.ids,
    sort: sort?.key, dir: sort ? (sort.direction === 'ascending' ? 'asc' : 'desc') : undefined,
    departmentIds: filters.departments.length ? filters.departments : undefined, positionIds: filters.positions.length ? filters.positions : undefined,
    scheduleIds: filters.schedules.length ? filters.schedules : undefined, payFrom: amount(filters.payFrom), payTo: amount(filters.payTo),
    hiredFrom: filters.hiredFrom || undefined, hiredTo: filters.hiredTo || undefined,
  }
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
  // In the table's header line: the search, then Add and Upload.
  const actions = <>
    <div className="w-full sm:w-64">
      <TextField type="search" size="sm" label={t('employees.search')} hideLabel placeholder={t('employees.searchPlaceholder')} value={search}
        onChange={e => { setSearch(e.target.value); setPage(1) }} />
    </div>
    <Button onClick={() => setBulk('add')}><PlusIcon />{t('employees.add')}</Button>
    <Button variant="secondary" onClick={() => setBulk('upload')}><UploadIcon />{t('employees.upload')}</Button>
  </>

  // What the company selector shows: the structure and schedules those companies use.
  type Scoped = { allCompanies: boolean; companyIds: string[] }
  const scoped = <T extends Scoped>(items: T[] | undefined) =>
    (items ?? []).filter(i => shown.all || i.allCompanies || i.companyIds.some(id => shown.ids.includes(id)))
  // The Company column's filter is the company selector's choice: ticking in either changes both. One stays ticked.
  const pickCompanies = (ids: string[], on: boolean) => {
    const all = companies.data?.map(c => c.id) ?? []
    const now = toggle(shown.all ? all : shown.ids, ids, on)
    if (now.length === 0) return
    setShown(now.length === all.length ? { all: true, ids: [] } : { all: false, ids: now })
    setPage(1)
  }
  const toggle = (list: string[], ids: string[], on: boolean) => (on ? [...new Set([...list, ...ids])] : list.filter(id => !ids.includes(id)))
  const narrowed = !!term || status !== 'current' || active.length > 0 || !shown.all
  const header = (key: SortKey, label: string, filtered: boolean, panel: ReactNode) => (
    <th scope="col" aria-sort={sort?.key === key ? sort.direction : undefined}>
      <span className="flex items-center gap-1">
        <span className="mr-auto">{label}</span>
        <SortButton label={label} direction={sort?.key === key ? sort.direction : null} onClick={() => sortBy(key)} />
        <FilterButton label={label} active={filtered}>{panel}</FilterButton>
      </span>
    </th>
  )
  const chips = (active.length > 0 || status !== 'current' || !shown.all) && (
    <span role="group" aria-label={t('employees.filteredBy')} className="flex flex-wrap items-center gap-2">
      {!shown.all && <Tag tone="brand">{t('employees.company')}</Tag>}
      {status !== 'current' && <Tag tone="brand">{t(`employees.statuses.${status}`)}</Tag>}
      {active.map(a => <Tag key={a} tone="brand">{t(`employees.filtered.${a}`)}</Tag>)}
      <button type="button" className="text-label font-medium text-brand hover:underline"
        onClick={() => { setFilters(noFilters); setStatus('current'); setShown({ all: true, ids: [] }); setPage(1) }}>{t('bulk.clearFilters')}</button>
    </span>
  )

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
        </div>
        <TableCard footer={<Pagination label={t('pagination.of', { what: t('employees.tableTitle') })} page={page} pageSize={pageSize}
          total={Number(employees.data.total)} onPage={setPage} onPageSize={size => { setPageSize(size); setPage(1) }} />}
          title={t('employees.tableTitle')} count={Number(employees.data.total)} actions={actions} status={chips}>
          {employees.data.items.length === 0 && !narrowed ? <EmptyState title={t('employees.emptyTitle')}>{t('employees.emptyText')}</EmptyState> : (
            <table>
              <thead>
                <tr>
                  {header('company', t('employees.company'), !shown.all,
                    <Checklist values={companies.data.map(c => ({ value: c.id, label: c.legalName }))}
                      isTicked={id => shown.all || shown.ids.includes(id)} onTick={(ids, on) => pickCompanies(ids, on)} />)}
                  {header('name', t('employees.employee'), status !== 'current', <>
                    <RadioGroup label={t('employees.status')} name="employee-status" value={status} onChange={value => { setStatus(value); setPage(1) }}
                      options={statuses.map(k => ({ value: k, label: t(`employees.statuses.${k}`) }))} />
                  </>)}
                  {header('job', t('employees.job'), filters.departments.length + filters.positions.length > 0, <>
                    <TextField type="search" size="sm" label={t('bulk.filterSearch', { column: t('employees.job') })} hideLabel placeholder={t('bulk.searchPlaceholder')}
                      value={jobSearch} onChange={e => setJobSearch(e.target.value)} />
                    <p className="m-0 text-hint font-medium text-muted">{t('employees.department')}</p>
                    <Checklist values={scoped(departments.data).filter(d => matches(d.name ?? '', jobSearch)).map(d => ({ value: d.id, label: d.name ?? '' }))}
                      isTicked={id => filters.departments.includes(id)} onTick={(ids, on) => filter({ departments: toggle(filters.departments, ids, on) })} />
                    <p className="m-0 text-hint font-medium text-muted">{t('employees.position')}</p>
                    <Checklist values={scoped(positions.data).filter(p => matches(p.title ?? p.name ?? '', jobSearch)).map(p => ({ value: p.id, label: p.title ?? p.name ?? '' }))}
                      isTicked={id => filters.positions.includes(id)} onTick={(ids, on) => filter({ positions: toggle(filters.positions, ids, on) })} />
                  </>)}
                  {header('schedule', t('employees.schedule'), filters.schedules.length > 0,
                    <Checklist values={scoped(schedules.data).map(s => ({ value: s.id, label: s.name }))}
                      isTicked={id => filters.schedules.includes(id)} onTick={(ids, on) => filter({ schedules: toggle(filters.schedules, ids, on) })} />)}
                  {header('basicPay', t('employees.basicPay'), !!(filters.payFrom || filters.payTo), <div className="grid grid-cols-2 gap-2">
                    <TextField size="sm" inputMode="decimal" label={t('employees.payFrom')} placeholder={t('employees.payFromPlaceholder')} value={filters.payFrom}
                      onChange={e => filter({ payFrom: e.target.value })} />
                    <TextField size="sm" inputMode="decimal" label={t('employees.payTo')} placeholder={t('employees.payToPlaceholder')} value={filters.payTo}
                      onChange={e => filter({ payTo: e.target.value })} />
                  </div>)}
                  {header('hireDate', t('employees.hireDate'), !!(filters.hiredFrom || filters.hiredTo), <div className="grid grid-cols-2 gap-2">
                    <TextField size="sm" type="date" label={t('employees.hiredFrom')} placeholder={t('employees.hiredFromPlaceholder')} value={filters.hiredFrom}
                      onChange={e => filter({ hiredFrom: e.target.value })} />
                    <TextField size="sm" type="date" label={t('employees.hiredTo')} placeholder={t('employees.hiredToPlaceholder')} value={filters.hiredTo}
                      onChange={e => filter({ hiredTo: e.target.value })} />
                  </div>)}
                  <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {employees.data.items.map(r => (
                  <tr key={r.id}>
                    <td>{companies.data.find(c => c.id === r.companyId)?.legalName ?? none}</td>
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
                    <td className="whitespace-nowrap tabular-nums">{shortDate(r.hireDate)}</td>
                    <td className="whitespace-nowrap text-right">
                      <IconButton label={t('employees.view', { name: r.name })} onClick={() => setViewing(r.id)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></svg>
                      </IconButton>
                      <IconButton label={t('employees.edit', { name: r.name })} onClick={() => setEditing(r.id)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                      </IconButton>
                      <IconButton tone="danger" label={t('employees.delete', { name: r.name })} onClick={() => setDeleting(r)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                    </td>
                  </tr>
                ))}
                {employees.data.items.length === 0 && (
                  <tr><td colSpan={7} className="text-center text-muted">{t('employees.noMatches')}</td></tr>
                )}
              </tbody>
            </table>
          )}
        </TableCard>
        <Proceed label={t('setup.proceed', { step: t('setup.steps.payItems') })} onProceed={() => void navigate({ to: '/setup/earnings-and-deductions' })} />

        {viewing && (
          <EmployeeView id={viewing} companies={companies.data} onClose={() => setViewing(null)}
            onEdit={() => { setEditing(viewing); setViewing(null) }} />
        )}
        {editing && (
          <EmployeeDrawer id={editing === 'new' ? null : editing} companies={companies.data}
            defaultCompany={shown.all ? companies.data[0].id : shown.ids[0] ?? companies.data[0].id} onClose={() => setEditing(null)} />
        )}
        {bulk && <UploadEmployees adding={bulk === 'add'} companies={companies.data}
          defaultCompany={shown.all ? companies.data[0].id : shown.ids[0] ?? companies.data[0].id} onClose={() => setBulk(null)} />}
      </>}
      <ConfirmDialog open={!!deleting} title={t('employees.deleteTitle', { name: deleting?.name })}
        confirmLabel={t('employees.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('employees.deleteText')}
      </ConfirmDialog>
    </SetupLayout>
  )
}

/**
 * An employee to read: the same tabs as editing, each detail as saved (ID number, KRA PIN and the
 * account masked), what payroll still needs on top, and Edit to change them.
 */
function EmployeeView({ id, companies, onClose, onEdit }: { id: string; companies: Company[]; onClose: () => void; onEdit: () => void }) {
  const { t } = useTranslation()
  const { api } = useServices()
  const tabsId = useId()
  const [tab, setTab] = useState<TabKey>('personal')
  const detail = useQuery({
    queryKey: ['employee', id],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/{id}', { params: { path: { id } } })
      if (error || !data) throw new Error('employee')
      return data
    },
  })
  const e = detail.data
  const company = companies.find(c => c.id === e?.companyId) ?? companies[0]
  const schedules = useSchedules()
  const plans = useLeavePlans()
  const locations = useLocations()
  const departments = useStructure('departments')
  const positions = useStructure('positions')
  const units = useStructure('units')
  const banks = useBanks(company.countryCode)
  // What they get besides basic pay: their own items and their company's "everyone" items.
  const payItems = useQuery({
    queryKey: ['employee', id, 'payItems'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/{id}/pay-items', { params: { path: { id } } })
      if (error || !data) throw new Error('payItems')
      return data
    },
  })
  const managers = useQuery({
    queryKey: ['employees', 'choices', company.id],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees/choices', { params: { query: { companyId: company.id } } })
      if (error || !data) throw new Error('choices')
      return data
    },
  })

  if (!e) {
    return <Drawer open size="wide" title={t('employees.viewTitle')} onClose={onClose}><Loading rows={6} /></Drawer>
  }
  const name = [e.firstName, e.middleName, e.lastName].filter(Boolean).join(' ')
  const nameOf = (items: StructureItem[] | undefined, itemId: string | null | undefined) => {
    const item = items?.find(i => i.id === itemId)
    return item ? item.name ?? item.title ?? null : null
  }
  const manager = managers.data?.find(m => m.id === e.job.lineManagerId)
  const bank = banks.data?.find(b => b.code === e.account?.bankCode)
  const branch = bank?.branches.find(b => b.code === e.account?.branchCode)
  const fields: Record<TabKey, [string, ReactNode][]> = {
    personal: [
      [t('employees.staffNo'), e.staffNo], [t('employees.firstName'), e.firstName], [t('employees.middleName'), e.middleName], [t('employees.lastName'), e.lastName],
      [t('employees.hireDate'), e.hireDate], [t('employees.exitDate'), e.exitDate ?? t('employees.stillHere')],
      [t('employees.dateOfBirth'), e.dateOfBirth], [t('employees.phone'), e.phone], [t('employees.email'), e.email], [t('employees.address'), e.address],
    ],
    ids: [
      [t('employees.nationalId'), e.nationalIdMasked], [t('employees.kraPin'), e.kraPinMasked], [t('employees.nssfNo'), e.nssfNo], [t('employees.shaNo'), e.shaNo],
    ],
    job: [
      [t('employees.company'), company.legalName], [t('employees.department'), nameOf(departments.data, e.job.departmentId)],
      [t('employees.position'), nameOf(positions.data, e.job.positionId)], [t('employees.unit'), nameOf(units.data, e.job.unitId)],
      [t('employees.location'), locations.data?.find(l => l.id === e.job.locationId)?.name],
      [t('employees.lineManager'), manager ? `${manager.name} (${manager.staffNo})` : null],
      [t('employees.payrollSchedule'), schedules.data?.find(s => s.id === e.job.payScheduleId)?.name],
      [t('employees.leavePlan'), plans.data?.find(p => p.id === e.job.leavePlanId)?.name],
      [t('employees.employmentType'), t(`employees.types.${e.job.employmentType}`)], [t('employees.contractEnd'), e.job.contractEnd],
    ],
    pay: [
      [t('employees.basicPay'), e.pay ? t(`employees.per.${e.pay.payBasis}`, { amount: formatMoney(e.pay.basicAmount, { currency: e.pay.currency }) }) : null],
    ],
    account: [
      [t('employees.method'), t(`employees.methods.${e.account?.method ?? 'none'}`)],
      ...(e.account?.method === 'bank' ? [
        [t('employees.bank'), bank?.name ?? e.account.bankCode], [t('employees.branch'), branch ? `${branch.name} (${branch.code})` : e.account.branchCode],
        [t('employees.accountNumber'), e.account.accountMasked],
      ] as [string, ReactNode][] : []),
      ...(e.account?.method === 'mpesa' ? [[t('employees.mpesa'), e.account.mpesaMasked]] as [string, ReactNode][] : []),
    ],
  }

  return (
    <Drawer open size="wide" title={name} onClose={onClose}
      tabs={<Tabs id={tabsId} label={t('employees.sections')} current={tab} onChange={k => setTab(k as TabKey)}
        tabs={tabs.map(k => ({ key: k, label: t(`employees.tabs.${k}`) }))} />}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.close')}</Button>
        <Button onClick={onEdit}>{t('employees.editButton')}</Button>
      </>}>
      {!e.readiness.ready && (
        <Notice>{t('employees.needs', { items: e.readiness.missing.map(m => m.label.en).join(', ') })}</Notice>
      )}
      {tabs.map(k => (
        <TabPanel key={k} id={tabsId} tab={k} current={tab}>
          <dl className="m-0 grid gap-x-6 gap-y-4 sm:grid-cols-2">
            {fields[k].map(([label, value]) => (
              <div key={label} className="flex flex-col gap-0.5">
                <dt className="text-label text-muted">{label}</dt>
                <dd className="m-0 text-body text-ink">{value || none}</dd>
              </div>
            ))}
          </dl>
          {k === 'pay' && (
            <section aria-label={t('employees.payItemsTitle')} className="flex flex-col gap-2">
              <h3 className="m-0 text-label font-semibold text-ink">{t('employees.payItemsTitle')}</h3>
              {payItems.data?.length === 0 && <p className="m-0 text-label text-muted">{t('employees.payItemsNone')}</p>}
              <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
                {(payItems.data ?? []).map(i => (
                  <li key={i.payItemId} className="text-body text-ink">
                    <span className="font-medium">{i.name}</span>{' · '}
                    {i.calcMethod === 'each_run' ? t('employees.payItemEachRun')
                      : i.rate != null ? t('employees.payItemRate', { rate: Number(i.rate) })
                        : i.amount != null ? formatMoney(i.amount) : none}
                    {' · '}{t('employees.payItemFrom', { date: shortDate(i.startsOn) })}
                    {i.endsOn && <> {t('employees.payItemUntil', { date: shortDate(i.endsOn) })}</>}
                    {i.everyone && <span className="text-muted"> {t('employees.payItemEveryone')}</span>}
                  </li>
                ))}
              </ul>
            </section>
          )}
        </TabPanel>
      ))}
    </Drawer>
  )
}

interface Values {
  firstName: string; middleName: string; lastName: string; dateOfBirth: string; phone: string; email: string; address: string; staffNo: string; hireDate: string; exitDate: string
  nationalId: string; kraPin: string; nssfNo: string; shaNo: string
  companyId: string; departmentId: string; positionId: string; unitId: string; locationId: string; lineManagerId: string; payScheduleId: string; leavePlanId: string
  employmentType: string; contractEnd: string
  basicAmount: string
  method: string; bankCode: string; branchCode: string; accountNumber: string; mpesaPhone: string
}

/** Which tab each field is on, to mark the tab when the server finds a problem there. */
const tabOf: Record<string, TabKey> = {
  firstName: 'personal', middleName: 'personal', lastName: 'personal', dateOfBirth: 'personal', phone: 'personal', email: 'personal', address: 'personal',
  staffNo: 'personal', hireDate: 'personal', exitDate: 'personal', nationalId: 'ids', kraPin: 'ids', nssfNo: 'ids', shaNo: 'ids',
  companyId: 'job', departmentId: 'job', positionId: 'job', unitId: 'job', locationId: 'job', lineManagerId: 'job', payScheduleId: 'job',
  leavePlanId: 'job', employmentType: 'job', contractEnd: 'job', basicAmount: 'pay',
  method: 'account', bankCode: 'account', branchCode: 'account', accountNumber: 'account', mpesaPhone: 'account',
}

/** The API's field names ("job.payScheduleId", "account.mpesaPhone") to the form's. */
const fieldOf = (name: string): FieldPath<Values> | null => {
  const last = name.split('.').pop()!
  const map: Record<string, FieldPath<Values>> = { basicAmount: 'basicAmount', payBasis: 'basicAmount' }
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
    // Each field is checked as soon as it is left, then as it changes.
    mode: 'onTouched',
    defaultValues: {
      firstName: employee?.firstName ?? '', middleName: employee?.middleName ?? '', lastName: employee?.lastName ?? '',
      dateOfBirth: employee?.dateOfBirth ?? '', phone: employee?.phone ?? '', email: employee?.email ?? '', address: employee?.address ?? '', staffNo: employee?.staffNo ?? '',
      hireDate: employee?.hireDate ?? '', exitDate: employee?.exitDate ?? '',
      nationalId: '', kraPin: '', nssfNo: employee?.nssfNo ?? '', shaNo: employee?.shaNo ?? '',
      companyId: companyStart,
      departmentId: employee?.job.departmentId ?? '', positionId: employee?.job.positionId ?? '', unitId: employee?.job.unitId ?? '',
      locationId: employee?.job.locationId ?? '', lineManagerId: employee?.job.lineManagerId ?? '',
      payScheduleId: employee?.job.payScheduleId ?? '', leavePlanId: employee?.job.leavePlanId ?? '',
      employmentType: employee?.job.employmentType ?? 'permanent', contractEnd: employee?.job.contractEnd ?? '',
      basicAmount: employee?.pay ? String(employee.pay.basicAmount) : '',
      method: employee?.account?.method ?? 'none', bankCode: employee?.account?.bankCode ?? '', branchCode: employee?.account?.branchCode ?? '',
      accountNumber: '', mpesaPhone: '',
    },
  })
  const [companyId, scheduleId, method, bankCode, type, departmentId] =
    useWatch({ control, name: ['companyId', 'payScheduleId', 'method', 'bankCode', 'employmentType', 'departmentId'] })
  // Date left only opens for someone who no longer works here.
  const [stillHere, setStillHere] = useState(!employee?.exitDate)
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

  const banks = useBanks(company.countryCode)
  const paysBy = usePaysBy(companyId).data
  const methods = paysBy?.methods ?? []
  // A new employee is paid the way the company usually pays.
  useEffect(() => {
    if (!employee && paysBy?.usual && getValues('method') === 'none') setValue('method', paysBy.usual)
  }, [employee, paysBy, getValues, setValue])
  const paysByMessage = () => (methods.length > 1 ? t('employees.rules.paysByEither')
    : t(methods[0] === 'bank' ? 'employees.rules.paysByBank' : 'employees.rules.paysByMpesa', { company: company.legalName }))
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
  const frequency = schedule?.frequency ?? 'monthly'
  const sameDepartmentFirst = [...(managers.data ?? [])].filter(m => m.id !== employee?.id)
    .sort((a, b) => Number(b.departmentId === departmentId && !!departmentId) - Number(a.departmentId === departmentId && !!departmentId))

  const invalidTabs = new Set(Object.keys(errors).map(name => tabOf[name]).filter(Boolean))

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!v.hireDate) {
      setError('hireDate', { message: t('employees.hireDateRequired') })
      return setTab('personal')
    }
    if (!stillHere && !v.exitDate) {
      setError('exitDate', { message: t('employees.exitDateRequired') })
      return setTab('personal')
    }
    // People who work here are paid the way the company pays.
    if (stillHere && methods.length > 0 && !methods.includes(v.method as 'bank' | 'mpesa')) {
      setError('method', { message: paysByMessage() })
      return setTab('account')
    }
    const accountTouched = !employee || ['method', 'bankCode', 'branchCode', 'accountNumber', 'mpesaPhone'].some(k => dirtyFields[k as keyof Values])
    const body = {
      companyId: v.companyId, staffNo: v.staffNo.trim() || null, firstName: v.firstName, middleName: v.middleName || null, lastName: v.lastName,
      dateOfBirth: v.dateOfBirth || null, phone: v.phone, email: v.email, address: v.address,
      // Saved numbers stay unless retyped (they're never sent back to the screen).
      nationalId: v.nationalId.trim() || null, kraPin: v.kraPin.trim() || null, nssfNo: v.nssfNo, shaNo: v.shaNo,
      hireDate: v.hireDate, exitDate: v.exitDate || null,
      job: {
        positionId: v.positionId || null, departmentId: v.departmentId || null, unitId: v.unitId || null, locationId: v.locationId || null,
        lineManagerId: v.lineManagerId || null, payScheduleId: v.payScheduleId || null, leavePlanId: v.leavePlanId || null,
        employmentType: v.employmentType, contractEnd: v.employmentType === 'contract' && v.contractEnd ? v.contractEnd : null,
      },
      // Paid as often as their schedule runs (the API takes it from the schedule).
      pay: v.basicAmount.trim() ? { basicAmount: Number(v.basicAmount.replace(/kes|ksh|[\s,]/gi, '')), payBasis: null } : null,
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

  /** A field's own check, as the API makes it: its message, or true. */
  const check = (rule: (value: string) => string | null) => ({
    validate: (value: string) => {
      const key = rule(value)
      return key ? t(`employees.rules.${key}`) : true
    },
  })
  const needed = (when: (v: Values) => boolean) => ({
    validate: (value: string, v: Values) => (when(v) && !value.trim() ? t('employees.rules.required') : true),
  })
  const saved = (masked: string | null | undefined, hint: string) => (employee && masked ? t('employees.saved_', { masked }) : hint)
  const select = (name: FieldPath<Values>, label: string, placeholder: string, options: { value: string; label: string }[],
    extra: { optional?: boolean; hint?: string; disabled?: boolean } = {}, required?: (v: Values) => boolean) => (
    <Controller control={control} name={name} rules={required ? needed(required) : undefined} render={({ field }) => (
      <SelectField label={label} placeholder={placeholder} error={errors[name]?.message} options={options} {...extra}
        value={String(field.value ?? '')} onChange={field.onChange} onBlur={field.onBlur} name={field.name} />
    )} />
  )
  const bank = banks.data?.find(b => b.code === bankCode)
  const keepsAccount = (m: string) => !!employee?.account && employee.account.method === m

  return (
    <Drawer open size="wide" title={employee ? t('employees.editTitle') : t('employees.addTitle')} onClose={onClose}
      tabs={<Tabs id={tabsId} label={t('employees.sections')} current={tab} onChange={k => setTab(k as TabKey)}
        tabs={tabs.map(k => ({ key: k, label: t(`employees.tabs.${k}`), invalid: invalidTabs.has(k) }))} />}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{employee ? t('employees.save') : t('employees.add')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit, found => { const first = Object.keys(found)[0]; if (tabOf[first]) setTab(tabOf[first]) })} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}

        <TabPanel id={tabsId} tab="personal" current={tab}>
          <div className="grid gap-4 sm:grid-cols-3">
            <TextField label={t('employees.firstName')} placeholder={t('employees.firstNamePlaceholder')} maxLength={60} error={errors.firstName?.message} {...register('firstName', check(rules.name))} />
            <TextField label={t('employees.middleName')} placeholder={t('employees.middleNamePlaceholder')} optional maxLength={60} error={errors.middleName?.message} {...register('middleName')} />
            <TextField label={t('employees.lastName')} placeholder={t('employees.lastNamePlaceholder')} maxLength={60} error={errors.lastName?.message} {...register('lastName', check(rules.name))} />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField type="date" label={t('employees.hireDate')} placeholder={t('employees.hireDatePlaceholder')} error={errors.hireDate?.message} {...register('hireDate', check(rules.required))} />
            <div className="flex flex-col gap-2">
              <Checkbox label={t('employees.stillHere')} checked={stillHere}
                onChange={e => { setStillHere(e.target.checked); if (e.target.checked) setValue('exitDate', '', { shouldDirty: true }) }} />
              <TextField type="date" label={t('employees.exitDate')} placeholder={t('employees.exitDatePlaceholder')} disabled={stillHere}
                hint={t('employees.exitDateHint')} error={errors.exitDate?.message} {...register('exitDate')} />
            </div>
            <TextField type="date" label={t('employees.dateOfBirth')} placeholder={t('employees.dateOfBirthPlaceholder')} optional error={errors.dateOfBirth?.message} {...register('dateOfBirth')} />
            <TextField label={t('employees.staffNo')} placeholder={t('employees.staffNoPlaceholder')} optional hint={t('employees.staffNoHint')} maxLength={20}
              error={errors.staffNo?.message} {...register('staffNo', check(rules.staffNo))} />
            <TextField type="tel" label={t('employees.phone')} placeholder={t('employees.phonePlaceholder')} optional error={errors.phone?.message} {...register('phone', check(rules.phone))} />
            <TextField type="email" label={t('employees.email')} placeholder={t('employees.emailPlaceholder')} optional error={errors.email?.message} {...register('email', check(rules.email))} />
            <div className="sm:col-span-2">
              <TextField label={t('employees.address')} placeholder={t('employees.addressPlaceholder')} optional maxLength={200} hint={t('employees.addressHint')}
                error={errors.address?.message} {...register('address')} />
            </div>
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="ids" current={tab}>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={t('employees.nationalId')} placeholder={t('employees.nationalIdPlaceholder')} optional inputMode="numeric"
              hint={saved(employee?.nationalIdMasked, t('employees.nationalIdHint'))} error={errors.nationalId?.message} {...register('nationalId', check(rules.nationalId))} />
            <TextField label={t('employees.kraPin')} placeholder={t('employees.kraPinPlaceholder')} optional autoCapitalize="characters"
              hint={saved(employee?.kraPinMasked, t('employees.kraPinHint'))} error={errors.kraPin?.message} {...register('kraPin', check(rules.kraPin))} />
            <TextField label={t('employees.nssfNo')} placeholder={t('employees.nssfNoPlaceholder')} optional inputMode="numeric" hint={t('employees.nssfNoHint')}
              error={errors.nssfNo?.message} {...register('nssfNo', check(rules.nssfNo))} />
            <TextField label={t('employees.shaNo')} placeholder={t('employees.shaNoPlaceholder')} optional hint={t('employees.shaNoHint')}
              error={errors.shaNo?.message} {...register('shaNo', check(rules.shaNo))} />
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="job" current={tab}>
          {companies.length > 1 && !employee && select('companyId', t('employees.company'), t('employees.companyPlaceholder'),
            companies.map(c => ({ value: c.id, label: c.legalName })))}
          <div className="grid gap-4 sm:grid-cols-2">
            {select('departmentId', t('employees.department'), t('employees.departmentPlaceholder'), serves(departments.data).map(d => ({ value: d.id, label: d.name ?? '' })), { optional: true })}
            {select('positionId', t('employees.position'), t('employees.positionPlaceholder'), serves(positions.data).map(p => ({ value: p.id, label: p.title ?? '' })), { optional: true })}
            {select('unitId', t('employees.unit'), t('employees.unitPlaceholder'), serves(units.data).map(u => ({ value: u.id, label: u.name ?? '' })), { optional: true })}
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
            <RadioGroup label={t('employees.employmentType')} name={field.name} value={field.value}
              // Only a contract has an end date.
              onChange={value => { field.onChange(value); if (value !== 'contract') setValue('contractEnd', '') }}
              options={types.map(k => ({ value: k, label: t(`employees.types.${k}`) }))} />
          )} />
          <div className="max-w-sm">
            <TextField type="date" label={t('employees.contractEnd')} placeholder={t('employees.contractEndPlaceholder')} optional disabled={type !== 'contract'}
              error={errors.contractEnd?.message} {...register('contractEnd')} />
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="pay" current={tab}>
          <div className="max-w-sm">
            <TextField label={t(`employees.amountLabels.${frequency}`)} placeholder={t('employees.amountPlaceholder')} optional inputMode="decimal"
              hint={t('employees.amountHint', { frequency: t(`employees.frequencies.${frequency}`) })}
              error={errors.basicAmount?.message} {...register('basicAmount', check(rules.amount))} />
          </div>
        </TabPanel>

        <TabPanel id={tabsId} tab="account" current={tab}>
          <Controller control={control} name="method" render={({ field }) => (
            <RadioGroup label={t('employees.method')} name={field.name} value={field.value} onChange={field.onChange} error={errors.method?.message}
              hint={methods.length === 1 && stillHere ? t('employees.paysByHint', { company: company.legalName, method: t(`employees.methods.${methods[0]}`) }) : undefined}
              options={(['bank', 'mpesa', 'none'] as const).map(m => ({ value: m, label: t(`employees.methods.${m}`) }))} />
          )} />
          {method === 'bank' && (
            <div className="grid gap-4 sm:grid-cols-2">
              <Controller control={control} name="bankCode" rules={needed(v => v.method === 'bank')} render={({ field }) => (
                <SelectField label={t('employees.bank')} placeholder={t('employees.bankPlaceholder')} error={errors.bankCode?.message}
                  options={(banks.data ?? []).map(b => ({ value: b.code, label: b.name }))} value={field.value} name={field.name} onBlur={field.onBlur}
                  // Another bank has other branches.
                  onChange={value => { field.onChange(value); setValue('branchCode', '') }} />
              )} />
              {select('branchCode', t('employees.branch'), t('employees.branchPlaceholder'), (bank?.branches ?? []).map(b => ({ value: b.code, label: `${b.name} (${b.code})` })),
                { disabled: !bankCode, hint: bankCode ? undefined : t('employees.rules.bankFirst') }, v => v.method === 'bank')}
              <TextField label={t('employees.accountNumber')} placeholder={t('employees.accountNumberPlaceholder')} inputMode="numeric"
                hint={employee?.account?.accountMasked ? t('employees.keepAccount', { masked: employee.account.accountMasked }) : undefined}
                error={errors.accountNumber?.message} {...register('accountNumber', {
                  validate: (value: string, v: Values) => {
                    if (v.method !== 'bank') return true
                    if (!value.trim()) return keepsAccount('bank') && v.bankCode === employee?.account?.bankCode ? true : t('employees.rules.required')
                    const problem = accountProblem(banks.data?.find(b => b.code === v.bankCode), value)
                    return problem ? t(`employees.rules.${problem.key}`, problem.values) : true
                  },
                })} />
            </div>
          )}
          {method === 'mpesa' && (
            <div className="max-w-sm">
              <TextField type="tel" label={t('employees.mpesa')} placeholder={t('employees.mpesaPlaceholder')}
                hint={employee?.account?.mpesaMasked ? t('employees.keepAccount', { masked: employee.account.mpesaMasked }) : t('employees.mpesaHint')}
                error={errors.mpesaPhone?.message} {...register('mpesaPhone', {
                  validate: (value: string, v: Values) => {
                    if (v.method !== 'mpesa') return true
                    if (!value.trim()) return keepsAccount('mpesa') ? true : t('employees.rules.required')
                    return rules.mpesa(value) ? t('employees.rules.mpesa') : true
                  },
                })} />
            </div>
          )}
        </TabPanel>
      </form>
    </Drawer>
  )
}

/**
 * Adding employees in a sheet (one row to start, Add row for more), or uploading a file (template,
 * then the review). One company at a time, chosen first: its schedules, leave plans, structure,
 * locations and banks are the dropdowns, its default schedule and leave plan come filled in, and
 * people who work here need the bank or M-Pesa details the company pays by. Each cell is checked
 * as soon as it is left, with the same rules as the API, which checks every row again.
 */
function UploadEmployees({ adding, companies, defaultCompany, onClose }: { adding: boolean; companies: Company[]; defaultCompany: string; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  // Uploading waits for a company to be chosen (unless there's only one); adding starts with the one shown.
  const [companyId, setCompanyId] = useState(adding || companies.length === 1 ? defaultCompany : '')
  const company = companies.find(c => c.id === companyId) ?? companies[0]
  const divisions = useStructure('divisions')
  const departments = useStructure('departments')
  const positions = useStructure('positions')
  const units = useStructure('units')
  const schedules = useSchedules()
  const plans = useLeavePlans()
  const banks = useBanks(company.countryCode)
  const paysBy = usePaysBy(company.id)
  // How the company pays only adds checks: if it can't be read, the sheet still opens.
  const loaded = [divisions, departments, positions, units, schedules, plans, banks].every(q => q.isSuccess) && !paysBy.isPending

  // What the chosen company uses, by name (as the template lists them).
  type Scoped = { allCompanies: boolean; companyIds: string[] }
  const serves = <T extends Scoped>(items: T[] | undefined) => (items ?? []).filter(i => i.allCompanies || i.companyIds.includes(company.id))
  const named = (names: string[]) => names.map(name => ({ value: name, label: name }))
  const structure = (items: StructureItem[] | undefined) => named(serves(items).map(i => i.name ?? i.title ?? ''))
  const scheduleOf = (name: string) => schedules.data?.find(s => s.name === name)
  const defaultSchedule = schedules.data?.find(s => s.defaultFor.includes(company.id))
  const planFor = (schedule: string) =>
    (plans.data?.find(p => p.defaultForSchedules.includes(schedule)) ?? plans.data?.find(p => p.defaultFor.includes(company.id)))?.name ?? ''
  const bankOf = (name: string) => banks.data?.find(b => b.name === name)
  const lists: Partial<Record<(typeof columnKeys)[number], { value: string; label: string }[]>> = {
    employmentType: types.map(k => ({ value: t(`employees.types.${k}`, { lng: 'en' }), label: t(`employees.types.${k}`) })),
    division: structure(divisions.data), department: structure(departments.data), position: structure(positions.data), unit: structure(units.data),
    payrollSchedule: named(serves(schedules.data).map(s => s.name)),
    leavePlan: named(serves(plans.data).map(p => p.name)),
    bank: named((banks.data ?? []).map(b => b.name)),
  }
  // A file can name something not in the list (a department to create): it stays, so the review shows what was written.
  const withCurrent = (options: { value: string; label: string }[], value: string | undefined) =>
    value?.trim() && !options.some(o => o.value.toLowerCase() === value.trim().toLowerCase()) ? [...options, { value, label: value }] : options

  const message = (key: string | null, values?: Record<string, unknown>) => (key ? t(`employees.rules.${key}`, values) : null)
  const working = (cells: Record<string, string>) => cells.stillHere !== 'No'
  // Paid into a bank or M-Pesa: the company's usual way to start with; for current staff, one the company pays from.
  const methods = paysBy.data?.methods ?? []
  const paidOptions = [{ value: 'Bank', label: t('employees.methods.bank') }, { value: 'M-Pesa', label: t('employees.methods.mpesa') }]
  const methodOf = (cells: Record<string, string>) => {
    const said = (cells.paidInto ?? '').replace(/[\s-]/g, '').toLowerCase()
    return said === 'bank' ? 'bank' : said === 'mpesa' ? 'mpesa' : null
  }
  const paysByMessage = () => (methods.length > 1 ? t('employees.rules.paysByEither')
    : t(methods[0] === 'bank' ? 'employees.rules.paysByBank' : 'employees.rules.paysByMpesa', { company: company.legalName }))
  const validators: Partial<Record<(typeof columnKeys)[number], (value: string, cells: Record<string, string>) => string | null>> = {
    firstName: v => message(rules.name(v)), lastName: v => message(rules.name(v)),
    staffNo: v => message(rules.staffNo(v)), phone: v => message(rules.phone(v)), email: v => message(rules.email(v)),
    nationalId: v => message(rules.nationalId(v)), kraPin: v => message(rules.kraPin(v)), nssfNo: v => message(rules.nssfNo(v)), shaNo: v => message(rules.shaNo(v)),
    hireDate: v => message(rules.required(v)),
    exitDate: (v, cells) => (!working(cells) && !v.trim() ? t('employees.rules.exitDate') : null),
    basicPay: v => message(rules.amount(v)),
    address: v => (v.trim().length > 200 ? message('addressTooLong') : null),
    paidInto: (v, cells) => {
      if (v.trim() && !methodOf(cells)) return message('paidInto')
      if (!working(cells) || methods.length === 0) return null
      return !methodOf(cells) || !methods.includes(methodOf(cells)!) ? paysByMessage() : null
    },
    bank: (v, cells) => (!v.trim() && methodOf(cells) === 'bank' ? message('required') : null),
    branch: (v, cells) => (!v.trim() && cells.bank?.trim() ? message('required') : null),
    accountNumber: (v, cells) => {
      if (!v.trim()) return methodOf(cells) === 'bank' ? message('required') : null
      const problem = accountProblem(bankOf(cells.bank ?? ''), v)
      return problem ? message(problem.key, problem.values) : null
    },
    mpesa: (v, cells) => (!v.trim() ? (methodOf(cells) === 'mpesa' ? message('required') : null) : message(rules.mpesa(v))),
  }
  const dates = new Set(['dateOfBirth', 'hireDate', 'exitDate', 'contractEnd'])
  const isContract = (cells: Record<string, string>) => (cells.employmentType ?? '').trim().toLowerCase() === 'contract'
  const columns: BulkUploadColumn[] = columnKeys.map(key => ({
    key, label: key === 'stillHere' ? t('employees.stillHere') : t(`employees.columns.${key}`),
    ...(lists[key] ? { optionsFor: (cells: Record<string, string>) => withCurrent(lists[key]!, cells[key]) } : {}),
    ...(key === 'branch' ? {
      optionsFor: (cells: Record<string, string>) => withCurrent(named((bankOf(cells.bank ?? '')?.branches ?? []).map(b => b.name)), cells.branch),
      disabledWhen: (cells: Record<string, string>) => methodOf(cells) !== 'bank' || !cells.bank?.trim(),
    } : {}),
    ...(key === 'stillHere' ? { type: 'checkbox' as const, ...(adding ? { defaultValue: 'Yes' } : {}) } : {}),
    ...(dates.has(key) ? { type: 'date' as const } : {}),
    ...(key === 'exitDate' ? { disabledWhen: (cells: Record<string, string>) => working(cells) } : {}),
    // Only a contract has an end date.
    ...(key === 'contractEnd' ? { disabledWhen: (cells: Record<string, string>) => !isContract(cells) } : {}),
    ...(adding && key === 'employmentType' ? { defaultValue: t('employees.types.permanent', { lng: 'en' }) } : {}),
    ...(adding && key === 'payrollSchedule' && defaultSchedule ? { defaultValue: defaultSchedule.name } : {}),
    ...(adding && key === 'leavePlan' ? { defaultValue: planFor(defaultSchedule?.id ?? '') } : {}),
    ...(key === 'paidInto' ? { options: paidOptions, ...(adding && paysBy.data?.usual ? { defaultValue: paysBy.data.usual === 'bank' ? 'Bank' : 'M-Pesa' } : {}) } : {}),
    // Only the details of the way they're paid are open.
    ...(['bank', 'accountNumber'].includes(key) ? { disabledWhen: (cells: Record<string, string>) => methodOf(cells) !== 'bank' } : {}),
    ...(key === 'mpesa' ? { disabledWhen: (cells: Record<string, string>) => methodOf(cells) !== 'mpesa' } : {}),
    ...(validators[key] ? { validate: validators[key] } : {}),
  })).map(c => (c.defaultValue === '' ? { ...c, defaultValue: undefined } : c))

  const follow = (cells: Record<string, string>, changed: string) =>
    changed === 'payrollSchedule' ? { ...cells, leavePlan: planFor(scheduleOf(cells.payrollSchedule)?.id ?? '') || cells.leavePlan }
      : changed === 'bank' ? { ...cells, branch: '' }
        : cells

  // Every row is for the chosen company (the sheet has no Company column).
  const forCompany = (rows: BulkUploadRow[]) => rows.map(r => ({ ...r, cells: { ...r.cells, company: company.legalName } }))

  const downloadTemplate = async () => {
    const { data } = await api.GET('/api/employees/template', { params: { query: { companyId: company.id } }, parseAs: 'blob' })
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
      params: { query: { companyId: company.id } },
      body: { file } as never,
      bodySerializer: () => {
        const form = new FormData()
        form.append('file', file)
        return form
      },
    })
    if (data) {
      // "Still works here" as a tick: empty means yes unless there's a date they left.
      const ticked = (cells: Record<string, string>) => {
        const said = (cells.stillHere ?? '').trim().toLowerCase()
        return said === 'yes' || (!said && !(cells.exitDate ?? '').trim()) ? 'Yes' : said === 'no' || !said ? 'No' : cells.stillHere
      }
      return { ...data, rows: data.rows.map(r => ({ ...r, cells: { ...r.cells, stillHere: ticked(r.cells) } })) }
    }
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('read')
  }

  const check = async (rows: BulkUploadRow[]) => {
    const { data, error } = await api.POST('/api/employees/import/check', { body: { rows: forCompany(rows) as ImportRow[] } })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('check')
  }

  const importRows = async (rows: BulkUploadRow[]) => {
    const { data, error } = await api.POST('/api/employees/import', { body: { rows: forCompany(rows) as ImportRow[], allCompanies: false, companyIds: [] } })
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

  // In the sheet's title bar it's compact, beside the search; in the upload drawer it takes the whole width.
  const companyField = (hint?: string, wide = false) => (
    <div className={wide ? 'w-full' : 'w-full sm:w-64'}>
      <SelectField label={t('employees.company')} placeholder={t('employees.companyPlaceholder')} hint={hint} value={companyId}
        size={wide ? 'md' : 'sm'} hideLabel={!wide}
        onChange={value => value && setCompanyId(value)} options={companies.map(c => ({ value: c.id, label: c.legalName }))} />
    </div>
  )

  if (!loaded) {
    return <Drawer open size={adding ? 'sheet' : 'md'} title={adding ? t('employees.add') : t('employees.upload')} onClose={onClose}><Loading rows={4} /></Drawer>
  }

  return (
    // A sheet being filled in starts again for another company, as its lists change.
    <BulkUpload key={adding ? company.id : 'upload'} open startRows={adding ? 1 : undefined} title={adding ? t('employees.add') : t('employees.upload')}
      columns={columns} onCellsChange={follow} onClose={onClose}
      onDownloadTemplate={downloadTemplate} onRead={read} onCheck={check} onImport={importRows}
      locked={companyId ? undefined : t('employees.chooseCompanyFirst')}
      firstStep={adding ? undefined : { title: t('employees.uploadCompany'), content: companyField(t('employees.uploadCompanyHint'), true) }}
      labels={{
        review: count => (adding ? t('employees.several', { count }) : t('employees.review', { count })),
        importButton: count => (adding ? t('employees.several', { count }) : t('employees.importButton', { count })),
        templateHint: t('employees.templateHint'),
      }}>
      {adding && companies.length > 1 ? companyField() : undefined}
    </BulkUpload>
  )
}
