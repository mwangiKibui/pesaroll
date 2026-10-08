import { useCallback, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ToastContext, type ToastTone } from './toast'

/** The API's error shape: every error has a code and a reference support can look up (PLAT-14). */
export interface ApiError {
  code: string
  message: string
  reference: string
}

/** A problem with the whole form or page. Field problems go under their field instead. */
export function FormErrorBanner({ error }: { error: ApiError }) {
  const { t } = useTranslation()
  return (
    <div role="alert" className="flex flex-col gap-1 rounded-card bg-danger-soft px-4 py-3 text-danger-ink">
      <p className="m-0 font-medium">{error.message}</p>
      <p className="m-0 font-mono text-hint">{t('errors.codeAndReference', { code: error.code, reference: error.reference })}</p>
    </div>
  )
}

type Tone = 'neutral' | 'brand' | 'warning' | 'danger'

const tones: Record<Tone, string> = {
  neutral: 'border border-line bg-raised text-ink',
  brand: 'bg-brand-soft text-brand-soft-ink',
  warning: 'bg-warning-soft text-warning-ink',
  danger: 'bg-danger-soft text-danger-ink',
}

/** A short label: "Not saved yet" (warning), "Main · KE" (brand), "All companies" (neutral). */
export function Tag({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return <span className={`inline-flex items-center whitespace-nowrap rounded-full px-2 py-0.5 text-tag font-medium ${tones[tone]}`}>{children}</span>
}

/** A fix-it message that blocks progress: say what's wrong and how to fix it. */
export function Notice({ tone = 'warning', children }: { tone?: Exclude<Tone, 'neutral'>; children: ReactNode }) {
  return <div className={`rounded-card px-4 py-3 text-label ${tones[tone]}`}>{children}</div>
}

/** Nothing here yet: one line on what is missing and the button that adds it. */
export function EmptyState({ title, children, action }: { title: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-3 px-6 py-10 text-center">
      <h2 className="m-0 font-display text-drawer-title text-ink">{title}</h2>
      {children && <p className="m-0 max-w-prose text-muted">{children}</p>}
      {action}
    </div>
  )
}

/** Placeholder shapes while data loads (no spinners); announced once to screen readers. */
export function Loading({ rows = 3 }: { rows?: number }) {
  const { t } = useTranslation()
  return (
    <div role="status" className="flex flex-col gap-2">
      <span className="sr-only">{t('common.loading')}</span>
      {Array.from({ length: rows }, (_, i) => <div key={i} aria-hidden="true" className="h-10 animate-pulse rounded-control bg-raised" />)}
    </div>
  )
}

interface Toast { id: number; message: string; tone: ToastTone }

/** Short confirmations ("Location added") that go away after 5 seconds. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const show = useCallback((message: string, tone: ToastTone = 'brand') => {
    const id = Date.now() + Math.random()
    setToasts(list => [...list, { id, message, tone }])
    setTimeout(() => setToasts(list => list.filter(x => x.id !== id)), 5000)
  }, [])
  return (
    <ToastContext.Provider value={{ show }}>
      {children}
      <div role="status" aria-live="polite" className="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex flex-col items-center gap-2">
        {toasts.map(x => (
          <div key={x.id} className={`pointer-events-auto rounded-card px-4 py-3 text-label font-medium shadow-float ${x.tone === 'danger' ? 'bg-danger text-on-danger' : 'bg-ink text-page'}`}>{x.message}</div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
