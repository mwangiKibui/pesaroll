import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useCurrentCompany } from '../app/company'
import { featureOn } from '../app/features'
import { OrgShell } from '../app/OrgShell'
import { usePermissions } from '../app/permissions'
import { Button } from '../components/Button'
import { Loading } from '../components/feedback'
import { visibleCards } from '../features/home/cards'
import { ComingCard, PeopleNumbersCard, ReadyForPayrollCard } from '../features/home/PeopleCards'
import { useHomePeople } from '../features/home/useHomePeople'
import { useCompanies, useMe } from '../setup/hooks'
import { EmployeeDrawer } from './EmployeesPage'

/** Home for someone with an organization, in the organization portal's shell (OP-01): cards by permission and flag (OP-30). */
export default function HomePage() {
  return (
    <OrgShell current="home">
      <HomeCards />
    </OrgShell>
  )
}

const longDate = () =>
  new Intl.DateTimeFormat('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'Africa/Nairobi' }).format(new Date())

/** Inside the shell, so it follows the company chosen in the top bar. */
function HomeCards() {
  const { t } = useTranslation()
  const me = useMe()
  const companies = useCompanies()
  const { companyId } = useCurrentCompany()
  const { ready, can } = usePermissions()
  const [adding, setAdding] = useState(false)

  const cards = ready ? visibleCards(can, featureOn, companyId ?? undefined) : []
  const shows = (key: string) => cards.some(c => c.key === key)
  const people = useHomePeople(companyId, shows('peopleNumbers') || shows('readyForPayroll'))
  const company = companies.data?.find(c => c.id === companyId)

  if (!me.data) return <div className="mx-auto max-w-5xl"><Loading rows={3} /></div>

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="m-0 font-display text-title text-ink">{t('home.title', { name: me.data.firstName })}</h1>
          <p className="m-0 text-muted">
            <span>{longDate()}</span>
            {company && <><span aria-hidden="true"> · </span><span>{company.legalName}</span></>}
          </p>
        </div>
        {shows('addEmployee') && <Button onClick={() => setAdding(true)}>{t('home.addEmployee')}</Button>}
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {cards.map(c => {
          switch (c.key) {
            case 'addEmployee': return null
            case 'peopleNumbers': return <PeopleNumbersCard key={c.key} people={people.data} />
            case 'readyForPayroll': return <ReadyForPayrollCard key={c.key} people={people.data} />
            default: return <ComingCard key={c.key} cardKey={c.key} />
          }
        })}
      </div>

      {adding && companies.data && companyId && (
        <EmployeeDrawer id={null} companies={companies.data} defaultCompany={companyId} onClose={() => setAdding(false)} />
      )}
    </div>
  )
}
