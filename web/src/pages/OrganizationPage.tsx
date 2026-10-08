import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { errorKey, errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button } from '../components/Button'
import { FormErrorBanner, Notice } from '../components/feedback'
import { TextField } from '../components/fields'
import { SetupLayout } from '../setup/SetupLayout'

// Same rule as the API and database (ONB-3501).
const schema = z.object({ name: z.string().trim().min(2, 'validation.orgName').max(120, 'validation.orgName') })
type Values = z.infer<typeof schema>

/** ONB-02: name the organization. Can't be skipped. The person who does it is recorded as onboarding it. */
export default function OrganizationPage() {
  const { t } = useTranslation()
  const { api, auth } = useServices()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: '' },
  })

  const onSubmit = async ({ name }: Values) => {
    setBanner(null)
    try {
      const { data, error } = await api.POST('/api/organizations', { body: { name } })
      if (data) {
        // The new token carries the organization; the page guard then moves the person home.
        auth.setAccessToken(data.token.accessToken, { broadcast: true })
        return
      }
      if (isApiError(error) && error.code === 'ONB-3501') setError('name', { message: errorKey(error.code) })
      else if (isApiError(error)) setBanner(error)
      else setBanner('network')
    } catch {
      setBanner('network')
    }
  }

  return (
    <SetupLayout current="organization" done={[]}>
      <div className="flex flex-col gap-1.5">
        <h1 className="m-0 font-display text-title text-ink">{t('organization.title')}</h1>
        <p className="m-0 text-muted">{t('organization.intro')}</p>
      </div>
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4 rounded-card border border-line bg-card p-5">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <TextField label={t('organization.name')} placeholder={t('organization.namePlaceholder')} hint={t('organization.nameHint')}
          autoComplete="organization" maxLength={120}
          error={errors.name?.message ? t(errors.name.message) : undefined} {...register('name')} />
        <div><Button type="submit" busy={isSubmitting}>{t('organization.submit')}</Button></div>
      </form>
    </SetupLayout>
  )
}
