// Button looks, shared by <Button> and links that look like buttons (e.g. "Proceed to Org Dashboard").
export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost'
export type ButtonSize = 'md' | 'lg'

const base = 'inline-flex items-center justify-center gap-2 rounded-control font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-60'

const variants: Record<ButtonVariant, string> = {
  primary: 'bg-brand text-on-brand hover:bg-brand-hover',
  secondary: 'border border-field-border bg-card font-medium text-ink hover:bg-raised',
  danger: 'bg-danger text-on-danger hover:opacity-90',
  ghost: 'font-medium text-brand hover:bg-brand-soft',
}

// md: 44 px, the smallest tap target; lg: 50 px, the phone portal's main buttons.
const sizes: Record<ButtonSize, string> = {
  md: 'min-h-11 px-5 text-label',
  lg: 'min-h-[50px] px-6 text-body',
}

export const buttonClasses = (variant: ButtonVariant = 'primary', size: ButtonSize = 'md') =>
  `${base} ${variants[variant]} ${sizes[size]} no-underline`
