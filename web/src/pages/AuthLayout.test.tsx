import { screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { fakeApi } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

it('has a navbar with the logo on the left, and no theme or language choice', async () => {
  fakeApi()
  renderApp('/signin')

  const navbar = await screen.findByRole('banner')
  expect(within(navbar).getByRole('link', { name: 'Pesaroll' })).toHaveAttribute('href', '/')
  expect(screen.queryByRole('button', { name: 'Theme' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Kiswahili' })).not.toBeInTheDocument()
})

it('the side panel keeps the heading, says what Pesaroll does in general terms, and has no figures', async () => {
  fakeApi()
  renderApp('/signin')

  expect(await screen.findByText('Run your first payroll today.')).toBeInTheDocument()
  expect(screen.getByText(/Everything you need to pay and manage your people/)).toBeInTheDocument()
  expect(screen.queryByText(/coming soon/i)).not.toBeInTheDocument()
  expect(screen.queryByText(/KES 0|~1 hr|sales calls/)).not.toBeInTheDocument()
  expect(screen.queryByText(/SHIF|NSSF|Housing Levy/)).not.toBeInTheDocument()
})

it('the side panel lists what Pesaroll covers: every kind of payroll, phones, performance and tasks', async () => {
  fakeApi()
  renderApp('/signin')

  const list = await screen.findByRole('list', { name: 'What Pesaroll does' })
  const items = within(list).getAllByRole('listitem').map(i => i.textContent)
  expect(items.some(t => /hourly/i.test(t ?? ''))).toBe(true)
  expect(items.some(t => /performance/i.test(t ?? ''))).toBe(true)
  expect(items.some(t => /task/i.test(t ?? ''))).toBe(true)
})

it.each(['/signin', '/signup'])('every field on %s has a placeholder: "Your" and the field name', async path => {
  fakeApi()
  renderApp(path)
  await screen.findByRole('heading', { level: 1 })

  const fields = [...document.querySelectorAll<HTMLInputElement>('input:not([type=radio]):not([type=checkbox])')]
  expect(fields.length).toBeGreaterThan(1)
  for (const field of fields) {
    const label = document.querySelector(`label[for="${field.id}"]`)?.firstChild?.textContent ?? ''
    expect(field.placeholder).toBe(`Your ${label.toLowerCase()}`)
  }
})

it.each(['/signin', '/signup'])('on %s, phones show only the form: the side panel is hidden below laptop width', async path => {
  fakeApi()
  renderApp(path)
  await screen.findByRole('heading', { level: 1 })

  // jsdom has no screen sizes, so this checks the responsive classes: hidden by default, shown from lg (1024 px).
  const panel = screen.getByText('Run your first payroll today.').closest('section')
  expect(panel).toHaveClass('hidden', 'lg:flex')
  expect(screen.getByRole('main')).toBeInTheDocument()
})
