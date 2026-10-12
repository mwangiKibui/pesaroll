import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { Checkbox, RadioGroup, SelectField, Switch, TextField } from './fields'

it('a text field has its label above, its hint below, and says when it is optional', async () => {
  const { container } = render(<TextField label="Middle name" placeholder="Your middle name" hint="As on your ID" optional />)

  const input = screen.getByRole('textbox', { name: /Middle name/ })
  expect(screen.getByRole('textbox', { name: 'Middle name optional' })).toBeInTheDocument()
  expect(input).toHaveAccessibleDescription('As on your ID')
  expect(input).not.toBeRequired()
  expect(input).toHaveAttribute('placeholder', 'Your middle name')
  await expectNoA11yViolations(container)
})

it('an error is shown under the field and read out with it', async () => {
  const { container } = render(<TextField label="Email" placeholder="Your email" error="Enter a valid email address." />)

  const input = screen.getByRole('textbox', { name: 'Email' })
  expect(input).toHaveAttribute('aria-invalid', 'true')
  expect(input).toHaveAccessibleDescription('Enter a valid email address.')
  await expectNoA11yViolations(container)
})

it('passes typing through', async () => {
  const onChange = vi.fn()
  render(<TextField label="First name" placeholder="Your first name" onChange={e => onChange(e.target.value)} />)
  await userEvent.type(screen.getByRole('textbox', { name: 'First name' }), 'W')
  expect(onChange).toHaveBeenCalledWith('W')
})

const counties = [
  { value: '047', label: 'Nairobi' },
  { value: '001', label: 'Mombasa' },
  { value: '021', label: "Murang'a" },
  { value: '045', label: 'Kéroka Town' },
]

function County({ optional = false, initial = '' }: { optional?: boolean; initial?: string }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <SelectField label="County" placeholder="Your county" hint="Sets public holidays." optional={optional}
        options={counties} value={value} onChange={setValue} />
      <output>{value || 'none'}</output>
    </>
  )
}

const county = (name = 'County') => screen.getByRole('combobox', { name })

it('a dropdown shows the choice like a text field and opens a styled list', async () => {
  const { container } = render(<County initial="001" />)

  expect(county()).toHaveValue('Mombasa')
  expect(county()).toHaveAccessibleDescription('Sets public holidays.')
  expect(county()).toHaveAttribute('aria-expanded', 'false')
  expect(document.querySelector('select')).toBeNull()
  await expectNoA11yViolations(container)

  await userEvent.click(county())

  expect(county()).toHaveAttribute('aria-expanded', 'true')
  expect(screen.getAllByRole('option')).toHaveLength(4)
  expect(screen.getByRole('option', { name: 'Mombasa' })).toHaveAttribute('aria-selected', 'true')
  await expectNoA11yViolations(container)
})

it('with nothing chosen it shows the placeholder', () => {
  render(<County />)
  expect(county()).toHaveValue('')
  expect(county()).toHaveAttribute('placeholder', 'Your county')
})

it('typing filters the options, ignoring case and accents, and says when nothing fits', async () => {
  render(<County />)

  await userEvent.type(county(), 'NAI')
  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['Nairobi'])

  await userEvent.clear(county())
  await userEvent.type(county(), 'kero')
  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['Kéroka Town'])

  await userEvent.clear(county())
  await userEvent.type(county(), 'zzz')
  expect(screen.queryAllByRole('option')).toHaveLength(0)
  expect(screen.getByText('No matches')).toBeInTheDocument()
})

it('clicking an option picks it and closes the list', async () => {
  render(<County />)
  await userEvent.click(county())
  await userEvent.click(screen.getByRole('option', { name: "Murang'a" }))

  expect(screen.getByRole('status')).toHaveTextContent('021')
  expect(county()).toHaveValue("Murang'a")
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
})

it('works from the keyboard: arrows open and move, Enter picks, Escape closes and keeps the choice', async () => {
  render(<County initial="047" />)
  county().focus()

  await userEvent.keyboard('{ArrowDown}')
  expect(screen.getByRole('listbox')).toBeInTheDocument()
  await userEvent.keyboard('{ArrowDown}{Enter}')
  expect(screen.getByRole('status')).toHaveTextContent('001')
  expect(county()).toHaveValue('Mombasa')

  await userEvent.type(county(), 'x')
  await userEvent.keyboard('{Escape}')
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  expect(county()).toHaveValue('Mombasa')
  expect(county()).toHaveFocus()
})

it('typing then Enter picks the first match', async () => {
  render(<County />)
  await userEvent.type(county(), 'mur{Enter}')
  expect(screen.getByRole('status')).toHaveTextContent('021')
})

it('an optional dropdown is cleared by emptying it; a required one keeps its choice', async () => {
  render(<><County optional initial="047" /></>)
  await userEvent.clear(county('County optional'))
  await userEvent.tab()
  expect(screen.getByRole('status')).toHaveTextContent('none')
  expect(county('County optional')).toHaveValue('')
})

