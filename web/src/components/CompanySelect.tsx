import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { matches } from './search'

export interface Company { id: string; name: string; code: string }

/** all = every company ticked, which also covers companies added later; otherwise the chosen ids. */
export interface CompanySelection { all: boolean; ids: string[] }

interface CompanySelectProps {
  label: string
  companies: Company[]
  value: CompanySelection
  onChange: (value: CompanySelection) => void
  /** Several companies (checkboxes) or one (round options). */
  multiple?: boolean
  placeholder?: string
  /** Compact (36 px) with its label for screen readers only: in a sheet's title bar. */
  compact?: boolean
}

/**
 * The company selector: on every list and setup screen where companies can differ.
 * A button opens a list with a search box on top: typing filters, arrows move, Enter picks, Escape closes.
 */
export function CompanySelect({ label, companies, value, onChange, multiple = true, placeholder, compact = false }: CompanySelectProps) {
  const { t } = useTranslation()
  const id = useId()
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const root = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const search = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState('')

  const options = companies.filter(c => matches(`${c.name} ${c.code}`, query)).map(c => c.id)
  const isOn = (cid: string) => (multiple && value.all) || value.ids.includes(cid)
  const picked = companies.filter(c => isOn(c.id))

  let summary = placeholder ?? t('company.placeholder')
  if (multiple && companies.length > 1 && picked.length === companies.length) summary = t('company.all')
  else if (picked.length === 1) summary = `${picked[0].name} · ${picked[0].code}`
  else if (picked.length > 1) summary = t('company.selected', { count: picked.length })

  useEffect(() => {
    if (!open) return
    search.current?.focus()
    const onDown = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  const close = () => { setOpen(false); setQuery(''); button.current?.focus() }

  const pick = (option: string) => {
    if (!multiple) {
      onChange({ all: false, ids: [option] })
      close()
      return
    }
    // Every company ticked means all of them, including companies added later.
    const ids = companies.map(c => c.id).filter(cid => (cid === option ? !isOn(cid) : isOn(cid)))
    onChange(ids.length === companies.length ? { all: true, ids } : { all: false, ids })
  }

  const onListKey = (e: KeyboardEvent) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(i => Math.min(i + 1, options.length - 1)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(i => Math.max(i - 1, 0)) }
    else if (e.key === 'Home') { e.preventDefault(); setActive(0) }
    else if (e.key === 'End') { e.preventDefault(); setActive(options.length - 1) }
    else if (e.key === 'Enter') { e.preventDefault(); if (options[active]) pick(options[active]) }
    // Escape closes only the list, not a drawer or modal around it.
    else if (e.key === 'Escape' || e.key === 'Tab') { e.preventDefault(); e.stopPropagation(); close() }
  }

  const box = (on: boolean) => (
    <span aria-hidden="true" className={`flex size-[18px] flex-none items-center justify-center border ${multiple ? 'rounded' : 'rounded-full'} ${on ? 'border-brand bg-brand text-on-brand' : 'border-field-border bg-card'}`}>
      {on && <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><path d="M5 12l5 5 9-10" /></svg>}
    </span>
  )

  return (
    <div ref={root} className="relative flex w-full flex-col gap-1.5">
      <span id={`${id}-label`} className={compact ? 'sr-only' : 'text-label font-medium text-ink'}>{label}</span>
      <button ref={button} id={`${id}-button`} type="button" aria-haspopup="listbox" aria-expanded={open}
        aria-labelledby={`${id}-label ${id}-button`} onClick={() => { setActive(0); setQuery(''); setOpen(o => !o) }}
        className={`flex w-full items-center justify-between gap-2 rounded-control border border-field-border bg-card px-3 text-left text-ink ${compact ? 'min-h-9 text-label' : 'min-h-11 text-body'}`}>
        <span className={`truncate ${picked.length || value.all ? 'text-ink' : 'text-muted'}`}>{summary}</span>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" className={`flex-none text-muted ${open ? 'rotate-180' : ''}`}><path d="M6 9l6 6 6-6" /></svg>
      </button>
      {open && (
        <div className="absolute inset-x-0 top-full z-30 mt-1 min-w-[260px] rounded-card border border-line bg-card p-1.5 shadow-float">
          <input ref={search} type="text" role="combobox" autoComplete="off" aria-label={t('company.search')}
            aria-expanded="true" aria-controls={`${id}-list`} aria-autocomplete="list"
            aria-activedescendant={options[active] ? `${id}-opt-${active}` : undefined}
            placeholder={t('company.searchPlaceholder')} value={query} onKeyDown={onListKey}
            onChange={e => { setQuery(e.target.value); setActive(0) }}
            className="mb-1.5 min-h-10 w-full rounded-control border border-field-border bg-card px-3 text-body text-ink placeholder:text-muted" />
          <ul id={`${id}-list`} role="listbox" aria-labelledby={`${id}-label`} aria-multiselectable={multiple}
            className="m-0 flex max-h-64 list-none flex-col gap-0.5 overflow-y-auto p-0">
            {options.map((option, i) => {
              const on = isOn(option)
              const company = companies.find(c => c.id === option)
              return (
                <li key={option} id={`${id}-opt-${i}`} role="option" aria-selected={on} onClick={() => pick(option)} onMouseEnter={() => setActive(i)}
                  // Keep focus in the search box, so keys (Escape) still reach the list.
                  onMouseDown={e => e.preventDefault()}
                  className={`flex min-h-11 cursor-pointer items-center gap-2.5 rounded-control px-3 text-body text-ink ${i === active ? 'bg-raised' : ''}`}>
                  {box(on)}
                  <span className="min-w-0 flex-1">{company?.name}</span><span className="text-tag text-muted">{company?.code}</span>
                </li>
              )
            })}
          </ul>
          {options.length === 0 && <p className="m-0 px-3 py-2.5 text-label text-muted">{t('common.noMatches')}</p>}
          {multiple && (
            <div className="mt-1 flex justify-end border-t border-line-soft px-1 pt-1.5">
              <button type="button" onClick={close} className="min-h-9 rounded-control bg-brand px-3.5 text-label font-semibold text-on-brand hover:bg-brand-hover">{t('common.done')}</button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
