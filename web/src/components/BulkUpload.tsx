import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, IconButton } from './Button'
import { buttonClasses } from './buttonStyles'
import { Drawer } from './Drawer'
import { FormErrorBanner, Notice, Tag, type ApiError } from './feedback'
import { Checkbox, SelectField, TextField } from './fields'
import { DownloadIcon, PlusIcon } from './icons'
import { matches } from './search'

/**
 * A column: a text cell, a dropdown (options), a date or a checkbox ("Yes" or "No"). A column can
 * start with a value and be closed while another cell says so (Date left while "Still works here"
 * is ticked); a closed cell is emptied. Its choices can depend on the row (a bank's branches), and
 * validate checks a cell as soon as it is left (a list or checkbox when it changes), and every
 * cell again before importing; it returns the message to show, or null.
 */
export interface BulkUploadColumn {
  key: string
  label: string
  options?: { value: string; label: string }[]
  optionsFor?: (cells: Record<string, string>) => { value: string; label: string }[]
  type?: 'text' | 'date' | 'checkbox'
  defaultValue?: string
  disabledWhen?: (cells: Record<string, string>) => boolean
  validate?: (value: string, cells: Record<string, string>) => string | null
}

/** A row as written in the file: its row number there and its cells by column key. */
export interface BulkUploadRow { line: number | string; cells: Record<string, string> }

/** A problem the server found: its row (0 for the whole file), the column key and what to fix. */
export interface BulkUploadProblem { line: number | string; column?: string | null; message: string }

/** notes: what rows name that doesn't exist yet and will be created on import (not a problem). */
export interface BulkUploadRead { rows: BulkUploadRow[]; problems: BulkUploadProblem[]; notes?: BulkUploadProblem[] }

export interface BulkUploadCheck { problems: BulkUploadProblem[]; notes?: BulkUploadProblem[] }

interface BulkUploadProps {
  open: boolean
  /** "Upload locations". */
  title: string
  /** The template's columns, in its order. */
  columns: BulkUploadColumn[]
  onClose: () => void
  /** Downloads the step's template (an Excel file with the columns and dropdowns). */
  onDownloadTemplate: () => Promise<void>
  /** Reads the file: every row as written, with the problems found. Nothing is saved. */
  onRead: (file: File) => Promise<BulkUploadRead | ApiError>
  /** Checks the reviewed rows again. */
  onCheck: (rows: BulkUploadRow[]) => Promise<BulkUploadCheck | ApiError>
  /** Saves the rows, all or nothing; null when it worked. The page shows the toast. */
  onImport: (rows: BulkUploadRow[]) => Promise<ApiError | null>
  labels: {
    /** "Review 12 locations": the review's title. */
    review: (count: number) => string
    /** "Import 12 locations". */
    importButton: (count: number) => string
    /** What goes in the template, under step 1. */
    templateHint: string
  }
  /** Where the rows apply (the company selector), shown above the review table; left out when there is nothing to choose. */
  children?: ReactNode
  /** A step before the template (what to upload, when one button uploads several kinds). */
  firstStep?: { title: string; content: ReactNode }
  /** Why the template and the upload wait (a company to choose first); while set, both are closed. */
  locked?: string
  /** Adding several by hand: opens straight into the review with this many empty rows, and Add row; empty rows are left out. */
  startRows?: number
  /** Changes that follow from a change (a schedule brings its leave plan; a new bank clears the branch). */
  onCellsChange?: (cells: Record<string, string>, changed: string) => Record<string, string>
}

/** A column's filter: rows whose cell contains `text`, without the values in `hidden` ("" for empty cells). */
interface ColumnFilterState { text: string; hidden: Set<string> }
const isActive = (f: ColumnFilterState | undefined) => !!f && (!!f.text.trim() || f.hidden.size > 0)

/** Values a filter lists at most; a search narrows the rest. */
const MAX_VALUES = 200

