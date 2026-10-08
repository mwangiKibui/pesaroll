import { createMemoryHistory, createRootRouteWithContext, createRoute, createRouter, lazyRouteComponent, Outlet, redirect } from '@tanstack/react-router'
import type { AuthClient } from '../auth/authClient'
import { Loading } from '../components/feedback'

// Each page loads only when visited, so phones download less up front.
const SignInPage = lazyRouteComponent(() => import('../pages/SignInPage'))
const SignUpPage = lazyRouteComponent(() => import('../pages/SignUpPage'))
const OrganizationPage = lazyRouteComponent(() => import('../pages/OrganizationPage'))
const HomePage = lazyRouteComponent(() => import('../pages/HomePage'))

interface RouterContext { auth: AuthClient }

/** Only paths on this site, so a planted ?redirect= can't send people elsewhere after sign-in. */
function safeRedirect(value: unknown) {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') ? value : undefined
}

const rootRoute = createRootRouteWithContext<RouterContext>()({ component: Outlet })

// Pages for signed-out people: someone already signed in goes on to where they were heading.
const signedOutOnly = async ({ context, search }: { context: RouterContext; search: { redirect?: string } }) => {
  await context.auth.start()
  if (context.auth.status() === 'signedIn') throw redirect({ href: search.redirect ?? '/' })
}

// Pages that need sign-in: signed-out people sign in first, then come back. Until the
// organization is named (ONB-02), every such page leads to that step.
const signedInWithOrganization = async ({ context, location }: { context: RouterContext; location: { href: string } }) => {
  await context.auth.start()
  if (context.auth.status() !== 'signedIn') throw redirect({ to: '/signin', search: { redirect: location.href } })
  if (!context.auth.organizationId()) throw redirect({ to: '/setup/organization' })
}

// The organization step: for signed-in people who haven't named their organization yet.
const signedInWithoutOrganization = async ({ context, location }: { context: RouterContext; location: { href: string } }) => {
  await context.auth.start()
  if (context.auth.status() !== 'signedIn') throw redirect({ to: '/signin', search: { redirect: location.href } })
  if (context.auth.organizationId()) throw redirect({ to: '/' })
}

const validateRedirect = (search: Record<string, unknown>): { redirect?: string } => ({ redirect: safeRedirect(search.redirect) })

const homeRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', beforeLoad: signedInWithOrganization, component: HomePage })
const signInRoute = createRoute({ getParentRoute: () => rootRoute, path: '/signin', validateSearch: validateRedirect, beforeLoad: signedOutOnly, component: SignInPage })
const signUpRoute = createRoute({ getParentRoute: () => rootRoute, path: '/signup', validateSearch: validateRedirect, beforeLoad: signedOutOnly, component: SignUpPage })
const organizationRoute = createRoute({ getParentRoute: () => rootRoute, path: '/setup/organization', beforeLoad: signedInWithoutOrganization, component: OrganizationPage })
// The old placeholder's address, kept so bookmarks still work.
const welcomeRoute = createRoute({ getParentRoute: () => rootRoute, path: '/welcome', beforeLoad: () => { throw redirect({ to: '/' }) } })

// Development only; production builds leave the components page out.
const devRoutes = import.meta.env.DEV
  ? [createRoute({ getParentRoute: () => rootRoute, path: '/dev/components', component: lazyRouteComponent(() => import('../dev/ComponentsPage')) })]
  : []

const routeTree = rootRoute.addChildren([homeRoute, signInRoute, signUpRoute, organizationRoute, welcomeRoute, ...devRoutes])

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
