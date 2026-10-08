import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { errorKey, errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { Button } from '../components/Button'
import { FormErrorBanner, Notice } from '../components/feedback'
import { TextField } from '../components/fields'
import { PasswordField } from '../components/PasswordField'
import { splitContact } from '../lib/identifiers'
import { AuthLayout } from './AuthLayout'

const schema = z.object({
  identifier: z.string().refine(v => splitContact(v) !== null, 'validation.contact'),
  password: z.string().min(1, 'validation.passwordRequired'),
})

type Values = z.infer<typeof schema>

export default function SignInPage() {
  const { t } = useTranslation()
  const { api, auth } = useServices()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { identifier: '', password: '' },
  })
  const fieldError = (name: keyof Values) => (errors[name]?.message ? t(errors[name].message) : undefined)

  const onSubmit = async (v: Values) => {
    setBanner(null)
    try {
      const { data, error } = await api.POST('/api/auth/signin', { body: { identifier: v.identifier.trim(), password: v.password } })
      if (data) {
        // The page guard then moves the person on (to where they were heading, or welcome).
        auth.setAccessToken(data.accessToken, { broadcast: true })
        return
      }
      if (isApiError(error) && error.code === 'AUTH-1005') setError('identifier', { message: errorKey(error.code) })
      else if (isApiError(error)) setBanner(error)
      else setBanner('network')
    } catch {
      setBanner('network')
    }
  }

  return (
    <AuthLayout title={t('signIn.title')} intro={t('signIn.intro')}>
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}
        <TextField label={t('auth.contact')} autoComplete="username" inputMode="email" error={fieldError('identifier')} {...register('identifier')} />
        <PasswordField label={t('auth.password')} error={fieldError('password')} {...register('password')} />
        <Button type="submit" size="lg" fullWidth busy={isSubmitting}>{t('signIn.submit')}</Button>
        <p className="m-0 text-label">{t('signIn.noAccount')} <Link to="/signup">{t('signIn.signUp')}</Link></p>
      </form>
    </AuthLayout>
  )
}