/**
 * A column header's filter, as in a spreadsheet: a search over the column (rows whose cell
 * contains it), and its values with how many rows have each, to untick. It floats over the
 * sheet (fixed, so the scrolling table doesn't clip it); Escape and a click outside close it.
 */
function ColumnFilter({ label, values, display, filter, onChange }: {
  label: string
  values: { value: string; count: number }[]
  display: (value: string) => string
  filter: ColumnFilterState
  onChange: (filter: ColumnFilterState) => void
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const [at, setAt] = useState({ top: 0, left: 0 })
  const button = useRef<HTMLButtonElement>(null)
  const panel = useRef<HTMLDivElement>(null)
  const name = t('bulk.filter', { column: label })

  useEffect(() => {
    if (!open) return
    panel.current?.querySelector<HTMLInputElement>('input[type="search"]')?.focus()
    const onDown = (e: MouseEvent) => {
      if (!panel.current?.contains(e.target as Node) && !button.current?.contains(e.target as Node)) setOpen(false)
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
  const listed = values.filter(v => (filter.text.trim() ? v.value && matches(display(v.value), filter.text) : true))
  const allTicked = listed.every(v => !filter.hidden.has(v.value))
  const tick = (value: string, on: boolean) => {
    const hidden = new Set(filter.hidden)
    if (on) hidden.delete(value)
    else hidden.add(value)
    onChange({ ...filter, hidden })
  }
  const tickAll = (on: boolean) => {
    const hidden = new Set(filter.hidden)
    for (const v of listed) {
      if (on) hidden.delete(v.value)
      else hidden.add(v.value)
    }
    onChange({ ...filter, hidden })
  }
  const active = isActive(filter)

  return (
    <>
      <button ref={button} type="button" aria-label={name} aria-pressed={active} aria-expanded={open} onClick={toggle}
        className={`inline-flex size-7 shrink-0 items-center justify-center rounded-control ${active ? 'bg-brand-soft text-brand' : 'text-muted hover:bg-card hover:text-ink'}`}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M3 5h18l-7 8v6l-4 2v-8L3 5z" /></svg>
      </button>
      {open && (
        <div ref={panel} role="dialog" aria-label={name} style={{ top: at.top, left: at.left }}
          onKeyDown={e => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close() } }}
          className="fixed z-[60] flex w-72 flex-col gap-2 rounded-card border border-line bg-card p-3 text-left font-normal shadow-float">
          <TextField type="search" size="sm" label={t('bulk.filterSearch', { column: label })} hideLabel placeholder={t('bulk.searchPlaceholder')}
            value={filter.text} onChange={e => onChange({ ...filter, text: e.target.value })} />
          <div className="flex max-h-64 flex-col gap-0.5 overflow-y-auto">
            {listed.length > 0 && <Checkbox label={t('bulk.all')} checked={allTicked} onChange={e => tickAll(e.target.checked)} />}
            {listed.slice(0, MAX_VALUES).map(v => (
              <label key={v.value} className="flex min-h-9 cursor-pointer items-center gap-2.5 rounded-control px-1 text-label text-ink hover:bg-raised">
                <input type="checkbox" checked={!filter.hidden.has(v.value)} onChange={e => tick(v.value, e.target.checked)} className="size-4 accent-brand" />
                <span className="flex-1 truncate">{v.value ? display(v.value) : t('bulk.empty')}</span>
                <span className="text-hint text-muted tabular-nums">{v.count}</span>
              </label>
            ))}
            {listed.length > MAX_VALUES && <p className="m-0 px-1 text-hint text-muted">{t('bulk.tooManyValues', { count: MAX_VALUES })}</p>}
          </div>
          {active && (
            <div className="flex justify-end">
              <Button variant="secondary" onClick={() => onChange({ text: '', hidden: new Set() })}>{t('bulk.clearFilter')}</Button>
            </div>
          )}
        </div>
      )}
    </>
  )
}

const isError = (value: unknown): value is ApiError => typeof value === 'object' && value !== null && 'code' in value && 'reference' in value
const keyOf = (line: number | string, column?: string | null) => `${line}:${column ?? ''}`

/** Rows shown at a time in the review, so files of thousands of rows stay quick to show and edit. */
const PAGE = 100

/** How far an arrow key scrolls the rows sideways: about one column. */
const STEP = 320

/** Where the arrow keys belong to what's focused: typing in a cell, a dropdown, a checkbox. */
const ownsArrows = (target: EventTarget | null) =>
  target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) || !!target.closest('[role="listbox"]'))

