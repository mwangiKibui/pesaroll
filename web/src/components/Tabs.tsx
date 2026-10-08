import { useRef, type KeyboardEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export interface Tab {
  key: string
  label: string
  /** Something on this tab needs fixing: a dot, and "needs fixing" for screen readers. */
  invalid?: boolean
}

interface TabsProps {
  /** Ties the tabs to their TabPanels (same id on both). */
  id: string
  /** What the tabs are, for screen readers: "Company sections". */
  label: string
  tabs: Tab[]
  current: string
  onChange: (key: string) => void
}

/**
 * Sections of one record, one at a time (a company's details, numbers, contact, accounts).
 * Arrows move and pick, Home and End jump; only the chosen tab is in the Tab order.
 * Panels (TabPanel) stay in the page when hidden, so a form keeps what was typed.
 */
export function Tabs({ id, label, tabs, current, onChange }: TabsProps) {
  const { t } = useTranslation()
  const list = useRef<HTMLDivElement>(null)

  const go = (index: number) => {
    const tab = tabs[(index + tabs.length) % tabs.length]
    onChange(tab.key)
    list.current?.querySelector<HTMLElement>(`#${CSS.escape(`${id}-tab-${tab.key}`)}`)?.focus()
  }

  const onKeyDown = (e: KeyboardEvent) => {
    const at = tabs.findIndex(tab => tab.key === current)
    if (e.key === 'ArrowRight') { e.preventDefault(); go(at + 1) }
    else if (e.key === 'ArrowLeft') { e.preventDefault(); go(at - 1) }
    else if (e.key === 'Home') { e.preventDefault(); go(0) }
    else if (e.key === 'End') { e.preventDefault(); go(tabs.length - 1) }
  }

  return (
    <div ref={list} role="tablist" aria-label={label} onKeyDown={onKeyDown}
      className="flex gap-1 overflow-x-auto border-b border-line">
      {tabs.map(tab => {
        const selected = tab.key === current
        return (
          <button key={tab.key} id={`${id}-tab-${tab.key}`} type="button" role="tab" aria-selected={selected}
            aria-controls={`${id}-panel-${tab.key}`} tabIndex={selected ? 0 : -1} onClick={() => onChange(tab.key)}
            className={`-mb-px flex min-h-11 flex-none items-center gap-1.5 border-b-2 px-3 text-label whitespace-nowrap ${selected ? 'border-brand font-semibold text-ink' : 'border-transparent text-muted hover:text-ink'}`}>
            {tab.label}
            {tab.invalid && <>
              <span aria-hidden="true" className="size-2 rounded-full bg-danger" />
              <span className="sr-only">{t('common.needsFixing')}</span>
            </>}
          </button>
        )
      })}
    </div>
  )
}

/** One tab's content. Hidden (not removed) when another tab is chosen. */
export function TabPanel({ id, tab, current, children }: { id: string; tab: string; current: string; children: ReactNode }) {
  return (
    <div id={`${id}-panel-${tab}`} role="tabpanel" aria-labelledby={`${id}-tab-${tab}`} hidden={tab !== current} tabIndex={0}
      className="flex flex-col gap-4 outline-none">
      {children}
    </div>
  )
}
