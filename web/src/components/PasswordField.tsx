import { forwardRef, useState, type ComponentProps } from 'react'
import { useTranslation } from 'react-i18next'
import { TextField } from './fields'

type PasswordFieldProps = Omit<ComponentProps<typeof TextField>, 'type'> & {
  /** A password being chosen (sign-up, reset): the browser offers to generate and save one. */
  isNew?: boolean
}

/** A password field with a Show/Hide button, so people on phones can check what they typed. */
export const PasswordField = forwardRef<HTMLInputElement, PasswordFieldProps>(function PasswordField({ isNew, ...props }, ref) {
  const { t } = useTranslation()
  const [visible, setVisible] = useState(false)
  return (
    <div className="flex flex-col gap-1">
      <TextField ref={ref} type={visible ? 'text' : 'password'} autoComplete={isNew ? 'new-password' : 'current-password'} {...props} />
      <button type="button" aria-pressed={visible} onClick={() => setVisible(v => !v)}
        className="min-h-9 self-start rounded-control px-1 text-label font-medium text-brand hover:underline">
        {visible ? t('auth.hidePassword') : t('auth.showPassword')}
      </button>
    </div>
  )
})
