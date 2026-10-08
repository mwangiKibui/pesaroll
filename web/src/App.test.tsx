import { screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { fakeApi } from './test/fakeApi'
import { renderApp } from './test/renderApp'

afterEach(() => vi.unstubAllGlobals())

it('opens on sign-in for someone without a session, in English', async () => {
  fakeApi()
  renderApp('/')

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(document.documentElement.lang).toBe('en')
})
