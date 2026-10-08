import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { Button } from './Button'
import { Drawer } from './Drawer'
import { TextField } from './fields'

function Page({ onClose = () => {} }: { onClose?: () => void }) {
  const [open, setOpen] = useState(false)
  const close = () => { setOpen(false); onClose() }
  return (
    <>
      <Button onClick={() => setOpen(true)}>Add location</Button>
      <Drawer open={open} title="Add location" onClose={close}
        footer={<><Button variant="secondary" onClick={close}>Cancel</Button><Button>Add location</Button></>}>
        <TextField label="Location name" placeholder="e.g. Westlands branch" />
      </Drawer>
    </>
  )
}

it('opens as a dialog named by its title, with focus inside', async () => {
  const { container } = render(<Page />)
  await userEvent.click(screen.getByRole('button', { name: 'Add location' }))

  const dialog = screen.getByRole('dialog', { name: 'Add location' })
  expect(dialog).toHaveAttribute('aria-modal', 'true')
  expect(dialog).toContainElement(document.activeElement as HTMLElement)
  await expectNoA11yViolations(container)
})

it('Escape closes it and focus goes back to the button that opened it', async () => {
  const onClose = vi.fn()
  render(<Page onClose={onClose} />)
  const opener = screen.getByRole('button', { name: 'Add location' })
  await userEvent.click(opener)

  await userEvent.keyboard('{Escape}')

  expect(onClose).toHaveBeenCalledOnce()
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(opener).toHaveFocus()
})

it('Tab stays inside the drawer', async () => {
  render(<Page />)
  await userEvent.click(screen.getByRole('button', { name: 'Add location' }))
  const dialog = screen.getByRole('dialog')

  for (let i = 0; i < 6; i++) {
    await userEvent.tab()
    expect(dialog).toContainElement(document.activeElement as HTMLElement)
  }
  await userEvent.tab({ shift: true })
  expect(dialog).toContainElement(document.activeElement as HTMLElement)
})

it('clicking outside closes it', async () => {
  const onClose = vi.fn()
  render(<Page onClose={onClose} />)
  await userEvent.click(screen.getByRole('button', { name: 'Add location' }))

  await userEvent.click(screen.getByTestId('drawer-scrim'))

  expect(onClose).toHaveBeenCalledOnce()
})
