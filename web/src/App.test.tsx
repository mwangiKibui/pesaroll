import { screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { THEME_KEY } from './theme/theme'
import { fakeApi } from './test/fakeApi'
import { renderApp } from './test/renderApp'

afterEach(() => vi.unstubAllGlobals())

it('opens on sign-in for someone without a session, in English', async () => {
  fakeApi()
  renderApp('/')

  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  expect(document.documentElement.lang).toBe('en')
})

it('is light only for now, even on a dark device or after choosing dark before', async () => {
  fakeApi()
  localStorage.setItem(THEME_KEY, 'dark')
  vi.spyOn(window, 'matchMedia').mockReturnValue({ matches: true, addEventListener: () => {}, removeEventListener: () => {} } as unknown as MediaQueryList)

  renderApp('/signin')

  await screen.findByRole('heading', { name: 'Sign in' })
  expect(document.documentElement.dataset.theme).toBe('light')
})
