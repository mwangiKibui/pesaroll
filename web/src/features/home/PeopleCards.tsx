import { useRouter } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { featureOn } from '../../app/features'
import { Loading, ProgressBar, Tag } from '../../components/feedback'
import { Card } from '../../components/TableCard'

type HomePeople = components['schemas']['HomePeople']
type Missing = components['schemas']['HomeMissing']

/** People → Employees, the app's page (OP-07), once its flag is on; until then no filtered links (Employees links to the setup list). */
const employeesPage = '/people/employees'
const employeesHref = (search = '') => featureOn('employees') ? `${employeesPage}${search}` : null

const shortDate = (iso: string) =>
  new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${iso}T00:00:00Z`))

/** Today in Kenya as YYYY-MM-DD (the API counts the month the same way). */
const kenyaToday = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Nairobi' }).format(new Date())

function AppLink({ href, label, className = '', children }: { href: string; label?: string; className?: string; children: ReactNode }) {
  const router = useRouter()
  return (
    <a href={href} aria-label={label} onClick={e => { e.preventDefault(); void router.history.push(href) }}
      className={`rounded-control text-brand no-underline hover:underline ${className}`}>{children}</a>
  )
}

function Stat({ value, label, href }: { value: number; label: string; href: string | null }) {
  const body = <><span className="block font-display text-title text-ink">{value}</span><span className="block text-label text-muted">{label}</span></>
  return href ? <AppLink href={href} className="block">{body}</AppLink> : <div>{body}</div>
}

/** Employees now and who joined this month, with a few names. */
export function PeopleNumbersCard({ people }: { people: HomePeople | undefined }) {
  const { t } = useTranslation()
  if (!people) return <Card title={t('home.people.title')}><Loading rows={2} /></Card>
  const monthStart = `${kenyaToday().slice(0, 8)}01`
  return (
    <Card title={t('home.people.title')}>
      <div className="grid grid-cols-2 gap-4">
        <Stat value={Number(people.employees)} label={t('home.people.employees')} href={employeesHref() ?? '/setup/employees'} />
        <Stat value={Number(people.joinedThisMonth)} label={t('home.people.joined')} href={employeesHref(`?hiredFrom=${monthStart}`)} />
      </div>
      {people.joined.length > 0 ? (
        <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
          {people.joined.map(j => (
            <li key={j.id} className="flex items-center justify-between gap-3 text-label">
              <span className="truncate text-ink">{j.name}</span>
              <span className="flex-none text-muted">{t('home.people.started', { date: shortDate(j.hireDate) })}</span>
            </li>
          ))}
        </ul>
      ) : <p className="m-0 text-label text-muted">{t('home.people.noneJoined')}</p>}
    </Card>
  )
}

/** How many can be paid in the next pay run, and what the rest lack, one line per detail with its code. */
export function ReadyForPayrollCard({ people }: { people: HomePeople | undefined }) {
  const { t, i18n } = useTranslation()
  const title = t('home.ready.title')
  if (!people) return <Card title={title}><Loading rows={2} /></Card>
  const { nextPayDate, missing } = people.readiness
  const ready = Number(people.readiness.ready)
  const total = Number(people.readiness.total)
  const line = (m: Missing) => t([`home.ready.missing.${m.key}`, 'home.ready.missing.other'],
    { count: Number(m.people), label: i18n.language === 'sw' ? m.label.sw : m.label.en })
  return (
    <Card title={title}>
      <div className="flex flex-col gap-2">
        <p className="m-0 text-body text-ink">
          {nextPayDate ? t('home.ready.summary', { ready, total, date: shortDate(nextPayDate) }) : t('home.ready.summaryNoDate', { ready, total })}
        </p>
        <ProgressBar label={title} value={ready} max={total} />
      </div>
      {total === 0 ? <p className="m-0 text-label text-muted">{t('home.ready.noPeople')}</p>
        : missing.length === 0 ? <p className="m-0 text-label text-muted">{t('home.ready.everyone')}</p>
          : (
            <ul className="m-0 flex list-none flex-col gap-2 p-0">
              {missing.map(m => {
                const href = employeesHref(`?missing=${m.key}`)
                return (
                  <li key={m.key} className="flex flex-wrap items-center gap-x-3 gap-y-1 text-label">
                    <span className="text-ink">{line(m)}</span>
                    {m.code && <Tag tone="warning">{m.code}</Tag>}
                    {href && <AppLink href={href} label={t('home.ready.fixLabel', { what: line(m) })} className="ml-auto font-medium">{t('home.ready.fix')}</AppLink>}
                  </li>
                )
              })}
            </ul>
          )}
    </Card>
  )
}

/** A card whose task isn't done yet; only ever shown with its flag on. */
export function ComingCard({ cardKey }: { cardKey: string }) {
  const { t } = useTranslation()
  return (
    <Card title={t(`home.cards.${cardKey}`)}>
      <p className="m-0 text-label text-muted">{t('home.comingSoon')}</p>
    </Card>
  )
}
