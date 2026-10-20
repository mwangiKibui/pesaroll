import { useRouter } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../../api/schema'
import { Loading, ProgressBar, Tag } from '../../components/feedback'
import { Card } from '../../components/TableCard'

type HomePeople = components['schemas']['HomePeople']
type Missing = components['schemas']['HomeMissing']

/** People → Employees (OP-07), filtered by its address: `?missing=<readiness key>`, `?hiredFrom=YYYY-MM-DD`. */
const employeesHref = (search = '') => `/people/employees${search}`

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

/** A number card: its title, and the number linking to the matching People → Employees list. */
function NumberCard({ title, value, href }: { title: string; value: number | undefined; href: string }) {
  return (
    <Card title={title}>
      {value === undefined ? <Loading rows={1} />
        : <AppLink href={href} label={`${value} ${title}`} className="block font-display text-title text-ink">{value}</AppLink>}
    </Card>
  )
}

/** Employees now (OP-30). */
export function EmployeesCard({ people }: { people: HomePeople | undefined }) {
  const { t } = useTranslation()
  return <NumberCard title={t('home.people.employees')} value={people && Number(people.employees)} href={employeesHref()} />
}

/** How many joined this month, up to today (OP-30); no names. */
export function JoinedThisMonthCard({ people }: { people: HomePeople | undefined }) {
  const { t } = useTranslation()
  return <NumberCard title={t('home.people.joined')} value={people && Number(people.joinedThisMonth)}
    href={employeesHref(`?hiredFrom=${kenyaToday().slice(0, 8)}01`)} />
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
                    <AppLink href={href} label={t('home.ready.fixLabel', { what: line(m) })} className="ml-auto font-medium">{t('home.ready.fix')}</AppLink>
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
