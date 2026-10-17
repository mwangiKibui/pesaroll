import { useEffect, useId, useRef, useState, type MouseEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Logo } from './Logo'

export interface NavItem { key: string; label: string; href: string; badge?: number | string }

/** A sidebar section: a heading that opens and closes (none for the top one), and its links. */
export interface ShellSection { key: string; label?: string; links: NavItem[] }

interface AppShellProps {
  /** Only what the person may open (app/navigation.ts decides). */
  sections: ShellSection[]
  current: string
  organization: string
  /** The rest of the top bar: the company switcher and search (app/OrgShell.tsx). */
  topBar?: ReactNode
  /** Who is signed in, at the bottom of the sidebar. */
  person: { name: string; detail?: string }
  /** The person's menu (how they use Pesaroll, Where you're signed in, Sign out); gets a function that closes it. */
  personMenu?: (close: () => void) => ReactNode
  /** Follows a link inside the app (no reload); links behave as plain links without it. */
  onNavigate?: (href: string) => void
  children: ReactNode
}

const chevron = (open: boolean) => (
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d={open ? 'M6 15l6-6 6 6' : 'M9 6l6 6-6 6'} />
  </svg>
)

/**
 * The organization portal (OP-01, as in the prototype): a white top bar across the page (logo,
 * organization, company switcher, search), then a green sidebar of sections that open and close
 * (the one holding the current page opens by itself; a closed one adds up its counts) with the
 * person at the bottom opening their menu, and the page. On phones the sidebar slides in from the
 * Menu button over a scrim.
 */
