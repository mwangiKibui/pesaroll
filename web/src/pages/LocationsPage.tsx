import { useNavigate } from '@tanstack/react-router'
import { useId, useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { BulkUpload } from '../components/BulkUpload'
import { Button, IconButton } from '../components/Button'
import { CompanySelect, type CompanySelection } from '../components/CompanySelect'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Drawer } from '../components/Drawer'
import { EmptyState, FormErrorBanner, Loading, Notice } from '../components/feedback'
import { Checkbox, SelectField, TextField } from '../components/fields'
import { TableCard } from '../components/TableCard'
import { useToast } from '../components/toast'
import i18n from '../i18n'
import { useCompanies, useCountries, useLocations, useSetupDone } from '../setup/hooks'
import { Proceed } from '../setup/Proceed'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type Country = components['schemas']['CountryResponse']
type Location = components['schemas']['LocationResponse']
type Text = components['schemas']['LocalizedText']

interface Values {
  name: string
  kind: string
  code: string
  regionCode: string
  town: string
  addressLine: string
  applies: CompanySelection
  mainFor: string[]
}

const local = (text: Text) => (i18n.language === 'sw' ? text.sw : text.en)
const none = <span className="text-muted">—</span>

/** The types a location can be (same list as the API). */
const locationKinds = ['head_office', 'branch', 'office', 'warehouse', 'site'] as const

/** The companies a location serves: all of them, or the chosen ones. */
const served = (all: boolean, ids: string[], companies: Company[]) => (all ? companies : companies.filter(c => ids.includes(c.id)))

/**
 * ONB-04: where people work. Each company already has its Head office (made with the company) as
 * main location; more locations serve one, several or all companies. The county (and the town, from
 * which the API works out the minimum-wage area) drive minimum-wage checks and public holidays later.
 */
