import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { SearchBox } from './SearchBox'

const pages = [
  { key: 'earnings', label: 'Earnings and deductions', group: 'Pay', href: '/earnings', words: 'payroll sacco' },
  { key: 'history', label: 'Payroll history', group: 'Pay', href: '/history', words: '' },
  { key: 'schedules', label: 'Payroll schedules', group: 'Organization', href: '/schedules', words: '' },
]
const search = (q: string) => pages.filter(p => `${p.label} ${p.words}`.toLowerCase().includes(q.toLowerCase()))

function box(onPick = vi.fn()) {
  render(<SearchBox label="Search Pesaroll" placeholder="Search, like payroll or leave" empty="Nothing you can open matches." search={search} onPick={onPick} />)
  return { onPick, input: screen.getByRole('combobox', { name: 'Search Pesaroll' }) }
}

it('lists what matches, grouped, as you type', async () => {
  const { input } = box()
  expect(input).toHaveAttribute('aria-expanded', 'false')
  await userEvent.type(input, 'payroll')
  expect(input).toHaveAttribute('aria-expanded', 'true')
  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['Earnings and deductions', 'Payroll history', 'Payroll schedules'])
  expect(screen.getByText('Organization')).toBeInTheDocument()
})

it('arrows move, Enter opens, and the box empties', async () => {
  const { input, onPick } = box()
  await userEvent.type(input, 'payroll')
  await userEvent.keyboard('{ArrowDown}{Enter}')
  expect(onPick).toHaveBeenCalledWith('/history')
  expect(input).toHaveValue('')
  expect(input).toHaveAttribute('aria-expanded', 'false')
})

it('a click opens a page; Escape closes the list', async () => {
  const { input, onPick } = box()
  await userEvent.type(input, 'sacco')
  await userEvent.click(screen.getByRole('option', { name: 'Earnings and deductions' }))
  expect(onPick).toHaveBeenCalledWith('/earnings')

  await userEvent.type(input, 'payroll')
  await userEvent.keyboard('{Escape}')
  expect(input).toHaveAttribute('aria-expanded', 'false')
})

it('says so when nothing matches', async () => {
  const { container } = render(<SearchBox label="Search Pesaroll" placeholder="Your search" empty="Nothing you can open matches." search={search} onPick={vi.fn()} />)
  await userEvent.type(screen.getByRole('combobox'), 'zzz')
  expect(screen.getByText('Nothing you can open matches.')).toBeInTheDocument()
  await expectNoA11yViolations(container)
})
