import { useTranslation } from 'react-i18next'
import { Button } from './Button'
import { SelectField } from './fields'
import { PAGE_SIZES } from './usePaged'

export interface PaginationProps {
  /** "Pages of employees": names the navigation for screen readers. */
  label: string
  /** 1 for the first page. */
  page: number
  pageSize: number
  total: number
  onPage: (page: number) => void
  onPageSize: (size: number) => void
  sizes?: number[]
}

/**
 * Every table's footer, shown even when everything fits on one page: "Showing 21–40 of 132",
 * rows per page, and Previous / Next with "Page 2 of 7". Changing rows per page goes back to page 1.
 */
export function Pagination({ label, page, pageSize, total, onPage, onPageSize, sizes = PAGE_SIZES }: PaginationProps) {
  const { t } = useTranslation()
  const pages = Math.max(1, Math.ceil(total / pageSize))
  const at = Math.min(Math.max(1, page), pages)
  const from = total === 0 ? 0 : (at - 1) * pageSize + 1
  const to = Math.min(total, at * pageSize)
  return (
    <nav aria-label={label} className="flex flex-wrap items-center justify-between gap-3">
      <span className="text-label text-muted">{t('pagination.showing', { from, to, total })}</span>
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2">
          <span aria-hidden="true" className="text-label whitespace-nowrap text-muted">{t('pagination.rowsPerPage')}</span>
          <div className="w-24">
            <SelectField size="sm" hideLabel label={t('pagination.rowsPerPage')} placeholder={String(pageSize)} value={String(pageSize)}
              options={sizes.map(s => ({ value: String(s), label: String(s) }))} onChange={value => value && onPageSize(Number(value))} />
          </div>
        </div>
        <span className="text-label whitespace-nowrap text-muted">{t('pagination.page', { page: at, pages })}</span>
        <Button variant="secondary" disabled={at <= 1} onClick={() => onPage(at - 1)}>{t('pagination.previous')}</Button>
        <Button variant="secondary" disabled={at >= pages} onClick={() => onPage(at + 1)}>{t('pagination.next')}</Button>
      </div>
    </nav>
  )
}
