import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { errorKey, errorMessage, isApiError } from '../app/apiErrors'
import { useServices, type ApiError } from '../app/services'
import { passwordStrength } from '../auth/password'
import { Button } from '../components/Button'
import { FormErrorBanner, Loading, Notice } from '../components/feedback'
import { TextField } from '../components/fields'
import { PasswordField } from '../components/PasswordField'
import { splitContact } from '../lib/identifiers'
import { AuthLayout } from './AuthLayout'

// Same rules as the API (AUTH-1004 to AUTH-1009), plus a password strength the screen asks for.
// Messages are translation keys.
function schemaFor(otherRoleId: string | undefined) {
  return z.object({
    firstName: z.string().trim().min(1, 'validation.firstName').max(60, 'validation.nameTooLong'),
    middleName: z.string().trim().max(60, 'validation.nameTooLong'),
    lastName: z.string().trim().min(1, 'validation.lastName').max(60, 'validation.nameTooLong'),
    contact: z.string().refine(v => splitContact(v) !== null, 'validation.contact'),
    password: z.string().refine(v => passwordStrength(v) !== 'weak', 'validation.passwordWeak'),
    roleId: z.string({ error: 'validation.role' }).min(1, 'validation.role'),
    roleOther: z.string().trim().max(80, 'validation.roleOtherLong'),
  }).refine(v => v.roleId !== otherRoleId || v.roleOther.length > 0, { message: 'validation.roleOther', path: ['roleOther'] })
}

type Values = z.infer<ReturnType<typeof schemaFor>>

// Server errors that belong under a field; the rest show as a banner.
const fieldFor: Record<string, keyof Values> = { 'AUTH-1003': 'contact', 'AUTH-1005': 'contact', 'AUTH-1004': 'password', 'AUTH-1009': 'roleOther' }

// Step 1 is your details, step 2 your role, so the form fits without scrolling.
const detailFields = ['firstName', 'middleName', 'lastName', 'contact', 'password'] as const
const isDetail = (field: keyof Values) => (detailFields as readonly string[]).includes(field)

