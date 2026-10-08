import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost'
type Size = 'md' | 'lg'

const base = 'inline-flex items-center justify-center gap-2 rounded-control font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-60'

const variants: Record<Variant, string> = {
  primary: 'bg-brand text-on-brand hover:bg-brand-hover',
  secondary: 'border border-field-border bg-card font-medium text-ink hover:bg-raised',
  danger: 'bg-danger text-on-danger hover:opacity-90',
  ghost: 'font-medium text-brand hover:bg-brand-soft',
}

// md: 44 px, the smallest tap target; lg: 50 px, the phone portal's main buttons.
const sizes: Record<Size, string> = {
  md: 'min-h-11 px-5 text-label',
  lg: 'min-h-[50px] px-6 text-body',
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  fullWidth?: boolean
  /** Waiting for the server: disabled, and announced as busy. */
  busy?: boolean
}

/** A button whose label says what it does: "Add location", never "Submit" or "OK". */
export function Button({ variant = 'primary', size = 'md', fullWidth, busy, disabled, type = 'button', className = '', ...rest }: ButtonProps) {
  return (
    <button type={type} disabled={disabled || busy} aria-busy={busy || undefined}
      className={`${base} ${variants[variant]} ${sizes[size]} ${fullWidth ? 'w-full' : ''} ${className}`} {...rest} />
  )
}

interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'aria-label'> {
  /** What a screen reader says, e.g. "Edit Westlands Restaurant". Required: the icon alone says nothing. */
  label: string
  tone?: 'neutral' | 'danger'
  children: ReactNode
}

export function IconButton({ label, tone = 'neutral', type = 'button', className = '', ...rest }: IconButtonProps) {
  const hover = tone === 'danger' ? 'hover:bg-danger-soft hover:text-danger-ink' : 'hover:bg-raised hover:text-ink'
  return (
    <button type={type} aria-label={label} title={label}
      className={`inline-flex size-11 items-center justify-center rounded-control text-muted ${hover} ${className}`} {...rest} />
  )
}
