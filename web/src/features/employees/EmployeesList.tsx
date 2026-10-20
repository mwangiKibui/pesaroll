import { useQuery } from '@tanstack/react-query'
import { useDeferredValue, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { permissions, usePermissions } from '../../app/permissions'
import { useServices } from '../../app/services'
import { Button, IconButton } from '../../components/Button'
import { Checklist, FilterButton, SortButton } from '../../components/ColumnControls'
import { CompanySelect, type CompanySelection } from '../../components/CompanySelect'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { EmptyState, Loading, Tag } from '../../components/feedback'
import { RadioGroup, SelectField, TextField } from '../../components/fields'
import { PlusIcon, UploadIcon } from '../../components/icons'
import { Pagination } from '../../components/Pagination'
import { matches } from '../../components/search'
import { TableCard } from '../../components/TableCard'
import { useToast } from '../../components/toast'
import { formatMoney } from '../../lib/money'
import { useCompanies, useSchedules, useStructure } from '../../setup/hooks'
import { EmployeeDrawer } from './EmployeeForm'
import { EmployeeView } from './EmployeeView'
import { listStatuses, missingFilters, type ListFilters, type Missing, type Status } from './filters'
import { formatDate, none, shortDate, todayInKenya, type Row } from './shared'
import { UploadEmployees } from './UploadEmployees'

const statuses = ['current', 'former', 'all'] as const

type SortKey = 'company' | 'name' | 'job' | 'schedule' | 'basicPay' | 'hireDate'

/** The column filters: ids ticked (none ticked: no filter), amounts and dates as typed. */
interface ColumnFilters { departments: string[]; positions: string[]; schedules: string[]; payFrom: string; payTo: string; hiredFrom: string; hiredTo: string }
const noFilters: ColumnFilters = { departments: [], positions: [], schedules: [], payFrom: '', payTo: '', hiredFrom: '', hiredTo: '' }
const activeFilters = (f: ColumnFilters) => [
  ...(f.departments.length ? ['department'] : []), ...(f.positions.length ? ['position'] : []), ...(f.schedules.length ? ['schedule'] : []),
  ...(f.payFrom || f.payTo ? ['basicPay'] : []), ...(f.hiredFrom || f.hiredTo ? ['hireDate'] : []),
]

/**
 * ONB-09 and OP-07: the organization's people. A server-searched, paged list (companies can have
 * thousands); adding one in a drawer with tabs (personal, IDs and numbers, job, pay, paid into),
 * several in a sheet of empty rows, or many from a spreadsheet, all held to the same checks by the
 * API. Used by the setup step (several companies, with the company selector) and People → Employees.
 */
export function EmployeesList({ after, companyId, filters: listFilters, onFiltersChange }: {
  after?: ReactNode
  /** The company chosen in the top bar (People → Employees); without it, every company with the company selector (setup). */
  companyId?: string
  /** Who to show and what's missing, when the page keeps them (in its address); otherwise kept here. */
  filters?: ListFilters
  onFiltersChange?: (filters: ListFilters) => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const companies = useCompanies()
  const schedules = useSchedules()
  const { can } = usePermissions()
  const one = companyId !== undefined
  const canManage = can(permissions.employees.manage, companyId)
  // ACC-05: pay is sent only when asked for (Show amounts), to people with View on pay (for this company, in the app).
  const canViewPay = can(permissions.pay.view, companyId)
  const [showPay, setShowPay] = useState(false)
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [search, setSearch] = useState('')
  const [ownStatus, setOwnStatus] = useState<Status>('current')
  const status = listFilters?.status ?? ownStatus
  const missing = listFilters?.missing ?? null
  const hiredSince = listFilters?.hiredFrom ?? null
  const changeFilters = (next: ListFilters) => {
    if (onFiltersChange) onFiltersChange(next)
    else setOwnStatus(next.status)
    setPage(1)
  }
  const setStatus = (value: string) => changeFilters({ status: value as Status, missing, hiredFrom: hiredSince })
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
  const [pageSize, setPageSize] = useState(10)
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const [viewing, setViewing] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<Row | null>(null)
  const [busy, setBusy] = useState(false)
  const [bulk, setBulk] = useState<'upload' | 'add' | null>(null)
  const term = useDeferredValue(search.trim())


  const amount = (text: string) => (text.trim() && Number(text.replace(/[\s,]/g, '')) >= 0 ? Number(text.replace(/[\s,]/g, '')) : undefined)
  const query = {
    search: term || undefined, status, page, pageSize, companyId, companyIds: one || shown.all ? undefined : shown.ids, missing: missing ?? undefined,
    sort: sort?.key, dir: sort ? (sort.direction === 'ascending' ? 'asc' : 'desc') : undefined,
    departmentIds: filters.departments.length ? filters.departments : undefined, positionIds: filters.positions.length ? filters.positions : undefined,
    scheduleIds: filters.schedules.length ? filters.schedules : undefined, payFrom: amount(filters.payFrom), payTo: amount(filters.payTo),
    hiredFrom: hiredSince ?? (filters.hiredFrom || undefined), hiredTo: filters.hiredTo || undefined, includePay: showPay || undefined,
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

  // In the table's header line: the search, then Add and Upload.
  const actions = <>
    <div className="w-full sm:w-64">
      <TextField type="search" size="sm" label={t('employees.search')} hideLabel placeholder={t('employees.searchPlaceholder')} value={search}
        onChange={e => { setSearch(e.target.value); setPage(1) }} />
    </div>
    {canViewPay && <Button variant="ghost" onClick={() => setShowPay(s => !s)}>{showPay ? t('employees.hideAmounts') : t('employees.showAmounts')}</Button>}
    {canManage && <>
      <Button onClick={() => setBulk('add')}><PlusIcon />{t('employees.add')}</Button>
      <Button variant="secondary" onClick={() => setBulk('upload')}><UploadIcon />{t('employees.upload')}</Button>
    </>}
  </>

  // What the company selector shows: the structure and schedules those companies use.
  type Scoped = { allCompanies: boolean; companyIds: string[] }
  const scoped = <T extends Scoped>(items: T[] | undefined) =>
    (items ?? []).filter(i => (one ? i.allCompanies || i.companyIds.includes(companyId)
      : shown.all || i.allCompanies || i.companyIds.some(id => shown.ids.includes(id))))
  // The Company column's filter is the company selector's choice: ticking in either changes both. One stays ticked.
  const pickCompanies = (ids: string[], on: boolean) => {
    const all = companies.data?.map(c => c.id) ?? []
    const now = toggle(shown.all ? all : shown.ids, ids, on)
    if (now.length === 0) return
    setShown(now.length === all.length ? { all: true, ids: [] } : { all: false, ids: now })
    setPage(1)
  }
  const toggle = (list: string[], ids: string[], on: boolean) => (on ? [...new Set([...list, ...ids])] : list.filter(id => !ids.includes(id)))
  const narrowed = !!term || status !== 'current' || !!missing || !!hiredSince || active.length > 0 || !shown.all
  const header = (key: SortKey, label: string, filtered: boolean, panel?: ReactNode) => (
    <th scope="col" aria-sort={sort?.key === key ? sort.direction : undefined}>
      <span className="flex items-center gap-1">
        <span className="mr-auto">{label}</span>
        <SortButton label={label} direction={sort?.key === key ? sort.direction : null} onClick={() => sortBy(key)} />
        {panel && <FilterButton label={label} active={filtered}>{panel}</FilterButton>}
      </span>
    </th>
  )
  const chips = (active.length > 0 || (!one && status !== 'current') || !!hiredSince || !shown.all) && (
    <span role="group" aria-label={t('employees.filteredBy')} className="flex flex-wrap items-center gap-2">
      {!shown.all && <Tag tone="brand">{t('employees.company')}</Tag>}
      {!one && status !== 'current' && <Tag tone="brand">{t(`employees.statuses.${status}`)}</Tag>}
      {hiredSince && <Tag tone="brand">{t('employees.joinedSince', { date: formatDate(hiredSince) })}</Tag>}
      {active.map(a => <Tag key={a} tone="brand">{t(`employees.filtered.${a}`)}</Tag>)}
      <button type="button" className="text-label font-medium text-brand hover:underline"
        onClick={() => { setFilters(noFilters); changeFilters({ status: 'current', missing: null, hiredFrom: null }); setShown({ all: true, ids: [] }) }}>{t('bulk.clearFilters')}</button>
    </span>
  )

  // People → Employees: when they joined, or when they leave or left (today in Kenya, as the API counts).
  const today = todayInKenya()
  const joinedOrLeaving = (r: Row) => !r.exitDate ? t('employees.joined', { date: formatDate(r.hireDate) })
    : r.exitDate >= today ? t('employees.leavingOn', { date: formatDate(r.exitDate) }) : t('employees.left', { date: formatDate(r.exitDate) })
  const readiness = (r: Row) => (r.readiness.ready ? <Tag tone="brand">{t('employees.ready')}</Tag> : (
    <ul className="m-0 flex list-none flex-col items-start gap-1 p-0">
      {r.readiness.missing.map(m => (
        <li key={m.key} className="flex flex-wrap items-center gap-1.5">
          <Tag tone="warning">{t(`employees.missing.${m.key}`, { defaultValue: m.label.en })}</Tag>
          {m.code && <span className="font-mono text-hint text-muted">{m.code}</span>}
        </li>
      ))}
    </ul>
  ))

  return (
    <>
      {!ready ? <Loading rows={4} /> : <>
        <div className="flex flex-wrap items-end gap-3">
          <div className="w-full sm:w-56">
            <SelectField label={t('employees.department')} placeholder={t('employees.departmentFilterPlaceholder')} size="sm"
              value={filters.departments.length === 1 ? filters.departments[0] : 'all'}
              options={[{ value: 'all', label: t('employees.allDepartments') }, ...scoped(departments.data).map(d => ({ value: d.id, label: d.name ?? '' }))]}
              onChange={value => filter({ departments: value === 'all' ? [] : [value] })} />
          </div>
          {one && <>
            <div className="w-full sm:w-56">
              <SelectField label={t('employees.listStatus')} placeholder={t('employees.listStatusPlaceholder')} size="sm" value={status}
                options={listStatuses.map(k => ({ value: k, label: t(`employees.listStatuses.${k}`) }))} onChange={setStatus} />
            </div>
            <div className="w-full sm:w-64">
              <SelectField label={t('employees.readyFilter')} placeholder={t('employees.readyFilterPlaceholder')} size="sm" value={missing ?? 'all'}
                options={['all', ...missingFilters].map(k => ({ value: k, label: t(`employees.readyFilters.${k}`) }))}
                onChange={value => changeFilters({ status, missing: value === 'all' ? null : value as Missing, hiredFrom: hiredSince })} />
            </div>
          </>}
          {!one && companies.data.length > 1 && (
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
                  {!one && header('company', t('employees.company'), !shown.all,
                    <Checklist values={companies.data.map(c => ({ value: c.id, label: c.legalName }))}
                      isTicked={id => shown.all || shown.ids.includes(id)} onTick={(ids, on) => pickCompanies(ids, on)} />)}
                  {header('name', t('employees.employee'), status !== 'current', one ? undefined : <>
                    <RadioGroup label={t('employees.status')} name="employee-status" value={status} onChange={setStatus}
                      options={statuses.map(k => ({ value: k, label: t(`employees.statuses.${k}`) }))} />
                  </>)}
                  {header('job', t('employees.position'), filters.positions.length > 0, <>
                    <TextField type="search" size="sm" label={t('bulk.filterSearch', { column: t('employees.position') })} hideLabel placeholder={t('bulk.searchPlaceholder')}
                      value={jobSearch} onChange={e => setJobSearch(e.target.value)} />
                    <Checklist values={scoped(positions.data).filter(p => matches(p.title ?? p.name ?? '', jobSearch)).map(p => ({ value: p.id, label: p.title ?? p.name ?? '' }))}
                      isTicked={id => filters.positions.includes(id)} onTick={(ids, on) => filter({ positions: toggle(filters.positions, ids, on) })} />
                  </>)}
                  {header('schedule', t('employees.schedule'), filters.schedules.length > 0,
                    <Checklist values={scoped(schedules.data).map(s => ({ value: s.id, label: s.name }))}
                      isTicked={id => filters.schedules.includes(id)} onTick={(ids, on) => filter({ schedules: toggle(filters.schedules, ids, on) })} />)}
                  {!canViewPay ? <th scope="col">{t('employees.basicPay')}</th> : header('basicPay', t('employees.basicPay'), !!(filters.payFrom || filters.payTo), <div className="grid grid-cols-2 gap-2">
                    <TextField size="sm" inputMode="decimal" label={t('employees.payFrom')} placeholder={t('employees.payFromPlaceholder')} value={filters.payFrom}
                      onChange={e => filter({ payFrom: e.target.value })} />
                    <TextField size="sm" inputMode="decimal" label={t('employees.payTo')} placeholder={t('employees.payToPlaceholder')} value={filters.payTo}
                      onChange={e => filter({ payTo: e.target.value })} />
                  </div>)}
                  {one && <th scope="col">{t('employees.readyForPayroll')}</th>}
                  {!one && header('hireDate', t('employees.hireDate'), !!(filters.hiredFrom || filters.hiredTo), <div className="grid grid-cols-2 gap-2">
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
                    {!one && <td>{companies.data.find(c => c.id === r.companyId)?.legalName ?? none}</td>}
                    <td>
                      <span className="block font-medium text-ink">{r.name}</span>
                      <span className="block font-mono text-hint text-muted">{r.staffNo}</span>
                      {one ? <span className="block text-hint text-muted">{joinedOrLeaving(r)}</span>
                        : r.exitDate && <span className="block text-hint text-muted">{t('employees.left', { date: r.exitDate })}</span>}
                    </td>
                    <td>
                      {r.positionTitle || r.departmentName ? <>
                        <span className="block">{r.positionTitle ?? none}</span>
                        {r.departmentName && <span className="block text-hint text-muted">{r.departmentName}</span>}
                      </> : none}
                    </td>
                    <td>{schedules.data.find(s => s.id === r.payScheduleId)?.name ?? none}</td>
                    <td className="whitespace-nowrap">
                      {r.payHidden ? <span className={canViewPay ? 'tabular-nums' : 'text-muted'}>{canViewPay ? t('employees.payDots') : t('employees.payHidden')}</span>
                        : r.basicAmount != null && r.payBasis
                          ? <span className="tabular-nums">{t(`employees.per.${r.payBasis}`, { amount: formatMoney(r.basicAmount, { currency: r.currency ?? 'KES' }) })}</span>
                          : none}
                    </td>
                    {one ? <td>{readiness(r)}</td> : <td className="whitespace-nowrap tabular-nums">{shortDate(r.hireDate)}</td>}
                    <td className="whitespace-nowrap text-right">
                      <IconButton label={t('employees.view', { name: r.name })} onClick={() => setViewing(r.id)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></svg>
                      </IconButton>
                      {canManage && <>
                      <IconButton label={t('employees.edit', { name: r.name })} onClick={() => setEditing(r.id)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                      </IconButton>
                      <IconButton tone="danger" label={t('employees.delete', { name: r.name })} onClick={() => setDeleting(r)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                      </>}
                    </td>
                  </tr>
                ))}
                {employees.data.items.length === 0 && (
                  <tr><td colSpan={one ? 6 : 7} className="text-center text-muted">{t('employees.noMatches')}</td></tr>
                )}
              </tbody>
            </table>
          )}
        </TableCard>
        {after}

        {viewing && (
          <EmployeeView id={viewing} companies={companies.data} onClose={() => setViewing(null)}
            onEdit={canManage ? () => { setEditing(viewing); setViewing(null) } : undefined} />
        )}
        {editing && (
          <EmployeeDrawer id={editing === 'new' ? null : editing} companies={companies.data}
            defaultCompany={companyId ?? (shown.all ? companies.data[0].id : shown.ids[0] ?? companies.data[0].id)} onClose={() => setEditing(null)} />
        )}
        {bulk && <UploadEmployees adding={bulk === 'add'} companies={companies.data}
          defaultCompany={companyId ?? (shown.all ? companies.data[0].id : shown.ids[0] ?? companies.data[0].id)} onClose={() => setBulk(null)} />}
      </>}
      <ConfirmDialog open={!!deleting} title={t('employees.deleteTitle', { name: deleting?.name })}
        confirmLabel={t('employees.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('employees.deleteText')}
      </ConfirmDialog>
    </>
  )
}