export default function SignUpPage() {
  const { t, i18n } = useTranslation()
  const { api, auth } = useServices()
  const [banner, setBanner] = useState<ApiError | 'network' | null>(null)
  const [step, setStep] = useState<1 | 2>(1)
  const stepHeading = useRef<HTMLHeadingElement>(null)
  const firstRender = useRef(true)
  const lang = i18n.language === 'sw' ? 'sw' : 'en'

  const roles = useQuery({
    queryKey: ['signup-roles'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/auth/signup-roles')
      if (error || !data) throw new Error('signup roles')
      return data
    },
  })
  const otherRoleId = useMemo(() => roles.data?.flatMap(g => g.roles).find(r => r.isOther)?.id, [roles.data])

  const { register, handleSubmit, control, setError, trigger, formState: { errors, isSubmitting } } = useForm<Values>({
    resolver: zodResolver(schemaFor(otherRoleId)),
    defaultValues: { firstName: '', middleName: '', lastName: '', contact: '', password: '', roleOther: '' },
  })
  const password = useWatch({ control, name: 'password' })
  const roleId = useWatch({ control, name: 'roleId' })
  const isOther = roleId !== undefined && roleId === otherRoleId
  const fieldError = (name: keyof Values) => (errors[name]?.message ? t(errors[name].message) : undefined)

  // Moving between steps puts focus on the new step's heading, so screen readers follow.
  useEffect(() => {
    if (firstRender.current) { firstRender.current = false; return }
    stepHeading.current?.focus()
  }, [step])

  const next = async () => { if (await trigger([...detailFields])) setStep(2) }

  const onSubmit = async (v: Values) => {
    setBanner(null)
    const contact = splitContact(v.contact)
    try {
      const { data, error } = await api.POST('/api/auth/signup', {
        body: {
          firstName: v.firstName, middleName: v.middleName || null, lastName: v.lastName,
          email: contact && 'email' in contact ? contact.email : null,
          phone: contact && 'phone' in contact ? contact.phone : null,
          password: v.password, roleId: v.roleId, roleOther: isOther ? v.roleOther : null,
        },
      })
      if (data) {
        auth.setAccessToken(data.accessToken, { broadcast: true })
        return
      }
      if (isApiError(error) && fieldFor[error.code]) {
        if (isDetail(fieldFor[error.code])) setStep(1)
        setError(fieldFor[error.code], { message: errorKey(error.code) })
      } else if (isApiError(error)) setBanner(error)
      else setBanner('network')
    } catch {
      setBanner('network')
    }
  }

  const level = passwordStrength(password)

  return (
    <AuthLayout title={t('signUp.title')} intro={t('signUp.intro')} alignTop>
      <form noValidate onSubmit={step === 1 ? e => { e.preventDefault(); void next() } : handleSubmit(onSubmit)} className="flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <p className="m-0 text-label font-medium text-muted">{t('signUp.stepOf', { step, total: 2 })}</p>
          <ol aria-label={t('signUp.stepsLabel')} className="m-0 flex list-none gap-2 p-0">
            {([1, 2] as const).map(n => (
              <li key={n} aria-current={n === step ? 'step' : undefined}
                className={`h-1.5 flex-1 rounded-full ${n <= step ? 'bg-brand' : 'bg-line'}`}>
                <span className="sr-only">{n === 1 ? t('signUp.detailsTitle') : t('signUp.role')}</span>
              </li>
            ))}
          </ol>
        </div>
        {banner === 'network' && <Notice tone="danger">{t('apiErrors.network')}</Notice>}
        {banner && banner !== 'network' && <FormErrorBanner error={{ ...banner, message: errorMessage(t, banner) }} />}

        {step === 1 && (
          <>
            <h2 ref={stepHeading} tabIndex={-1} className="m-0 text-body font-semibold text-ink outline-none">{t('signUp.detailsTitle')}</h2>
            <div className="grid gap-4 @sm:grid-cols-2">
              <TextField label={t('signUp.firstName')} placeholder={t('signUp.firstNamePlaceholder')} autoComplete="given-name" error={fieldError('firstName')} {...register('firstName')} />
              <TextField label={t('signUp.lastName')} placeholder={t('signUp.lastNamePlaceholder')} autoComplete="family-name" error={fieldError('lastName')} {...register('lastName')} />
            </div>
            <TextField label={t('signUp.middleName')} placeholder={t('signUp.middleNamePlaceholder')} optional autoComplete="additional-name" error={fieldError('middleName')} {...register('middleName')} />
            <TextField label={t('auth.contact')} placeholder={t('auth.contactPlaceholder')} hint={t('signUp.contactHint')} autoComplete="username" inputMode="email" error={fieldError('contact')} {...register('contact')} />
            <div className="flex flex-col gap-1.5">
              <PasswordField label={t('auth.password')} placeholder={t('signUp.passwordPlaceholder')} isNew
                hint={password ? t('signUp.strength', { level: t(`signUp.${level}`) }) : t('signUp.passwordHint')}
                error={fieldError('password')} {...register('password')} />
              {password && (
                <div aria-hidden="true" className="flex gap-1">
                  {(['weak', 'fair', 'strong'] as const).map((s, i) => {
                    const filled = i <= ['weak', 'fair', 'strong'].indexOf(level)
                    const colour = level === 'weak' ? 'bg-danger' : level === 'fair' ? 'bg-warning-ink' : 'bg-brand'
                    return <span key={s} className={`h-1.5 flex-1 rounded-full ${filled ? colour : 'bg-line'}`} />
                  })}
                </div>
              )}
            </div>
            <Button type="submit" size="lg" fullWidth>{t('signUp.continue')}</Button>
          </>
        )}

        {step === 2 && (
          <>
            <h2 ref={stepHeading} id="role-title" tabIndex={-1} className="m-0 text-body font-semibold text-ink outline-none">{t('signUp.role')}</h2>
            <div role="group" aria-labelledby="role-title" aria-describedby={errors.roleId ? 'role-error' : undefined} className="flex flex-col gap-2">
              {roles.isPending && <Loading rows={3} />}
              {roles.data?.map(group => (
                <fieldset key={group.code} className="m-0 flex flex-col gap-1 border-0 p-0">
                  <legend className="mb-1 p-0 text-tag uppercase tracking-wider text-muted">{group.name[lang]}</legend>
                  <div className="grid gap-2 @sm:grid-cols-2 @lg:grid-cols-3">
                    {group.roles.map(role => (
                      <label key={role.id} className="flex min-h-11 cursor-pointer items-center gap-2.5 rounded-control border border-line bg-card px-3 py-2 text-label font-medium text-ink has-checked:border-brand has-checked:bg-brand-soft">
                        <input type="radio" value={role.id} className="size-[18px] flex-none accent-brand" {...register('roleId')} />
                        {role.name[lang]}
                      </label>
                    ))}
                  </div>
                </fieldset>
              ))}
              {errors.roleId && <p id="role-error" className="m-0 text-hint font-medium text-danger-ink">{fieldError('roleId')}</p>}
            </div>
            {isOther && <TextField label={t('signUp.roleOther')} placeholder={t('signUp.roleOtherPlaceholder')} error={fieldError('roleOther')} maxLength={80} {...register('roleOther')} />}
            <div className="flex gap-3">
              <Button variant="secondary" size="lg" onClick={() => setStep(1)}>{t('signUp.back')}</Button>
              <Button type="submit" size="lg" fullWidth busy={isSubmitting}>{t('signUp.submit')}</Button>
            </div>
            <p className="m-0 text-hint text-muted">{t('signUp.terms')}</p>
          </>
        )}

        <p className="m-0 text-label">{t('signUp.haveAccount')} <Link to="/signin">{t('signUp.signIn')}</Link></p>
      </form>
    </AuthLayout>
  )
}
