import { useEffect, useEffectEvent, useId, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'
import { trapKeys } from './dialogKeys'

interface ConfirmDialogProps {
  open: boolean
  /** A question: "Delete Westlands Restaurant?" */
  title: string
  /** What happens if they go ahead. */
  children: ReactNode
  /** Names the action: "Delete company". */
  confirmLabel: string
  /** Why the last try was refused (a FormErrorBanner with its code), shown above the buttons. */
  problem?: ReactNode
  busy?: boolean
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Asks before something that can't be undone. Focus starts on Cancel (the safe choice), Escape
 * and the scrim cancel (without closing a drawer underneath), Tab stays inside, and focus
 * returns to the button that opened it.
 */
export function ConfirmDialog({ open, title, children, confirmLabel, problem, busy, onConfirm, onCancel }: ConfirmDialogProps) {
  const { t } = useTranslation()
  const titleId = useId()
  const textId = useId()
  const cancel = useRef<HTMLButtonElement>(null)
  const panel = useRef<HTMLDivElement>(null)
  const close = useEffectEvent(onCancel)

  useEffect(() => {
    if (!open) return
    const opener = document.activeElement as HTMLElement | null
    cancel.current?.focus()
    // Before a drawer underneath gets the key, so Escape cancels only this.
    const onKey = (e: KeyboardEvent) => trapKeys(e, panel.current, close)
    document.addEventListener('keydown', onKey, true)
    return () => {
      document.removeEventListener('keydown', onKey, true)
      opener?.focus()
    }
  }, [open])

  if (!open) return null
  return (
    <>
      <div aria-hidden="true" className="fixed inset-0 z-[60] bg-scrim" onClick={onCancel} />
      <div ref={panel} role="alertdialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={textId}
        className="fixed top-1/2 left-1/2 z-[70] flex w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col gap-4 rounded-card bg-card p-6 shadow-drawer">
        <h2 id={titleId} className="m-0 font-display text-drawer-title text-ink">{title}</h2>
        <p id={textId} className="m-0 text-muted">{children}</p>
        {problem}
        <div className="flex justify-end gap-3">
          <Button ref={cancel} variant="secondary" onClick={onCancel}>{t('common.cancel')}</Button>
          <Button variant="danger" busy={busy} onClick={onConfirm}>{confirmLabel}</Button>
        </div>
      </div>
    </>
  )
}
