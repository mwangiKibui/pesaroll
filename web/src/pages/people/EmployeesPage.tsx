import { useNavigate, useSearch } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { useCurrentCompany } from '../../app/company'
import { OrgShell } from '../../app/OrgShell'
import { permissions, usePermissions } from '../../app/permissions'
import { EmptyState, Loading } from '../../components/feedback'
import { EmployeesList } from '../../features/employees/EmployeesList'
import { readFilters, type ListFilters } from '../../features/employees/filters'

/**
 * OP-07, People → Employees: the people of the company chosen in the top bar, searched, filtered
 * (status and what payroll still needs, kept in the address) and paged on the server; Add and Upload
 * for people with Manage on employees; pay as ACC-05 allows. The list, profile, form and upload are
 * the setup step's (features/employees).
 */
export default function PeopleEmployeesPage() {
  return (
    <OrgShell current="employees">
      <Employees />
    </OrgShell>
  )
}

function Employees() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { companyId } = useCurrentCompany()
  const { ready, can } = usePermissions()
  const search = readFilters(useSearch({ strict: false }))
  const filters: ListFilters = { status: search.status ?? 'current', missing: search.missing ?? null, hiredFrom: search.hiredFrom ?? null }
  const setFilters = (next: ListFilters) => void navigate({
    to: '/people/employees', replace: true,
    search: { ...(next.status !== 'current' ? { status: next.status } : {}), ...(next.missing ? { missing: next.missing } : {}), ...(next.hiredFrom ? { hiredFrom: next.hiredFrom } : {}) },
  })

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('employees.peopleTitle')}</h1>
        <p className="m-0 text-muted">{t('employees.peopleIntro')}</p>
      </div>
      {!ready || !companyId ? <Loading rows={4} />
        : !can(permissions.employees.view, companyId) ? <EmptyState title={t('employees.noAccessTitle')}>{t('employees.noAccessText')}</EmptyState>
          // A new company starts afresh: page 1, nothing searched, amounts hidden again.
          : <EmployeesList key={companyId} companyId={companyId} filters={filters} onFiltersChange={setFilters} />}
    </div>
  )
}