/**
 * Uploading many records at once, the same everywhere (locations now; departments, positions and
 * employees later). A drawer: 1 download the template, 2 upload the filled file. Then a large
 * review: every column and row of the file in an editable table (lists as searchable dropdowns),
 * searchable, scrolling under a fixed header, with problems in their cells. Import checks the
 * edited rows again and saves them all, or shows what is still wrong; nothing is imported blindly.
 */
export function BulkUpload({ open, title, columns, onClose, onDownloadTemplate, onRead, onCheck, onImport, labels, children, firstStep, startRows, onCellsChange, locked }: BulkUploadProps) {
  const { t } = useTranslation()
  const inputId = useId()
  const [file, setFile] = useState<File | null>(null)
  const defaults = Object.fromEntries(columns.filter(c => c.defaultValue !== undefined).map(c => [c.key, c.defaultValue!]))
  const blank = (from: number, count: number): BulkUploadRow[] => Array.from({ length: count }, (_, i) => ({ line: from + i, cells: { ...defaults } }))
  const [rows, setRows] = useState<BulkUploadRow[] | null>(() => (startRows ? blank(1, startRows) : null))
  const [problems, setProblems] = useState<BulkUploadProblem[]>([])
  const [notes, setNotes] = useState<BulkUploadProblem[]>([])
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const [reviewBanner, setReviewBanner] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState<'template' | 'read' | 'import' | null>(null)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)
  // Show: all rows, the ones to fix, or the ready ones, as they were when chosen: a row being fixed stays in view until the choice changes.
  const [view, setView] = useState<{ kind: 'all' | 'fix' | 'ready'; lines: Set<string> | null }>({ kind: 'all', lines: null })
  const rowsRef = useRef<HTMLDivElement>(null)
  // Each column's search and unticked values, applied with the search above and Show.
  const [filters, setFilters] = useState<Record<string, ColumnFilterState>>({})
  const setFilter = (key: string, filter: ColumnFilterState) => { setFilters(all => ({ ...all, [key]: filter })); setPage(0) }
  // One column's sort, as the rows were when it was clicked: rows don't move while a cell is being fixed. Click again to re-sort.
  const [sort, setSort] = useState<{ key: string; direction: 'ascending' | 'descending'; order: Map<string, number> } | null>(null)

  // While the rows are open, ← and → scroll them sideways (wide sheets have 30 columns), unless a cell is being typed in.
  const showingRows = open && !!rows
  useEffect(() => {
    if (!showingRows) return
    const onKey = (e: KeyboardEvent) => {
      if ((e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') || e.altKey || e.ctrlKey || e.metaKey || ownsArrows(e.target)) return
      const region = rowsRef.current
      if (!region) return
      e.preventDefault()
      const left = e.key === 'ArrowRight' ? STEP : -STEP
      if (typeof region.scrollBy === 'function') region.scrollBy({ left, behavior: 'smooth' })
      else region.scrollLeft = Math.max(0, region.scrollLeft + left)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [showingRows])

  const attempt = async <T,>(what: typeof busy, show: (error: ApiError | 'network' | null) => void, work: () => Promise<T>) => {
    setBusy(what)
    show(null)
    try {
      return await work()
    } catch {
      show('network')
      return undefined
    } finally {
      setBusy(null)
    }
  }

  // Choosing a file only picks it: its name shows, another can be chosen, and Proceed to preview reads it.
  const [wrongType, setWrongType] = useState(false)
  const pick = (picked: File | undefined) => {
    if (!picked) return
    setFile(picked)
    setBanner(null)
    setProblems([])
    setWrongType(!/\.(xlsx|csv)$/i.test(picked.name))
  }

  const read = async () => {
    if (!file || wrongType) return
    const picked = file
    const result = await attempt('read', setBanner, () => onRead(picked))
    if (isError(result)) return setBanner(result)
    if (!result) return
    setProblems(result.problems)
    setNotes(result.notes ?? [])
    setSearch('')
    setPage(0)
    setView({ kind: 'all', lines: null })
    // A file with rows opens the review; one with none says why, here.
    setRows(result.rows.length > 0 ? result.rows : null)
  }

  // A cell's own check: its message replaces what was there for that cell, or clears it.
  const checked = (list: BulkUploadProblem[], line: number | string, cells: Record<string, string>, keys: string[]) => {
    const found = columns.filter(c => keys.includes(c.key) && c.validate && !c.disabledWhen?.(cells)).flatMap(c => {
      const message = c.validate!(cells[c.key] ?? '', cells)
      return message ? [{ line, column: c.key, message }] : []
    })
    const redone = new Set(columns.filter(c => keys.includes(c.key) && c.validate).map(c => keyOf(line, c.key)))
    return [...list.filter(p => !redone.has(keyOf(p.line, p.column))), ...found]
  }

  const edit = (line: number | string, column: string, value: string, validateNow = false) => {
    const row = rows?.find(r => r.line === line)
    if (!row) return
    let cells = { ...row.cells, [column]: value }
    cells = onCellsChange?.(cells, column) ?? cells
    // Cells that close because of this change are emptied.
    cells = { ...cells, ...Object.fromEntries(columns.filter(c => c.disabledWhen?.(cells)).map(c => [c.key, ''])) }
    setRows(list => list?.map(r => (r.line === line ? { ...r, cells } : r)) ?? null)
    setNotes(list => list.filter(p => keyOf(p.line, p.column) !== keyOf(line, column)))
    // The cell's message goes while typing; cells already marked in the row are checked again, as this one may fix them.
    const marked = problems.filter(p => p.line === line && p.column && p.column !== column).map(p => p.column!)
    const changedOthers = Object.keys(cells).filter(k => k !== column && cells[k] !== row.cells[k])
    setProblems(list => checked(list.filter(p => keyOf(p.line, p.column) !== keyOf(line, column)), line, cells,
      [...marked, ...changedOthers, ...(validateNow ? [column] : [])]))
  }

  const leave = (line: number | string, column: string) => {
    const row = rows?.find(r => r.line === line)
    if (row) setProblems(list => checked(list, line, row.cells, [column]))
  }

  const remove = (line: number | string) => {
    setRows(list => list?.filter(r => r.line !== line) ?? null)
    setProblems(list => list.filter(p => p.line !== line))
    setNotes(list => list.filter(p => p.line !== line))
  }

  const importRows = async () => {
    if (!rows) return
    // Rows nobody typed into (adding several) are left out.
    const filled = rows.filter(r => Object.entries(r.cells).some(([key, v]) => v.trim() && v !== (defaults[key] ?? '')))
    if (filled.length === 0) return
    // Every cell's own check first; the server sees the rows only once they pass.
    const mine = filled.flatMap(r => checked([], r.line, r.cells, columns.map(c => c.key)))
    if (mine.length > 0) {
      setProblems(list => [...list.filter(p => !mine.some(m => keyOf(m.line, m.column) === keyOf(p.line, p.column))), ...mine])
      return
    }
    const done = await attempt('import', setReviewBanner, async () => {
      const check = await onCheck(filled)
      if (isError(check)) return check
      setNotes(check.notes ?? [])
      if (check.problems.length > 0) {
        setProblems(check.problems)
        return 'problems' as const
      }
      return onImport(filled)
    })
    if (done === null) onClose()
    else if (isError(done)) setReviewBanner(done)
  }

  // A problem in a column the sheet doesn't show is shown on its row, so nothing to fix is hidden.
  const shownKeys = new Set(columns.map(c => c.key))
  // A cell as the sheet shows it: a dropdown's label rather than its value.
  const display = (c: BulkUploadColumn, value: string) => c.options?.find(o => o.value === value)?.label ?? value
  // Each column's values across every row, empty first, then A to Z, with how many rows have each.
  const valuesOf = (key: string) => {
    const counts = new Map<string, number>()
    for (const r of rows ?? []) counts.set(cellText(r, key), (counts.get(cellText(r, key)) ?? 0) + 1)
    return [...counts].map(([value, count]) => ({ value, count })).sort((a, b) => (a.value === '' ? -1 : b.value === '' ? 1 : a.value.localeCompare(b.value)))
  }
  const filtered = columns.filter(c => isActive(filters[c.key]))
  const sortBy = (c: BulkUploadColumn) => {
    const direction = sort?.key !== c.key ? 'ascending' : sort.direction === 'ascending' ? 'descending' : null
    if (!direction) return setSort(null)
    const text = (r: BulkUploadRow) => display(c, cellText(r, c.key))
    const sorted = [...(rows ?? [])].sort((a, b) => {
      const [x, y] = [text(a), text(b)]
      if (!x || !y) return !x && !y ? 0 : !x ? 1 : -1   // empty cells last, either way
      const order = x.localeCompare(y, undefined, { numeric: true, sensitivity: 'base' })
      return direction === 'ascending' ? order : -order
    })
    setSort({ key: c.key, direction, order: new Map(sorted.map((r, i) => [String(r.line), i])) })
    setPage(0)
  }
  const problemAt = new Map<string, string>()
  for (const p of problems) {
    const key = p.column && shownKeys.has(p.column) ? keyOf(p.line, p.column) : keyOf(p.line)
    problemAt.set(key, problemAt.has(key) && key === keyOf(p.line) ? `${problemAt.get(key)} ${p.message}` : p.message)
  }
  const noteAt = new Map(notes.map(n => [keyOf(n.line, n.column), n.message]))
  // Each new name once, however many rows use it (ignoring case).
  const toCreate = new Set(notes.map(n => {
    const cell = rows?.find(r => String(r.line) === String(n.line))?.cells[n.column ?? ''] ?? n.message
    return `${n.column}:${cell.trim().toLowerCase()}`
  })).size
  const linesWithProblems = new Set(problems.filter(p => Number(p.line) > 0).map(p => String(p.line)))
  const fileProblems = problems.filter(p => Number(p.line) === 0)
  const allLines = (rows ?? []).map(r => String(r.line))
  const counts = { all: allLines.length, fix: linesWithProblems.size, ready: allLines.filter(l => !linesWithProblems.has(l)).length }
  const showRows = (kind: 'all' | 'fix' | 'ready') => {
    setView({ kind, lines: kind === 'all' ? null : new Set(allLines.filter(l => linesWithProblems.has(l) === (kind === 'fix'))) })
    setPage(0)
  }
  const cellText = (r: BulkUploadRow, key: string) => (r.cells[key] ?? '').trim()
  const passesFilters = (r: BulkUploadRow) => columns.every(c => {
    const f = filters[c.key]
    if (!isActive(f)) return true
    const value = cellText(r, c.key)
    return (!f.text.trim() || matches(display(c, value), f.text)) && !f.hidden.has(value)
  })
  const shown = (rows ?? []).filter(r =>
    (!view.lines || view.lines.has(String(r.line))) && passesFilters(r)
    && (!search.trim() || matches(`${r.line} ${Object.values(r.cells).join(' ')}`, search)))
  // Rows added since the sort go after the sorted ones, in their own order.
  if (sort) shown.sort((a, b) => (sort.order.get(String(a.line)) ?? Infinity) - (sort.order.get(String(b.line)) ?? Infinity))
  // Search and the filter look across every row; the table shows one page of what they find.
  const pages = Math.max(1, Math.ceil(shown.length / PAGE))
  const at = Math.min(page, pages - 1)
  const onPage = shown.slice(at * PAGE, at * PAGE + PAGE)
  const step = 'm-0 text-label font-semibold text-ink'
  const n = firstStep ? 1 : 0

  const footer = <>
          {startRows
            ? <Button variant="secondary" className="mr-auto" onClick={onClose}>{t('common.cancel')}</Button>
            : <Button variant="secondary" className="mr-auto" onClick={() => setRows(null)}>{t('bulk.back')}</Button>}
          {startRows && (
            <Button variant="secondary" onClick={() => setRows(list => [...(list ?? []), ...blank(Math.max(0, ...(list ?? []).map(r => Number(r.line))) + 1, 1)])}>
              <PlusIcon />{t('bulk.addRow')}
            </Button>
          )}
          <Button disabled={!rows?.length} busy={busy === 'import'} onClick={() => void importRows()}>{labels.importButton(rows?.length ?? 0)}</Button>
        </>

  const review = <>
        {reviewBanner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {reviewBanner && reviewBanner !== 'network' && <FormErrorBanner error={reviewBanner} />}
        {/* One line: where the rows apply, the search, what needs fixing and the filter for it. */}
        <div className="flex flex-wrap items-end gap-x-4 gap-y-3">
          {children}
          {/* Beside a labelled selector, the search shows its label too, so the two fields line up. */}
          <div className="w-full sm:w-72">
            <TextField type="search" label={t('bulk.search')} placeholder={t('bulk.searchPlaceholder')}
              value={search} onChange={e => { setSearch(e.target.value); setPage(0) }} />
          </div>
          <div className="w-full sm:w-56">
            <SelectField label={t('bulk.show')} placeholder={t('bulk.showPlaceholder')} value={view.kind}
              options={(['all', 'fix', 'ready'] as const).map(kind => ({ value: kind, label: t(`bulk.views.${kind}`, { count: counts[kind] }) }))}
              onChange={value => showRows(value as 'all' | 'fix' | 'ready')} />
          </div>
          {linesWithProblems.size > 0 && <Notice>{t('bulk.toFix', { count: linesWithProblems.size })}</Notice>}
          {toCreate > 0 && <Notice tone="brand">{t('bulk.toCreate', { count: toCreate })}</Notice>}
          {filtered.length > 0 && (
            <div className="flex flex-wrap items-center gap-2">
              {filtered.map(c => (
                <Tag key={c.key} tone="brand">
                  {c.label}
                  <button type="button" aria-label={t('bulk.removeFilter', { column: c.label })} className="ml-1 text-brand hover:text-ink"
                    onClick={() => setFilter(c.key, { text: '', hidden: new Set() })}>×</button>
                </Tag>
              ))}
              <Button variant="secondary" onClick={() => { setFilters({}); setPage(0) }}>{t('bulk.clearFilters')}</Button>
            </div>
          )}
          <span className="ml-auto text-label text-muted">{t('bulk.showing', { shown: shown.length, total: rows?.length ?? 0 })}</span>
        </div>

        {/* Scrolls on its own, header fixed, so hundreds of rows stay workable. */}
        <div ref={rowsRef} role="region" aria-label={t('bulk.rows')} tabIndex={0} className="min-h-0 flex-1 overflow-auto rounded-card border border-line">
          <table className="w-full border-collapse text-label">
            <thead className="sticky top-0 z-10 bg-raised">
              <tr>
                <th scope="col" className="border-b border-line px-3 py-2 text-left font-medium text-muted">{t('bulk.rowHeader')}</th>
                {columns.map(c => (
                  <th key={c.key} scope="col" aria-sort={sort?.key === c.key ? sort.direction : undefined}
                    className="min-w-40 border-b border-line px-3 py-1.5 text-left font-medium whitespace-nowrap text-muted">
                    <span className="flex items-center gap-1">
                      <span className="mr-auto">{c.label}</span>
                      <button type="button" aria-label={t('bulk.sort', { column: c.label })} onClick={() => sortBy(c)}
                        title={sort?.key === c.key ? t(`bulk.sorted.${sort.direction}`) : undefined}
                        className={`inline-flex size-7 shrink-0 items-center justify-center rounded-control ${sort?.key === c.key ? 'bg-brand-soft text-brand' : 'text-muted hover:bg-card hover:text-ink'}`}>
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                          {sort?.key !== c.key && <path d="M8 4v16M4 8l4-4 4 4M16 20V4M12 16l4 4 4-4" />}
                          {sort?.key === c.key && sort.direction === 'ascending' && <path d="M12 20V4M6 10l6-6 6 6" />}
                          {sort?.key === c.key && sort.direction === 'descending' && <path d="M12 4v16M6 14l6 6 6-6" />}
                        </svg>
                      </button>
                      <ColumnFilter label={c.label} values={valuesOf(c.key)} display={value => display(c, value)}
                        filter={filters[c.key] ?? { text: '', hidden: new Set() }} onChange={f => setFilter(c.key, f)} />
                    </span>
                  </th>
                ))}
                <th scope="col" className="border-b border-line px-3 py-2"><span className="sr-only">{t('companies.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {onPage.map(r => (
                <tr key={r.line} className={linesWithProblems.has(String(r.line)) ? 'bg-warning-soft' : ''}>
                  <td className="border-b border-line-soft px-3 py-2 align-top font-mono text-muted">
                    {r.line}
                    {problemAt.get(keyOf(r.line)) && <p className="m-0 mt-1 font-sans text-hint font-medium text-danger-ink">{problemAt.get(keyOf(r.line))}</p>}
                  </td>
                  {columns.map(c => {
                    const label = t('bulk.cell', { column: c.label, line: r.line })
                    const error = problemAt.get(keyOf(r.line, c.key))
                    // A name that will be created reads under the cell, without marking it wrong.
                    const note = error ? undefined : noteAt.get(keyOf(r.line, c.key))
                    const options = c.optionsFor?.(r.cells) ?? c.options
                    const disabled = c.disabledWhen?.(r.cells)
                    return (
                      <td key={c.key} className="border-b border-line-soft px-2 py-1.5 align-top">
                        {c.type === 'checkbox'
                          ? <>
                              <input type="checkbox" aria-label={label} checked={r.cells[c.key] === 'Yes'} className="m-2 size-5 cursor-pointer accent-brand"
                                onChange={e => edit(r.line, c.key, e.target.checked ? 'Yes' : 'No', true)} />
                              {error && <p className="m-0 text-hint font-medium text-danger-ink">{error}</p>}
                            </>
                          : options
                            ? <SelectField size="sm" hideLabel label={label} placeholder={c.label} error={error} hint={note} options={options}
                                disabled={disabled} value={r.cells[c.key] ?? ''} onChange={value => edit(r.line, c.key, value, true)} />
                            // A date written another way in the file shows as written, to be fixed.
                            : <TextField size="sm" hideLabel label={label} placeholder={c.label} error={error} hint={note}
                                type={c.type === 'date' && (!r.cells[c.key] || /^\d{4}-\d{2}-\d{2}$/.test(r.cells[c.key])) ? 'date' : 'text'}
                                disabled={disabled} value={r.cells[c.key] ?? ''} onChange={e => edit(r.line, c.key, e.target.value)}
                                onBlur={() => leave(r.line, c.key)} />}
                      </td>
                    )
                  })}
                  <td className="border-b border-line-soft px-2 py-1.5 text-right align-top">
                    {/* Adding by hand, the first row stays: there is always one to fill in. */}
                    {!(startRows && r.line === rows?.[0]?.line) && (
                      <IconButton tone="danger" label={t('bulk.removeRow', { line: r.line })} onClick={() => remove(r.line)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {(rows?.length ?? 0) > PAGE && (
          <nav aria-label={t('bulk.pages')} className="flex items-center justify-end gap-3">
            <span className="text-label text-muted">{t('bulk.page', { page: at + 1, pages })}</span>
            <Button variant="secondary" disabled={at === 0} onClick={() => setPage(at - 1)}>{t('bulk.previous')}</Button>
            <Button variant="secondary" disabled={at >= pages - 1} onClick={() => setPage(at + 1)}>{t('bulk.next')}</Button>
          </nav>
        )}
  </>

  return (
    <>
      <Drawer open={open && !rows && !startRows} title={title} onClose={onClose}
        footer={<>
          <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
          <Button disabled={!file || wrongType || !!locked} busy={busy === 'read'} onClick={() => void read()}>{t('bulk.proceed')}</Button>
        </>}>
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={banner} />}

        {firstStep && (
          <section className="flex flex-col gap-2 border-b border-line pb-4">
            <h3 className={step}>{`1. ${firstStep.title}`}</h3>
            {firstStep.content}
          </section>
        )}

        <section className="flex flex-col gap-2">
          <h3 className={step}>{`${n + 1}. ${t('bulk.step1')}`}</h3>
          <p className="m-0 text-hint text-muted">{labels.templateHint}</p>
          {locked && <p className="m-0 text-hint font-medium text-warning-ink">{locked}</p>}
          <div>
            <Button variant="secondary" disabled={!!locked} busy={busy === 'template'} onClick={() => void attempt('template', setBanner, onDownloadTemplate)}>
              <DownloadIcon />{t('bulk.download')}
            </Button>
          </div>
        </section>

        <section className="flex flex-col gap-2 border-t border-line pt-4">
          <h3 className={step}>{`${n + 2}. ${t('bulk.step2')}`}</h3>
          <p className="m-0 text-hint text-muted">{t('bulk.fileHint')}</p>
          <div className="flex flex-wrap items-center gap-3">
            {/* A real file input, hidden behind a button-looking label so it can be styled. */}
            <input id={inputId} type="file" accept=".xlsx,.csv" className="peer sr-only" disabled={busy === 'read' || !!locked}
              onChange={e => { pick(e.target.files?.[0]); e.target.value = '' }} />
            <label htmlFor={inputId} aria-disabled={locked ? true : undefined}
              className={`${buttonClasses('secondary')} ${locked ? 'pointer-events-none opacity-50' : 'cursor-pointer'} peer-focus-visible:outline-2 peer-focus-visible:outline-brand`}>
              {file ? t('bulk.chooseAnother') : t('bulk.choose')}
            </label>
            {file && <span className="text-label font-medium text-ink">{file.name}</span>}
            {busy === 'read' && <span role="status" className="text-label text-muted">{t('bulk.checking')}</span>}
          </div>
          {wrongType && file && (
            <Notice tone="danger">{t('bulk.wrongType', { name: file.name })}</Notice>
          )}
          {fileProblems.map((p, i) => <Notice key={i}>{p.message}</Notice>)}
        </section>
      </Drawer>

      {/* Adding by hand and reviewing a file: the same drawer, 97% wide. */}
      <Drawer open={open && !!rows} size="sheet" initialFocus="panel" title={labels.review(rows?.length ?? 0)} onClose={startRows ? onClose : () => setRows(null)} footer={footer}>
        <div className="flex min-h-0 flex-1 flex-col gap-4">{review}</div>
      </Drawer>
    </>
  )
}
