import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { PasswordField } from './PasswordField'

it('hides the password until asked, then shows it', async () => {
  const { container } = render(<PasswordField label="Password" hint="Use 8 or more characters." />)
  const input = screen.getByLabelText('Password')
  expect(input).toHaveAttribute('type', 'password')
  expect(input).toHaveAttribute('autocomplete', 'current-password')

  await userEvent.click(screen.getByRole('button', { name: 'Show password' }))

  expect(input).toHaveAttribute('type', 'text')
  expect(screen.getByRole('button', { name: 'Hide password' })).toHaveAttribute('aria-pressed', 'true')
  await expectNoA11yViolations(container)
})

it('for a new password, asks the browser to suggest one', () => {
  render(<PasswordField label="Password" isNew />)
  expect(screen.getByLabelText('Password')).toHaveAttribute('autocomplete', 'new-password')
})