it('a required dropdown keeps its choice when emptied and left', async () => {
  render(<County initial="047" />)
  await userEvent.clear(county())
  await userEvent.tab()
  expect(screen.getByRole('status')).toHaveTextContent('047')
  expect(county()).toHaveValue('Nairobi')
})

it('the list floats over everything near it, and opens upward when there is no room below', async () => {
  render(
    // A card that clips what sticks out, as table cards do.
    <div style={{ overflow: 'hidden' }}><County initial="001" /></div>,
  )
  const field = county().parentElement!
  const rect = (top: number) => ({ top, bottom: top + 36, left: 40, right: 136, width: 96, height: 36, x: 40, y: top, toJSON: () => ({}) })

  field.getBoundingClientRect = () => rect(100)
  await userEvent.click(county())
  const below = screen.getByRole('listbox').parentElement!
  expect(below).toHaveClass('fixed')   // not clipped by the card
  expect(below.style.top).toBe('140px')
  expect(below.style.minWidth).toBe('96px')
  await userEvent.keyboard('{Escape}')

  field.getBoundingClientRect = () => rect(window.innerHeight - 50)   // at the foot of the screen
  await userEvent.click(county())
  const above = screen.getByRole('listbox').parentElement!
  expect(above.style.top).toBe('')
  expect(above.style.bottom).toBe('54px')   // opens upward, just above the field
})

it('inside a centred dialog the list sits under its field, measured from the dialog', async () => {
  render(<div data-testid="dialog" style={{ transform: 'translate(-50%, -50%)' }}><County initial="001" /></div>)
  const dialog = screen.getByTestId('dialog')
  dialog.getBoundingClientRect = () => ({ top: 200, bottom: 600, left: 300, right: 800, width: 500, height: 400, x: 300, y: 200, toJSON: () => ({}) })
  county().parentElement!.getBoundingClientRect = () => ({ top: 260, bottom: 296, left: 330, right: 630, width: 300, height: 36, x: 330, y: 260, toJSON: () => ({}) })

  await userEvent.click(county())
  const list = screen.getByRole('listbox').parentElement!
  expect([list.style.top, list.style.left]).toEqual(['100px', '30px'])   // just under the field, inside the dialog
})

it('a dropdown error is shown under it and read out with it', async () => {
  const { container } = render(<SelectField label="Industry" placeholder="Your industry" error="Choose an industry from the list."
    options={[{ value: 'hotels', label: 'Hotels' }]} value="" onChange={() => {}} />)
  expect(county('Industry')).toHaveAttribute('aria-invalid', 'true')
  expect(county('Industry')).toHaveAccessibleDescription('Choose an industry from the list.')
  await expectNoA11yViolations(container)
})

it('a checkbox toggles by its label', async () => {
  render(<Checkbox label="Main location for Mama Oliech Restaurants" />)
  await userEvent.click(screen.getByText('Main location for Mama Oliech Restaurants'))
  expect(screen.getByRole('checkbox')).toBeChecked()
})

it('a radio group names its question and picks one option', async () => {
  function PayFrom() {
    const [value, setValue] = useState('bank')
    return <><RadioGroup label="Pay from" name="method" value={value} onChange={setValue}
      options={[{ value: 'bank', label: 'Bank account' }, { value: 'mpesa', label: 'M-Pesa', hint: 'A B2C shortcode' }]} /><output>{value}</output></>
  }
  const { container } = render(<PayFrom />)

  const group = screen.getByRole('radiogroup', { name: 'Pay from' })
  expect(screen.getByRole('radio', { name: 'Bank account' })).toBeChecked()
  await userEvent.click(screen.getByText('M-Pesa'))
  expect(screen.getByRole('status')).toHaveTextContent('mpesa')
  expect(screen.getByRole('radio', { name: /M-Pesa/ })).toHaveAccessibleDescription('A B2C shortcode')
  expect(group).toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('a switch turns a setting on and off, says why when it is locked, and works from the keyboard', async () => {
  function Default({ disabled = false }) {
    const [on, setOn] = useState(false)
    return <><Switch label="Use as the default" hint="Payroll is paid from it unless you choose another." checked={on} onChange={setOn} disabled={disabled} /><output>{on ? 'on' : 'off'}</output></>
  }
  const { container, unmount } = render(<Default />)

  const toggle = screen.getByRole('switch', { name: 'Use as the default' })
  expect(toggle).toHaveAttribute('aria-checked', 'false')
  expect(toggle).toHaveAccessibleDescription('Payroll is paid from it unless you choose another.')
  await userEvent.click(toggle)
  expect(toggle).toHaveAttribute('aria-checked', 'true')
  expect(screen.getByRole('status')).toHaveTextContent('on')
  await userEvent.keyboard(' ')
  expect(screen.getByRole('status')).toHaveTextContent('off')
  await expectNoA11yViolations(container)
  unmount()

  render(<Default disabled />)
  await userEvent.click(screen.getByRole('switch', { name: 'Use as the default' }))
  expect(screen.getByRole('status')).toHaveTextContent('off')
})
