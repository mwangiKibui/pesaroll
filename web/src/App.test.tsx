import { screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { fakeApi } from './test/fakeApi'
import { renderApp } from './test/renderApp'

afterEach(() => vi.unstubAllGlobals())

it('opens on sign-in for someone without a session, with the language and theme switches', async () => {
  fakeApi()
  renderApp('/')

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Kiswahili' })).toBeInTheDocument()
  expect(screen.getByRole('group', { name: 'Theme' })).toBeInTheDocument()
})
