import { useId, useState } from 'react'

export interface SearchResult { key: string; label: string; group?: string; href: string }

interface SearchBoxProps {
  label: string
  placeholder: string
  /** What shows when nothing matches. */
  empty: string
  /** The results for what was typed (only what the person may open). */
  search: (query: string) => SearchResult[]
  onPick: (href: string) => void
}

/**
 * The top bar's search (OP-01): type a page or a word people use ("payroll", "leave"), and the
 * matches the person may open show below, grouped; arrows move, Enter or a click opens one,
 * Escape closes. Results come from app/navigation.ts (people of the chosen company join with OP-07).
 */
export function SearchBox({ label, placeholder, empty, search, onPick }: SearchBoxProps) {
  const id = useId()
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [open, setOpen] = useState(false)
  const results = query.trim() ? search(query) : []
  const shown = open && query.trim() !== ''

  const pick = (r: SearchResult) => {
    setQuery('')
    setOpen(false)
    onPick(r.href)
  }

  return (
    <div className="relative w-full max-w-md">
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"
        className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted"><circle cx="11" cy="11" r="7" /><path d="M20 20l-3.5-3.5" /></svg>
      <input id={id} type="search" role="combobox" aria-label={label} placeholder={placeholder} autoComplete="off"
        aria-expanded={shown} aria-controls={`${id}-list`} aria-activedescendant={shown && results[active] ? `${id}-${results[active].key}` : undefined}
        value={query}
        onChange={e => { setQuery(e.target.value); setActive(0); setOpen(true) }}
        onBlur={() => setOpen(false)}
        onKeyDown={e => {
          if (e.key === 'ArrowDown') { e.preventDefault(); setOpen(true); setActive(a => Math.min(a + 1, results.length - 1)) }
          else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => Math.max(a - 1, 0)) }
          else if (e.key === 'Enter' && shown && results[active]) { e.preventDefault(); pick(results[active]) }
          else if (e.key === 'Escape') { e.preventDefault(); setOpen(false) }
        }}
        className="min-h-10 w-full rounded-control border border-field-border bg-card pl-9 pr-3 text-label text-ink placeholder:text-muted" />
      {shown && results.length === 0 && (
        <p id={`${id}-list`} role="status" className="absolute left-0 right-0 top-full z-40 m-0 mt-1 rounded-card border border-line bg-card px-4 py-3 text-label text-muted shadow-float">{empty}</p>
      )}
      {shown && results.length > 0 && (
        <div id={`${id}-list`} role="listbox" aria-label={label}
          className="absolute left-0 right-0 top-full z-40 mt-1 flex max-h-96 flex-col gap-0.5 overflow-y-auto rounded-card border border-line bg-card p-1.5 shadow-float">
          {results.map((r, i) => (
              <div key={r.key} role="presentation">
                {r.group && r.group !== results[i - 1]?.group && <div role="presentation" className="px-2.5 pb-0.5 pt-2 text-tag uppercase tracking-wider text-muted">{r.group}</div>}
                <div id={`${id}-${r.key}`} role="option" aria-selected={i === active}
                  onMouseDown={e => { e.preventDefault(); pick(r) }}
                  className={`flex min-h-10 cursor-pointer items-center rounded-control px-2.5 text-label text-ink ${i === active ? 'bg-raised' : 'hover:bg-raised'}`}>{r.label}</div>
              </div>
            ))}
        </div>
      )}
    </div>
  )
}
