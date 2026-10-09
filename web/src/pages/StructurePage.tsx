import { useNavigate } from '@tanstack/react-router'
import { useId, useState, type ReactNode } from 'react'
import { Controller, useForm, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { BulkUpload, type BulkUploadRow } from '../components/BulkUpload'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Drawer } from '../components/Drawer'
import { EmptyState, FormErrorBanner, Loading, Notice } from '../components/feedback'
import { SelectField, TextField } from '../components/fields'
import { TableCard } from '../components/TableCard'
import { TabPanel, Tabs } from '../components/Tabs'
import { useToast } from '../components/toast'
import { useCompanies, useSetupDone, useStructure, type LevelKey, type StructureItem } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']

const levels: LevelKey[] = ['divisions', 'departments', 'positions', 'units']
const none = <span className="text-muted">—</span>
const label = (item: StructureItem) => item.title ?? item.name ?? ''

interface Values {
  name: string
  code: string
  divisionId: string
  parentId: string
  departmentId: string
  applies: CompanySelection
}

/**
 * ONB-05: how the organization is organised, one tab per level (Divisions · Departments ·
 * Positions · Units), each item for all or chosen companies.
 */
export default function StructurePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const tabsId = useId()
  const companies = useCompanies()
  const data = {
    divisions: useStructure('divisions'),
    departments: useStructure('departments'),
    positions: useStructure('positions'),
    units: useStructure('units'),
  }
  const done = useSetupDone(true)
  const [tab, setTab] = useState<LevelKey>('departments')
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })

  const ready = companies.data && levels.every(l => data[l].data)
  const visible = (items: StructureItem[]) => items.filter(i => shown.all || i.allCompanies || i.companyIds.some(id => shown.ids.includes(id)))
  // A new item serves the companies being shown, as the selector suggests.
  const defaultApplies: CompanySelection = companies.data?.length === 1 ? { all: false, ids: [companies.data[0].id] } : shown

  return (
    <SetupLayout current="structure" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('structure.title')}</h1>
        <p className="m-0 text-muted">{t('structure.intro')}</p>
      </div>
      {!ready ? <Loading rows={4} /> : <>
        {companies.data.length > 1 && (
          <div className="max-w-sm">
            <CompanySelect label={t('locations.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <div className="flex flex-col gap-4">
          <Tabs id={tabsId} label={t('structure.levels')} current={tab} onChange={key => setTab(key as LevelKey)}
            tabs={levels.map(l => ({ key: l, label: t(`structure.${l}.tab`) }))} />
          {levels.map(l => (
            <TabPanel key={l} id={tabsId} tab={l} current={tab}>
              <LevelTable level={l} items={visible(data[l].data!)} all={data} companies={companies.data} defaultApplies={defaultApplies} />
            </TabPanel>
          ))}
        </div>
        {/* Payroll schedules (ONB-06) isn't built yet, so the dashboard comes next. */}
        <Proceed label={t('organization.proceed')} onProceed={() => void navigate({ to: '/' })} />
      </>}
    </SetupLayout>
  )
}

type AllLevels = Record<LevelKey, { data?: StructureItem[] }>

function LevelTable({ level, items, all, companies, defaultApplies }: {
  level: LevelKey
  items: StructureItem[]
  all: AllLevels
  companies: Company[]
  defaultApplies: CompanySelection
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [editing, setEditing] = useState<StructureItem | 'new' | null>(null)
  const [deleting, setDeleting] = useState<StructureItem | null>(null)
  const [busy, setBusy] = useState(false)
  const [uploading, setUploading] = useState(false)
  const nameOf = (key: LevelKey, id?: string | null) => {
    const found = all[key].data?.find(i => i.id === id)
    return found ? label(found) : null
  }
  const applies = (i: StructureItem) => i.allCompanies
    ? t('locations.allCompanies')
    : companies.filter(c => i.companyIds.includes(c.id)).map(c => c.legalName).join(', ') || null

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      const { error, response } = await api.DELETE(`/api/structure/${level}/{id}` as '/api/structure/departments/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        void queryClient.invalidateQueries({ queryKey: ['structure'] })
        toast.show(t(`structure.${level}.deleted`))
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

  // The columns after the name: what the item belongs to.
  const extra: { header: string; cell: (i: StructureItem) => ReactNode }[] = {
    divisions: [],
    departments: [
      { header: t('structure.division'), cell: (i: StructureItem) => nameOf('divisions', i.divisionId) ?? none },
      { header: t('structure.partOf'), cell: (i: StructureItem) => nameOf('departments', i.parentId) ?? none },
    ],
    positions: [
      { header: t('structure.department'), cell: (i: StructureItem) => nameOf('departments', i.departmentId) ?? none },
    ],
    units: [{ header: t('structure.department'), cell: (i: StructureItem) => nameOf('departments', i.departmentId) ?? none }],
  }[level]

  return (
    <>
      <TableCard title={t(`structure.${level}.tab`)} count={items.length}
        actions={<>
          <Button variant="secondary" onClick={() => setUploading(true)}>{t(`structure.${level}.upload`)}</Button>
          <Button onClick={() => setEditing('new')}>{t(`structure.${level}.add`)}</Button>
        </>}>
        {items.length === 0 ? <EmptyState title={t(`structure.${level}.emptyTitle`)}>{t(`structure.${level}.emptyText`)}</EmptyState> : (
          <table>
            <thead>
              <tr>
                <th scope="col">{t(`structure.${level}.column`)}</th>
                {extra.map(c => <th key={c.header} scope="col">{c.header}</th>)}
                <th scope="col">{t('locations.appliesTo')}</th>
                <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {items.map(i => (
                <tr key={i.id}>
                  <td>
                    <span className="block font-medium text-ink">{label(i)}</span>
                    <span className="block font-mono text-hint text-muted">{i.code}</span>
                  </td>
                  {extra.map(c => <td key={c.header}>{c.cell(i)}</td>)}
                  <td>{applies(i) ?? none}</td>
                  <td className="whitespace-nowrap text-right">
                    <IconButton label={t('locations.edit', { name: label(i) })} onClick={() => setEditing(i)}>
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                    </IconButton>
                    <IconButton tone="danger" label={t('locations.delete', { name: label(i) })} onClick={() => setDeleting(i)}>
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                    </IconButton>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </TableCard>
      {uploading && <UploadLevel level={level} all={all} companies={companies} onClose={() => setUploading(false)} />}
      {editing && (
        <ItemDrawer level={level} item={editing === 'new' ? null : editing} all={all} companies={companies}
          defaultApplies={defaultApplies} onClose={() => setEditing(null)} />
      )}
      <ConfirmDialog open={!!deleting} title={t('locations.deleteTitle', { name: deleting ? label(deleting) : '' })}
        confirmLabel={t('structure.delete')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('structure.deleteText')}
      </ConfirmDialog>
    </>
  )
}

function ItemDrawer({ level, item, all, companies, defaultApplies, onClose }: {
  level: LevelKey
  item: StructureItem | null
  all: AllLevels
  companies: Company[]
  defaultApplies: CompanySelection
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { register, handleSubmit, control, setError, formState: { errors, isSubmitting } } = useForm<Values>({
    defaultValues: {
      name: item ? label(item) : '',
      code: item?.code ?? '',
      divisionId: item?.divisionId ?? '',
      parentId: item?.parentId ?? '',
      departmentId: item?.departmentId ?? '',
      applies: item ? { all: item.allCompanies, ids: item.allCompanies ? companies.map(c => c.id) : item.companyIds } : defaultApplies,
    },
  })
  const options = (key: LevelKey, except?: string) => (all[key].data ?? []).filter(i => i.id !== except).map(i => ({ value: i.id, label: label(i) }))

  // The same checks as the API, so most mistakes show before anything is sent.
  const check = (v: Values) => {
    const problems: [FieldPath<Values>, string][] = []
    if (v.name.trim().length < 2 || v.name.trim().length > 120) problems.push(['name', t(`structure.${level}.nameInvalid`)])
    if (v.code.trim() && !/^[A-Z0-9][A-Z0-9-]{1,19}$/.test(v.code.trim().toUpperCase())) problems.push(['code', t('locations.codeInvalid')])
    if (level === 'units' && !v.departmentId) problems.push(['departmentId', t('structure.unitDepartmentRequired')])
    if (!v.applies.all && v.applies.ids.length === 0) problems.push(['applies', t('locations.companiesRequired')])
    for (const [name, message] of problems) setError(name, { message })
    return problems.length === 0
  }

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!check(v)) return
    const common = {
      code: v.code.trim() ? v.code.trim().toUpperCase() : null,
      allCompanies: v.applies.all,
      companyIds: v.applies.all ? [] : v.applies.ids,
    }
    const orNull = (s: string) => s.trim() || null
    const body = {
      divisions: { ...common, name: v.name.trim() },
      departments: { ...common, name: v.name.trim(), divisionId: orNull(v.divisionId), parentId: orNull(v.parentId) },
      positions: { ...common, title: v.name.trim(), departmentId: orNull(v.departmentId) },
      units: { ...common, name: v.name.trim(), departmentId: orNull(v.departmentId) },
    }[level]
    try {
      const { data, error } = item
        ? await api.PATCH(`/api/structure/${level}/{id}` as '/api/structure/departments/{id}', { params: { path: { id: item.id } }, body: body as never })
        : await api.POST(`/api/structure/${level}` as '/api/structure/departments', { body: body as never })
      if (data) {
        void queryClient.invalidateQueries({ queryKey: ['structure'] })
        toast.show(t(item ? `structure.${level}.saved` : `structure.${level}.added`))
        onClose()
        return
      }
      showError(error)
    } catch {
      setBanner('network')
    }
  }

  const showError = (error: unknown) => {
    if (!isApiError(error)) return setBanner('network')
    if (error.code === 'ONB-3520') return setError('code', { message: errorMessage(t, error) })
    const fields: Record<string, FieldPath<Values>> = {
      name: 'name', title: 'name', code: 'code', divisionId: 'divisionId', parentId: 'parentId',
      departmentId: 'departmentId', companyIds: 'applies',
    }
    const known = Object.entries(error.fields ?? {}).filter(([name]) => fields[name])
    for (const [name, message] of known) setError(fields[name], { message })
    if (known.length === 0) setBanner(error)
  }

  const select = (name: 'divisionId' | 'parentId' | 'departmentId', fieldLabel: string, from: LevelKey, optional: boolean, hint?: string) => (
    <Controller control={control} name={name} render={({ field }) => (
      <SelectField label={fieldLabel} placeholder={t('companies.yourField', { field: fieldLabel.toLowerCase() })} optional={optional}
        hint={hint} error={errors[name]?.message} options={options(from, name === 'parentId' ? item?.id : undefined)} {...field} />
    )} />
  )

  return (
    <Drawer open title={t(item ? `structure.${level}.editTitle` : `structure.${level}.add`)} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{item ? t('locations.save') : t(`structure.${level}.add`)}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <TextField label={t(`structure.${level}.nameLabel`)} placeholder={t(`structure.${level}.namePlaceholder`)} maxLength={120}
          error={errors.name?.message} {...register('name')} />
        <TextField label={t('locations.code')} placeholder={t('structure.codePlaceholder')} optional hint={t('structure.codeHint')}
          maxLength={20} autoCapitalize="characters" error={errors.code?.message} {...register('code')} />
        {level === 'departments' && <>
          {select('divisionId', t('structure.division'), 'divisions', true)}
          {select('parentId', t('structure.partOf'), 'departments', true, t('structure.partOfHint'))}
        </>}
        {level === 'positions' && <>
          {select('departmentId', t('structure.department'), 'departments', true, t('structure.sharedHint'))}
        </>}
        {level === 'units' && select('departmentId', t('structure.department'), 'departments', false)}
        {companies.length > 1 && (
          <div className="flex flex-col gap-1.5">
            <Controller control={control} name="applies" render={({ field }) => (
              <CompanySelect label={t('locations.appliesTo')} value={field.value} onChange={field.onChange}
                companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
            )} />
            {errors.applies?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.applies.message}</p>}
          </div>
        )}
      </form>
    </Drawer>
  )
}

/**
 * Uploading one level (ONB-05 part 2) with the shared BulkUpload: the template, a review of every
 * row (up to 5,000, 100 a page), and, with several companies, which companies they serve. Rows name
 * their division or department; departments may name one further up or down the same file.
 */
function UploadLevel({ level, all, companies, onClose }: { level: LevelKey; all: AllLevels; companies: Company[]; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [applies, setApplies] = useState<CompanySelection>({ all: true, ids: [] })
  const names = (key: LevelKey) => (all[key].data ?? []).map(i => ({ value: label(i), label: label(i) }))
  const name = { key: 'name', label: t(`structure.${level}.nameLabel`) }
  const code = { key: 'code', label: t('locations.code') }
  const department = { key: 'department', label: t('structure.department'), options: names('departments') }
  const columns = {
    divisions: [name, code],
    departments: [name, code, { key: 'division', label: t('structure.division'), options: names('divisions') }, { key: 'parent', label: t('structure.partOf'), options: names('departments') }],
    positions: [name, code, department],
    units: [name, code, department],
  }[level]
  type Path = '/api/structure/departments/import'

  const downloadTemplate = async () => {
    const { data } = await api.GET(`/api/structure/${level}/template` as '/api/structure/departments/template', { parseAs: 'blob' })
    if (!data) throw new Error('template')
    const url = URL.createObjectURL(data)
    const link = document.createElement('a')
    link.href = url
    link.download = `pesaroll-${level}-template.xlsx`
    link.click()
    URL.revokeObjectURL(url)
  }

  const read = async (file: File) => {
    const { data, error } = await api.POST(`/api/structure/${level}/import/read` as '/api/structure/departments/import/read', {
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
    const { data, error } = await api.POST(`/api/structure/${level}/import/check` as '/api/structure/departments/import/check', { body: { rows } as never })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('check')
  }

  const importRows = async (rows: BulkUploadRow[]) => {
    // With one company the rows are simply its own.
    const body = companies.length === 1
      ? { rows, allCompanies: false, companyIds: [companies[0].id] }
      : { rows, allCompanies: applies.all, companyIds: applies.all ? [] : applies.ids }
    const { data, error } = await api.POST(`/api/structure/${level}/import` as Path, { body: body as never })
    if (data) {
      void queryClient.invalidateQueries({ queryKey: ['structure'] })
      toast.show(t(`structure.${level}.imported`, { count: data.imported }))
      return null
    }
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('import')
  }

  return (
    <BulkUpload open title={t(`structure.${level}.upload`)} columns={columns} onClose={onClose} onDownloadTemplate={downloadTemplate}
      onRead={read} onCheck={check} onImport={importRows}
      labels={{
        review: count => t(`structure.${level}.review`, { count }),
        importButton: count => t(`structure.${level}.importButton`, { count }),
        templateHint: t(`structure.${level}.templateHint`),
      }}>
      {companies.length > 1 && (
        <div className="max-w-sm">
          <CompanySelect label={t('locations.applyTo')} value={applies} onChange={setApplies}
            companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
        </div>
      )}
    </BulkUpload>
  )
}
