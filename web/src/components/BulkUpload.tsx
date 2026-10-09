import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, IconButton } from './Button'
import { buttonClasses } from './buttonStyles'
import { Drawer } from './Drawer'
import { FormErrorBanner, Notice, type ApiError } from './feedback'
import { Checkbox, SelectField, TextField } from './fields'
import { Modal } from './Modal'
import { matches } from './search'

/** A template column: its key in a row's cells, its header, and the choices when it is a list (a dropdown). */
export interface BulkUploadColumn { key: string; label: string; options?: { value: string; label: string }[] }

/** A row as written in the file: its row number there and its cells by column key. */
export interface BulkUploadRow { line: number | string; cells: Record<string, string> }

/** A problem the server found: its row (0 for the whole file), the column key and what to fix. */
export interface BulkUploadProblem { line: number | string; column?: string | null; message: string }

export interface BulkUploadRead { rows: BulkUploadRow[]; problems: BulkUploadProblem[] }

export interface BulkUploadCheck { problems: BulkUploadProblem[] }

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
}

const isError = (value: unknown): value is ApiError => typeof value === 'object' && value !== null && 'code' in value && 'reference' in value
const keyOf = (line: number | string, column?: string | null) => `${line}:${column ?? ''}`

/**
 * Uploading many records at once, the same everywhere (locations now; departments, positions and
 * employees later). A drawer: 1 download the template, 2 upload the filled file. Then a large
 * review: every column and row of the file in an editable table (lists as searchable dropdowns),
 * searchable, scrolling under a fixed header, with problems in their cells. Import checks the
 * edited rows again and saves them all, or shows what is still wrong; nothing is imported blindly.
 */
export function BulkUpload({ open, title, columns, onClose, onDownloadTemplate, onRead, onCheck, onImport, labels, children }: BulkUploadProps) {
  const { t } = useTranslation()
  const inputId = useId()
  const [file, setFile] = useState<File | null>(null)
  const [rows, setRows] = useState<BulkUploadRow[] | null>(null)
  const [problems, setProblems] = useState<BulkUploadProblem[]>([])
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const [reviewBanner, setReviewBanner] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState<'template' | 'read' | 'import' | null>(null)
  const [search, setSearch] = useState('')
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
    setSearch('')
    setOnlyProblems(null)
    // A file with rows opens the review; one with none says why, here.
    setRows(result.rows.length > 0 ? result.rows : null)
  }

  const edit = (line: number | string, column: string, value: string) => {
    setRows(list => list?.map(r => (r.line === line ? { ...r, cells: { ...r.cells, [column]: value } } : r)) ?? null)
    setProblems(list => list.filter(p => keyOf(p.line, p.column) !== keyOf(line, column)))
  }

  const remove = (line: number | string) => {
    setRows(list => list?.filter(r => r.line !== line) ?? null)
    setProblems(list => list.filter(p => p.line !== line))
  }

  const importRows = async () => {
    if (!rows) return
    const done = await attempt('import', setReviewBanner, async () => {
      const check = await onCheck(rows)
      if (isError(check)) return check
      if (check.problems.length > 0) {
        setProblems(check.problems)
        return 'problems' as const
      }
      return onImport(rows)
    })
    if (done === null) onClose()
    else if (isError(done)) setReviewBanner(done)
  }

  const problemAt = new Map(problems.map(p => [keyOf(p.line, p.column), p.message]))
  const linesWithProblems = new Set(problems.filter(p => Number(p.line) > 0).map(p => String(p.line)))
  const fileProblems = problems.filter(p => Number(p.line) === 0)
  const shown = (rows ?? []).filter(r =>
    (!onlyProblems || onlyProblems.has(String(r.line)))
    && (!search.trim() || matches(`${r.line} ${Object.values(r.cells).join(' ')}`, search)))
  const step = 'm-0 text-label font-semibold text-ink'

  return (
    <>
      <Drawer open={open && !rows} title={title} onClose={onClose}
        footer={<Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>}>
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={banner} />}

        <section className="flex flex-col gap-2">
          <h3 className={step}>{t('bulk.step1')}</h3>
          <p className="m-0 text-hint text-muted">{labels.templateHint}</p>
          <div>
            <Button variant="secondary" busy={busy === 'template'} onClick={() => void attempt('template', setBanner, onDownloadTemplate)}>
              {t('bulk.download')}
            </Button>
          </div>
        </section>

        <section className="flex flex-col gap-2 border-t border-line pt-4">
          <h3 className={step}>{t('bulk.step2')}</h3>
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

      <Modal open={open && !!rows} size="wide" title={labels.review(rows?.length ?? 0)} onClose={() => setRows(null)}
        footer={<>
          <Button variant="secondary" className="mr-auto" onClick={() => setRows(null)}>{t('bulk.back')}</Button>
          <Button disabled={!rows?.length} busy={busy === 'import'} onClick={() => void importRows()}>{labels.importButton(rows?.length ?? 0)}</Button>
        </>}>
        {reviewBanner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {reviewBanner && reviewBanner !== 'network' && <FormErrorBanner error={reviewBanner} />}
        {children && <div className="flex flex-col gap-1.5">{children}</div>}

        <div className="flex flex-wrap items-end gap-4">
          <div className="w-full max-w-sm">
            <TextField type="search" label={t('bulk.search')} placeholder={t('bulk.searchPlaceholder')} hideLabel
              value={search} onChange={e => setSearch(e.target.value)} />
          </div>
          {linesWithProblems.size > 0 && <Notice>{t('bulk.toFix', { count: linesWithProblems.size })}</Notice>}
          {(linesWithProblems.size > 0 || onlyProblems) && <>
            <Checkbox label={t('bulk.onlyProblems')} checked={!!onlyProblems} onChange={e => setOnlyProblems(e.target.checked ? new Set(linesWithProblems) : null)} />
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
              {shown.map(r => (
                <tr key={r.line} className={linesWithProblems.has(String(r.line)) ? 'bg-warning-soft' : ''}>
                  <td className="border-b border-line-soft px-3 py-2 align-top font-mono text-muted">
                    {r.line}
                    {problemAt.get(keyOf(r.line)) && <p className="m-0 mt-1 font-sans text-hint font-medium text-danger-ink">{problemAt.get(keyOf(r.line))}</p>}
                  </td>
                  {columns.map(c => {
                    const label = t('bulk.cell', { column: c.label, line: r.line })
                    const error = problemAt.get(keyOf(r.line, c.key))
                    return (
                      <td key={c.key} className="border-b border-line-soft px-2 py-1.5 align-top">
                        {c.options
                          ? <SelectField size="sm" hideLabel label={label} placeholder={c.label} error={error} options={c.options}
                              value={r.cells[c.key] ?? ''} onChange={value => edit(r.line, c.key, value)} />
                          : <TextField size="sm" hideLabel label={label} placeholder={c.label} error={error}
                              value={r.cells[c.key] ?? ''} onChange={e => edit(r.line, c.key, e.target.value)} />}
                      </td>
                    )
                  })}
                  <td className="border-b border-line-soft px-2 py-1.5 text-right align-top">
                    <IconButton tone="danger" label={t('bulk.removeRow', { line: r.line })} onClick={() => remove(r.line)}>
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                    </IconButton>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Modal>
    </>
  )
}
