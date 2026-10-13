import { forwardRef, useEffect, useId, useRef, useState, type CSSProperties, type InputHTMLAttributes, type KeyboardEvent, type ReactNode, type Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { matches } from './search'

type Size = 'sm' | 'md' | 'lg'

interface FieldProps {
  label: ReactNode
  /** Shown under the field in secondary text. */
  hint?: ReactNode
  /** warning: the hint is something to check (amber), not just information. */
  hintTone?: 'muted' | 'warning'
  /** Shown under the field and read out with it; marks the field invalid. */
  error?: ReactNode
  /** Writes "optional" beside the label. Required fields carry no marker. */
  optional?: boolean
  /** lg: the phone portal's 52 px fields; sm: cells of an editable table (36 px). */
  size?: Size
  /** The label is read out but not shown: a table cell, where the column header says what it is. */
  hideLabel?: boolean
}

const control = 'w-full rounded-control border border-field-border bg-card px-3 text-ink placeholder:text-muted aria-invalid:border-danger-ink'
const controlSize: Record<Size, string> = { sm: 'min-h-9 text-label', md: 'min-h-11 text-body', lg: 'min-h-[52px] text-lg' }

/** A dropdown list's tallest (max-h-64 plus its padding) and its gap from the field, in px. */
const LIST_MAX = 268
const GAP = 4

function useFieldIds(hint: unknown, error: unknown) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(' ') || undefined
  return { id, hintId, errorId, describedBy }
}

