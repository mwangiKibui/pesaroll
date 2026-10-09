// Keyboard rules shared by Modal and ConfirmDialog (dialogs that can sit on top of a drawer).

export const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * Escape closes and Tab cycles inside `panel`. Called in the capture phase and stops the event,
 * so a drawer underneath doesn't also close or take focus back.
 */
export function trapKeys(e: KeyboardEvent, panel: HTMLElement | null, close: () => void) {
  if (e.key === 'Escape') {
    // An open dropdown inside the dialog closes first; the next Escape closes the dialog.
    if ((e.target as HTMLElement | null)?.getAttribute?.('aria-expanded') === 'true') return
    e.preventDefault()
    e.stopPropagation()
    close()
    return
  }
  if (e.key !== 'Tab' || !panel) return
  e.stopPropagation()
  const items = [...panel.querySelectorAll<HTMLElement>(focusable)]
  if (items.length === 0) { e.preventDefault(); return }
  const [head, tail] = [items[0], items[items.length - 1]]
  if (e.shiftKey && (document.activeElement === head || !panel.contains(document.activeElement))) { e.preventDefault(); tail.focus() }
  else if (!e.shiftKey && (document.activeElement === tail || !panel.contains(document.activeElement))) { e.preventDefault(); head.focus() }
}
