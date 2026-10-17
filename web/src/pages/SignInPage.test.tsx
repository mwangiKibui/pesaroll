import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, fakeApi, orgToken, token } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

it('signs in with a phone number and goes home', async () => {
  const calls = fakeApi({ 'POST /api/auth/signin': () => orgToken() })
  const { container } = renderApp('/signin')
  await expectNoA11yViolations(await screen.findByRole('main').then(() => container))

  await userEvent.type(screen.getByRole('textbox', { name: 'Email or phone number' }), '0712 345 678')
  await userEvent.type(screen.getByLabelText('Password'), 'Mombasa-raha-7')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

  expect(await screen.findByRole('heading', { name: /Habari, Wanjiru/ })).toBeInTheDocument()
  expect(screen.getAllByText('Mama Oliech Group').length).toBeGreaterThan(0)
  expect(calls.find(c => c.path === '/api/auth/signin')?.body).toEqual({ identifier: '0712 345 678', password: 'Mombasa-raha-7' })
})

it('a wrong password shows the message with its code and reference, and keeps what was typed', async () => {
  fakeApi({ 'POST /api/auth/signin': () => apiError(401, 'AUTH-1001', "That email or phone number and password don't match.") })
  renderApp('/signin')

  await userEvent.type(await screen.findByRole('textbox', { name: 'Email or phone number' }), 'wanjiru@example.com')
  await userEvent.type(screen.getByLabelText('Password'), 'wrong-pass-1')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

  const alert = await screen.findByRole('alert')
  expect(alert).toHaveTextContent("don't match")
  expect(alert).toHaveTextContent('AUTH-1001')
  expect(alert).toHaveTextContent('7F3K92QD')
  expect(screen.getByRole('textbox', { name: 'Email or phone number' })).toHaveValue('wanjiru@example.com')
})

it('error messages are in Swahili when the app is', async () => {
  fakeApi({ 'POST /api/auth/signin': () => apiError(423, 'AUTH-1002', 'Your account is locked after too many attempts. Try again in 15 minutes or reset your password.') })
  await i18n.changeLanguage('sw')
  renderApp('/signin')

  await userEvent.type(await screen.findByRole('textbox', { name: 'Barua pepe au nambari ya simu' }), 'wanjiru@example.com')
  await userEvent.type(screen.getByLabelText('Nenosiri'), 'Mombasa-raha-7')
  await userEvent.click(screen.getByRole('button', { name: 'Ingia' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Akaunti yako imefungwa')
  await i18n.changeLanguage('en')
})

it('someone who signed up but never named their organization is taken to that step', async () => {
  fakeApi({ 'POST /api/auth/signin': () => token() })
  renderApp('/signin')

  await userEvent.type(await screen.findByRole('textbox', { name: 'Email or phone number' }), 'wanjiru@example.com')
  await userEvent.type(screen.getByLabelText('Password'), 'Mombasa-raha-7')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

  expect(await screen.findByRole('heading', { name: "What's your organization called?" })).toBeInTheDocument()
})
