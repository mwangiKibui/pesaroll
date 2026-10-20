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
import { EmployeeDrawer } from '../features/employees/EmployeeForm'

/** Home for someone with an organization, in the organization portal's shell (OP-01): cards by permission and flag (OP-30). */
export default function HomePage() {
  return (
    <OrgShell current="home">
      <HomeCards />
    </OrgShell>
  )
}

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

  if (!me.data) return <Loading rows={3} />

  return (
    <div className="flex flex-col gap-5">
      {/* No greeting or date on screen; the page is titled for screen readers. */}
      <h1 className="sr-only">{t('home.title')}</h1>
      {shows('addEmployee') && <div className="flex justify-end"><Button onClick={() => setAdding(true)}>{t('home.addEmployee')}</Button></div>}

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
