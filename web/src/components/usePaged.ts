import { useState } from 'react'

/** Rows per page a table offers; 20 to start. */
export const PAGE_SIZES = [10, 20, 50, 100]

/**
 * A list paged in the browser (the setup lists): the rows of the current page and the footer's
 * props. A page past the end (after a delete or a narrower search) shows the last one.
 */
export function usePaged<T>(items: T[], initialSize = 20) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(initialSize)
  const pages = Math.max(1, Math.ceil(items.length / pageSize))
  const at = Math.min(page, pages)
  return {
    items: items.slice((at - 1) * pageSize, at * pageSize),
    pagination: {
      page: at, pageSize, total: items.length, onPage: setPage,
      onPageSize: (size: number) => { setPageSize(size); setPage(1) },
    },
  }
}
