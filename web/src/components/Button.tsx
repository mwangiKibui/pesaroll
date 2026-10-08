import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'
import { buttonClasses, type ButtonSize, type ButtonVariant } from './buttonStyles'

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  fullWidth?: boolean
  /** Waiting for the server: disabled, and announced as busy. */
  busy?: boolean
  ref?: Ref<HTMLButtonElement>
}

/** A button whose label says what it does: "Add location", never "Submit" or "OK". */
export function Button({ variant = 'primary', size = 'md', fullWidth, busy, disabled, type = 'button', className = '', ...rest }: ButtonProps) {
  return (
    <button type={type} disabled={disabled || busy} aria-busy={busy || undefined}
      className={`${buttonClasses(variant, size)} ${fullWidth ? 'w-full' : ''} ${className}`} {...rest} />
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
