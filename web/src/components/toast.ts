import { createContext, useContext } from 'react'

export type ToastTone = 'brand' | 'danger'

export const ToastContext = createContext<{ show: (message: string, tone?: ToastTone) => void } | null>(null)

/** Show a short confirmation: useToast().show('Location added'). Needs a ToastProvider above. */
export function useToast() {
  const value = useContext(ToastContext)
  if (!value) throw new Error('useToast needs a ToastProvider above it')
  return value
}
