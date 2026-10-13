import { useQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices } from '../app/services'
import { BulkUpload, type BulkUploadColumn, type BulkUploadRow } from '../components/BulkUpload'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { EmptyState, Loading } from '../components/feedback'
import { SelectField } from '../components/fields'
import { UploadIcon } from '../components/icons'
import { Pagination } from '../components/Pagination'
import { TableCard } from '../components/TableCard'
import { useToast } from '../components/toast'
import { usePaged } from '../components/usePaged'
import { formatMoney } from '../lib/money'
import { useCompanies, useSetupDone } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type Batch = components['schemas']['PayrollHistoryBatchResponse']
type ImportRow = components['schemas']['ImportRow']

/** The template's columns, in its order (same keys as the API; the company is chosen first). */
const columnKeys = ['staffNo', 'kraPin', 'nationalId', 'name', 'month', 'payDate', 'basic', 'allowances', 'benefits', 'quarters', 'gross', 'pension',
  'nssf1', 'nssf2', 'nssf', 'nssfEr', 'shif', 'ahl', 'ahlEr', 'prmf', 'mortgage', 'taxable', 'relief', 'insurance', 'paye', 'other', 'net', 'nita', 'notes'] as const
const required = new Set(['kraPin', 'name', 'month', 'basic', 'gross', 'shif', 'paye', 'net'])
const amounts = new Set(['basic', 'allowances', 'benefits', 'quarters', 'gross', 'pension', 'nssf1', 'nssf2', 'nssf', 'nssfEr', 'shif', 'ahl', 'ahlEr', 'prmf',
  'mortgage', 'taxable', 'relief', 'insurance', 'paye', 'other', 'net', 'nita'])

