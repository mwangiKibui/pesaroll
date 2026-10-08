import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useTheme, type ThemePreference } from '../theme/theme'

const options: ThemePreference[] = ['system', 'light', 'dark']

const icons: Record<ThemePreference, React.ReactNode> = {
  system: <><rect x="3" y="4" width="18" height="12" rx="2" /><path d="M8 20h8M12 16v4" /></>,
  light: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></>,
  dark: <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />,
}

const Icon = ({ name }: { name: ThemePreference }) => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{icons[name]}</svg>
)

/** The navbar's theme button: shows the current choice and opens System, Light and Dark. */
export function ThemeMenu() {
  const { t } = useTranslation()
  const { preference, setPreference } = useTheme()
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const id = useId()
  const root = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const items = useRef<(HTMLLIElement | null)[]>([])

  useEffect(() => {
    if (!open) return
    items.current[active]?.focus()
  }, [open, active])

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  const close = () => { setOpen(false); button.current?.focus() }
  const pick = (choice: ThemePreference) => { setPreference(choice); close() }

  const onKey = (e: KeyboardEvent) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(i => (i + 1) % options.length) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(i => (i + options.length - 1) % options.length) }
    else if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(options[active]) }
    else if (e.key === 'Escape' || e.key === 'Tab') { e.preventDefault(); close() }
  }

  return (
    <div ref={root} className="relative">
      <button ref={button} type="button" aria-label={t('theme.label')} aria-haspopup="menu" aria-expanded={open} aria-controls={open ? id : undefined}
        onClick={() => { setActive(options.indexOf(preference)); setOpen(o => !o) }}
        className="inline-flex size-11 items-center justify-center rounded-control text-muted hover:bg-raised hover:text-ink">
        <Icon name={preference} />
      </button>
      {open && (
        <ul id={id} role="menu" aria-label={t('theme.label')} onKeyDown={onKey}
          className="absolute right-0 top-full z-30 m-0 mt-1 flex w-44 list-none flex-col gap-0.5 rounded-card border border-line bg-card p-1.5 shadow-float">
          {options.map((option, i) => (
            <li key={option} ref={el => { items.current[i] = el }} role="menuitemradio" aria-checked={preference === option} tabIndex={-1}
              onClick={() => pick(option)} onMouseEnter={() => setActive(i)}
              className={`flex min-h-11 cursor-pointer items-center gap-2.5 rounded-control px-3 text-label outline-none ${i === active ? 'bg-raised' : ''} ${preference === option ? 'font-semibold text-brand' : 'text-ink'}`}>
              <Icon name={option} />
              {t(`theme.${option}`)}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
