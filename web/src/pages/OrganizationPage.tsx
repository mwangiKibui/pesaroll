import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { useState, useSyncExternalStore } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { errorKey, errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button } from '../components/Button'
import { FormErrorBanner, Loading, Notice } from '../components/feedback'
import { TextField } from '../components/fields'
import { useToast } from '../components/toast'
import { SetupLayout } from '../setup/SetupLayout'

// Same rule as the API and database (ONB-3501).
const schema = z.object({ name: z.string().trim().min(2, 'validation.orgName').max(120, 'validation.orgName') })
type Values = z.infer<typeof schema>

/**
 * ONB-02: name the organization (can't be skipped; the person is recorded as onboarding it).
 * Once it exists, the step says it is set up and keeps the name in the field so a typo can be
 * fixed (only by the person who onboarded it, for now); the navbar offers the dashboard.
 */
export default function OrganizationPage() {
  const { t } = useTranslation()
  const { api, auth } = useServices()
  const organizationId = useSyncExternalStore(auth.subscribe, auth.organizationId)
  const organization = useQuery({
    queryKey: ['organization'],
    enabled: !!organizationId,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/organizations/current')
      if (error || !data) throw new Error('organization')
      return data
    },
  })
  return (
    <SetupLayout current="organization" done={organizationId ? ['organization'] : []} hasOrganization={!!organizationId}>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('organization.title')}</h1>
        <p className="m-0 text-muted">{t('organization.intro')}</p>
      </div>
      {organizationId
        ? <Notice tone="brand"><strong>{t('organization.createdTitle')}</strong> {t('organization.createdText')}</Notice>
        : <Notice tone="brand"><strong>{t('organization.noOrgTitle')}</strong> {t('organization.noOrgText')}</Notice>}
      {organizationId && !organization.data
        ? <Loading rows={2} />
        : <NameForm key={organization.data?.id ?? 'new'} existing={organization.data} />}
    </SetupLayout>
  )
}

function NameForm({ existing }: { existing?: { id: string; name: string } }) {
  const { t } = useTranslation()
  const { api, auth, queryClient } = useServices()
  const toast = useToast()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: existing?.name ?? '' },
  })

  const onSubmit = async ({ name }: Values) => {
    setBanner(null)
    try {
      if (existing) {
        const { data, error } = await api.PATCH('/api/organizations/current', { body: { name } })
        if (data) {
          queryClient.setQueryData(['organization'], data)
          toast.show(t('organization.saved'))
          return
        }
        showError(error)
        return
      }
      const { data, error } = await api.POST('/api/organizations', { body: { name } })
      if (data) {
        // The new token carries the organization; this step then shows it as set up.
        queryClient.setQueryData(['organization'], data.organization)
        auth.setAccessToken(data.token.accessToken, { broadcast: true })
        return
      }
      showError(error)
    } catch {
      setBanner('network')
    }
  }

  const showError = (error: unknown) => {
    if (isApiError(error) && error.code === 'ONB-3501') setError('name', { message: errorKey(error.code) })
    else if (isApiError(error)) setBanner(error)
    else setBanner('network')
  }

  return (
    <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4 rounded-card border border-line bg-card p-5 md:p-6">
      {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
      {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
      <TextField label={t('organization.name')} placeholder={t('organization.namePlaceholder')} hint={t('organization.nameHint')}
        autoComplete="organization" maxLength={120}
        error={errors.name?.message ? t(errors.name.message) : undefined} {...register('name')} />
      <div><Button type="submit" busy={isSubmitting}>{existing ? t('organization.save') : t('organization.submit')}</Button></div>
    </form>
  )
}
