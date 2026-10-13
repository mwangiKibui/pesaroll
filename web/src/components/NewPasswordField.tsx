import { forwardRef, type ComponentProps } from 'react'
import { useTranslation } from 'react-i18next'
import { passwordStrength } from '../auth/password'
import { PasswordField } from './PasswordField'

type NewPasswordFieldProps = Omit<ComponentProps<typeof PasswordField>, 'isNew' | 'hint'> & {
  /** What has been typed, for the strength shown under the field. */
  value: string
}

/**
 * A password being chosen (sign-up, accepting an invitation): the eye inside, the rule as the hint
 * until something is typed, then its strength in words and as a three-part bar.
 */
export const NewPasswordField = forwardRef<HTMLInputElement, NewPasswordFieldProps>(function NewPasswordField({ value, ...props }, ref) {
  const { t } = useTranslation()
  const level = passwordStrength(value)
  return (
    <div className="flex flex-col gap-1.5">
      <PasswordField ref={ref} isNew hint={value ? t('signUp.strength', { level: t(`signUp.${level}`) }) : t('signUp.passwordHint')} {...props} />
      {value && (
        <div aria-hidden="true" className="flex gap-1">
          {(['weak', 'fair', 'strong'] as const).map((s, i) => {
            const filled = i <= ['weak', 'fair', 'strong'].indexOf(level)
            const colour = level === 'weak' ? 'bg-danger' : level === 'fair' ? 'bg-warning-ink' : 'bg-brand'
            return <span key={s} className={`h-1.5 flex-1 rounded-full ${filled ? colour : 'bg-line'}`} />
          })}
        </div>
      )}
    </div>
  )
})
