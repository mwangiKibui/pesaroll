import { zodResolver } from '@hookform/resolvers/zod'
import { useNavigate } from '@tanstack/react-router'
import { useId, useState } from 'react'
import { Controller, useForm, useWatch, type FieldPath, type Resolver } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import type { components } from '../api/schema'
import { errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button, IconButton } from '../components/Button'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Drawer } from '../components/Drawer'
import { EmptyState, FormErrorBanner, Loading, Notice, Tag } from '../components/feedback'
import { SelectField, TextField } from '../components/fields'
import { TableCard } from '../components/TableCard'
import { useToast } from '../components/toast'
import { PaymentAccounts } from './PaymentAccounts'
import i18n from '../i18n'
import { useCompanies, useCountries, useIndustries, useOrganization, useSetupDone } from '../setup/hooks'
import { SetupLayout } from '../setup/SetupLayout'

type Company = components['schemas']['CompanyResponse']
type Country = components['schemas']['CountryResponse']
type Industry = components['schemas']['IndustryResponse']
type Text = components['schemas']['LocalizedText']

interface Values {
  legalName: string
  tradingName: string
  countryCode: string
  industryCode: string
  addressLine: string
  town: string
  regionCode: string
  postalAddress: string
  postalCode: string
  phone: string
  email: string
  identifiers: Record<string, string>
}

const local = (text: Text) => (i18n.language === 'sw' ? text.sw : text.en)

/**
 * ONB-03: the companies that employ people and pay them (payroll runs per company). The numbers a
 * company records come from its country, so they aren't hard-coded here. Missing numbers don't
 * stop setup; each company shows what it still needs before its first payroll.
 */
