import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { Button } from './Button'
import { ConfirmDialog } from './ConfirmDialog'
import { Drawer } from './Drawer'
import { CompanySelect } from './CompanySelect'
import { SelectField, TextField } from './fields'
import { Modal } from './Modal'

/** A drawer with a button that opens a modal on top, like adding a paying account to a company. */
function CompanyDrawer() {
  const [modal, setModal] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const [drawer, setDrawer] = useState(true)
  return (
    <>
      <Drawer open={drawer} title="Edit company" onClose={() => setDrawer(false)}>
        <Button onClick={() => setModal(true)}>Add paying account</Button>
        <Button onClick={() => setConfirm(true)}>Remove account</Button>
      </Drawer>
      <Modal open={modal} title="Add paying account" onClose={() => setModal(false)}
        footer={<><Button variant="secondary" onClick={() => setModal(false)}>Cancel</Button><Button>Add account</Button></>}>
        <TextField label="Account name" placeholder="Your account name" />
        <TextField label="Account number" placeholder="Your account number" />
      </Modal>
      <ConfirmDialog open={confirm} title="Remove account?" confirmLabel="Remove account" onConfirm={() => setConfirm(false)} onCancel={() => setConfirm(false)}>
        It stays on record.
      </ConfirmDialog>
    </>
  )
}

it('opens over the drawer as a dialog named by its title, with focus on its first field', async () => {
  const { container } = render(<CompanyDrawer />)
  await userEvent.click(screen.getByRole('button', { name: 'Add paying account' }))

  const modal = screen.getByRole('dialog', { name: 'Add paying account' })
  expect(modal).toHaveAttribute('aria-modal', 'true')
  expect(screen.getByRole('textbox', { name: 'Account name' })).toHaveFocus()
  await expectNoA11yViolations(container)
})

it('Escape closes only the modal, the drawer stays, and focus goes back to the button that opened it', async () => {
  render(<CompanyDrawer />)
  await userEvent.click(screen.getByRole('button', { name: 'Add paying account' }))

  await userEvent.keyboard('{Escape}')

  expect(screen.queryByRole('dialog', { name: 'Add paying account' })).not.toBeInTheDocument()
  expect(screen.getByRole('dialog', { name: 'Edit company' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Add paying account' })).toHaveFocus()
})

it('Tab stays inside the modal', async () => {
  render(<CompanyDrawer />)
  await userEvent.click(screen.getByRole('button', { name: 'Add paying account' }))

  await userEvent.tab()
  await userEvent.tab()
  await userEvent.tab()
  await userEvent.tab()   // past Add account: back to the first focusable in the modal
  expect(screen.getByRole('dialog', { name: 'Add paying account' })).toContainElement(document.activeElement as HTMLElement)
})

it('a confirmation asked from a drawer closes on Escape without closing the drawer', async () => {
  render(<CompanyDrawer />)
  await userEvent.click(screen.getByRole('button', { name: 'Remove account' }))

  await userEvent.keyboard('{Escape}')

  expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  expect(screen.getByRole('dialog', { name: 'Edit company' })).toBeInTheDocument()
})

it('Escape in a dropdown inside a drawer closes only the dropdown', async () => {
  function Form() {
    const [town, setTown] = useState('')
    const [shown, setShown] = useState({ all: true, ids: [] as string[] })
    const [open, setOpen] = useState(true)
    return (
      <Drawer open={open} title="Add location" onClose={() => setOpen(false)}>
        <SelectField label="Town" placeholder="Your town" options={[{ value: 'nbo', label: 'Nairobi' }]} value={town} onChange={setTown} />
        <CompanySelect label="Apply to" companies={[{ id: 'ke', name: 'Mama Oliech', code: 'KE' }]} value={shown} onChange={setShown} />
      </Drawer>
    )
  }
  render(<Form />)

  await userEvent.click(screen.getByRole('combobox', { name: 'Town' }))
  await userEvent.keyboard('{Escape}')
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  expect(screen.getByRole('dialog', { name: 'Add location' })).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: /Apply to/ }))
  await userEvent.click(screen.getByRole('option', { name: /Mama Oliech/ }))   // focus stays in the list
  await userEvent.keyboard('{Escape}')
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  expect(screen.getByRole('dialog', { name: 'Add location' })).toBeInTheDocument()
})
