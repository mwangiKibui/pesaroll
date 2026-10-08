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

function Harness({ multiple = true, initial = { all: false, ids: ['ke'] } }: { multiple?: boolean; initial?: CompanySelection }) {
  const [value, setValue] = useState<CompanySelection>(initial)
  return (
    <>
      <CompanySelect label="Companies" companies={companies} value={value} onChange={setValue} multiple={multiple} />
      <output>{value.all ? 'all' : value.ids.join(',')}</output>
    </>
  )
}

it('shows what is chosen and opens a list of companies', async () => {
  const { container } = render(<Harness />)
  const field = screen.getByRole('button', { name: /Companies/ })
  expect(field).toHaveTextContent('Mama Oliech Restaurants · KE')
  expect(field).toHaveAttribute('aria-expanded', 'false')

  await userEvent.click(field)

  expect(field).toHaveAttribute('aria-expanded', 'true')
  expect(screen.getByRole('listbox')).toHaveAttribute('aria-multiselectable', 'true')
  expect(screen.getByRole('option', { name: /All companies/ })).toHaveTextContent('Includes companies added later')
  expect(screen.getByRole('option', { name: /Mama Oliech Restaurants/ })).toHaveAttribute('aria-selected', 'true')
  await expectNoA11yViolations(container)
})

it('picks several, then all', async () => {
  render(<Harness />)
  await userEvent.click(screen.getByRole('button', { name: /Companies/ }))

  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Uganda/ }))
  expect(screen.getByRole('status')).toHaveTextContent('ke,ug')
  expect(screen.getByRole('button', { name: /Companies/ })).toHaveTextContent('2 selected')

  await userEvent.click(screen.getByRole('option', { name: /All companies/ }))
  expect(screen.getByRole('status')).toHaveTextContent('all')
})

it('for one company it closes as soon as one is picked', async () => {
  render(<Harness multiple={false} />)
  await userEvent.click(screen.getByRole('button', { name: /Companies/ }))
  expect(screen.queryByRole('option', { name: /All companies/ })).not.toBeInTheDocument()

  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Uganda/ }))

  expect(screen.getByRole('status')).toHaveTextContent('ug')
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
})

it('works from the keyboard: arrows move, Enter picks, Escape closes', async () => {
  render(<Harness />)
  screen.getByRole('button', { name: /Companies/ }).focus()

  await userEvent.keyboard('{Enter}')
  await userEvent.keyboard('{ArrowDown}{ArrowDown}{Enter}')
  expect(screen.getByRole('status')).toHaveTextContent('ke,ug')

  await userEvent.keyboard('{Escape}')
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: /Companies/ })).toHaveFocus()
})
