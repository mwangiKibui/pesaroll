import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { errorMessage, isApiError } from '../../app/apiErrors'
import { permissions, usePermissions } from '../../app/permissions'
import { useServices } from '../../app/services'
import { BulkUpload, type BulkUploadColumn, type BulkUploadRow } from '../../components/BulkUpload'
import { Drawer } from '../../components/Drawer'
import { Loading } from '../../components/feedback'
import { SelectField } from '../../components/fields'
import { useToast } from '../../components/toast'
import { accountProblem, rules } from '../../lib/employeeRules'
import { useBanks, useLeavePlans, useSchedules, useStructure, type StructureItem } from '../../setup/hooks'
import { types, usePaysBy, type Company, type ImportRow } from './shared'

/** The sheet's columns, in the template's order (same keys as the API; the company is chosen above, and pay follows the schedule). */
const columnKeys = ['staffNo', 'firstName', 'middleName', 'lastName', 'dateOfBirth', 'phone', 'email', 'nationalId', 'kraPin', 'nssfNo',
  'address', 'shaNo', 'hireDate', 'stillHere', 'exitDate', 'employmentType', 'contractEnd', 'division', 'department', 'unit', 'position', 'lineManager',
  'payrollSchedule', 'leavePlan', 'basicPay', 'paidInto', 'bank', 'branch', 'accountNumber', 'mpesa'] as const

/** The upload's pay and bank columns: only for people with Manage on pay (ACC-05). */
const payColumns: readonly string[] = ['basicPay', 'paidInto', 'bank', 'branch', 'accountNumber', 'mpesa']

/**
 * Adding employees in a sheet (one row to start, Add row for more), or uploading a file (template,
 * then the review). One company at a time, chosen first: its schedules, leave plans, structure,
 * locations and banks are the dropdowns, its default schedule and leave plan come filled in, and
 * people who work here need the bank or M-Pesa details the company pays by. Each cell is checked
 * as soon as it is left, with the same rules as the API, which checks every row again.
 */
