import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { PlusIcon } from './icons'
import { matches } from './search'

interface MultiSelectFieldProps {
  label: string
  hint?: string
  error?: string
  placeholder: string
  options: { value: string; label: string }[]
  value: string[]
  onChange: (value: string[]) => void
  /** A first choice above the list, such as "Add your own", that opens a form instead of ticking. */
  addLabel?: string
  onAdd?: () => void
}

/**
 * Several choices from a list, styled and searchable like every dropdown (CompanySelect is the
 * company version). A button opens the list with a search box on top: typing filters, arrows move,
 * Enter ticks or unticks, Escape closes only the list.
 */
export function MultiSelectField({ label, hint, error, placeholder, options, value, onChange, addLabel, onAdd }: MultiSelectFieldProps) {
  const { t } = useTranslation()
  const id = useId()
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [query, setQuery] = useState('')
  const root = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const search = useRef<HTMLInputElement>(null)

  const shown = options.filter(o => matches(o.label, query))
  const picked = options.filter(o => value.includes(o.value))
  const summary = picked.length === 0 ? placeholder : picked.length === 1 ? picked[0].label : t('company.selected', { count: picked.length })
  const describedBy = [hint ? `${id}-hint` : null, error ? `${id}-error` : null].filter(Boolean).join(' ') || undefined

  useEffect(() => {
    if (!open) return
    search.current?.focus()
    const onDown = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  const close = () => { setOpen(false); setQuery(''); button.current?.focus() }
  const toggle = (option: string) => onChange(value.includes(option) ? value.filter(v => v !== option) : [...value, option])

  const onListKey = (e: KeyboardEvent) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(i => Math.min(i + 1, shown.length - 1)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(i => Math.max(i - 1, 0)) }
    else if (e.key === 'Enter') { e.preventDefault(); if (shown[active]) toggle(shown[active].value) }
    // Escape closes only the list, not a drawer or modal around it.
    else if (e.key === 'Escape' || e.key === 'Tab') { e.preventDefault(); e.stopPropagation(); close() }
  }

  return (
    <div ref={root} className="relative flex w-full flex-col gap-1.5">
      <span id={`${id}-label`} className="text-label font-medium text-ink">{label}</span>
      {hint && <p id={`${id}-hint`} className="m-0 text-hint text-muted">{hint}</p>}
      <button ref={button} id={`${id}-button`} type="button" aria-haspopup="listbox" aria-expanded={open}
        aria-labelledby={`${id}-label ${id}-button`} aria-describedby={describedBy} aria-invalid={error ? true : undefined}
        onClick={() => { setActive(0); setQuery(''); setOpen(o => !o) }}
        className={`flex min-h-11 w-full cursor-pointer items-center justify-between gap-2 rounded-control border bg-card px-3 text-left text-body ${error ? 'border-danger' : 'border-field-border'}`}>
        <span className={`truncate ${picked.length ? 'text-ink' : 'text-muted'}`}>{summary}</span>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" className={`flex-none text-muted ${open ? 'rotate-180' : ''}`}><path d="M6 9l6 6 6-6" /></svg>
      </button>
      {error && <p id={`${id}-error`} className="m-0 text-hint font-medium text-danger-ink">{error}</p>}
      {open && (
        <div className="absolute inset-x-0 top-full z-30 mt-1 min-w-[260px] rounded-card border border-line bg-card p-1.5 shadow-float">
          {addLabel && onAdd && (
            <button type="button" onClick={() => { setOpen(false); setQuery(''); onAdd() }}
              className="mb-1 flex min-h-11 w-full cursor-pointer items-center gap-2.5 rounded-control px-3 text-left text-body font-medium text-brand hover:bg-raised">
              <PlusIcon />{addLabel}
            </button>
          )}
          <input ref={search} type="text" role="combobox" autoComplete="off" aria-label={t('common.searchIn', { what: label.toLowerCase() })}
            aria-expanded="true" aria-controls={`${id}-list`} aria-autocomplete="list"
            aria-activedescendant={shown[active] ? `${id}-opt-${active}` : undefined}
            placeholder={t('common.searchPlaceholder')} value={query} onKeyDown={onListKey}
            onChange={e => { setQuery(e.target.value); setActive(0) }}
            className="mb-1.5 min-h-10 w-full rounded-control border border-field-border bg-card px-3 text-body text-ink placeholder:text-muted" />
          <ul id={`${id}-list`} role="listbox" aria-labelledby={`${id}-label`} aria-multiselectable="true"
            className="m-0 flex max-h-64 list-none flex-col gap-0.5 overflow-y-auto p-0">
            {shown.map((option, i) => {
              const on = value.includes(option.value)
              return (
                <li key={option.value} id={`${id}-opt-${i}`} role="option" aria-selected={on} onClick={() => toggle(option.value)}
                  onMouseEnter={() => setActive(i)}
                  // Keep focus in the search box, so keys (Escape) still reach the list.
                  onMouseDown={e => e.preventDefault()}
                  className={`flex min-h-11 cursor-pointer items-center gap-2.5 rounded-control px-3 text-body text-ink ${i === active ? 'bg-raised' : ''}`}>
                  <span aria-hidden="true" className={`flex size-[18px] flex-none items-center justify-center rounded border ${on ? 'border-brand bg-brand text-on-brand' : 'border-field-border bg-card'}`}>
                    {on && <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><path d="M5 12l5 5 9-10" /></svg>}
                  </span>
                  <span className="min-w-0 flex-1">{option.label}</span>
                </li>
              )
            })}
          </ul>
          {shown.length === 0 && <p className="m-0 px-3 py-2.5 text-label text-muted">{t('common.noMatches')}</p>}
          <div className="mt-1 flex justify-end border-t border-line-soft px-1 pt-1.5">
            <button type="button" onClick={close} className="min-h-9 cursor-pointer rounded-control bg-brand px-3.5 text-label font-semibold text-on-brand hover:bg-brand-hover">{t('common.done')}</button>
          </div>
        </div>
      )}
    </div>
  )
}