export function AppShell({ sections, current, organization, topBar, person, personMenu, onNavigate, children }: AppShellProps) {
  const { t } = useTranslation()
  const [menuOpen, setMenuOpen] = useState(false)
  // Sections the person opened or closed themselves; others follow the current page.
  const [chosen, setChosen] = useState<Record<string, boolean>>({})
  const navId = useId()
  const holds = (s: ShellSection) => s.links.some(l => l.key === current)
  const isOpen = (s: ShellSection) => !s.label || (chosen[s.key] ?? holds(s))

  const follow = (href: string) => (e: MouseEvent) => {
    setMenuOpen(false)
    if (!onNavigate || e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return
    e.preventDefault()
    onNavigate(href)
  }

  const link = (item: NavItem, child: boolean) => {
    const here = item.key === current
    return (
      <a key={item.key} href={item.href} aria-current={here ? 'page' : undefined} onClick={follow(item.href)}
        className={`flex min-h-10 items-center gap-2.5 rounded-control px-3 text-label no-underline ${child ? 'ml-5 border-l border-sidebar-raise' : ''} ${here ? 'bg-sidebar-current font-semibold text-sidebar-current-ink' : 'font-medium text-sidebar-ink hover:bg-sidebar-raise'}`}>
        <span aria-hidden="true" className={`size-1.5 flex-none rounded-full ${here ? 'bg-brand' : 'bg-sidebar-muted'}`} />
        <span className="min-w-0 flex-1">{item.label}</span>
        {item.badge !== undefined && <span className="ml-auto rounded-full bg-warning-soft px-2 text-tag font-semibold text-warning-ink">{item.badge}</span>}
      </a>
    )
  }

  return (
    <div className="flex min-h-dvh flex-col bg-page">
      <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b border-line bg-card px-3 md:gap-4 md:px-6">
        <button type="button" aria-expanded={menuOpen} aria-controls={navId} onClick={() => setMenuOpen(o => !o)}
          className="min-h-11 rounded-control px-3 text-label font-medium text-ink hover:bg-raised md:hidden">{t('common.menu')}</button>
        <a href="/" onClick={follow('/')} className="flex-none rounded-control no-underline"><Logo /></a>
        <span className="hidden whitespace-nowrap border-l border-line pl-4 text-label font-semibold text-ink md:inline">{organization}</span>
        <div className="flex min-w-0 flex-1 items-center justify-end gap-3 md:justify-start">{topBar}</div>
      </header>
      <div className="flex flex-1">
        {menuOpen && <button type="button" aria-label={t('nav.closeMenu')} onClick={() => setMenuOpen(false)} className="fixed inset-0 top-16 z-20 bg-scrim md:hidden" />}
        <nav id={navId} aria-label={t('nav.main')}
          className={`${menuOpen ? 'flex' : 'hidden'} fixed bottom-0 left-0 top-16 z-20 w-72 flex-col bg-sidebar shadow-drawer md:sticky md:flex md:h-[calc(100dvh-4rem)] md:w-64 md:flex-none md:shadow-none`}>
          <div className="flex flex-1 flex-col gap-0.5 overflow-y-auto px-3 py-4">
            {sections.map(section => {
              if (!section.label) return <div key={section.key} className="flex flex-col gap-0.5">{section.links.map(l => link(l, false))}</div>
              const open = isOpen(section)
              const count = section.links.reduce((n, l) => n + (Number(l.badge) || 0), 0)
              const listId = `${navId}-${section.key}`
              return (
                <div key={section.key} className="flex flex-col gap-0.5">
                  <button type="button" aria-expanded={open} aria-controls={listId} onClick={() => setChosen(c => ({ ...c, [section.key]: !open }))}
                    className={`mt-1 flex min-h-10 items-center gap-2.5 rounded-control px-3 text-left text-label hover:bg-sidebar-raise ${holds(section) ? 'font-semibold text-sidebar-heading' : 'font-medium text-sidebar-ink'}`}>
                    <span className="min-w-0 flex-1">{section.label}</span>
                    {!open && count > 0 && <span className="rounded-full bg-warning-soft px-2 text-tag font-semibold text-warning-ink">{count}</span>}
                    <span className="text-sidebar-muted">{chevron(open)}</span>
                  </button>
                  {open && <div id={listId} className="flex flex-col gap-0.5">{section.links.map(l => link(l, true))}</div>}
                </div>
              )
            })}
          </div>
          <PersonButton person={person} menu={personMenu} />
        </nav>
        <main className="min-w-0 flex-1 px-4 py-6 md:px-8">{children}</main>
      </div>
    </div>
  )
}

/** The person at the bottom of the sidebar; opens their menu above it. Escape or a click elsewhere closes it. */
function PersonButton({ person, menu }: { person: { name: string; detail?: string }; menu?: (close: () => void) => ReactNode }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const buttonId = useId()
  const box = useRef<HTMLDivElement>(null)
  const close = () => { setOpen(false); document.getElementById(buttonId)?.focus() }

  useEffect(() => {
    if (!open) return
    const outside = (e: PointerEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('pointerdown', outside)
    return () => document.removeEventListener('pointerdown', outside)
  }, [open])

  const initials = person.name.split(' ').filter(Boolean).map(p => p[0]).slice(0, 2).join('')
  return (
    <div ref={box} className="relative border-t border-sidebar-raise p-3" onKeyDown={e => { if (e.key === 'Escape' && open) { e.stopPropagation(); close() } }}>
      {open && menu && (
        <div role="dialog" aria-label={t('nav.account')} className="absolute bottom-full left-3 z-40 mb-1.5 flex w-80 max-w-[calc(100vw-1.5rem)] flex-col gap-1 rounded-card border border-line bg-card p-2 text-ink shadow-float">
          {menu(close)}
        </div>
      )}
      <button id={buttonId} type="button" aria-expanded={open} aria-haspopup="dialog" onClick={() => setOpen(o => !o)}
        className="flex w-full items-center gap-2.5 rounded-card p-2 text-left text-sidebar-ink hover:bg-sidebar-raise">
        <span aria-hidden="true" className="flex size-9 flex-none items-center justify-center rounded-full bg-sidebar-accent text-hint font-semibold text-sidebar">{initials}</span>
        <span className="flex min-w-0 flex-1 flex-col">
          <span className="truncate text-label font-semibold text-sidebar-heading">{person.name}</span>
          {person.detail && <span className="truncate text-tag text-sidebar-muted">{person.detail}</span>}
        </span>
        <span className="text-sidebar-muted">{chevron(true)}</span>
      </button>
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
