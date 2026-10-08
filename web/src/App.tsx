import { QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { createAppRouter } from './app/router'
import { ServicesContext, type Services } from './app/services'
import { ToastProvider } from './components/feedback'

/** The app: services for every screen, and the router. initialPath is for tests. */
export default function App({ services, initialPath }: { services: Services; initialPath?: string }) {
  const [router] = useState(() => createAppRouter(services.auth, initialPath))

  // Signing in or out (here or in another tab) re-checks the current page's guard,
  // which moves the person to the right page.
  useEffect(() => services.auth.subscribe(() => { void router.invalidate() }), [services, router])

  return (
    <ServicesContext.Provider value={services}>
      <QueryClientProvider client={services.queryClient}>
        <ToastProvider>
          <RouterProvider router={router} />
        </ToastProvider>
      </QueryClientProvider>
    </ServicesContext.Provider>
  )
}
