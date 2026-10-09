import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { CompanySelect, type CompanySelection } from './CompanySelect'

const companies = [
  { id: 'ke', name: 'Mama Oliech Restaurants', code: 'KE' },
  { id: 'ug', name: 'Oliech Catering Uganda', code: 'UG' },
]

function Harness({ multiple = true, initial = { all: true, ids: [] } }: { multiple?: boolean; initial?: CompanySelection }) {
  const [value, setValue] = useState<CompanySelection>(initial)
  return (
    <>
      <CompanySelect label="Companies" companies={companies} value={value} onChange={setValue} multiple={multiple} />
      <output>{value.all ? 'all' : value.ids.join(',')}</output>
    </>
  )
}

it('starts with every company ticked and no separate "All companies" option', async () => {
  const { container } = render(<Harness />)
  const field = screen.getByRole('button', { name: /Companies/ })
  expect(field).toHaveTextContent('All companies')
  expect(field).toHaveAttribute('aria-expanded', 'false')

  await userEvent.click(field)

  expect(field).toHaveAttribute('aria-expanded', 'true')
  expect(screen.getByRole('listbox')).toHaveAttribute('aria-multiselectable', 'true')
  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['Mama Oliech RestaurantsKE', 'Oliech Catering UgandaUG'])
  expect(screen.getAllByRole('option').every(o => o.getAttribute('aria-selected') === 'true')).toBe(true)
  await expectNoA11yViolations(container)
})

it('unticking narrows the choice; ticking every company again means all of them', async () => {
  render(<Harness />)
  await userEvent.click(screen.getByRole('button', { name: /Companies/ }))

  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Uganda/ }))
  expect(screen.getByRole('status')).toHaveTextContent('ke')
  expect(screen.getByRole('button', { name: /Companies/ })).toHaveTextContent('Mama Oliech Restaurants · KE')

  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Uganda/ }))
  expect(screen.getByRole('status')).toHaveTextContent('all')
  expect(screen.getByRole('button', { name: /Companies/ })).toHaveTextContent('All companies')
})

it('for one company it closes as soon as one is picked', async () => {
  render(<Harness multiple={false} initial={{ all: false, ids: ['ke'] }} />)
  await userEvent.click(screen.getByRole('button', { name: /Companies/ }))

  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Uganda/ }))

  expect(screen.getByRole('status')).toHaveTextContent('ug')
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
})

it('works from the keyboard: arrows move, Enter picks, Escape closes', async () => {
  render(<Harness initial={{ all: false, ids: ['ke'] }} />)
  screen.getByRole('button', { name: /Companies/ }).focus()

  await userEvent.keyboard('{Enter}')
  await userEvent.keyboard('{ArrowDown}{Enter}')
  expect(screen.getByRole('status')).toHaveTextContent('all')

  await userEvent.keyboard('{Escape}')
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: /Companies/ })).toHaveFocus()
})

it('a search box at the top of the list filters the companies', async () => {
  const { container } = render(<Harness initial={{ all: false, ids: ['ke'] }} />)
  await userEvent.click(screen.getByRole('button', { name: /Companies/ }))

  const search = screen.getByRole('combobox', { name: 'Search companies' })
  expect(search).toHaveFocus()
  expect(search).toHaveAttribute('placeholder', 'Your company name')
  await userEvent.type(search, 'uganda')

  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual([expect.stringContaining('Oliech Catering Uganda')])
  await userEvent.keyboard('{Enter}')
  expect(screen.getByRole('status')).toHaveTextContent('all')

  await userEvent.clear(search)
  await userEvent.type(search, 'zzz')
  expect(screen.getByText('No matches')).toBeInTheDocument()
  await expectNoA11yViolations(container)
})
