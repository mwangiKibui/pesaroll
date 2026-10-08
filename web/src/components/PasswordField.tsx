import { forwardRef, useState, type ComponentProps } from 'react'
import { useTranslation } from 'react-i18next'
import { TextField } from './fields'

type PasswordFieldProps = Omit<ComponentProps<typeof TextField>, 'type' | 'end'> & {
  /** A password being chosen (sign-up, reset): the browser offers to generate and save one. */
  isNew?: boolean
}

const eye = <><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></>
const eyeOff = <><path d="M3 3l18 18" /><path d="M10.6 5.1A10.4 10.4 0 0 1 12 5c6.5 0 10 7 10 7a17.6 17.6 0 0 1-3.2 4.2M6.6 6.6C3.8 8.4 2 12 2 12s3.5 7 10 7a9.7 9.7 0 0 0 5.4-1.6" /><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" /></>

/** A password field with an eye button inside it, so people on phones can check what they typed. */
export const PasswordField = forwardRef<HTMLInputElement, PasswordFieldProps>(function PasswordField({ isNew, ...props }, ref) {
  const { t } = useTranslation()
  const [visible, setVisible] = useState(false)
  const label = visible ? t('auth.hidePassword') : t('auth.showPassword')
  return (
    <TextField ref={ref} type={visible ? 'text' : 'password'} autoComplete={isNew ? 'new-password' : 'current-password'} {...props}
      end={
        <button type="button" aria-label={label} title={label} aria-pressed={visible} onClick={() => setVisible(v => !v)}
          className="inline-flex size-10 items-center justify-center rounded-control text-muted hover:bg-raised hover:text-ink">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{visible ? eyeOff : eye}</svg>
        </button>
      } />
  )
})
