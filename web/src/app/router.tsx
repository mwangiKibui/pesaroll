import { createMemoryHistory, createRootRouteWithContext, createRoute, createRouter, lazyRouteComponent, Outlet, redirect } from '@tanstack/react-router'
import type { AuthClient } from '../auth/authClient'
import { Loading } from '../components/feedback'

// Each page loads only when visited, so phones download less up front.
const SignInPage = lazyRouteComponent(() => import('../pages/SignInPage'))
const SignUpPage = lazyRouteComponent(() => import('../pages/SignUpPage'))
const WelcomePage = lazyRouteComponent(() => import('../pages/WelcomePage'))

interface RouterContext { auth: AuthClient }

/** Only paths on this site, so a planted ?redirect= can't send people elsewhere after sign-in. */
function safeRedirect(value: unknown) {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') ? value : undefined
}

const rootRoute = createRootRouteWithContext<RouterContext>()({ component: Outlet })

// Pages for signed-out people: someone already signed in goes on to where they were heading.
const signedOutOnly = async ({ context, search }: { context: RouterContext; search: { redirect?: string } }) => {
  await context.auth.start()
  if (context.auth.status() === 'signedIn') throw redirect({ href: search.redirect ?? '/welcome' })
}

// Pages that need sign-in: signed-out people sign in first, then come back.
const signedInOnly = async ({ context, location }: { context: RouterContext; location: { href: string } }) => {
  await context.auth.start()
  if (context.auth.status() !== 'signedIn') throw redirect({ to: '/signin', search: { redirect: location.href } })
}

const validateRedirect = (search: Record<string, unknown>): { redirect?: string } => ({ redirect: safeRedirect(search.redirect) })

const indexRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', beforeLoad: () => { throw redirect({ to: '/welcome' }) } })
const signInRoute = createRoute({ getParentRoute: () => rootRoute, path: '/signin', validateSearch: validateRedirect, beforeLoad: signedOutOnly, component: SignInPage })
const signUpRoute = createRoute({ getParentRoute: () => rootRoute, path: '/signup', validateSearch: validateRedirect, beforeLoad: signedOutOnly, component: SignUpPage })
const welcomeRoute = createRoute({ getParentRoute: () => rootRoute, path: '/welcome', beforeLoad: signedInOnly, component: WelcomePage })

// Development only; production builds leave the components page out.
const devRoutes = import.meta.env.DEV
  ? [createRoute({ getParentRoute: () => rootRoute, path: '/dev/components', component: lazyRouteComponent(() => import('../dev/ComponentsPage')) })]
  : []

const routeTree = rootRoute.addChildren([indexRoute, signInRoute, signUpRoute, welcomeRoute, ...devRoutes])

export function createAppRouter(auth: AuthClient, initialPath?: string) {
  return createRouter({
    routeTree,
    context: { auth },
    history: initialPath ? createMemoryHistory({ initialEntries: [initialPath] }) : undefined,
    defaultPendingComponent: () => <div className="mx-auto max-w-md p-8"><Loading /></div>,
  })
}

declare module '@tanstack/react-router' {
  interface Register { router: ReturnType<typeof createAppRouter> }
}