/** "January 2026". */
const monthName = (iso: string) =>
  new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${iso.slice(0, 7)}-01T00:00:00Z`))
/** "20 Jan 2026". */
const shortDate = (iso: string) => new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'Africa/Nairobi' }).format(new Date(iso))

/**
 * ONB-10, the optional Payroll history step: what the previous system paid, month by month, so
 * year-to-date totals and P9A tax cards start complete. The page lists the imports (for all
 * companies or those shown), each of which can be deleted; Upload opens the import (company first,
 * then the template and the file, reviewed in the wide drawer, as for employees).
 */
export default function PayrollHistoryPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const companies = useCompanies()
  const done = useSetupDone(true)
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [uploading, setUploading] = useState(false)
  const [deleting, setDeleting] = useState<Batch | null>(null)
  const [busy, setBusy] = useState(false)
  const companyIds = shown.all ? undefined : shown.ids

  const batches = useQuery({
    queryKey: ['payrollHistory', 'batches', companyIds ?? 'all'],
    placeholderData: previous => previous,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/payroll-history/batches', { params: { query: { companyIds } } })
      if (error || !data) throw new Error('batches')
      return data
    },
  })
  const imports = usePaged(batches.data ?? [], 20)
  const companyName = (id: string) => companies.data?.find(c => c.id === id)?.legalName ?? ''

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      const { error, response } = await api.DELETE('/api/payroll-history/batches/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        void queryClient.invalidateQueries({ queryKey: ['payrollHistory'] })
        toast.show(t('history.deleted'))
      } else {
        toast.show(isApiError(error) ? errorMessage(t, error) : t('apiErrors.network'), 'danger')
      }
    } catch {
      toast.show(t('apiErrors.network'), 'danger')
    } finally {
      setBusy(false)
      setDeleting(null)
    }
  }

  const ready = companies.data && batches.data
  const many = (companies.data?.length ?? 0) > 1
  const upload = <Button onClick={() => setUploading(true)}><UploadIcon />{t('history.upload')}</Button>

  return (
    <SetupLayout current="history" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('history.title')}</h1>
        <p className="m-0 text-muted">{t('history.intro')}</p>
      </div>
      {!ready ? <Loading rows={4} /> : <>
        {many && (
          <div className="w-full max-w-sm">
            <CompanySelect label={t('history.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <TableCard title={t('history.imports')} count={batches.data.length} actions={upload}
          footer={<Pagination label={t('pagination.of', { what: t('history.imports') })} {...imports.pagination} />}>
          {batches.data.length === 0 ? <EmptyState title={t('history.emptyTitle')}>{t('history.emptyText')}</EmptyState> : (
            <table>
              <thead>
                <tr>
                  {many && <th scope="col">{t('history.company')}</th>}
                  <th scope="col">{t('history.file')}</th>
                  <th scope="col">{t('history.covers')}</th>
                  <th scope="col">{t('history.rows')}</th>
                  <th scope="col">{t('history.gross')}</th>
                  <th scope="col">{t('history.paye')}</th>
                  <th scope="col">{t('history.importedOn')}</th>
                  <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {imports.items.map(b => (
                  <tr key={b.id}>
                    {many && <td>{companyName(b.companyId)}</td>}
                    <td className="font-medium text-ink">{b.fileName}</td>
                    <td className="whitespace-nowrap">
                      {b.firstPeriod === b.lastPeriod ? monthName(b.firstPeriod) : `${monthName(b.firstPeriod)} – ${monthName(b.lastPeriod)}`}
                    </td>
                    <td className="tabular-nums">{Number(b.rowCount)}</td>
                    <td className="tabular-nums">{formatMoney(b.grossTotal)}</td>
                    <td className="tabular-nums">{formatMoney(b.payeTotal)}</td>
                    <td className="whitespace-nowrap">{shortDate(b.createdAt)}</td>
                    <td className="text-right">
                      <IconButton tone="danger" label={t('history.delete', { name: b.fileName })} onClick={() => setDeleting(b)}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                      </IconButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TableCard>
        {/* Team access (ONB-11) is next; until it has a page, the dashboard. */}
        <Proceed label={t('organization.proceed')} onProceed={() => void navigate({ to: '/' })} />
        {uploading && <UploadHistory companies={companies.data} onClose={() => setUploading(false)}
          defaultCompany={companies.data.length === 1 ? companies.data[0].id : !shown.all && shown.ids.length === 1 ? shown.ids[0] : ''} />}
      </>}
      <ConfirmDialog open={!!deleting} title={t('history.deleteTitle', { name: deleting?.fileName })}
        confirmLabel={t('history.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('history.deleteText', { count: Number(deleting?.rowCount ?? 0) })}
      </ConfirmDialog>
    </SetupLayout>
  )
}

/** Uploading payroll history: the company first, then the template and the file, reviewed in the wide drawer. */
function UploadHistory({ companies, defaultCompany, onClose }: { companies: Company[]; defaultCompany: string; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [companyId, setCompanyId] = useState(defaultCompany)
  const [fileName, setFileName] = useState<string | null>(null)
  const company = companies.find(c => c.id === companyId) ?? companies[0]

  const amount = (v: string) => (v.trim() && !/^\s*(kes|ksh)?\s*[\d,]+(\.\d+)?\s*$/i.test(v) ? t('history.rules.amount') : null)
  const columns: BulkUploadColumn[] = columnKeys.map(key => ({
    key, label: t(`history.columns.${key}`),
    ...(key === 'payDate' ? { type: 'date' as const } : {}),
    validate: (value: string) => (required.has(key) && !value.trim() ? t('history.rules.required') : amounts.has(key) ? amount(value) : null),
  }))

  // Every row is for the chosen company (the sheet has no Company column).
  const forCompany = (rows: BulkUploadRow[]) => rows.map(r => ({ ...r, cells: { ...r.cells, company: company.legalName } })) as ImportRow[]

  const downloadTemplate = async () => {
    const { data } = await api.GET('/api/payroll-history/template', { params: { query: { companyId: company.id } }, parseAs: 'blob' })
    if (!data) throw new Error('template')
    const url = URL.createObjectURL(data)
    const link = document.createElement('a')
    link.href = url
    link.download = 'pesaroll-payroll-history-template.xlsx'
    link.click()
    URL.revokeObjectURL(url)
  }

  const read = async (file: File) => {
    setFileName(file.name)
    const { data, error } = await api.POST('/api/payroll-history/import/read', {
      params: { query: { companyId: company.id } },
      body: { file } as never,
      bodySerializer: () => {
        const form = new FormData()
        form.append('file', file)
        return form
      },
    })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('read')
  }

  const check = async (rows: BulkUploadRow[]) => {
    const { data, error } = await api.POST('/api/payroll-history/import/check', { body: { rows: forCompany(rows) } })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('check')
  }

  const importRows = async (rows: BulkUploadRow[]) => {
    const { data, error } = await api.POST('/api/payroll-history/import', { body: { rows: forCompany(rows), allCompanies: false, companyIds: [], fileName } })
    if (data) {
      void queryClient.invalidateQueries({ queryKey: ['payrollHistory'] })
      void queryClient.invalidateQueries({ queryKey: ['employees'] })   // people it added, as former employees
      toast.show(t('history.imported', { count: Number(data.imported) }))
      return null
    }
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('import')
  }

  return (
    <BulkUpload open title={t('history.upload')} columns={columns} onClose={onClose}
      onDownloadTemplate={downloadTemplate} onRead={read} onCheck={check} onImport={importRows}
      locked={companyId ? undefined : t('history.chooseCompanyFirst')}
      firstStep={{
        title: t('history.uploadCompany'),
        content: (
          <div className="w-full">
            <SelectField label={t('history.company')} placeholder={t('history.companyPlaceholder')} hint={t('history.uploadCompanyHint')} value={companyId}
              onChange={value => value && setCompanyId(value)} options={companies.map(c => ({ value: c.id, label: c.legalName }))} />
          </div>
        ),
      }}
      labels={{
        review: count => t('history.review', { count }),
        importButton: count => t('history.importButton', { count }),
        templateHint: t('history.templateHint'),
      }} />
  )
}
