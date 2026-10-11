import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, IconButton } from './Button'
import { buttonClasses } from './buttonStyles'
import { Drawer } from './Drawer'
import { FormErrorBanner, Notice, type ApiError } from './feedback'
import { Checkbox, SelectField, TextField } from './fields'
import { DownloadIcon, PlusIcon } from './icons'
import { Modal } from './Modal'
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
  /** Adding several by hand: opens straight into the review with this many empty rows, and Add row; empty rows are left out. */
  startRows?: number
  /** Changes that follow from a change (a schedule brings its leave plan; a new bank clears the branch). */
  onCellsChange?: (cells: Record<string, string>, changed: string) => Record<string, string>
}

const isError = (value: unknown): value is ApiError => typeof value === 'object' && value !== null && 'code' in value && 'reference' in value
const keyOf = (line: number | string, column?: string | null) => `${line}:${column ?? ''}`

/** Rows shown at a time in the review, so files of thousands of rows stay quick to show and edit. */
const PAGE = 100

/**
 * Uploading many records at once, the same everywhere (locations now; departments, positions and
 * employees later). A drawer: 1 download the template, 2 upload the filled file. Then a large
 * review: every column and row of the file in an editable table (lists as searchable dropdowns),
 * searchable, scrolling under a fixed header, with problems in their cells. Import checks the
 * edited rows again and saves them all, or shows what is still wrong; nothing is imported blindly.
 */
export function BulkUpload({ open, title, columns, onClose, onDownloadTemplate, onRead, onCheck, onImport, labels, children, firstStep, startRows, onCellsChange }: BulkUploadProps) {
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
  // The rows that needed fixing when "Only rows to fix" was ticked: fixed rows stay in view until it is unticked.
  const [onlyProblems, setOnlyProblems] = useState<Set<string> | null>(null)

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

  const read = async (picked: File | undefined) => {
    if (!picked) return
    setFile(picked)
    const result = await attempt('read', setBanner, () => onRead(picked))
    if (isError(result)) return setBanner(result)
    if (!result) return
    setProblems(result.problems)
    setNotes(result.notes ?? [])
    setSearch('')
    setPage(0)
    setOnlyProblems(null)
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

  const problemAt = new Map(problems.map(p => [keyOf(p.line, p.column), p.message]))
  const noteAt = new Map(notes.map(n => [keyOf(n.line, n.column), n.message]))
  // Each new name once, however many rows use it (ignoring case).
  const toCreate = new Set(notes.map(n => {
    const cell = rows?.find(r => String(r.line) === String(n.line))?.cells[n.column ?? ''] ?? n.message
    return `${n.column}:${cell.trim().toLowerCase()}`
  })).size
  const linesWithProblems = new Set(problems.filter(p => Number(p.line) > 0).map(p => String(p.line)))
  const fileProblems = problems.filter(p => Number(p.line) === 0)
  const shown = (rows ?? []).filter(r =>
    (!onlyProblems || onlyProblems.has(String(r.line)))
    && (!search.trim() || matches(`${r.line} ${Object.values(r.cells).join(' ')}`, search)))
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
            <TextField type="search" label={t('bulk.search')} placeholder={t('bulk.searchPlaceholder')} hideLabel={!children}
              value={search} onChange={e => { setSearch(e.target.value); setPage(0) }} />
          </div>
          {linesWithProblems.size > 0 && <Notice>{t('bulk.toFix', { count: linesWithProblems.size })}</Notice>}
          {toCreate > 0 && <Notice tone="brand">{t('bulk.toCreate', { count: toCreate })}</Notice>}
          {(linesWithProblems.size > 0 || onlyProblems) && <>
            <Checkbox label={t('bulk.onlyProblems')} checked={!!onlyProblems} onChange={e => { setOnlyProblems(e.target.checked ? new Set(linesWithProblems) : null); setPage(0) }} />
          </>}
          <span className="ml-auto text-label text-muted">{t('bulk.showing', { shown: shown.length, total: rows?.length ?? 0 })}</span>
        </div>

        {/* Scrolls on its own, header fixed, so hundreds of rows stay workable. */}
        <div role="region" aria-label={t('bulk.rows')} tabIndex={0} className="min-h-0 flex-1 overflow-auto rounded-card border border-line">
          <table className="w-full border-collapse text-label">
            <thead className="sticky top-0 z-10 bg-raised">
              <tr>
                <th scope="col" className="border-b border-line px-3 py-2 text-left font-medium text-muted">{t('bulk.rowHeader')}</th>
                {columns.map(c => <th key={c.key} scope="col" className="min-w-40 border-b border-line px-3 py-2 text-left font-medium whitespace-nowrap text-muted">{c.label}</th>)}
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
                            : <TextField size="sm" hideLabel label={label} placeholder={c.label} error={error} hint={note} type={c.type === 'date' ? 'date' : 'text'}
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
        footer={<Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>}>
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
          <div>
            <Button variant="secondary" busy={busy === 'template'} onClick={() => void attempt('template', setBanner, onDownloadTemplate)}>
              <DownloadIcon />{t('bulk.download')}
            </Button>
          </div>
        </section>

        <section className="flex flex-col gap-2 border-t border-line pt-4">
          <h3 className={step}>{`${n + 2}. ${t('bulk.step2')}`}</h3>
          <p className="m-0 text-hint text-muted">{t('bulk.fileHint')}</p>
          <div className="flex flex-wrap items-center gap-3">
            {/* A real file input, hidden behind a button-looking label so it can be styled. */}
            <input id={inputId} type="file" accept=".xlsx,.csv" className="peer sr-only" disabled={busy === 'read'}
              onChange={e => { void read(e.target.files?.[0]); e.target.value = '' }} />
            <label htmlFor={inputId} className={`${buttonClasses('secondary')} cursor-pointer peer-focus-visible:outline-2 peer-focus-visible:outline-brand`}>
              {file ? t('bulk.chooseAnother') : t('bulk.choose')}
            </label>
            {file && <span className="text-label text-muted">{file.name}</span>}
            {busy === 'read' && <span role="status" className="text-label text-muted">{t('bulk.checking')}</span>}
          </div>
          {fileProblems.map((p, i) => <Notice key={i}>{p.message}</Notice>)}
        </section>
      </Drawer>

      {/* Adding by hand: a drawer 97% wide; reviewing a file: a large dialog. */}
      {startRows
        ? <Drawer open={open && !!rows} size="sheet" title={labels.review(rows?.length ?? 0)} onClose={onClose} footer={footer}>
            <div className="flex min-h-0 flex-1 flex-col gap-4">{review}</div>
          </Drawer>
        : <Modal open={open && !!rows} size="wide" title={labels.review(rows?.length ?? 0)} onClose={() => setRows(null)} footer={footer}>
            {review}
          </Modal>}
    </>
  )
}
