import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Checkbox } from './fields'

const iconButton = (on: boolean) =>
  `inline-flex size-7 shrink-0 items-center justify-center rounded-control ${on ? 'bg-brand-soft text-brand' : 'text-muted hover:bg-card hover:text-ink'}`

/** A column header's sort: A to Z, Z to A, then off; the header carries aria-sort. */
export function SortButton({ label, direction, onClick }: { label: string; direction: 'ascending' | 'descending' | null; onClick: () => void }) {
  const { t } = useTranslation()
  return (
    <button type="button" aria-label={t('bulk.sort', { column: label })} onClick={onClick}
      title={direction ? t(`bulk.sorted.${direction}`) : undefined} className={iconButton(!!direction)}>
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        {!direction && <path d="M8 4v16M4 8l4-4 4 4M16 20V4M12 16l4 4 4-4" />}
        {direction === 'ascending' && <path d="M12 20V4M6 10l6-6 6 6" />}
        {direction === 'descending' && <path d="M12 4v16M6 14l6 6 6-6" />}
      </svg>
    </button>
  )
}

/**
 * A column header's filter: a button that opens a panel floating over the table (fixed, so the
 * card or a scrolling sheet can't clip it). Its first field takes focus; Escape and a click
 * outside close it, and Escape doesn't close the drawer around it. What's in it is the column's.
 */
export function FilterButton({ label, active, children }: { label: string; active: boolean; children: ReactNode }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const [at, setAt] = useState({ top: 0, left: 0 })
  const button = useRef<HTMLButtonElement>(null)
  const panel = useRef<HTMLDivElement>(null)
  const name = t('bulk.filter', { column: label })

  useEffect(() => {
    if (!open) return
    panel.current?.querySelector<HTMLInputElement>('input')?.focus()
    const onDown = (e: MouseEvent) => {
      const target = e.target as Node
      // A dropdown's list (fixed, outside the panel) counts as inside.
      if (!panel.current?.contains(target) && !button.current?.contains(target) && !(target as HTMLElement).closest?.('[role="listbox"]')) setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  const toggle = () => {
    const box = button.current?.getBoundingClientRect()
    if (box) setAt({ top: box.bottom + 4, left: Math.max(8, Math.min(box.left, window.innerWidth - 296)) })
    setOpen(o => !o)
  }
  const close = () => { setOpen(false); button.current?.focus() }

  return (
    <>
      <button ref={button} type="button" aria-label={name} aria-pressed={active} aria-expanded={open} onClick={toggle} className={iconButton(active)}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M3 5h18l-7 8v6l-4 2v-8L3 5z" /></svg>
      </button>
      {open && (
        <div ref={panel} role="dialog" aria-label={name} style={{ top: at.top, left: at.left }}
          onKeyDown={e => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close() } }}
          className="fixed z-[60] flex w-72 flex-col gap-2 rounded-card border border-line bg-card p-3 text-left font-normal whitespace-normal shadow-float">
          {children}
        </div>
      )}
    </>
  )
}

/** Values to tick in a filter (with how many rows have each, when known), and All for those listed. */
export function Checklist({ values, isTicked, onTick, max = 200 }: {
  values: { value: string; label: string; count?: number }[]
  isTicked: (value: string) => boolean
  onTick: (values: string[], on: boolean) => void
  max?: number
}) {
  const { t } = useTranslation()
  return (
    <div className="flex max-h-64 flex-col gap-0.5 overflow-y-auto">
      {values.length > 0 && <Checkbox label={t('bulk.all')} checked={values.every(v => isTicked(v.value))} onChange={e => onTick(values.map(v => v.value), e.target.checked)} />}
      {values.slice(0, max).map(v => (
        <label key={v.value} className="flex min-h-9 cursor-pointer items-center gap-2.5 rounded-control px-1 text-label text-ink hover:bg-raised">
          <input type="checkbox" checked={isTicked(v.value)} onChange={e => onTick([v.value], e.target.checked)} className="size-4 accent-brand" />
          <span className="flex-1 truncate">{v.label}</span>
          {v.count !== undefined && <span className="text-hint text-muted tabular-nums">{v.count}</span>}
        </label>
      ))}
      {values.length > max && <p className="m-0 px-1 text-hint text-muted">{t('bulk.tooManyValues', { count: max })}</p>}
      {values.length === 0 && <p className="m-0 px-1 text-hint text-muted">{t('common.noMatches')}</p>}
    </div>
  )
}
