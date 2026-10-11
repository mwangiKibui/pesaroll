import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { MultiSelectField } from './MultiSelectField'

const options = [
  { value: 'overtime', label: 'Overtime and extra hours' },
  { value: 'new_employees', label: 'New employees' },
  { value: 'leavers', label: 'People leaving' },
]

function Harness({ initial = ['overtime'], onAdd }: { initial?: string[]; onAdd?: () => void }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <MultiSelectField label="Applies to" hint="Tick what waits for the next payroll." placeholder="Your items" options={options}
        value={value} onChange={setValue} addLabel={onAdd ? 'Add your own' : undefined} onAdd={onAdd} />
      <output>{value.join(',')}</output>
    </>
  )
}

it('shows what is ticked and opens a searchable list of checkboxes', async () => {
  const { container } = render(<Harness />)
  const field = screen.getByRole('button', { name: /Applies to/ })
  expect(field).toHaveTextContent('Overtime and extra hours')
  expect(field).toHaveAccessibleDescription('Tick what waits for the next payroll.')

  await userEvent.click(field)
  expect(screen.getByRole('listbox')).toHaveAttribute('aria-multiselectable', 'true')
  expect(screen.getByRole('option', { name: 'Overtime and extra hours' })).toHaveAttribute('aria-selected', 'true')
  await userEvent.type(screen.getByRole('combobox'), 'leav')
  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['People leaving'])
  await userEvent.keyboard('{Enter}')

  expect(screen.getByText('overtime,leavers', { selector: 'output' })).toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('ticks and unticks by click; several ticked show as a count', async () => {
  render(<Harness />)
  await userEvent.click(screen.getByRole('button', { name: /Applies to/ }))

  await userEvent.click(screen.getByRole('option', { name: 'New employees' }))
  await userEvent.click(screen.getByRole('option', { name: 'Overtime and extra hours' }))
  await userEvent.click(screen.getByRole('option', { name: 'Overtime and extra hours' }))
  await userEvent.keyboard('{Escape}')

  expect(screen.getByText('new_employees,overtime', { selector: 'output' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /Applies to/ })).toHaveTextContent('2 selected')
  expect(screen.getByRole('button', { name: /Applies to/ })).toHaveFocus()   // Escape closes the list only
})

it('nothing ticked shows the placeholder', () => {
  render(<Harness initial={[]} />)
  expect(screen.getByRole('button', { name: /Applies to/ })).toHaveTextContent('Your items')
})

it('can offer adding your own as the first choice', async () => {
  const onAdd = vi.fn()
  render(<Harness onAdd={onAdd} />)
  await userEvent.click(screen.getByRole('button', { name: /Applies to/ }))

  const add = screen.getByRole('button', { name: 'Add your own' })
  expect(add.compareDocumentPosition(screen.getByRole('listbox')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  await userEvent.click(add)

  expect(onAdd).toHaveBeenCalledOnce()
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
})
