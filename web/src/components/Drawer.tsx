import { useEffect, useEffectEvent, useId, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { IconButton } from './Button'

const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

interface DrawerProps {
  open: boolean
  /** "Add location" or "Edit location". */
  title: string
  onClose: () => void
  children: ReactNode
  /** Cancel, then a button that names the action ("Add location", "Save changes"). */
  footer?: ReactNode
  /** wide (760 px): records with several sections, shown as Tabs in `tabs`. */
  size?: 'md' | 'wide'
  /** Tabs under the title; they stay in place while the content scrolls. */
  tabs?: ReactNode
}

/**
 * Add and edit happen in a drawer from the right (480 px, or 760 px with tabs for records with
 * several sections; full width on phones), never a
 * separate page. Escape and the scrim close it, Tab stays inside, and focus returns to the
 * button that opened it.
 */
export function Drawer({ open, title, onClose, children, footer, size = 'md', tabs }: DrawerProps) {
  const { t } = useTranslation()
  const titleId = useId()
  const panel = useRef<HTMLDivElement>(null)
  const body = useRef<HTMLDivElement>(null)
  const close = useEffectEvent(onClose)

  useEffect(() => {
    if (!open) return
    const opener = document.activeElement as HTMLElement | null
    const first = body.current?.querySelector<HTMLElement>(focusable) ?? panel.current
    first?.focus()

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        close()
        return
      }
      if (e.key !== 'Tab' || !panel.current) return
      const items = [...panel.current.querySelectorAll<HTMLElement>(focusable)]
      if (items.length === 0) return
      const [head, tail] = [items[0], items[items.length - 1]]
      if (e.shiftKey && document.activeElement === head) { e.preventDefault(); tail.focus() }
      else if (!e.shiftKey && document.activeElement === tail) { e.preventDefault(); head.focus() }
    }
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('keydown', onKey)
      opener?.focus()
    }
  }, [open])

  if (!open) return null
  return (
    <>
      <div data-testid="drawer-scrim" aria-hidden="true" className="fixed inset-0 z-40 bg-scrim" onClick={onClose} />
      <div ref={panel} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}
        className={`fixed inset-y-0 right-0 z-50 flex w-full flex-col bg-card shadow-drawer ${size === 'wide' ? 'max-w-[760px]' : 'max-w-[480px]'}`}>
        <div className="flex items-center justify-between gap-3 border-b border-line px-6 py-4">
          <h2 id={titleId} className="m-0 font-display text-drawer-title text-ink">{title}</h2>
          <IconButton label={t('common.close')} onClick={onClose}>
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
          </IconButton>
        </div>
        {tabs && <div className="px-6">{tabs}</div>}
        <div ref={body} className="flex flex-1 flex-col gap-4 overflow-y-auto p-6">{children}</div>
        {footer && <div className="flex justify-end gap-3 border-t border-line px-6 py-4">{footer}</div>}
      </div>
    </>
  )
}
