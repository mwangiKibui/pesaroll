import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import i18n from '../i18n'
import { expectNoA11yViolations } from '../test/a11y'
import { LanguageSwitch, ThemeSwitch } from './switches'

it('the theme switch offers System, Light and Dark and applies the choice', async () => {
  const { container } = render(<ThemeSwitch />)
  expect(screen.getByRole('radio', { name: 'System' })).toBeChecked()

  await userEvent.click(screen.getByRole('radio', { name: 'Dark' }))

  expect(document.documentElement.dataset.theme).toBe('dark')
  expect(screen.getByRole('radio', { name: 'Dark' })).toBeChecked()
  await expectNoA11yViolations(container)
})

it('the language switch changes the words on screen', async () => {
  render(<LanguageSwitch />)

  await userEvent.click(screen.getByRole('button', { name: 'Kiswahili' }))

  expect(i18n.language).toBe('sw')
  expect(screen.getByText('Lugha:')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Kiswahili' })).toHaveAttribute('aria-pressed', 'true')
  await i18n.changeLanguage('en')
})
