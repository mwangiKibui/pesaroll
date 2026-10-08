import { useEffect, useEffectEvent, useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { IconButton } from './Button'
import { focusable, trapKeys } from './dialogKeys'

interface ModalProps {
  open: boolean
  /** "Add paying account". */
  title: string
  onClose: () => void
  children: ReactNode
  /** Cancel, then a button that names the action. */
  footer?: ReactNode
}

/**
 * A small form on top of a drawer (adding a paying account to the company being edited): a
 * centred dialog, never a drawer on a drawer. It takes Escape and Tab before the drawer does, so
 * Escape closes only the modal and Tab stays inside it; focus returns to the button that opened it.
 */
export function Modal({ open, title, onClose, children, footer }: ModalProps) {
  const { t } = useTranslation()
  const titleId = useId()
  const panel = useRef<HTMLDivElement>(null)
  const body = useRef<HTMLDivElement>(null)
  const close = useEffectEvent(onClose)

  useEffect(() => {
    if (!open) return
    const opener = document.activeElement as HTMLElement | null
    ;(body.current?.querySelector<HTMLElement>(focusable) ?? panel.current)?.focus()
    const onKey = (e: KeyboardEvent) => trapKeys(e, panel.current, close)
    document.addEventListener('keydown', onKey, true)
    return () => {
      document.removeEventListener('keydown', onKey, true)
      opener?.focus()
    }
  }, [open])

  if (!open) return null
  return createPortal(
    <>
      <div aria-hidden="true" className="fixed inset-0 z-[60] bg-scrim" onClick={onClose} />
      <div ref={panel} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}
        className="fixed top-1/2 left-1/2 z-[70] flex max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-xl -translate-x-1/2 -translate-y-1/2 flex-col rounded-card bg-card shadow-drawer">
        <div className="flex items-center justify-between gap-3 border-b border-line px-6 py-4">
          <h2 id={titleId} className="m-0 font-display text-drawer-title text-ink">{title}</h2>
          <IconButton label={t('common.close')} onClick={onClose}>
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
          </IconButton>
        </div>
        <div ref={body} className="flex flex-1 flex-col gap-4 overflow-y-auto p-6">{children}</div>
        {footer && <div className="flex justify-end gap-3 border-t border-line px-6 py-4">{footer}</div>}
      </div>
    </>,
    document.body,
  )
}
