import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { Button, IconButton } from './Button'

it('is a plain button that does what its label says', async () => {
  const onClick = vi.fn()
  render(<Button onClick={onClick}>Add location</Button>)

  const button = screen.getByRole('button', { name: 'Add location' })
  expect(button).toHaveAttribute('type', 'button')
  await userEvent.click(button)
  expect(onClick).toHaveBeenCalledOnce()
})

it('while busy it is disabled and says so', async () => {
  const onClick = vi.fn()
  render(<Button busy onClick={onClick}>Save changes</Button>)

  const button = screen.getByRole('button', { name: 'Save changes' })
  expect(button).toBeDisabled()
  expect(button).toHaveAttribute('aria-busy', 'true')
  await userEvent.click(button)
  expect(onClick).not.toHaveBeenCalled()
})

it('submits a form when asked to', () => {
  render(<form><Button type="submit">Sign in</Button></form>)
  expect(screen.getByRole('button', { name: 'Sign in' })).toHaveAttribute('type', 'submit')
})

it('an icon button is named for screen readers', async () => {
  const { container } = render(<IconButton label="Edit Westlands Restaurant"><svg aria-hidden="true" /></IconButton>)
  expect(screen.getByRole('button', { name: 'Edit Westlands Restaurant' })).toBeInTheDocument()
  await expectNoA11yViolations(container)
})
