import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, fakeApi, roles, token } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

async function fillDetails({ contact = 'Wanjiru@Example.com', password = 'Mombasa-raha-7' } = {}) {
  await userEvent.type(await screen.findByRole('textbox', { name: 'First name' }), 'Wanjiru')
  await userEvent.type(screen.getByRole('textbox', { name: 'Last name' }), 'Kamau')
  await userEvent.type(screen.getByRole('textbox', { name: 'Email or phone number' }), contact)
  await userEvent.type(screen.getByLabelText('Password'), password)
}

async function fill({ contact = 'Wanjiru@Example.com', password = 'Mombasa-raha-7', role = /CEO or Managing Director/ } = {}) {
  await fillDetails({ contact, password })
  await userEvent.click(screen.getByRole('button', { name: 'Continue' }))
  await userEvent.click(await screen.findByRole('radio', { name: role }))
}

it('step 1 asks for your details, step 2 for your role, and Back keeps what you typed', async () => {
  fakeApi()
  renderApp('/signup')

  expect(await screen.findByText('Step 1 of 2')).toBeInTheDocument()
  expect(screen.queryByRole('radio')).not.toBeInTheDocument()
  await fillDetails()
  await userEvent.click(screen.getByRole('button', { name: 'Continue' }))

  expect(await screen.findByText('Step 2 of 2')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Your role in the business' })).toHaveFocus()
  expect(screen.queryByRole('textbox', { name: 'First name' })).not.toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Back' }))
  expect(await screen.findByRole('textbox', { name: 'First name' })).toHaveValue('Wanjiru')
  expect(screen.getByLabelText('Password')).toHaveValue('Mombasa-raha-7')
})

it('shows the role cards from the API, grouped, with Swahili names in Swahili', async () => {
  fakeApi()
  const { container } = renderApp('/signup')
  await fillDetails()
  await userEvent.click(screen.getByRole('button', { name: 'Continue' }))

  const group = await screen.findByRole('group', { name: 'Leadership' })
  expect(within(group).getByRole('radio', { name: /CEO or Managing Director/ })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Create your account' })).toBeInTheDocument()
  await expectNoA11yViolations(container)

  await i18n.changeLanguage('sw')
  expect(await screen.findByRole('group', { name: 'Uongozi' })).toBeInTheDocument()
  await i18n.changeLanguage('en')
})

it('checks each step before moving on, and sends nothing until both are right', async () => {
  const calls = fakeApi()
  renderApp('/signup')

  await userEvent.click(await screen.findByRole('button', { name: 'Continue' }))

  expect(await screen.findByRole('textbox', { name: 'First name' })).toHaveAccessibleDescription('Enter your first name.')
  expect(screen.getByRole('textbox', { name: 'Email or phone number' })).toHaveAccessibleDescription(/valid email address or Kenyan phone number/)
  expect(screen.getByText('Step 1 of 2')).toBeInTheDocument()

  await fillDetails()
  await userEvent.click(screen.getByRole('button', { name: 'Continue' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Create account' }))

  expect(await screen.findByText('Choose your role.')).toBeInTheDocument()
  expect(calls.filter(c => c.path === '/api/auth/signup')).toHaveLength(0)
})

it('a weak password is refused, and the meter says how strong it is', async () => {
  const calls = fakeApi()
  renderApp('/signup')
  await fillDetails({ password: 'password1' })

  expect(screen.getByText('Password strength: Weak')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Continue' }))

  expect(screen.getByLabelText('Password')).toHaveAccessibleDescription(/Use at least 8 characters/)
  expect(screen.getByText('Step 1 of 2')).toBeInTheDocument()
  expect(calls.filter(c => c.path === '/api/auth/signup')).toHaveLength(0)
})

it('choosing Other asks for the role, and sends it', async () => {
  const calls = fakeApi({ 'POST /api/auth/signup': () => token() })
  renderApp('/signup')
  await fill({ role: /^Other/ })

  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
  expect(await screen.findByRole('textbox', { name: 'Your role' })).toHaveAccessibleDescription('Type your role.')

  await userEvent.type(screen.getByRole('textbox', { name: 'Your role' }), 'Head Chef')
  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

  await screen.findByRole('heading', { name: "What's your organization called?" })
  expect(calls.find(c => c.path === '/api/auth/signup')?.body).toMatchObject({ roleId: roles[1].roles[0].id, roleOther: 'Head Chef' })
})

it('creates the account and goes on to the organization step, signed in', async () => {
  const calls = fakeApi({ 'POST /api/auth/signup': () => token('new-token') })
  renderApp('/signup')
  await fill({ contact: '0712 345 678' })

  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

  expect(await screen.findByRole('heading', { name: "What's your organization called?" })).toBeInTheDocument()
  expect(calls.find(c => c.path === '/api/auth/signup')?.body).toEqual({
    firstName: 'Wanjiru', middleName: null, lastName: 'Kamau', phone: '+254712345678', email: null,
    password: 'Mombasa-raha-7', roleId: roles[0].roles[0].id, roleOther: null,
  })
  expect(calls.find(c => c.path === '/api/auth/me')?.auth).toBe('Bearer new-token')
})

it('an account that already exists takes you back to step 1, with the message under the email or phone', async () => {
  fakeApi({ 'POST /api/auth/signup': () => apiError(409, 'AUTH-1003', 'An account with this email or phone number already exists. Sign in instead.') })
  renderApp('/signup')
  await fill()

  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Email or phone number' })).toHaveAccessibleDescription(/already exists/))
})

it('other server errors show as a banner with the code and reference', async () => {
  fakeApi({ 'POST /api/auth/signup': () => apiError(429, 'AUTH-1006', 'Too many attempts. Wait a minute and try again.') })
  renderApp('/signup')
  await fill()

  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

  const alert = await screen.findByRole('alert')
  expect(alert).toHaveTextContent('Too many attempts')
  expect(alert).toHaveTextContent('AUTH-1006')
  expect(alert).toHaveTextContent('7F3K92QD')
})
