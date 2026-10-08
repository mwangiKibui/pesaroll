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

async function choose(name: string, search: string, option: string) {
  await userEvent.type(within(section()).getByRole('combobox', { name }), search)
  await userEvent.click(within(section()).getByRole('option', { name: option }))
}

it('a company without a paying account says payroll needs one; a new company is saved first', async () => {
  const { container } = await openEdit()

  expect(within(section()).getByText(/can't run payroll until it has one/)).toBeInTheDocument()
  await expectNoA11yViolations(container)

  await userEvent.click(within(drawer()).getByRole('button', { name: 'Cancel' }))
  await userEvent.click(screen.getByRole('button', { name: 'Add company' }))
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Paying accounts' }))
  expect(within(section()).getByText('Save the company first, then add where its payroll is paid from.')).toBeVisible()
})

it('adds a bank account; the number only shows masked', async () => {
  let accounts: unknown[] = []
  const { calls, container } = await openEdit({
    [`GET ${path}`]: () => json(200, accounts),
    [`POST ${path}`]: () => { accounts = [equityAccount()]; return json(201, accounts[0]) },
  })

  await userEvent.click(within(section()).getByRole('button', { name: 'Add paying account' }))
  expect(within(section()).getByRole('radio', { name: 'Bank account' })).toBeChecked()
  expect(within(section()).getByRole('textbox', { name: 'Account name' })).toHaveValue('Mama Oliech Restaurant Ltd')
  await expectNoA11yViolations(container)
  await choose('Bank', 'equ', 'Equity Bank')
  await choose('Branch', 'moi', 'Moi Avenue, Nairobi')
  await userEvent.type(within(section()).getByRole('textbox', { name: 'Account number' }), '0123 4567 8901')
  await userEvent.click(within(section()).getByRole('button', { name: 'Add account' }))

  expect(await screen.findByText('Paying account added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === path)?.body).toEqual({
    method: 'bank', bankCode: '68', branchCode: '047', accountName: 'Mama Oliech Restaurant Ltd', accountNumber: '0123 4567 8901', mpesaShortcode: null,
  })
  const row = within(section()).getByRole('listitem')
  expect(row).toHaveTextContent('Equity Bank · Moi Avenue, Nairobi')
  expect(row).toHaveTextContent('•••• 8901')
  expect(row).toHaveTextContent('Default')
})

it('checks the details before sending; problems the server finds show under their fields', async () => {
  const { calls } = await openEdit({
    [`POST ${path}`]: () => json(400, { code: 'ONB-3510', message: 'Some paying account details don\'t look right.', reference: '7F3K92QD', fields: { branchCode: 'Choose a branch of this bank from the list.' } }),
  })
  await userEvent.click(within(section()).getByRole('button', { name: 'Add paying account' }))

  await userEvent.type(within(section()).getByRole('textbox', { name: 'Account number' }), '12ab')
  await userEvent.click(within(section()).getByRole('button', { name: 'Add account' }))
  expect(within(section()).getByRole('combobox', { name: 'Bank' })).toHaveAccessibleDescription('Choose a bank from the list.')
  expect(within(section()).getByRole('textbox', { name: 'Account number' })).toHaveAccessibleDescription('Enter the account number: 6 to 20 digits.')
  expect(calls.filter(c => c.method === 'POST' && c.path === path)).toHaveLength(0)

  await choose('Bank', 'equ', 'Equity Bank')
  await choose('Branch', 'moi', 'Moi Avenue, Nairobi')
  await userEvent.clear(within(section()).getByRole('textbox', { name: 'Account number' }))
  await userEvent.type(within(section()).getByRole('textbox', { name: 'Account number' }), '0123456789')
  await userEvent.click(within(section()).getByRole('button', { name: 'Add account' }))
  expect(await within(section()).findByText('Choose a branch of this bank from the list.')).toBeInTheDocument()
})

it('adds an M-Pesa shortcode instead', async () => {
  const { calls } = await openEdit({ [`POST ${path}`]: () => json(201, mpesa) })
  await userEvent.click(within(section()).getByRole('button', { name: 'Add paying account' }))

  await userEvent.click(within(section()).getByText('M-Pesa'))
  expect(within(section()).queryByRole('combobox', { name: 'Bank' })).not.toBeInTheDocument()
  await userEvent.type(within(section()).getByRole('textbox', { name: 'M-Pesa shortcode' }), '600123')
  await userEvent.click(within(section()).getByRole('button', { name: 'Add account' }))

  expect(await screen.findByText('Paying account added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === path)?.body).toEqual({
    method: 'mpesa', bankCode: null, branchCode: null, accountName: null, accountNumber: null, mpesaShortcode: '600123',
  })
})

it('makes another account the default, and removes one after asking', async () => {
  let accounts = [equityAccount(), mpesa]
  const { calls } = await openEdit({
    [`GET ${path}`]: () => json(200, accounts),
    [`POST ${path}/${mpesa.id}/default`]: () => { accounts = [equityAccount({ isDefault: false }), { ...mpesa, isDefault: true }]; return json(200, accounts) },
    [`DELETE ${path}/${equityAccount().id}`]: () => { accounts = [{ ...mpesa, isDefault: true }]; return new Response(null, { status: 204 }) },
  })
  await within(section()).findByText('M-Pesa · Shortcode 600123')

  await userEvent.click(within(section()).getByRole('button', { name: 'Make default: M-Pesa · Shortcode 600123' }))
  expect(await screen.findByText('Default paying account changed')).toBeInTheDocument()
  expect(within(section()).getAllByRole('listitem')[1]).toHaveTextContent('Default')

  await userEvent.click(within(section()).getByRole('button', { name: 'Remove Equity Bank •••• 8901' }))
  const confirm = screen.getByRole('alertdialog', { name: 'Remove Equity Bank •••• 8901?' })
  expect(confirm).toHaveTextContent("It stays on record for past payrolls but won't be used again.")
  await userEvent.click(within(confirm).getByRole('button', { name: 'Remove account' }))

  expect(await screen.findByText('Paying account removed')).toBeInTheDocument()
  expect(calls.filter(c => c.method === 'DELETE')).toHaveLength(1)
  expect(within(section()).getAllByRole('listitem')).toHaveLength(1)
})
