import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export interface NavItem { key: string; label: string; href: string; badge?: number | string }
export interface NavGroup { label?: string; items: NavItem[] }

interface AppShellProps {
  /** Grouped like the prototype: Home · People · Pay · Organization · Admin. */
  groups: NavGroup[]
  current: string
  organization: string
  person: string
  /** "Finish setting up" card while setup is incomplete. */
  setup?: { done: number; total: number; next: string; href: string }
  children: ReactNode
}

/** The admin app: dark sidebar on the left (behind a Menu button on phones), page on the right. */
export function AppShell({ groups, current, organization, person, setup, children }: AppShellProps) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const navId = useId()
  return (
    <div className="min-h-dvh bg-page md:grid md:grid-cols-[248px_minmax(0,1fr)]">
      <div className="flex items-center justify-between bg-sidebar px-4 py-3 md:hidden">
        <span className="font-display text-drawer-title text-sidebar-accent">Pesaroll</span>
        <button type="button" aria-expanded={open} aria-controls={navId} onClick={() => setOpen(o => !o)}
          className="min-h-11 rounded-control px-3 text-label font-medium text-sidebar-ink hover:bg-sidebar-raise">{t('common.menu')}</button>
      </div>
      <nav id={navId} aria-label={t('nav.main')}
        className={`${open ? 'flex' : 'hidden'} flex-col gap-0.5 bg-sidebar px-3 py-4 md:sticky md:top-0 md:flex md:h-dvh md:overflow-y-auto`}>
        <span className="hidden px-3 pb-1.5 font-display text-drawer-title text-sidebar-accent md:block">Pesaroll</span>
        <div className="mb-2 mt-1 rounded-card bg-sidebar-raise px-3 py-2.5 text-hint">
          <div className="font-semibold text-sidebar-ink">{organization}</div>
          <div className="text-sidebar-muted">{person}</div>
        </div>
        {groups.map((group, gi) => (
          <div key={group.label ?? gi} className="flex flex-col gap-0.5">
            {group.label && <div className="px-3 pb-1 pt-3.5 text-tag uppercase tracking-wider text-sidebar-muted">{group.label}</div>}
            {group.items.map(item => {
              const here = item.key === current
              return (
                <a key={item.key} href={item.href} aria-current={here ? 'page' : undefined}
                  className={`flex min-h-10 items-center gap-2.5 rounded-control px-3 text-label no-underline ${here ? 'bg-sidebar-current font-semibold text-sidebar-current-ink' : 'font-medium text-sidebar-ink hover:bg-sidebar-raise'}`}>
                  <span aria-hidden="true" className={`size-1.5 flex-none rounded-full ${here ? 'bg-brand' : ''}`} />
                  {item.label}
                  {item.badge !== undefined && <span className="ml-auto rounded-full bg-warning-soft px-2 text-tag font-semibold text-warning-ink">{item.badge}</span>}
                </a>
              )
            })}
          </div>
        ))}
        {setup && (
          <a href={setup.href} className="mt-3.5 flex flex-col gap-1.5 rounded-card bg-sidebar-raise p-3 no-underline">
            <span className="text-hint font-semibold text-sidebar-ink">{t('setup.progress', { done: setup.done, total: setup.total })}</span>
            <span aria-hidden="true" className="block h-1.5 overflow-hidden rounded-full bg-sidebar"><span className="block h-1.5 bg-sidebar-accent" style={{ width: `${Math.round(setup.done / setup.total * 100)}%` }} /></span>
            <span className="text-tag text-sidebar-muted">{t('setup.next', { step: setup.next })}</span>
          </a>
        )}
      </nav>
      <main className="min-w-0 px-4 py-6 md:px-8">{children}</main>
    </div>
  )
}

interface PhoneShellProps {
  company: string
  /** "Habari, Peter" */
  title: string
  tabs: NavItem[]
  current: string
  children: ReactNode
}

/** The employee portal, phone first: one column of cards and a bottom bar. */
export function PhoneShell({ company, title, tabs, current, children }: PhoneShellProps) {
  const { t } = useTranslation()
  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-[480px] flex-col bg-page">
      <header className="px-5 pb-3 pt-7">
        <div className="text-hint text-muted">{company}</div>
        <h1 className="m-0 font-display text-title-sm text-ink">{title}</h1>
      </header>
      <main className="flex flex-1 flex-col gap-3.5 px-5 pb-5">{children}</main>
      <nav aria-label={t('nav.main')} className="sticky bottom-0 flex border-t border-line bg-card">
        {tabs.map(tab => {
          const here = tab.key === current
          return (
            <a key={tab.key} href={tab.href} aria-current={here ? 'page' : undefined}
              className={`flex min-h-14 flex-1 items-center justify-center text-tag no-underline ${here ? 'font-semibold text-brand' : 'text-muted'}`}>{tab.label}</a>
          )
        })}
      </nav>
    </div>
  )
}
