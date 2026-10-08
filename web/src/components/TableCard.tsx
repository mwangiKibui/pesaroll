import type { ReactNode } from 'react'

/** A white panel with an optional heading. highlight: the one card that matters most (this month's payslip). */
export function Card({ title, highlight, children, className = '' }: { title?: string; highlight?: boolean; children: ReactNode; className?: string }) {
  return (
    <section className={`flex flex-col gap-3 rounded-card bg-card p-5 ${highlight ? 'border-2 border-brand' : 'border border-line'} ${className}`}>
      {title && <h2 className="m-0 text-body font-semibold text-ink">{title}</h2>}
      {children}
    </section>
  )
}

interface TableCardProps {
  /** "Locations"; shown as "Locations · 4" with the count. */
  title: string
  count?: number
  /** A tag beside the title, e.g. "Not saved yet". */
  status?: ReactNode
  /** Bulk upload, then "Add <thing>". */
  actions?: ReactNode
  children: ReactNode
}

/** The setup and list pattern: title and count, actions on the right, a table that scrolls sideways on phones. */
export function TableCard({ title, count, status, actions, children }: TableCardProps) {
  return (
    <section className="overflow-hidden rounded-card border border-line bg-card">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-3.5">
        <div className="flex flex-wrap items-center gap-2.5">
          <h2 className="m-0 text-body font-semibold text-ink">{count === undefined ? title : `${title} · ${count}`}</h2>
          {status}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      <div role="region" aria-label={title} tabIndex={0}
        className="overflow-x-auto [&_table]:w-full [&_table]:border-collapse [&_table]:text-label [&_td]:border-b [&_td]:border-line-soft [&_td]:px-4 [&_td]:py-2.5 [&_th]:whitespace-nowrap [&_th]:border-b [&_th]:border-line [&_th]:bg-raised [&_th]:px-4 [&_th]:py-2.5 [&_th]:text-left [&_th]:font-medium [&_th]:text-muted">
        {children}
      </div>
    </section>
  )
}
