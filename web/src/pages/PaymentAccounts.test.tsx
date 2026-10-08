import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { company, equityAccount, fakeApi, json, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const saved = company()
const path = `/api/companies/${saved.id}/payment-accounts`
const mpesa = equityAccount({ id: '0199c0de-0000-7000-8000-00000000e002', method: 'mpesa', bankCode: null, bankName: null, branchCode: null,
  branchName: null, accountName: null, accountNumberMasked: null, mpesaShortcode: '600123', isDefault: false })

const drawer = () => screen.getByRole('dialog')
const section = () => within(drawer()).getByRole('tabpanel', { name: 'Paying accounts' })

async function openEdit(routes: Parameters<typeof fakeApi>[0] = {}) {
  const calls = fakeApi({ 'POST /api/auth/refresh': () => orgToken(), 'GET /api/companies': () => json(200, [saved]), ...routes })
  const result = renderApp('/setup/companies')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Mama Oliech Restaurant Ltd' }))
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Paying accounts' }))
  await within(section()).findByRole('button', { name: 'Add paying account' })
  return { calls, ...result }
}

const modal = () => screen.getByRole('dialog', { name: 'Add paying account' })
/** The accounts table's rows, without the header. */
const rows = () => within(section()).getAllByRole('row').slice(1)

async function choose(name: string, search: string, option: string) {
  await userEvent.type(within(modal()).getByRole('combobox', { name }), search)
  await userEvent.click(within(modal()).getByRole('option', { name: option }))
}

it('a company without a paying account says payroll needs one; a new company gets accounts once added', async () => {
  const { container } = await openEdit()

  expect(within(section()).getByText(/can't run payroll until it has one/)).toBeInTheDocument()
  await expectNoA11yViolations(container)

  await userEvent.click(within(drawer()).getByRole('button', { name: 'Cancel' }))
  await userEvent.click(screen.getByRole('button', { name: 'Add company' }))
  expect(within(drawer()).getByRole('tab', { name: 'Paying accounts' })).toHaveAttribute('aria-disabled', 'true')
})

it('adds a bank account; the number only shows masked', async () => {
  let accounts: unknown[] = []
  const { calls, container } = await openEdit({
    [`GET ${path}`]: () => json(200, accounts),
    [`POST ${path}`]: () => { accounts = [equityAccount()]; return json(201, accounts[0]) },
  })

  await userEvent.click(within(section()).getByRole('button', { name: 'Add paying account' }))
  expect(within(modal()).getByRole('radio', { name: 'Bank account' })).toBeChecked()
  expect(within(modal()).getByRole('textbox', { name: 'Account name' })).toHaveValue('Mama Oliech Restaurant Ltd')
  await expectNoA11yViolations(container)
  await choose('Bank', 'equ', 'Equity Bank')
  await choose('Branch', 'moi', 'Moi Avenue, Nairobi')
  await userEvent.type(within(modal()).getByRole('textbox', { name: 'Account number' }), '0123 4567 8901')
  await userEvent.click(within(modal()).getByRole('button', { name: 'Add account' }))

  expect(await screen.findByText('Paying account added')).toBeInTheDocument()
  expect(screen.queryByRole('dialog', { name: 'Add paying account' })).not.toBeInTheDocument()
  expect(drawer()).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === path)?.body).toEqual({
    method: 'bank', bankCode: '68', branchCode: '047', accountName: 'Mama Oliech Restaurant Ltd', accountNumber: '0123 4567 8901', mpesaShortcode: null, isDefault: true,
  })
  expect(within(section()).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Pay from', 'Bank and branch', 'Account name', 'Account', 'Default', 'Actions'])
  expect(await within(section()).findByText('•••• 8901')).toBeInTheDocument()
  expect(within(rows()[0]).getAllByRole('cell').map(c => c.textContent).slice(0, 5))
    .toEqual(['Bank account', 'Equity BankMoi Avenue, Nairobi', 'Mama Oliech Restaurant Ltd', '•••• 8901', 'Default'])
})

it('checks the details before sending; problems the server finds show under their fields', async () => {
  const { calls } = await openEdit({
    [`POST ${path}`]: () => json(400, { code: 'ONB-3510', message: 'Some paying account details don\'t look right.', reference: '7F3K92QD', fields: { branchCode: 'Choose a branch of this bank from the list.' } }),
  })
  await userEvent.click(within(section()).getByRole('button', { name: 'Add paying account' }))

  await userEvent.type(within(modal()).getByRole('textbox', { name: 'Account number' }), '12ab')
  await userEvent.click(within(modal()).getByRole('button', { name: 'Add account' }))
  expect(within(modal()).getByRole('combobox', { name: 'Bank' })).toHaveAccessibleDescription('Choose a bank from the list.')
  expect(within(modal()).getByRole('textbox', { name: 'Account number' })).toHaveAccessibleDescription('Enter the account number: 6 to 20 digits.')
  expect(calls.filter(c => c.method === 'POST' && c.path === path)).toHaveLength(0)

  await choose('Bank', 'equ', 'Equity Bank')
  await choose('Branch', 'moi', 'Moi Avenue, Nairobi')
  await userEvent.clear(within(modal()).getByRole('textbox', { name: 'Account number' }))
  await userEvent.type(within(modal()).getByRole('textbox', { name: 'Account number' }), '0123456789')
  await userEvent.click(within(modal()).getByRole('button', { name: 'Add account' }))
  expect(await within(modal()).findByText('Choose a branch of this bank from the list.')).toBeInTheDocument()
})

it('adds an M-Pesa shortcode instead', async () => {
  const { calls } = await openEdit({ [`POST ${path}`]: () => json(201, mpesa) })
  await userEvent.click(within(section()).getByRole('button', { name: 'Add paying account' }))

  await userEvent.click(within(modal()).getByText('M-Pesa'))
  expect(within(modal()).queryByRole('combobox', { name: 'Bank' })).not.toBeInTheDocument()
  await userEvent.type(within(modal()).getByRole('textbox', { name: 'M-Pesa shortcode' }), '600123')
  await userEvent.click(within(modal()).getByRole('button', { name: 'Add account' }))

  expect(await screen.findByText('Paying account added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === path)?.body).toEqual({
    method: 'mpesa', bankCode: null, branchCode: null, accountName: null, accountNumber: null, mpesaShortcode: '600123', isDefault: true,
  })
})

it('makes another account the default, and removes one after asking', async () => {
  let accounts = [equityAccount(), mpesa]
  const { calls } = await openEdit({
    [`GET ${path}`]: () => json(200, accounts),
    [`POST ${path}/${mpesa.id}/default`]: () => { accounts = [equityAccount({ isDefault: false }), { ...mpesa, isDefault: true }]; return json(200, accounts) },
    [`DELETE ${path}/${equityAccount().id}`]: () => { accounts = [{ ...mpesa, isDefault: true }]; return new Response(null, { status: 204 }) },
  })
  await within(section()).findByText('Shortcode 600123')

  await userEvent.click(within(section()).getByRole('button', { name: 'Make default: M-Pesa · Shortcode 600123' }))
  expect(await screen.findByText('Default paying account changed')).toBeInTheDocument()
  expect(rows()[1]).toHaveTextContent('Default')

  await userEvent.click(within(section()).getByRole('button', { name: 'Remove Equity Bank •••• 8901' }))
  const confirm = screen.getByRole('alertdialog', { name: 'Remove Equity Bank •••• 8901?' })
  expect(confirm).toHaveTextContent("It stays on record for past payrolls but won't be used again.")
  await userEvent.click(within(confirm).getByRole('button', { name: 'Remove account' }))

  expect(await screen.findByText('Paying account removed')).toBeInTheDocument()
  expect(calls.filter(c => c.method === 'DELETE')).toHaveLength(1)
  expect(rows()).toHaveLength(1)
})

it('edits an account in a modal; leaving the number empty keeps it', async () => {
  let accounts = [equityAccount()]
  const edited = equityAccount({ id: '0199c0de-0000-7000-8000-00000000e003', bankCode: '01', bankName: 'KCB Bank', branchCode: '100' })
  const { calls } = await openEdit({
    [`GET ${path}`]: () => json(200, accounts),
    [`PUT ${path}/${equityAccount().id}`]: () => { accounts = [edited]; return json(200, edited) },
  })
  await within(section()).findByText('•••• 8901')

  await userEvent.click(within(section()).getByRole('button', { name: 'Edit Equity Bank •••• 8901' }))

  const dialog = screen.getByRole('dialog', { name: 'Edit paying account' })
  expect(within(dialog).getByRole('combobox', { name: 'Bank' })).toHaveValue('Equity Bank')
  expect(within(dialog).getByRole('combobox', { name: 'Branch' })).toHaveValue('Moi Avenue, Nairobi')
  const number = within(dialog).getByRole('textbox', { name: 'Account number' })
  expect(number).toHaveValue('')
  expect(number).toHaveAccessibleDescription('Leave empty to keep •••• 8901.')
  await userEvent.type(within(dialog).getByRole('combobox', { name: 'Bank' }), '{Control>}a{/Control}kcb')
  await userEvent.click(within(dialog).getByRole('option', { name: 'KCB Bank' }))
  await userEvent.type(within(dialog).getByRole('combobox', { name: 'Branch' }), 'moi')
  await userEvent.click(within(dialog).getByRole('option', { name: 'Moi Avenue, Nairobi' }))
  await userEvent.click(within(dialog).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Paying account saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PUT')?.body).toMatchObject({ method: 'bank', bankCode: '01', branchCode: '100', accountNumber: '' })
  expect(await within(section()).findByText('KCB Bank')).toBeInTheDocument()
})

it('the first account is always the default: the toggle is on and locked', async () => {
  await openEdit()
  await userEvent.click(within(section()).getByRole('button', { name: 'Add paying account' }))

  const first = within(modal()).getByRole('switch', { name: 'Use as the default' })
  expect(first).toHaveAttribute('aria-checked', 'true')
  expect(first).toBeDisabled()
  expect(first).toHaveAccessibleDescription('The first account is always the default.')
})

it('another account can be made the default from the modal; the default itself stays locked', async () => {
  const { calls } = await openEdit({
    [`GET ${path}`]: () => json(200, [equityAccount()]),
    [`POST ${path}`]: () => json(201, mpesa),
  })
  await within(section()).findByText('•••• 8901')

  await userEvent.click(within(section()).getByRole('button', { name: 'Edit Equity Bank •••• 8901' }))
  const editing = screen.getByRole('dialog', { name: 'Edit paying account' })
  const locked = within(editing).getByRole('switch', { name: 'Use as the default' })
  expect(locked).toHaveAttribute('aria-checked', 'true')
  expect(locked).toBeDisabled()
  expect(locked).toHaveAccessibleDescription('To change the default, turn this on for another account.')
  await userEvent.click(within(editing).getByRole('button', { name: 'Cancel' }))

  await userEvent.click(within(section()).getByRole('button', { name: 'Add paying account' }))
  const toggle = within(modal()).getByRole('switch', { name: 'Use as the default' })
  expect(toggle).toHaveAttribute('aria-checked', 'false')
  await userEvent.click(within(modal()).getByText('M-Pesa'))
  await userEvent.type(within(modal()).getByRole('textbox', { name: 'M-Pesa shortcode' }), '600123')
  await userEvent.click(toggle)
  await userEvent.click(within(modal()).getByRole('button', { name: 'Add account' }))

  expect(await screen.findByText('Paying account added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === path)?.body).toMatchObject({ method: 'mpesa', isDefault: true })
})