export default function CompaniesPage() {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const navigate = useNavigate()
  const organization = useOrganization()
  const companies = useCompanies()
  const countries = useCountries()
  const industries = useIndustries()
  const done = useSetupDone(true)
  const [editing, setEditing] = useState<Company | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Company | null>(null)
  const [busyDeleting, setBusyDeleting] = useState(false)
  const [needOne, setNeedOne] = useState(false)

  const list = companies.data ?? []
  const ready = companies.data && countries.data && industries.data && organization.data

  const remove = async () => {
    if (!deleting) return
    setBusyDeleting(true)
    try {
      const { error, response } = await api.DELETE('/api/companies/{id}', { params: { path: { id: deleting.id } } })
      if (response.ok) {
        queryClient.setQueryData<Company[]>(['companies'], old => old?.filter(c => c.id !== deleting.id))
        toast.show(t('companies.deleted'))
      } else {
        toast.show(isApiError(error) ? errorMessage(t, error) : t('apiErrors.network'), 'danger')
      }
    } catch {
      toast.show(t('apiErrors.network'), 'danger')
    } finally {
      setBusyDeleting(false)
      setDeleting(null)
    }
  }

  const proceed = () => {
    if (list.length === 0) {
      setNeedOne(true)
      return
    }
    void navigate({ to: '/' })
  }

  const addButton = <Button onClick={() => { setNeedOne(false); setEditing('new') }}>{t('companies.add')}</Button>

  return (
    <SetupLayout current="companies" done={done} hasOrganization>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('companies.title')}</h1>
        <p className="m-0 text-muted">{t('companies.intro')}</p>
      </div>
      {needOne && list.length === 0 && <Notice>{t('companies.needOne')}</Notice>}
      {list.some(c => !c.readiness.ready) && <Notice>{t('companies.missingNotice')}</Notice>}
      {!ready ? <Loading rows={3} /> : list.length === 0 ? (
        <section className="rounded-card border border-line bg-card">
          <EmptyState title={t('companies.emptyTitle')} action={addButton}>{t('companies.emptyText')}</EmptyState>
        </section>
      ) : (
        <TableCard title={t('companies.tableTitle')} count={list.length} actions={addButton}>
          <table>
            <thead>
              <tr>
                <th scope="col">{t('companies.company')}</th>
                <th scope="col">{t('companies.country')}</th>
                <th scope="col">{t('companies.industry')}</th>
                <th scope="col">{t('companies.location')}</th>
                <th scope="col">{t('companies.contact')}</th>
                <th scope="col">{t('companies.readiness')}</th>
                <th scope="col"><span className="sr-only">{t('companies.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {list.map(c => (
                <tr key={c.id}>
                  <td>
                    <span className="block font-medium text-ink">{c.legalName}</span>
                    {c.tradingName && <span className="block text-hint text-muted">{c.tradingName}</span>}
                  </td>
                  <td>{countryName(countries.data, c.countryCode)}</td>
                  <td>{industryName(industries.data, c.industryCode) ?? none}</td>
                  <td>{location(countries.data, c) ?? none}</td>
                  <td>
                    {c.phone || c.email ? <>
                      {c.phone && <span className="block whitespace-nowrap">{c.phone}</span>}
                      {c.email && <span className="block text-hint text-muted">{c.email}</span>}
                    </> : none}
                  </td>
                  <td>
                    {c.readiness.ready
                      ? <Tag tone="brand">{t('companies.ready')}</Tag>
                      : <Tag tone="warning">{t('companies.missing', { items: c.readiness.missing.map(m => local(m.label)).join(', ') })}</Tag>}
                  </td>
                  <td className="whitespace-nowrap text-right">
                    <IconButton label={t('companies.edit', { name: c.legalName })} onClick={() => setEditing(c)}>
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4" /></svg>
                    </IconButton>
                    <IconButton tone="danger" label={t('companies.delete', { name: c.legalName })} onClick={() => setDeleting(c)}>
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" /></svg>
                    </IconButton>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </TableCard>
      )}
      <div><Button onClick={proceed}>{t('companies.continue')}</Button></div>

      {ready && editing && (
        <CompanyDrawer company={editing === 'new' ? null : editing} countries={countries.data} industries={industries.data}
          // The first company is usually the organization itself, so it starts with its name.
          suggestedName={list.length === 0 ? organization.data.name : ''}
          onClose={() => setEditing(null)} />
      )}
      <ConfirmDialog open={!!deleting} title={t('companies.deleteTitle', { name: deleting?.legalName })}
        confirmLabel={t('companies.deleteConfirm')} busy={busyDeleting} onConfirm={remove} onCancel={() => setDeleting(null)}>
        {t('companies.deleteText')}
      </ConfirmDialog>
    </SetupLayout>
  )
}

const none = <span className="text-muted">—</span>

function industryName(industries: Industry[] | undefined, code: string | null) {
  const industry = industries?.find(i => i.code === code)
  return industry ? local(industry.name) : null
}

/** "Nairobi, Nairobi": the town, then the county (or the country's region). */
function location(countries: Country[] | undefined, c: Company) {
  const region = countries?.find(x => x.code === c.countryCode)?.regions.find(r => r.code === c.regionCode)?.name
  return [c.town, region].filter(Boolean).join(', ') || null
}

function countryName(countries: Country[] | undefined, code: string) {
  const country = countries?.find(c => c.code === code)
  return country ? local(country.name) : code
}

const formFields: FieldPath<Values>[] = ['legalName', 'tradingName', 'countryCode', 'industryCode', 'addressLine', 'town',
  'regionCode', 'postalAddress', 'postalCode', 'phone', 'email']

function CompanyDrawer({ company, countries, industries, suggestedName, onClose }: {
  company: Company | null
  countries: Country[]
  industries: Industry[]
  suggestedName: string
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { api, queryClient } = useServices()
  const toast = useToast()
  const formId = useId()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const countryOf = (code: string) => countries.find(c => c.code === code) ?? countries[0]

  // The same checks as the API: the numbers' formats come from the chosen country.
  const schemaFor = (country: Country) => {
    const optional = (max: number) => z.string().trim().max(max)
    return z.object({
      legalName: z.string().trim().min(2, t('validation.companyName')).max(160, t('validation.companyName')),
      tradingName: optional(160),
      countryCode: z.string(),
      industryCode: z.string(),
      addressLine: optional(200),
      town: optional(100),
      regionCode: z.string(),
      postalAddress: optional(100),
      postalCode: optional(20),
      phone: z.string().trim().refine(v => !v || /^\+?[0-9 ()-]{7,20}$/.test(v), t('validation.phone')),
      email: z.string().trim().refine(v => !v || z.email().safeParse(v).success, t('validation.email')),
      identifiers: z.object(Object.fromEntries(country.fields.map(f => [f.key, z.string().trim().toUpperCase().refine(
        v => !v || (v.length <= 60 && (!f.pattern || new RegExp(f.pattern).test(v))),
        f.example ? t('validation.numberExample', { label: local(f.label), example: f.example }) : t('validation.number', { label: local(f.label) }),
      )]))),
    })
  }

  const { register, handleSubmit, control, setError, formState: { errors, isSubmitting } } = useForm<Values>({
    resolver: ((values, context, options) => zodResolver(schemaFor(countryOf(values.countryCode)))(values, context, options as never)) as Resolver<Values>,
    defaultValues: {
      legalName: company?.legalName ?? suggestedName,
      tradingName: company?.tradingName ?? '',
      countryCode: company?.countryCode ?? countries[0].code,
      industryCode: company?.industryCode ?? '',
      addressLine: company?.addressLine ?? '',
      town: company?.town ?? '',
      regionCode: company?.regionCode ?? '',
      postalAddress: company?.postalAddress ?? '',
      postalCode: company?.postalCode ?? '',
      phone: company?.phone ?? '',
      email: company?.email ?? '',
      identifiers: Object.fromEntries(countries.flatMap(c => c.fields).map(f => [f.key, company?.identifiers[f.key] ?? ''])),
    },
  })
  const country = countryOf(useWatch({ control, name: 'countryCode' }))
  const regionLabel = local(country.regionLabel)

  const onSubmit = async (values: Values) => {
    setBanner(null)
    // Only this country's numbers; an empty one clears it.
    const body = { ...values, identifiers: Object.fromEntries(country.fields.map(f => [f.key, values.identifiers[f.key] ?? ''])) }
    try {
      const { data, error } = company
        ? await api.PATCH('/api/companies/{id}', { params: { path: { id: company.id } }, body })
        : await api.POST('/api/companies', { body })
      if (data) {
        queryClient.setQueryData<Company[]>(['companies'], old =>
          company ? old?.map(c => (c.id === data.id ? data : c)) : [...(old ?? []), data])
        toast.show(company ? t('companies.saved') : t('companies.added'))
        onClose()
        return
      }
      showError(error)
    } catch {
      setBanner('network')
    }
  }

  const showError = (error: unknown) => {
    if (!isApiError(error)) {
      setBanner('network')
      return
    }
    if (error.code === 'ONB-3505' || error.code === 'ONB-3508') {
      setError('legalName', { message: errorMessage(t, error) })
      return
    }
    if (error.code === 'ONB-3506') {
      setError('countryCode', { message: errorMessage(t, error) })
      return
    }
    const known = [...formFields, ...country.fields.map(f => `identifiers.${f.key}`)]
    const fields = Object.entries(error.fields ?? {})
    for (const [name, message] of fields.filter(([name]) => known.includes(name))) {
      setError(name as FieldPath<Values>, { message })
    }
    // Problems we can't show under a field go in the banner, with the code and reference.
    if (fields.length === 0 || fields.some(([name]) => !known.includes(name))) setBanner(error)
  }

  const err = (name: Exclude<keyof Values, 'identifiers'>) => errors[name]?.message
  const section = 'm-0 border-t border-line pt-4 text-label font-semibold text-ink first:border-t-0 first:pt-0'

  return (
    <Drawer open title={company ? t('companies.editTitle') : t('companies.addTitle')} onClose={onClose}
      footer={<>
        <Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button>
        <Button type="submit" form={formId} busy={isSubmitting}>{company ? t('companies.save') : t('companies.add')}</Button>
      </>}>
      <form id={formId} noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}

        <h3 className={section}>{t('companies.details')}</h3>
        <Controller control={control} name="countryCode" render={({ field }) => (
          <SelectField label={t('companies.country')} placeholder={t('companies.countryPlaceholder')} hint={t('companies.countryHint')}
            error={err('countryCode')} options={countries.map(c => ({ value: c.code, label: local(c.name) }))} {...field} />
        )} />
        <TextField label={t('companies.legalName')} placeholder={t('companies.legalNamePlaceholder')} hint={t('companies.legalNameHint')}
          maxLength={160} error={err('legalName')} {...register('legalName')} />
        <TextField label={t('companies.tradingName')} placeholder={t('companies.tradingNamePlaceholder')} optional
          maxLength={160} error={err('tradingName')} {...register('tradingName')} />
        <Controller control={control} name="industryCode" render={({ field }) => (
          <SelectField label={t('companies.industry')} placeholder={t('companies.industryPlaceholder')} optional error={err('industryCode')}
            options={industries.map(i => ({ value: i.code, label: local(i.name) }))} {...field} />
        )} />

        <h3 className={section}>{t('companies.numbers')}</h3>
        {country.fields.map(f => (
          <TextField key={`${country.code}-${f.key}`} label={local(f.label)} placeholder={t('companies.yourField', { field: local(f.label) })}
            optional={!f.requiredForPayroll} maxLength={60} autoCapitalize="characters"
            hint={[f.hint && local(f.hint), f.example && t('companies.example', { example: f.example }), f.requiredForPayroll && t('companies.neededForPayroll')].filter(Boolean).join(' ') || undefined}
            error={errors.identifiers?.[f.key]?.message} {...register(`identifiers.${f.key}`)} />
        ))}

        <h3 className={section}>{t('companies.addressAndContact')}</h3>
        <TextField label={t('companies.addressLine')} placeholder={t('companies.addressLinePlaceholder')} optional
          autoComplete="street-address" maxLength={200} error={err('addressLine')} {...register('addressLine')} />
        <TextField label={t('companies.town')} placeholder={t('companies.townPlaceholder')} optional
          maxLength={100} error={err('town')} {...register('town')} />
        <Controller control={control} name="regionCode" render={({ field }) => (
          <SelectField label={regionLabel} placeholder={t('companies.yourField', { field: regionLabel.toLowerCase() })} optional
            error={err('regionCode')} options={country.regions.map(r => ({ value: r.code, label: r.name }))} {...field} />
        )} />
        <TextField label={t('companies.postalAddress')} placeholder={t('companies.postalAddressPlaceholder')} optional
          maxLength={100} error={err('postalAddress')} {...register('postalAddress')} />
        <TextField label={t('companies.postalCode')} placeholder={t('companies.postalCodePlaceholder')} optional
          autoComplete="postal-code" maxLength={20} error={err('postalCode')} {...register('postalCode')} />
        <TextField label={t('companies.phone')} placeholder={t('companies.phonePlaceholder')} optional type="tel"
          autoComplete="tel" maxLength={20} error={err('phone')} {...register('phone')} />
        <TextField label={t('companies.email')} placeholder={t('companies.emailPlaceholder')} optional type="email"
          autoComplete="email" maxLength={254} error={err('email')} {...register('email')} />
      </form>
      {company ? <PaymentAccounts company={company} /> : (
        <section className="flex flex-col gap-1.5 border-t border-line pt-4">
          <h3 className="m-0 text-label font-semibold text-ink">{t('payingAccounts.title')}</h3>
          <p className="m-0 text-hint text-muted">{t('payingAccounts.saveFirst')}</p>
        </section>
      )}
    </Drawer>
  )
}