export function UploadEmployees({ adding, companies, defaultCompany, onClose }: { adding: boolean; companies: Company[]; defaultCompany: string; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const canManagePay = usePermissions().can(permissions.pay.manage, defaultCompany)
  const toast = useToast()
  // Uploading waits for a company to be chosen (unless there's only one); adding starts with the one shown.
  const [companyId, setCompanyId] = useState(adding || companies.length === 1 ? defaultCompany : '')
  const company = companies.find(c => c.id === companyId) ?? companies[0]
  const divisions = useStructure('divisions')
  const departments = useStructure('departments')
  const positions = useStructure('positions')
  const units = useStructure('units')
  const schedules = useSchedules()
  const plans = useLeavePlans()
  const banks = useBanks(company.countryCode)
  const paysBy = usePaysBy(company.id)
  // How the company pays only adds checks: if it can't be read, the sheet still opens.
  const loaded = [divisions, departments, positions, units, schedules, plans, banks].every(q => q.isSuccess) && !paysBy.isPending

  // What the chosen company uses, by name (as the template lists them).
  type Scoped = { allCompanies: boolean; companyIds: string[] }
  const serves = <T extends Scoped>(items: T[] | undefined) => (items ?? []).filter(i => i.allCompanies || i.companyIds.includes(company.id))
  const named = (names: string[]) => names.map(name => ({ value: name, label: name }))
  const structure = (items: StructureItem[] | undefined) => named(serves(items).map(i => i.name ?? i.title ?? ''))
  const scheduleOf = (name: string) => schedules.data?.find(s => s.name === name)
  const defaultSchedule = schedules.data?.find(s => s.defaultFor.includes(company.id))
  const planFor = (schedule: string) =>
    (plans.data?.find(p => p.defaultForSchedules.includes(schedule)) ?? plans.data?.find(p => p.defaultFor.includes(company.id)))?.name ?? ''
  const bankOf = (name: string) => banks.data?.find(b => b.name === name)
  const lists: Partial<Record<(typeof columnKeys)[number], { value: string; label: string }[]>> = {
    employmentType: types.map(k => ({ value: t(`employees.types.${k}`, { lng: 'en' }), label: t(`employees.types.${k}`) })),
    division: structure(divisions.data), department: structure(departments.data), position: structure(positions.data), unit: structure(units.data),
    payrollSchedule: named(serves(schedules.data).map(s => s.name)),
    leavePlan: named(serves(plans.data).map(p => p.name)),
    bank: named((banks.data ?? []).map(b => b.name)),
  }
  // A file can name something not in the list (a department to create): it stays, so the review shows what was written.
  const withCurrent = (options: { value: string; label: string }[], value: string | undefined) =>
    value?.trim() && !options.some(o => o.value.toLowerCase() === value.trim().toLowerCase()) ? [...options, { value, label: value }] : options

  const message = (key: string | null, values?: Record<string, unknown>) => (key ? t(`employees.rules.${key}`, values) : null)
  const working = (cells: Record<string, string>) => cells.stillHere !== 'No'
  // Paid into a bank or M-Pesa: the company's usual way to start with; for current staff, one the company pays from.
  const methods = paysBy.data?.methods ?? []
  const paidOptions = [{ value: 'Bank', label: t('employees.methods.bank') }, { value: 'M-Pesa', label: t('employees.methods.mpesa') }]
  const methodOf = (cells: Record<string, string>) => {
    const said = (cells.paidInto ?? '').replace(/[\s-]/g, '').toLowerCase()
    return said === 'bank' ? 'bank' : said === 'mpesa' ? 'mpesa' : null
  }
  const paysByMessage = () => (methods.length > 1 ? t('employees.rules.paysByEither')
    : t(methods[0] === 'bank' ? 'employees.rules.paysByBank' : 'employees.rules.paysByMpesa', { company: company.legalName }))
  const validators: Partial<Record<(typeof columnKeys)[number], (value: string, cells: Record<string, string>) => string | null>> = {
    firstName: v => message(rules.name(v)), lastName: v => message(rules.name(v)),
    staffNo: v => message(rules.staffNo(v)), phone: v => message(rules.phone(v)), email: v => message(rules.email(v)),
    nationalId: v => message(rules.nationalId(v)), kraPin: v => message(rules.kraPin(v)), nssfNo: v => message(rules.nssfNo(v)), shaNo: v => message(rules.shaNo(v)),
    hireDate: v => message(rules.required(v)),
    exitDate: (v, cells) => (!working(cells) && !v.trim() ? t('employees.rules.exitDate') : null),
    basicPay: v => message(rules.amount(v)),
    address: v => (v.trim().length > 200 ? message('addressTooLong') : null),
    paidInto: (v, cells) => {
      if (v.trim() && !methodOf(cells)) return message('paidInto')
      if (!working(cells) || methods.length === 0) return null
      return !methodOf(cells) || !methods.includes(methodOf(cells)!) ? paysByMessage() : null
    },
    bank: (v, cells) => (!v.trim() && methodOf(cells) === 'bank' ? message('required') : null),
    branch: (v, cells) => (!v.trim() && cells.bank?.trim() ? message('required') : null),
    accountNumber: (v, cells) => {
      if (!v.trim()) return methodOf(cells) === 'bank' ? message('required') : null
      const problem = accountProblem(bankOf(cells.bank ?? ''), v)
      return problem ? message(problem.key, problem.values) : null
    },
    mpesa: (v, cells) => (!v.trim() ? (methodOf(cells) === 'mpesa' ? message('required') : null) : message(rules.mpesa(v))),
  }
  const dates = new Set(['dateOfBirth', 'hireDate', 'exitDate', 'contractEnd'])
  const isContract = (cells: Record<string, string>) => (cells.employmentType ?? '').trim().toLowerCase() === 'contract'
  const columns: BulkUploadColumn[] = columnKeys.filter(key => canManagePay || !payColumns.includes(key)).map(key => ({
    key, label: key === 'stillHere' ? t('employees.stillHere') : t(`employees.columns.${key}`),
    ...(lists[key] ? { optionsFor: (cells: Record<string, string>) => withCurrent(lists[key]!, cells[key]) } : {}),
    ...(key === 'branch' ? {
      optionsFor: (cells: Record<string, string>) => withCurrent(named((bankOf(cells.bank ?? '')?.branches ?? []).map(b => b.name)), cells.branch),
      disabledWhen: (cells: Record<string, string>) => methodOf(cells) !== 'bank' || !cells.bank?.trim(),
    } : {}),
    ...(key === 'stillHere' ? { type: 'checkbox' as const, ...(adding ? { defaultValue: 'Yes' } : {}) } : {}),
    ...(dates.has(key) ? { type: 'date' as const } : {}),
    ...(key === 'exitDate' ? { disabledWhen: (cells: Record<string, string>) => working(cells) } : {}),
    // Only a contract has an end date.
    ...(key === 'contractEnd' ? { disabledWhen: (cells: Record<string, string>) => !isContract(cells) } : {}),
    ...(adding && key === 'employmentType' ? { defaultValue: t('employees.types.permanent', { lng: 'en' }) } : {}),
    ...(adding && key === 'payrollSchedule' && defaultSchedule ? { defaultValue: defaultSchedule.name } : {}),
    ...(adding && key === 'leavePlan' ? { defaultValue: planFor(defaultSchedule?.id ?? '') } : {}),
    ...(key === 'paidInto' ? { options: paidOptions, ...(adding && paysBy.data?.usual ? { defaultValue: paysBy.data.usual === 'bank' ? 'Bank' : 'M-Pesa' } : {}) } : {}),
    // Only the details of the way they're paid are open.
    ...(['bank', 'accountNumber'].includes(key) ? { disabledWhen: (cells: Record<string, string>) => methodOf(cells) !== 'bank' } : {}),
    ...(key === 'mpesa' ? { disabledWhen: (cells: Record<string, string>) => methodOf(cells) !== 'mpesa' } : {}),
    ...(validators[key] ? { validate: validators[key] } : {}),
  })).map(c => (c.defaultValue === '' ? { ...c, defaultValue: undefined } : c))

  const follow = (cells: Record<string, string>, changed: string) =>
    changed === 'payrollSchedule' ? { ...cells, leavePlan: planFor(scheduleOf(cells.payrollSchedule)?.id ?? '') || cells.leavePlan }
      : changed === 'bank' ? { ...cells, branch: '' }
        : cells

  // Every row is for the chosen company (the sheet has no Company column).
  const forCompany = (rows: BulkUploadRow[]) => rows.map(r => ({ ...r, cells: { ...r.cells, company: company.legalName } }))

  const downloadTemplate = async () => {
    const { data } = await api.GET('/api/employees/template', { params: { query: { companyId: company.id } }, parseAs: 'blob' })
    if (!data) throw new Error('template')
    const url = URL.createObjectURL(data)
    const link = document.createElement('a')
    link.href = url
    link.download = 'pesaroll-employees-template.xlsx'
    link.click()
    URL.revokeObjectURL(url)
  }

  const read = async (file: File) => {
    const { data, error } = await api.POST('/api/employees/import/read', {
      params: { query: { companyId: company.id } },
      body: { file } as never,
      bodySerializer: () => {
        const form = new FormData()
        form.append('file', file)
        return form
      },
    })
    if (data) {
      // "Still works here" as a tick: empty means yes unless there's a date they left.
      const ticked = (cells: Record<string, string>) => {
        const said = (cells.stillHere ?? '').trim().toLowerCase()
        return said === 'yes' || (!said && !(cells.exitDate ?? '').trim()) ? 'Yes' : said === 'no' || !said ? 'No' : cells.stillHere
      }
      return { ...data, rows: data.rows.map(r => ({ ...r, cells: { ...r.cells, stillHere: ticked(r.cells) } })) }
    }
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('read')
  }

  const check = async (rows: BulkUploadRow[]) => {
    const { data, error } = await api.POST('/api/employees/import/check', { body: { rows: forCompany(rows) as ImportRow[] } })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('check')
  }

  const importRows = async (rows: BulkUploadRow[]) => {
    const { data, error } = await api.POST('/api/employees/import', { body: { rows: forCompany(rows) as ImportRow[], allCompanies: false, companyIds: [] } })
    if (data) {
      for (const key of [['employees'], ['structure', 'departments'], ['structure', 'positions'], ['structure', 'divisions']]) {
        void queryClient.invalidateQueries({ queryKey: key })
      }
      toast.show(t('employees.imported', { count: Number(data.imported) }))
      return null
    }
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('import')
  }

  // In the sheet's title bar it's compact, beside the search; in the upload drawer it takes the whole width.
  const companyField = (hint?: string, wide = false) => (
    <div className={wide ? 'w-full' : 'w-full sm:w-64'}>
      <SelectField label={t('employees.company')} placeholder={t('employees.companyPlaceholder')} hint={hint} value={companyId}
        size={wide ? 'md' : 'sm'} hideLabel={!wide}
        onChange={value => value && setCompanyId(value)} options={companies.map(c => ({ value: c.id, label: c.legalName }))} />
    </div>
  )

  if (!loaded) {
    return <Drawer open size={adding ? 'sheet' : 'md'} title={adding ? t('employees.add') : t('employees.upload')} onClose={onClose}><Loading rows={4} /></Drawer>
  }

  return (
    // A sheet being filled in starts again for another company, as its lists change.
    <BulkUpload key={adding ? company.id : 'upload'} open startRows={adding ? 1 : undefined} title={adding ? t('employees.add') : t('employees.upload')}
      columns={columns} onCellsChange={follow} onClose={onClose}
      onDownloadTemplate={downloadTemplate} onRead={read} onCheck={check} onImport={importRows}
      locked={companyId ? undefined : t('employees.chooseCompanyFirst')}
      firstStep={adding ? undefined : { title: t('employees.uploadCompany'), content: companyField(t('employees.uploadCompanyHint'), true) }}
      labels={{
        review: count => (adding ? t('employees.several', { count }) : t('employees.review', { count })),
        importButton: count => (adding ? t('employees.several', { count }) : t('employees.importButton', { count })),
        templateHint: t('employees.templateHint'),
      }}>
      {adding && companies.length > 1 ? companyField() : undefined}
    </BulkUpload>
  )
}
