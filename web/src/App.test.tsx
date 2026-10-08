import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { expectNoA11yViolations } from './test/a11y'
import App from './App'

it('until the sign-in screens land (#141), the app shows its name and the language and theme switches', async () => {
  const { container } = render(<App />)

  expect(screen.getByRole('heading', { name: 'Pesaroll' })).toBeInTheDocument()
  expect(screen.getByRole('group', { name: 'Theme' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Kiswahili' })).toBeInTheDocument()
  await expectNoA11yViolations(container)
})