export default function LocationsPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const companies = useCompanies()
  const locations = useLocations()
  const countries = useCountries()
  const done = useSetupDone(true)
  const [shown, setShown] = useState<CompanySelection>({ all: true, ids: [] })
  const [editing, setEditing] = useState<Location | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Location | null>(null)
  const [busy, setBusy] = useState(false)
  const [uploading, setUploading] = useState(false)

  const ready = companies.data && locations.data && countries.data
  const list = (locations.data ?? []).filter(l => shown.all || l.allCompanies || l.companyIds.some(id => shown.ids.includes(id)))
  const country = countries.data?.find(c => c.code === companies.data?.[0]?.countryCode) ?? countries.data?.[0]
  const companyNames = (ids: string[]) => (companies.data ?? []).filter(c => ids.includes(c.id)).map(c => c.legalName).join(', ')

  const remove = async () => {
    if (!deleting) return
    setBusy(true)
    try {
      const { error, response } = await api.DELETE('/api/locations/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        queryClient.setQueryData<Location[]>(['locations'], old => old?.filter(l => l.id !== deleting.id))
        toast.show(t('locations.deleted'))
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

  const addButton = <>
    <Button variant="secondary" onClick={() => setUploading(true)}>{t('locations.upload')}</Button>
    <Button onClick={() => setEditing('new')}>{t('locations.add')}</Button>
  </>

  return (
    <SetupLayout current="locations" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('locations.title')}</h1>
        <p className="m-0 text-muted">{t('locations.intro')}</p>
      </div>
      {!ready || !country ? <Loading rows={3} /> : <>
        {companies.data.length > 1 && (
          <div className="max-w-sm">
            <CompanySelect label={t('locations.show')} value={shown} onChange={setShown}
              companies={companies.data.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
          </div>
        )}
        <TableCard title={t('locations.tableTitle')} count={list.length} actions={addButton}>
          {list.length === 0 ? <EmptyState title={t('locations.emptyTitle')}>{t('locations.emptyText')}</EmptyState> : (
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('locations.location')}</th>
                  <th scope="col">{t('locations.kind')}</th>
                  <th scope="col">{t('locations.townAndCounty', { region: local(country.regionLabel).toLowerCase() })}</th>
                  <th scope="col">{t('locations.appliesTo')}</th>
                  <th scope="col">{t('locations.mainFor')}</th>
                  <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {list.map(l => {
                  const region = country.regions.find(r => r.code === l.regionCode)?.name
                  return (
                    <tr key={l.id}>
                      <td>
                        <span className="block font-medium text-ink">{l.name}</span>
                        <span className="block font-mono text-hint text-muted">{l.code}</span>
                      </td>
                      <td>{t(`locations.kinds.${l.kind}`)}</td>
                      <td>{[l.town, region].filter(Boolean).join(', ') || none}</td>
                      <td>{l.allCompanies ? t('locations.allCompanies') : companyNames(l.companyIds) || none}</td>
                      <td>{companyNames(l.mainFor) || none}</td>
                      <td className="whitespace-nowrap text-right">
                        <IconButton label={t('locations.edit', { name: l.name })} onClick={() => setEditing(l)}>
                          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                        </IconButton>
                        <IconButton tone="danger" label={t('locations.delete', { name: l.name })} onClick={() => setDeleting(l)}>
                          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                        </IconButton>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </TableCard>
        <Proceed label={t('setup.proceed', { step: t('setup.steps.structure') })} onProceed={() => void navigate({ to: '/setup/structure' })} />

        {uploading && <UploadLocations companies={companies.data} country={country} onClose={() => setUploading(false)} />}
        {editing && (
          <LocationDrawer location={editing === 'new' ? null : editing} companies={companies.data} country={country}
            // A new location serves the companies being shown, as the selector suggests.
            defaultApplies={companies.data.length === 1 ? { all: false, ids: [companies.data[0].id] } : shown}
            onClose={() => setEditing(null)} />
        )}
      </>}
      <ConfirmDialog open={!!deleting} title={t('locations.deleteTitle', { name: deleting?.name })}
        confirmLabel={t('locations.deleteConfirm')} busy={busy} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('locations.deleteText')}
      </ConfirmDialog>
    </SetupLayout>
  )
}

function LocationDrawer({ location, companies, country, defaultApplies, onClose }: {
  location: Location | null
  companies: Company[]
  country: Country
  defaultApplies: CompanySelection
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const regionLabel = local(country.regionLabel)

  const { register, handleSubmit, control, setError, formState: { errors, isSubmitting } } = useForm<Values>({
    defaultValues: {
      name: location?.name ?? '',
      kind: location?.kind ?? 'branch',
      code: location?.code ?? '',
      regionCode: location?.regionCode ?? '',
      town: location?.town ?? '',
      addressLine: location?.addressLine ?? '',
      applies: location ? { all: location.allCompanies, ids: location.allCompanies ? companies.map(c => c.id) : location.companyIds } : defaultApplies,
      mainFor: location?.mainFor ?? [],
    },
  })
  const applies = useWatch({ control, name: 'applies' })
  const serves = served(applies.all, applies.ids, companies)

  // The same checks as the API, so most mistakes show before anything is sent.
  const check = (v: Values) => {
    const problems: [FieldPath<Values>, string][] = []
    if (v.name.trim().length < 2 || v.name.trim().length > 120) problems.push(['name', t('locations.nameInvalid')])
    if (v.code.trim() && !/^[A-Z0-9][A-Z0-9-]{1,19}$/.test(v.code.trim().toUpperCase())) problems.push(['code', t('locations.codeInvalid')])
    if (!v.regionCode) problems.push(['regionCode', t('locations.regionRequired', { region: regionLabel.toLowerCase() })])
    if (!v.applies.all && v.applies.ids.length === 0) problems.push(['applies', t('locations.companiesRequired')])
    for (const [name, message] of problems) setError(name, { message })
    return problems.length === 0
  }

  const onSubmit = async (v: Values) => {
    setBanner(null)
    if (!check(v)) return
    const body = {
      code: v.code.trim() ? v.code.trim().toUpperCase() : null,
      name: v.name.trim(),
      kind: v.kind,
      regionCode: v.regionCode,
      town: v.town.trim() || null,
      addressLine: v.addressLine.trim() || null,
      wageAreaCode: null,   // the API works it out from the town
      allCompanies: v.applies.all,
      companyIds: v.applies.all ? [] : v.applies.ids,
      // Only companies it still serves can have it as main.
      mainFor: v.mainFor.filter(id => served(v.applies.all, v.applies.ids, companies).some(c => c.id === id)),
    }
    try {
      const { data, error } = location
        ? await api.PATCH('/api/locations/{id}', { params: { path: { id: location.id } }, body })
        : await api.POST('/api/locations', { body })
      if (data) {
        // Making it main moves other companies' mains, so the whole list is fetched again.
        void queryClient.invalidateQueries({ queryKey: ['locations'] })
        toast.show(location ? t('locations.saved') : t('locations.added'))
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
    if (error.code === 'ONB-3513') return setError('code', { message: errorMessage(t, error) })
    const fields: Record<string, FieldPath<Values>> = {
      name: 'name', kind: 'kind', code: 'code', regionCode: 'regionCode', town: 'town', companyIds: 'applies', mainFor: 'mainFor',
    }
    const known = Object.entries(error.fields ?? {}).filter(([name]) => fields[name])
    for (const [name, message] of known) setError(fields[name], { message })
    if (known.length === 0) setBanner(error)
  }

  return (
    <Drawer open title={location ? t('locations.editTitle') : t('locations.addTitle')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{location ? t('locations.save') : t('locations.add')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <TextField label={t('locations.name')} placeholder={t('locations.namePlaceholder')} maxLength={120}
          error={errors.name?.message} {...register('name')} />
        <Controller control={control} name="kind" render={({ field }) => (
          <SelectField label={t('locations.kind')} placeholder={t('locations.kindPlaceholder')} error={errors.kind?.message}
            options={locationKinds.map(k => ({ value: k, label: t(`locations.kinds.${k}`) }))} {...field} />
        )} />
        <TextField label={t('locations.code')} placeholder={t('locations.codePlaceholder')} optional hint={t('locations.codeHint')}
          maxLength={20} autoCapitalize="characters" error={errors.code?.message} {...register('code')} />
        <Controller control={control} name="regionCode" render={({ field }) => (
          <SelectField label={regionLabel} placeholder={t('companies.yourField', { field: regionLabel.toLowerCase() })}
            error={errors.regionCode?.message} options={country.regions.map(r => ({ value: r.code, label: r.name }))} {...field} />
        )} />
        <TextField label={t('companies.town')} placeholder={t('companies.townPlaceholder')} optional maxLength={100}
          error={errors.town?.message} {...register('town')} />
        <TextField label={t('companies.addressLine')} placeholder={t('companies.addressLinePlaceholder')} optional maxLength={200}
          {...register('addressLine')} />

        {companies.length > 1 && (
          <div className="flex flex-col gap-1.5">
            <Controller control={control} name="applies" render={({ field }) => (
              <CompanySelect label={t('locations.appliesTo')} value={field.value} onChange={field.onChange}
                companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
            )} />
            {errors.applies?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.applies.message}</p>}
          </div>
        )}
        <fieldset className="m-0 flex flex-col gap-1 border-0 p-0">
          <legend className="mb-1 p-0 text-label font-medium text-ink">{t('locations.mainFor')}</legend>
          <p className="m-0 text-hint text-muted">{t('locations.mainHint')}</p>
          <Controller control={control} name="mainFor" render={({ field }) => <>
            {serves.map(c => (
              <Checkbox key={c.id} label={t('locations.mainForCompany', { name: c.legalName })} checked={field.value.includes(c.id)}
                onChange={e => field.onChange(e.target.checked ? [...field.value, c.id] : field.value.filter(id => id !== c.id))} />
            ))}
          </>} />
          {errors.mainFor?.message && <p className="m-0 text-hint font-medium text-danger-ink">{errors.mainFor.message}</p>}
        </fieldset>
      </form>
    </Drawer>
  )
}

type ImportRow = components['schemas']['ImportRow']

/**
 * Uploading locations (ONB-04 part 2) with the shared BulkUpload: the template, then a review of
 * every row of the file (editable, with the problems in their cells) and, with several companies,
 * which companies the new locations serve.
 */
function UploadLocations({ companies, country, onClose }: { companies: Company[]; country: Country; onClose: () => void }) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const [applies, setApplies] = useState<CompanySelection>({ all: true, ids: [] })

  // The template's columns; list columns hold the same words as its dropdowns.
  const columns = [
    { key: 'name', label: t('locations.name') },
    { key: 'kind', label: t('locations.kind'), options: locationKinds.map(k => ({ value: t(`locations.kinds.${k}`, { lng: 'en' }), label: t(`locations.kinds.${k}`) })) },
    { key: 'code', label: t('locations.code') },
    { key: 'county', label: local(country.regionLabel), options: country.regions.map(r => ({ value: r.name, label: r.name })) },
    { key: 'town', label: t('companies.town') },
    { key: 'address', label: t('locations.addressColumn') },
    { key: 'main', label: t('locations.mainColumn'), options: [{ value: 'Yes', label: t('locations.yes') }, { value: 'No', label: t('locations.no') }] },
  ]

  const downloadTemplate = async () => {
    const { data } = await api.GET('/api/locations/template', { parseAs: 'blob' })
    if (!data) throw new Error('template')
    const url = URL.createObjectURL(data)
    const link = document.createElement('a')
    link.href = url
    link.download = 'pesaroll-locations-template.xlsx'
    link.click()
    URL.revokeObjectURL(url)
  }

  const read = async (file: File) => {
    const { data, error } = await api.POST('/api/locations/import/read', {
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
    const { data, error } = await api.POST('/api/locations/import/check', { body: { rows } })
    if (data) return data
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('check')
  }

  const importRows = async (rows: ImportRow[]) => {
    // With one company the locations are simply its own.
    const body = companies.length === 1
      ? { rows, allCompanies: false, companyIds: [companies[0].id] }
      : { rows, allCompanies: applies.all, companyIds: applies.all ? [] : applies.ids }
    const { data, error } = await api.POST('/api/locations/import', { body })
    if (data) {
      void queryClient.invalidateQueries({ queryKey: ['locations'] })
      toast.show(t('locations.imported', { count: data.imported }))
      return null
    }
    if (isApiError(error)) return { ...error, message: errorMessage(t, error) }
    throw new Error('import')
  }

  return (
    <BulkUpload open title={t('locations.upload')} columns={columns} onClose={onClose} onDownloadTemplate={downloadTemplate}
      onRead={read} onCheck={check} onImport={importRows}
      labels={{
        review: count => t('locations.review', { count }),
        importButton: count => t('locations.importButton', { count }),
        templateHint: t('locations.templateHint'),
      }}>
      {companies.length > 1 && <>
        <div className="max-w-sm">
          <CompanySelect label={t('locations.applyTo')} value={applies} onChange={setApplies}
            companies={companies.map(c => ({ id: c.id, name: c.legalName, code: c.countryCode }))} />
        </div>
        <p className="m-0 text-hint text-muted">{t('locations.importMainHint')}</p>
      </>}
    </BulkUpload>
  )
}
