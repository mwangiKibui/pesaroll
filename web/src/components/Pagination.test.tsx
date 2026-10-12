import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import { Pagination } from './Pagination'
import { usePaged } from './usePaged'

function List({ count }: { count: number }) {
  const paged = usePaged(Array.from({ length: count }, (_, i) => `Item ${i + 1}`))
  return (
    <>
      <ul>{paged.items.map(i => <li key={i}>{i}</li>)}</ul>
      <Pagination label="Pages of items" {...paged.pagination} />
    </>
  )
}

it('shows even when everything fits on one page', () => {
  render(<List count={3} />)
  const nav = screen.getByRole('navigation', { name: 'Pages of items' })
  expect(within(nav).getByText('Showing 1–3 of 3')).toBeInTheDocument()
  expect(within(nav).getByText('Page 1 of 1')).toBeInTheDocument()
  expect(within(nav).getByRole('button', { name: 'Previous page' })).toBeDisabled()
  expect(within(nav).getByRole('button', { name: 'Next page' })).toBeDisabled()
})

it('pages 20 at a time, and rows per page goes back to the first page', async () => {
  render(<List count={45} />)
  const nav = screen.getByRole('navigation', { name: 'Pages of items' })
  expect(screen.getAllByRole('listitem')).toHaveLength(20)
  await userEvent.click(within(nav).getByRole('button', { name: 'Next page' }))
  await userEvent.click(within(nav).getByRole('button', { name: 'Next page' }))
  expect(within(nav).getByText('Showing 41–45 of 45')).toBeInTheDocument()
  expect(within(nav).getByText('Page 3 of 3')).toBeInTheDocument()

  await userEvent.click(within(nav).getByRole('combobox', { name: 'Rows per page' }))
  await userEvent.click(screen.getByRole('option', { name: '50' }))
  expect(screen.getAllByRole('listitem')).toHaveLength(45)
  expect(within(nav).getByText('Page 1 of 1')).toBeInTheDocument()
})

it('an empty table says so', () => {
  render(<Pagination label="Pages of items" page={1} pageSize={20} total={0} onPage={() => {}} onPageSize={() => {}} />)
  expect(screen.getByText('Showing 0–0 of 0')).toBeInTheDocument()
})
