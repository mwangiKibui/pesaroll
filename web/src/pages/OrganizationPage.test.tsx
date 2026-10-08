import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, fakeApi, orgToken, token } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const signedUp = () => ({ 'POST /api/auth/refresh': () => token('no-org-yet') })

it('asks for the organization name, with the setup steps and who is signed in', async () => {
  fakeApi(signedUp())
  const { container } = renderApp('/setup/organization')

  expect(await screen.findByRole('heading', { name: "What's your organization called?" })).toBeInTheDocument()
  expect(screen.getByRole('textbox', { name: 'Organization name' })).toHaveAttribute('placeholder', 'Your organization name')
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getAllByRole('listitem')).toHaveLength(10)
  expect(within(steps).getByText('Organization').closest('li')).toHaveAttribute('aria-current', 'step')
  const navbar = screen.getByRole('banner')
  expect(await within(navbar).findByText('Wanjiru Kamau')).toBeInTheDocument()
  expect(within(navbar).getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /skip/i })).not.toBeInTheDocument()
  expect(screen.getByText("You don't have an organization yet.")).toBeInTheDocument()
  expect(screen.getByText('Set one up now to get started.')).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /Proceed to Org Dashboard/ })).not.toBeInTheDocument()
  expect(screen.getByRole('complementary', { name: 'Setup steps' })).toContainElement(steps)
  await expectNoA11yViolations(container)
})

it('checks the name before sending anything', async () => {
  const calls = fakeApi(signedUp())
  renderApp('/setup/organization')

  await userEvent.type(await screen.findByRole('textbox', { name: 'Organization name' }), ' A ')
  await userEvent.click(screen.getByRole('button', { name: 'Create organization' }))

  expect(screen.getByRole('textbox', { name: 'Organization name' })).toHaveAccessibleDescription(/Enter your organization's name \(2 to 120 characters\)/)
  expect(calls.filter(c => c.path === '/api/organizations')).toHaveLength(0)
})

it('creates the organization, then keeps the name in the field to fix, with Organization ticked and the dashboard offered', async () => {
  const calls = fakeApi(signedUp())
  renderApp('/setup/organization')

  await userEvent.type(await screen.findByRole('textbox', { name: 'Organization name' }), '  Mama Oliech Group ')
  await userEvent.click(screen.getByRole('button', { name: 'Create organization' }))

  expect(await screen.findByText('Your organization is set up.')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/organizations')?.body).toEqual({ name: 'Mama Oliech Group' })
  expect(await screen.findByDisplayValue('Mama Oliech Group')).toBe(screen.getByRole('textbox', { name: 'Organization name' }))
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Create organization' })).not.toBeInTheDocument()
  expect(screen.queryByText("You don't have an organization yet.")).not.toBeInTheDocument()
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Organization').closest('li')).toHaveTextContent('✓')

  await userEvent.click(within(screen.getByRole('banner')).getByRole('link', { name: 'Proceed to Org Dashboard' }))

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
})

it('with an organization, the step shows the name ready to fix, and saving a typo fix updates it', async () => {
  const calls = fakeApi({ 'POST /api/auth/refresh': () => orgToken() })
  renderApp('/setup/organization')

  const field = await screen.findByDisplayValue('Mama Oliech Group')
  expect(screen.getByText('Your organization is set up.')).toBeInTheDocument()
  expect(within(screen.getByRole('banner')).getByRole('link', { name: 'Proceed to Org Dashboard' })).toHaveAttribute('href', '/')

  await userEvent.clear(field)
  await userEvent.type(field, 'Mama Oliech Group Ltd ')
  await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Organization name saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PATCH' && c.path === '/api/organizations/current')?.body).toEqual({ name: 'Mama Oliech Group Ltd' })
})

it('a fix is checked like the first name, and a refusal shows its code', async () => {
  const calls = fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'PATCH /api/organizations/current': () => apiError(403, 'ACC-8001', "You don't have permission to do this. Ask an administrator in your organization."),
  })
  renderApp('/setup/organization')
  const field = await screen.findByDisplayValue('Mama Oliech Group')

  await userEvent.clear(field)
  await userEvent.type(field, 'A')
  await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(field).toHaveAccessibleDescription(/Enter your organization's name \(2 to 120 characters\)/)
  expect(calls.filter(c => c.method === 'PATCH')).toHaveLength(0)

  await userEvent.type(field, 'cme Ltd')
  await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  const alert = await screen.findByRole('alert')
  expect(alert).toHaveTextContent('ACC-8001')
})

it('a name the server refuses shows under the field; other refusals show as a banner with the reference', async () => {
  fakeApi({ ...signedUp(), 'POST /api/organizations': () => apiError(400, 'ONB-3501', "Enter your organization's name (2 to 120 characters).") })
  renderApp('/setup/organization')
  await userEvent.type(await screen.findByRole('textbox', { name: 'Organization name' }), 'Mama Oliech Group')
  await userEvent.click(screen.getByRole('button', { name: 'Create organization' }))
  expect(await screen.findByText("Enter your organization's name (2 to 120 characters).")).toBeInTheDocument()
})

it('already having an organization shows the message with its code and reference', async () => {
  fakeApi({ ...signedUp(), 'POST /api/organizations': () => apiError(409, 'ONB-3502', 'You already have an organization.') })
  renderApp('/setup/organization')
  await userEvent.type(await screen.findByRole('textbox', { name: 'Organization name' }), 'Mama Oliech Group')
  await userEvent.click(screen.getByRole('button', { name: 'Create organization' }))

  const alert = await screen.findByRole('alert')
  expect(alert).toHaveTextContent('You already have an organization.')
  expect(alert).toHaveTextContent('ONB-3502')
  expect(alert).toHaveTextContent('7F3K92QD')
})