function Field({ id, label, hint, hintTone = 'muted', hintId, error, errorId, optional, hideLabel, children }: FieldProps & { id: string; hintId: string; errorId: string; children: ReactNode }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className={hideLabel ? 'sr-only' : 'text-label font-medium text-ink'}>
        {label}
        {optional && <> <span className="ml-1 font-normal text-muted">{t('common.optional')}</span></>}
      </label>
      {children}
      {hint && <p id={hintId} className={`m-0 text-hint ${hintTone === 'warning' ? 'font-medium text-warning-ink' : 'text-muted'}`}>{hint}</p>}
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

export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField({ label, hint, hintTone, error, optional, size = 'md', hideLabel, end, className = '', ...input }, ref) {
  const ids = useFieldIds(hint, error)
  return (
    <Field {...ids} label={label} hint={hint} hintTone={hintTone} error={error} optional={optional} hideLabel={hideLabel}>
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
export function SelectField({ label, hint, hintTone, error, optional, size = 'md', hideLabel, options, placeholder, value, onChange, onBlur, name, disabled, ref }: SelectFieldProps) {
  const { t } = useTranslation()
  const ids = useFieldIds(hint, error)
  const listId = `${ids.id}-list`
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState<string | null>(null)   // null: not typing, show the choice
  const [active, setActive] = useState(0)
  const list = useRef<HTMLUListElement>(null)
  const box = useRef<HTMLDivElement>(null)
  const [at, setAt] = useState<CSSProperties>({})

  // The list floats (fixed) so a card or a scrolling table can't clip it, next to the field, and
  // opens upward when there's no room below. Inside a centred dialog (a box moved with transform or
  // translate) fixed is measured from that box, not the screen.
  const place = () => {
    const field = box.current
    if (!field) return
    const rect = field.getBoundingClientRect()
    let frame = { top: 0, left: 0, bottom: window.innerHeight }
    for (let el = field.parentElement; el; el = el.parentElement) {
      // transform, or Tailwind 4's translate (the centred Modal), scale or rotate: any makes fixed measure from that box.
      const style = getComputedStyle(el)
      if ([style.transform, style.translate, style.scale, style.rotate, style.filter].some(v => v && v !== 'none')) {
        const r = el.getBoundingClientRect()
        frame = { top: r.top, left: r.left, bottom: r.bottom }
        break
      }
    }
    const tall = Math.min(LIST_MAX, options.length * 44 + 12)
    const upward = window.innerHeight - rect.bottom < tall + GAP && rect.top > window.innerHeight - rect.bottom
    setAt(upward
      ? { bottom: frame.bottom - rect.top + GAP, left: rect.left - frame.left, minWidth: rect.width }
      : { top: rect.bottom - frame.top + GAP, left: rect.left - frame.left, minWidth: rect.width })
  }

  // While open it follows the field when the page or a table scrolls, or the window resizes.
  useEffect(() => {
    if (!open) return
    window.addEventListener('scroll', place, true)
    window.addEventListener('resize', place)
    return () => {
      window.removeEventListener('scroll', place, true)
      window.removeEventListener('resize', place)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const chosen = options.find(o => o.value === value)
  const shown = query === null ? options : options.filter(o => matches(o.label, query))

  useEffect(() => {
    list.current?.querySelector(`[data-index="${active}"]`)?.scrollIntoView?.({ block: 'nearest' })
  }, [active, open])

  const openList = () => {
    if (open) return
    place()
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
      // Closes only the list, not a drawer or modal the field sits in.
      e.preventDefault()
      e.stopPropagation()
      close()
    }
  }

  return (
    <Field {...ids} label={label} hint={hint} hintTone={hintTone} error={error} optional={optional} hideLabel={hideLabel}>
      <div ref={box} className="relative">
        <input ref={ref} id={ids.id} name={name} type="text" role="combobox" autoComplete="off" disabled={disabled}
          aria-expanded={open} aria-controls={listId} aria-autocomplete="list"
          aria-activedescendant={open && shown[active] ? `${listId}-${active}` : undefined}
          aria-describedby={ids.describedBy} aria-invalid={error ? true : undefined}
          // A value not in the list (typed in an uploaded file) stays visible, to be fixed.
          placeholder={placeholder} value={query ?? chosen?.label ?? value}
          onChange={e => { setQuery(e.target.value); setActive(0); if (!open) place(); setOpen(true) }}
          onClick={openList} onKeyDown={onKeyDown} onBlur={leave}
          className={`${control} ${controlSize[size]} cursor-pointer pr-10`} />
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"
          className={`pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-muted ${open ? 'rotate-180' : ''}`}><path d="M6 9l6 6 6-6" /></svg>
        {open && (
          <div style={at} className="fixed z-[80] w-max max-w-[min(32rem,calc(100vw-2rem))] rounded-card border border-line bg-card p-1.5 shadow-float">
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

interface RadioGroupProps {
  /** The question: "Pay from". */
  label: string
  name: string
  options: { value: string; label: string; hint?: string }[]
  value: string
  onChange: (value: string) => void
  /** Under the choices: what to know, or (error) what to fix. */
  hint?: string
  error?: string
}

/** One choice from a few (two to four) shown side by side; more than that is a SelectField. */
export function RadioGroup({ label, name, options, value, onChange, hint, error }: RadioGroupProps) {
  const id = useId()
  const note = error ?? hint
  return (
    <fieldset role="radiogroup" aria-labelledby={`${id}-label`} aria-describedby={note ? `${id}-note` : undefined} aria-invalid={error ? true : undefined} className="m-0 flex flex-col gap-1.5 border-0 p-0">
      <legend id={`${id}-label`} className="mb-1.5 p-0 text-label font-medium text-ink">{label}</legend>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        {options.map(o => (
          <label key={o.value} className={`flex min-h-11 cursor-pointer items-start gap-2.5 rounded-control border px-3 py-2.5 ${o.value === value ? 'border-brand bg-brand-soft' : 'border-field-border bg-card'}`}>
            <input type="radio" name={name} value={o.value} checked={o.value === value} onChange={() => onChange(o.value)}
              aria-labelledby={`${id}-${o.value}-label`} aria-describedby={o.hint ? `${id}-${o.value}-hint` : undefined} className="mt-1 accent-brand" />
            <span className="flex flex-col">
              <span id={`${id}-${o.value}-label`} className="text-body text-ink">{o.label}</span>
              {o.hint && <span id={`${id}-${o.value}-hint`} className="text-hint text-muted">{o.hint}</span>}
            </span>
          </label>
        ))}
      </div>
      {note && <p id={`${id}-note`} className={`m-0 text-hint ${error ? 'font-medium text-danger-ink' : 'text-muted'}`}>{note}</p>}
    </fieldset>
  )
}

interface SwitchProps {
  label: string
  /** Why it matters, or why it can't be changed right now. */
  hint?: string
  checked: boolean
  onChange: (checked: boolean) => void
  disabled?: boolean
}

/** A setting that is on or off and applies at once in the form ("Use as the default"). Space or click toggles. */
export function Switch({ label, hint, checked, onChange, disabled, accessibleLabel }: SwitchProps & {
  /** A fuller name for screen readers when the visible label repeats (a toggle per table row: "From profile for Wanjiru Kamau"). */
  accessibleLabel?: string
}) {
  const id = useId()
  return (
    <div className="flex items-start justify-between gap-4">
      <span className="flex flex-col gap-0.5">
        <span id={`${id}-label`} className="text-label font-medium text-ink">{label}</span>
        {hint && <span id={`${id}-hint`} className="text-hint text-muted">{hint}</span>}
      </span>
      <button type="button" role="switch" aria-checked={checked} aria-labelledby={accessibleLabel ? undefined : `${id}-label`} aria-label={accessibleLabel}
        aria-describedby={hint ? `${id}-hint` : undefined} disabled={disabled} onClick={() => onChange(!checked)}
        className="flex min-h-11 min-w-14 flex-none items-center justify-center rounded-control">
        {/* 44 px to tap; the track inside is smaller. */}
        <span aria-hidden="true" className={`flex h-7 w-12 items-center rounded-full transition-colors ${checked ? 'bg-brand' : 'bg-field-border'}`}>
          <span className={`inline-block size-5 rounded-full bg-card shadow transition-transform ${checked ? 'translate-x-6' : 'translate-x-1'}`} />
        </span>
      </button>
    </div>
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
