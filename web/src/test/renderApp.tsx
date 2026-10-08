import { render } from '@testing-library/react'
import { onTestFinished } from 'vitest'
import App from '../App'
import { createServices } from '../app/services'

/** The whole app at a path, with fresh services (auth, API client, query cache). */
export function renderApp(path: string) {
  const services = createServices({ channelName: `test-${crypto.randomUUID()}` })
  onTestFinished(() => services.auth.dispose())
  const result = render(<App services={services} initialPath={path} />)
  return { ...result, services }
}
