import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { PasswordField } from './PasswordField'

it('an eye icon inside the field shows and hides the password', async () => {
  const { container } = render(<PasswordField label="Password" placeholder="Your password" />)
  const input = screen.getByLabelText('Password')
  const eye = screen.getByRole('button', { name: 'Show password' })
  expect(input).toHaveAttribute('type', 'password')
  expect(input).toHaveAttribute('autocomplete', 'current-password')
  expect(input.parentElement).toContainElement(eye)
  expect(eye).toHaveTextContent('')

  await userEvent.click(eye)

  expect(input).toHaveAttribute('type', 'text')
  expect(screen.getByRole('button', { name: 'Hide password' })).toHaveAttribute('aria-pressed', 'true')
  expect(screen.queryByText('Show password')).not.toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('for a new password, asks the browser to suggest one', () => {
  render(<PasswordField label="Password" placeholder="At least 8 characters" isNew />)
  expect(screen.getByLabelText('Password')).toHaveAttribute('autocomplete', 'new-password')
})
