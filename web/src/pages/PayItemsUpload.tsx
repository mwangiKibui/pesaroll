import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices } from '../app/services'
import { BulkUpload } from '../components/BulkUpload'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { useToast } from '../components/toast'
import { usePayees } from '../setup/hooks'

type Company = components['schemas']['CompanyResponse']
type ImportRow = components['schemas']['ImportRow']

const kinds = ['earning', 'deduction', 'employer_cost'] as const
const methods = ['fixed', 'percent_of_basic', 'each_run'] as const

/**
 * ONB-07 #196: earnings and deductions from a spreadsheet, through the shared upload (template,
 * Proceed to preview, the review sheet). List columns hold the template's words (English); Paid to
 * is one of the organization's payees. Apply to (the companies) is chosen in the review.
 */
export function PayItemsUpload({ companies, onClose }: { companies: Company[]; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const payees = usePayees()
  const [applies, setApplies] = useState<CompanySelection>({ all: true, ids: [] })
  const yesNo = [{ value: 'Yes', label: t('locations.yes') }, { value: 'No', label: t('locations.no') }]

  const columns = [
    { key: 'name', label: t('payItems.columns.name') },
    { key: 'code', label: t('payItems.columns.code') },
    { key: 'kind', label: t('payItems.columns.kind'), options: kinds.map(k => ({ value: t(`payItems.kindLabels.${k}`, { lng: 'en' }), label: t(`payItems.kindLabels.${k}`) })) },
    { key: 'calcMethod', label: t('payItems.columns.calcMethod'), options: methods.map(m => ({ value: t(`payItems.columnMethods.${m}`, { lng: 'en' }), label: t(`payItems.columnMethods.${m}`) })) },
    { key: 'defaultAmount', label: t('payItems.columns.defaultAmount') },
    { key: 'rate', label: t('payItems.columns.rate') },
    { key: 'taxable', label: t('payItems.columns.taxable'), options: yesNo },
    { key: 'beforeTax', label: t('payItems.columns.beforeTax'), options: yesNo },
    { key: 'payee', label: t('payItems.columns.payee'), options: (payees.data ?? []).map(p => ({ value: p.name, label: p.name })) },
  ]

  const downloadTemplate = async () => {
    const { data } = await api.GET('/api/pay-items/template', { parseAs: 'blob' })
    if (!data) throw new Error('template')
    const url = URL.createObjectURL(data)
    const link = document.createElement('a')
    link.href = url
    link.download = 'pesaroll-earnings-and-deductions-template.xlsx'
    link.click()
    URL.revokeObjectURL(url)
  }

  const read = async (file: File) => {
    const { data, error } = await api.POST('/api/pay-items/import/read', {
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

  const check = async (rows: ImportRow[]) => {
    const { data, error } = await api.POST('/api/pay-items/import/check', { body: { rows } })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('check')
  }

  const importRows = async (rows: ImportRow[]) => {
    // With one company the items are simply its own.
    const body = companies.length === 1
      ? { rows, allCompanies: false, companyIds: [companies[0].id] }
      : { rows, allCompanies: applies.all, companyIds: applies.all ? [] : applies.ids }
    const { data, error } = await api.POST('/api/pay-items/import', { body })
    if (data) {
      void queryClient.invalidateQueries({ queryKey: ['payItems'] })
      toast.show(t('payItems.imported', { count: Number(data.imported) }))
      return null
    }
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('import')
  }

  return (
    <BulkUpload open title={t('payItems.uploadTitle')} columns={columns} onClose={onClose} onDownloadTemplate={downloadTemplate}
      onRead={read} onCheck={check} onImport={importRows}
      labels={{
        review: count => t('payItems.review', { count }),
        importButton: count => t('payItems.importButton', { count }),
        templateHint: t('payItems.templateHint'),
      }}>
      {companies.length > 1 && (
        <div className="w-full sm:w-64">
          <CompanySelect label={t('locations.applyTo')} value={applies} onChange={setApplies} compact
            companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
        </div>
      )}
    </BulkUpload>
  )
}
