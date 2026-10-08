import { forwardRef, useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes } from 'react'
import { useTranslation } from 'react-i18next'

type Size = 'md' | 'lg'

interface FieldProps {
  label: ReactNode
  /** Shown under the field in secondary text. */
  hint?: ReactNode
  /** Shown under the field and read out with it; marks the field invalid. */
  error?: ReactNode
  /** Writes "optional" beside the label. Required fields carry no marker. */
  optional?: boolean
  /** lg: the phone portal's 52 px fields. */
  size?: Size
}

const control = 'w-full rounded-control border border-field-border bg-card px-3 text-ink placeholder:text-muted aria-invalid:border-danger-ink'
const controlSize: Record<Size, string> = { md: 'min-h-11 text-body', lg: 'min-h-[52px] text-lg' }

function useFieldIds(hint: unknown, error: unknown) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(' ') || undefined
  return { id, hintId, errorId, describedBy }
}

function Field({ id, label, hint, hintId, error, errorId, optional, children }: FieldProps & { id: string; hintId: string; errorId: string; children: ReactNode }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-label font-medium text-ink">
        {label}
        {optional && <span className="ml-1.5 font-normal text-muted">{t('common.optional')}</span>}
      </label>
      {children}
      {hint && <p id={hintId} className="m-0 text-hint text-muted">{hint}</p>}
      {error && <p id={errorId} className="m-0 text-hint font-medium text-danger-ink">{error}</p>}
    </div>
  )
}

type TextFieldProps = FieldProps & Omit<InputHTMLAttributes<HTMLInputElement>, 'size' | 'placeholder'> & {
  /** Required: every field shows an example of what goes in it ("e.g. Amina"). The label still says what it is. */
  placeholder: string
  /** Something inside the field on the right, e.g. the password field's eye button. */
  end?: ReactNode
}

export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField({ label, hint, error, optional, size = 'md', end, className = '', ...input }, ref) {
  const ids = useFieldIds(hint, error)
  return (
    <Field {...ids} label={label} hint={hint} error={error} optional={optional}>
      <div className="relative">
        <input ref={ref} id={ids.id} aria-describedby={ids.describedBy} aria-invalid={error ? true : undefined}
          className={`${control} ${controlSize[size]} ${end ? 'pr-12' : ''} ${className}`} {...input} />
        {end && <div className="absolute inset-y-0 right-0 flex items-center pr-1">{end}</div>}
      </div>
    </Field>
  )
})

type SelectFieldProps = FieldProps & Omit<SelectHTMLAttributes<HTMLSelectElement>, 'size'> & {
  options: { value: string; label: string }[]
}

export function SelectField({ label, hint, error, optional, size = 'md', options, className = '', ...select }: SelectFieldProps) {
  const ids = useFieldIds(hint, error)
  return (
    <Field {...ids} label={label} hint={hint} error={error} optional={optional}>
      <select id={ids.id} aria-describedby={ids.describedBy} aria-invalid={error ? true : undefined}
        className={`${control} ${controlSize[size]} ${className}`} {...select}>
        {options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </Field>
  )
}

type CheckboxProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { label: ReactNode }

export function Checkbox({ label, className = '', ...input }: CheckboxProps) {
  return (
    <label className={`flex min-h-10 items-center gap-2.5 text-body text-ink ${className}`}>
      <input type="checkbox" className="size-5 accent-brand" {...input} />
      {label}
    </label>
  )
}
