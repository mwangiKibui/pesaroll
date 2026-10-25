import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { EmptyState, FormErrorBanner, Loading, ProgressBar, Tag, ToastProvider } from './feedback'
import { useToast } from './toast'
import { Button } from './Button'

it('a form error shows the message, the code and the reference support can look up', async () => {
  const { container } = render(<FormErrorBanner error={{ code: 'AUTH-1001', message: "That email or phone number and password don't match.", reference: '7F3K92QD' }} />)

  const alert = screen.getByRole('alert')
  expect(alert).toHaveTextContent("That email or phone number and password don't match.")
  expect(alert).toHaveTextContent('AUTH-1001')
  expect(alert).toHaveTextContent('7F3K92QD')
  await expectNoA11yViolations(container)
})

it('a tag is plain text with a tone', () => {
  render(<Tag tone="warning">Not saved yet</Tag>)
  expect(screen.getByText('Not saved yet')).toBeInTheDocument()
})

it('an empty state says what is missing and offers the next step', () => {
  render(<EmptyState title="No locations yet" action={<Button>Add location</Button>}>Add your branches, or skip for now.</EmptyState>)
  expect(screen.getByRole('heading', { name: 'No locations yet' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Add location' })).toBeInTheDocument()
})

it('loading is announced once, while the placeholder shapes stay silent', () => {
  render(<Loading rows={3} />)
  expect(screen.getByRole('status')).toHaveTextContent('Loading')
})

function Saver() {
  const toast = useToast()
  return <Button onClick={() => toast.show('Location added')}>Save</Button>
}

it('a toast is announced and goes away by itself', async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  render(<ToastProvider><Saver /></ToastProvider>)

  await userEvent.click(screen.getByRole('button', { name: 'Save' }))
  expect(screen.getByRole('status')).toHaveTextContent('Location added')

  act(() => vi.advanceTimersByTime(6000))
  expect(screen.queryByText('Location added')).not.toBeInTheDocument()
  vi.useRealTimers()
})

it('a progress bar reads out how far along it is, and an empty one is at nothing', async () => {
  const { container, rerender } = render(<ProgressBar label="Ready for payroll" value={37} max={40} />)
  const bar = screen.getByRole('progressbar', { name: 'Ready for payroll' })
  expect([bar.getAttribute('aria-valuenow'), bar.getAttribute('aria-valuemax')]).toEqual(['37', '40'])
  expect((bar.firstElementChild as HTMLElement).style.width).toBe('93%')
  await expectNoA11yViolations(container)

  rerender(<ProgressBar label="Ready for payroll" value={0} max={0} />)
  expect((screen.getByRole('progressbar').firstElementChild as HTMLElement).style.width).toBe('0%')
})
