import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { TabPanel, Tabs } from './Tabs'

function Company({ invalid = [] as string[] }) {
  const [current, setCurrent] = useState('details')
  const tabs = [
    { key: 'details', label: 'Company details' },
    { key: 'numbers', label: 'Statutory numbers', invalid: invalid.includes('numbers') },
    { key: 'contact', label: 'Address and contact' },
  ]
  return (
    <>
      <Tabs id="co" label="Company sections" tabs={tabs} current={current} onChange={setCurrent} />
      <TabPanel id="co" tab="details" current={current}><p>Legal name here</p></TabPanel>
      <TabPanel id="co" tab="numbers" current={current}><input aria-label="KRA PIN" /></TabPanel>
      <TabPanel id="co" tab="contact" current={current}><p>Phone here</p></TabPanel>
    </>
  )
}

it('shows one section at a time; the others stay in the page, hidden, so nothing typed is lost', async () => {
  const { container } = render(<Company />)

  expect(screen.getByRole('tablist', { name: 'Company sections' })).toBeInTheDocument()
  expect(screen.getByRole('tab', { name: 'Company details' })).toHaveAttribute('aria-selected', 'true')
  expect(screen.getByRole('tabpanel', { name: 'Company details' })).toHaveTextContent('Legal name here')
  expect(screen.queryByText('Phone here')).not.toBeVisible()
  await expectNoA11yViolations(container)

  await userEvent.click(screen.getByRole('tab', { name: 'Statutory numbers' }))
  await userEvent.type(screen.getByRole('textbox', { name: 'KRA PIN' }), 'P051')
  await userEvent.click(screen.getByRole('tab', { name: 'Company details' }))
  await userEvent.click(screen.getByRole('tab', { name: 'Statutory numbers' }))
  expect(screen.getByRole('textbox', { name: 'KRA PIN' })).toHaveValue('P051')
})

it('works from the keyboard: arrows move between tabs and pick them, Home and End jump', async () => {
  render(<Company />)
  screen.getByRole('tab', { name: 'Company details' }).focus()

  await userEvent.keyboard('{ArrowRight}')
  expect(screen.getByRole('tab', { name: 'Statutory numbers' })).toHaveFocus()
  expect(screen.getByRole('tab', { name: 'Statutory numbers' })).toHaveAttribute('aria-selected', 'true')
  await userEvent.keyboard('{End}')
  expect(screen.getByRole('tab', { name: 'Address and contact' })).toHaveAttribute('aria-selected', 'true')
  await userEvent.keyboard('{ArrowRight}')
  expect(screen.getByRole('tab', { name: 'Company details' })).toHaveFocus()
  await userEvent.keyboard('{ArrowLeft}{Home}')
  expect(screen.getByRole('tab', { name: 'Company details' })).toHaveAttribute('aria-selected', 'true')
  // Only the chosen tab is in the Tab order.
  expect(screen.getByRole('tab', { name: 'Statutory numbers' })).toHaveAttribute('tabindex', '-1')
})

it('a tab with something to fix says so', async () => {
  const { container } = render(<Company invalid={['numbers']} />)
  expect(screen.getByRole('tab', { name: 'Statutory numbers, needs fixing' })).toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('a tab can be unavailable for now: announced as such, not chosen by click or arrows', async () => {
  function Adding() {
    const [current, setCurrent] = useState('details')
    return (
      <>
        <Tabs id="new" label="Company sections" current={current} onChange={setCurrent}
          tabs={[{ key: 'details', label: 'Company details' }, { key: 'accounts', label: 'Paying accounts', disabled: true }]} />
        <TabPanel id="new" tab="details" current={current}><p>Details</p></TabPanel>
        <TabPanel id="new" tab="accounts" current={current}><p>Accounts</p></TabPanel>
      </>
    )
  }
  const { container } = render(<Adding />)

  const accounts = screen.getByRole('tab', { name: 'Paying accounts' })
  expect(accounts).toHaveAttribute('aria-disabled', 'true')
  await userEvent.click(accounts)
  expect(screen.getByRole('tab', { name: 'Company details' })).toHaveAttribute('aria-selected', 'true')
  screen.getByRole('tab', { name: 'Company details' }).focus()
  await userEvent.keyboard('{ArrowRight}')
  expect(screen.getByRole('tab', { name: 'Company details' })).toHaveAttribute('aria-selected', 'true')
  await expectNoA11yViolations(container)
})
