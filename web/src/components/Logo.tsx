/** The Pesaroll mark and name. The mark is a payslip with a tick; colours follow the theme. */
export function Logo() {
  return (
    <span className="inline-flex items-center gap-2">
      <svg width="28" height="28" viewBox="0 0 28 28" aria-hidden="true">
        <rect width="28" height="28" rx="7" className="fill-brand" />
        <path d="M9 7h10a2 2 0 0 1 2 2v12l-2.5-1.5L16 21l-2-1.5L12 21l-2.5-1.5L7 21V9a2 2 0 0 1 2-2z" className="fill-on-brand" />
        <path d="M10.5 13.5l2.2 2.2 4.8-4.7" fill="none" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="stroke-brand" />
      </svg>
      <span className="font-display text-drawer-title leading-none text-ink">Pesaroll</span>
    </span>
  )
}
