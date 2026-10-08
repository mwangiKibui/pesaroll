import { forwardRef, useEffect, useId, useRef, useState, type InputHTMLAttributes, type KeyboardEvent, type ReactNode, type Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { matches } from './search'

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
        {optional && <> <span className="ml-1 font-normal text-muted">{t('common.optional')}</span></>}
      </label>
      {children}
      {hint && <p id={hintId} className="m-0 text-hint text-muted">{hint}</p>}
      {error && <p id={errorId} className="m-0 text-hint font-medium text-danger-ink">{error}</p>}
    </div>
  )
}

type TextFieldProps = FieldProps & Omit<InputHTMLAttributes<HTMLInputElement>, 'size' | 'placeholder'> & {
  /** Required: "Your" and the field name, e.g. "Your first name". The label still says what it is. */
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

interface SelectFieldProps extends FieldProps {
  options: { value: string; label: string }[]
  /** Required, like every field: "Your" and the field name, e.g. "Your county". */
  placeholder: string
  /** The chosen option's value; '' for none. */
  value: string
  onChange: (value: string) => void
  onBlur?: () => void
  name?: string
  disabled?: boolean
  /** For forms: focus the field when it has an error (react-hook-form's Controller passes it). */
  ref?: Ref<HTMLInputElement>
}

/**
 * Every dropdown (docs/standards/ui.md, rule 6): styled like a text field, and searchable.
 * Typing filters the options (case and accents don't count), arrows move, Enter picks,
 * Escape closes. Emptying an optional one clears it; a required one keeps its choice.
 */
export function SelectField({ label, hint, error, optional, size = 'md', options, placeholder, value, onChange, onBlur, name, disabled, ref }: SelectFieldProps) {
  const { t } = useTranslation()
  const ids = useFieldIds(hint, error)
  const listId = `${ids.id}-list`
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState<string | null>(null)   // null: not typing, show the choice
  const [active, setActive] = useState(0)
  const list = useRef<HTMLUListElement>(null)

  const chosen = options.find(o => o.value === value)
  const shown = query === null ? options : options.filter(o => matches(o.label, query))

  useEffect(() => {
    list.current?.querySelector(`[data-index="${active}"]`)?.scrollIntoView?.({ block: 'nearest' })
  }, [active, open])

  const openList = () => {
    if (open) return
    setActive(Math.max(0, options.findIndex(o => o.value === value)))
    setOpen(true)
  }

  const close = () => {
    setOpen(false)
    setQuery(null)
  }

  const pick = (option: { value: string } | undefined) => {
    if (option) onChange(option.value)
    close()
  }

  const leave = () => {
    if (query !== null && query.trim() === '' && optional) onChange('')
    close()
    onBlur?.()
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) openList()
      else setActive(i => Math.min(i + 1, shown.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      if (!open) openList()
      else setActive(i => Math.max(i - 1, 0))
    } else if (e.key === 'Enter') {
      if (!open) return
      e.preventDefault()
      pick(shown[active])
    } else if (e.key === 'Escape' && open) {
      e.preventDefault()
      close()
    }
  }

  return (
    <Field {...ids} label={label} hint={hint} error={error} optional={optional}>
      <div className="relative">
        <input ref={ref} id={ids.id} name={name} type="text" role="combobox" autoComplete="off" disabled={disabled}
          aria-expanded={open} aria-controls={listId} aria-autocomplete="list"
          aria-activedescendant={open && shown[active] ? `${listId}-${active}` : undefined}
          aria-describedby={ids.describedBy} aria-invalid={error ? true : undefined}
          placeholder={placeholder} value={query ?? chosen?.label ?? ''}
          onChange={e => { setQuery(e.target.value); setActive(0); setOpen(true) }}
          onClick={openList} onKeyDown={onKeyDown} onBlur={leave}
          className={`${control} ${controlSize[size]} cursor-pointer pr-10`} />
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"
          className={`pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-muted ${open ? 'rotate-180' : ''}`}><path d="M6 9l6 6 6-6" /></svg>
        {open && (
          <div className="absolute inset-x-0 top-full z-30 mt-1 rounded-card border border-line bg-card p-1.5 shadow-float">
            <ul ref={list} id={listId} role="listbox" aria-label={typeof label === 'string' ? label : undefined}
              className="m-0 flex max-h-64 list-none flex-col gap-0.5 overflow-y-auto p-0">
              {shown.map((o, i) => (
                <li key={o.value} id={`${listId}-${i}`} data-index={i} role="option" aria-selected={o.value === value}
                  // Keep focus in the field so picking doesn't count as leaving it.
                  onMouseDown={e => e.preventDefault()} onClick={() => pick(o)} onMouseEnter={() => setActive(i)}
                  className={`flex min-h-11 cursor-pointer items-center justify-between gap-2 rounded-control px-3 text-body text-ink ${i === active ? 'bg-raised' : ''} ${o.value === value ? 'font-semibold' : ''}`}>
                  {o.label}
                  {o.value === value && <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true" className="flex-none text-brand"><path d="M5 12l5 5 9-10" /></svg>}
                </li>
              ))}
            </ul>
            {shown.length === 0 && <p className="m-0 px-3 py-2.5 text-label text-muted">{t('common.noMatches')}</p>}
          </div>
        )}
      </div>
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
