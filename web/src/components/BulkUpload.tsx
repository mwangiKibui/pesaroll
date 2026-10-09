import { useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'
import { buttonClasses } from './buttonStyles'
import { Drawer } from './Drawer'
import { FormErrorBanner, Notice, type ApiError } from './feedback'

/** A problem the server found in the file: its row (0 for the whole file), the column and what to fix. */
export interface BulkUploadProblem { line: number | string; column?: string | null; message: string }

export interface BulkUploadCheck<Row> { rows: Row[]; problems: BulkUploadProblem[] }

interface BulkUploadProps<Row> {
  open: boolean
  /** "Upload locations". */
  title: string
  onClose: () => void
  /** Downloads the step's template (an Excel file with the columns and dropdowns). */
  onDownloadTemplate: () => Promise<void>
  /** Sends the file to be checked; nothing is saved yet. */
  onCheck: (file: File) => Promise<BulkUploadCheck<Row> | ApiError>
  /** Saves the checked rows, all or nothing; null when it worked. The page shows the toast. */
  onImport: (rows: Row[]) => Promise<ApiError | null>
  labels: {
    /** "12 locations ready". */
    ready: (count: number) => string
    /** "Import 12 locations". */
    importButton: (count: number) => string
    /** What goes in the template, under step 1. */
    templateHint: string
  }
  /** Step 3, choosing where the rows apply (the company selector); left out when there is nothing to choose. */
  children?: ReactNode
}

const isError = (value: unknown): value is ApiError => typeof value === 'object' && value !== null && 'code' in value && 'reference' in value

/**
 * Uploading many records at once, the same on every table step (locations now; departments,
 * positions and employees later), in a drawer: 1 download the template, 2 upload the filled file
 * (checked by the server, problems listed by row, upload again after fixing), 3 choose where the
 * rows apply, then import them all in one go.
 */
export function BulkUpload<Row>({ open, title, onClose, onDownloadTemplate, onCheck, onImport, labels, children }: BulkUploadProps<Row>) {
  const { t } = useTranslation()
  const inputId = useId()
  const [file, setFile] = useState<File | null>(null)
  const [checked, setChecked] = useState<BulkUploadCheck<Row> | null>(null)
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const [busy, setBusy] = useState<'template' | 'check' | 'import' | null>(null)

  const run = async <T,>(what: typeof busy, work: () => Promise<T>) => {
    setBusy(what)
    setBanner(null)
    try {
      return await work()
    } catch {
      setBanner('network')
      return undefined
    } finally {
      setBusy(null)
    }
  }

  const choose = async (picked: File | undefined) => {
    if (!picked) return
    setFile(picked)
    setChecked(null)
    const result = await run('check', () => onCheck(picked))
    if (isError(result)) setBanner(result)
    else if (result) setChecked(result)
  }

  const importRows = async () => {
    if (!checked) return
    const error = await run('import', () => onImport(checked.rows))
    if (error === null) onClose()
    else if (error) setBanner(error)
  }

  const ready = checked && checked.rows.length > 0 && checked.problems.length === 0
  const step = 'm-0 text-label font-semibold text-ink'

  return (
    <Drawer open={open} title={title} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button disabled={!ready} busy={busy === 'import'} onClick={() => void importRows()}>{labels.importButton(checked?.rows.length ?? 0)}</Button>
      </>}>
      {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
      {banner && banner !== 'network' && <FormErrorBanner error={banner} />}

      <section className="flex flex-col gap-2">
        <h3 className={step}>{t('bulk.step1')}</h3>
        <p className="m-0 text-hint text-muted">{labels.templateHint}</p>
        <div><Button variant="secondary" busy={busy === 'template'} onClick={() => void run('template', onDownloadTemplate)}>{t('bulk.download')}</Button></div>
      </section>

      <section className="flex flex-col gap-2 border-t border-line pt-4">
        <h3 className={step}>{t('bulk.step2')}</h3>
        <p className="m-0 text-hint text-muted">{t('bulk.fileHint')}</p>
        <div className="flex flex-wrap items-center gap-3">
          {/* A real file input, hidden behind a button-looking label so it can be styled. */}
          <input id={inputId} type="file" accept=".xlsx,.csv" className="peer sr-only" disabled={busy === 'check'}
            onChange={e => { void choose(e.target.files?.[0]); e.target.value = '' }} />
          <label htmlFor={inputId} className={`${buttonClasses('secondary')} cursor-pointer peer-focus-visible:outline-2 peer-focus-visible:outline-brand`}>
            {file ? t('bulk.chooseAnother') : t('bulk.choose')}
          </label>
          {file && <span className="text-label text-muted">{file.name}</span>}
          {busy === 'check' && <span role="status" className="text-label text-muted">{t('bulk.checking')}</span>}
        </div>
        {checked && checked.rows.length > 0 && <Notice tone="brand">{labels.ready(checked.rows.length)}</Notice>}
        {checked && checked.problems.length > 0 && (
          <div className="flex flex-col gap-2 rounded-card bg-warning-soft px-4 py-3 text-label text-warning-ink">
            <p className="m-0 font-medium">{t('bulk.fixAndUpload')}</p>
            <ul aria-label={t('bulk.problems', { count: checked.problems.length })} className="m-0 flex flex-col gap-1 pl-5">
              {checked.problems.map((p, i) => (
                <li key={i}>
                  {Number(p.line) > 0 ? `${t('bulk.row', { line: p.line })}${p.column ? ` · ${p.column}` : ''}: ${p.message}` : p.message}
                </li>
              ))}
            </ul>
          </div>
        )}
      </section>

      {children && (
        <section className="flex flex-col gap-2 border-t border-line pt-4">
          <h3 className={step}>{t('bulk.step3')}</h3>
          {children}
        </section>
      )}
    </Drawer>
  )
}
