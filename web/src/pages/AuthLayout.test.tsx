import { screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { fakeApi } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

it('has a navbar with the logo on the left and the theme menu on the right, and no language choice', async () => {
  fakeApi()
  renderApp('/signin')

  const navbar = await screen.findByRole('banner')
  expect(within(navbar).getByRole('link', { name: 'Pesaroll' })).toHaveAttribute('href', '/')
  expect(within(navbar).getByRole('button', { name: 'Theme' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Kiswahili' })).not.toBeInTheDocument()
})

it('the side panel keeps the heading, says what Pesaroll does in general terms, and has no figures', async () => {
  fakeApi()
  renderApp('/signin')

  expect(await screen.findByText('Run your first payroll today.')).toBeInTheDocument()
  expect(screen.getByText(/Payroll, payslips and leave in one place/)).toBeInTheDocument()
  expect(screen.queryByText(/KES 0|~1 hr|sales calls/)).not.toBeInTheDocument()
  expect(screen.queryByText(/SHIF|NSSF|Housing Levy/)).not.toBeInTheDocument()
})
