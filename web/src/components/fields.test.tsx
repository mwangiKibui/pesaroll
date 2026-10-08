import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { Checkbox, SelectField, TextField } from './fields'

it('a text field has its label above, its hint below, and says when it is optional', async () => {
  const { container } = render(<TextField label="Middle name" placeholder="e.g. Akinyi" hint="As on your ID" optional />)

  const input = screen.getByRole('textbox', { name: /Middle name/ })
  expect(screen.getByText('optional')).toBeInTheDocument()
  expect(input).toHaveAccessibleDescription('As on your ID')
  expect(input).not.toBeRequired()
  expect(input).toHaveAttribute('placeholder', 'e.g. Akinyi')
  await expectNoA11yViolations(container)
})

it('an error is shown under the field and read out with it', async () => {
  const { container } = render(<TextField label="Email" placeholder="you@company.com" error="Enter a valid email address." />)

  const input = screen.getByRole('textbox', { name: 'Email' })
  expect(input).toHaveAttribute('aria-invalid', 'true')
  expect(input).toHaveAccessibleDescription('Enter a valid email address.')
  await expectNoA11yViolations(container)
})

it('passes typing through', async () => {
  const onChange = vi.fn()
  render(<TextField label="First name" placeholder="e.g. Amina" onChange={e => onChange(e.target.value)} />)
  await userEvent.type(screen.getByRole('textbox', { name: 'First name' }), 'W')
  expect(onChange).toHaveBeenCalledWith('W')
})

it('a select lists its options', async () => {
  const { container } = render(
    <SelectField label="Town" defaultValue="Mombasa" options={[{ value: 'Nairobi', label: 'Nairobi' }, { value: 'Mombasa', label: 'Mombasa' }]} />)

  expect(screen.getByRole('combobox', { name: 'Town' })).toHaveValue('Mombasa')
  await expectNoA11yViolations(container)
})

it('a checkbox toggles by its label', async () => {
  render(<Checkbox label="Main location for Mama Oliech Restaurants" />)
  await userEvent.click(screen.getByText('Main location for Mama Oliech Restaurants'))
  expect(screen.getByRole('checkbox')).toBeChecked()
})
