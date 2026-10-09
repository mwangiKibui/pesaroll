import { createMemoryHistory, createRootRouteWithContext, createRoute, createRouter, lazyRouteComponent, Outlet, redirect } from '@tanstack/react-router'
import type { AuthClient } from '../auth/authClient'
import type { Services } from './services'
import { Loading } from '../components/feedback'

// Each page loads only when visited, so phones download less up front.
const SignInPage = lazyRouteComponent(() => import('../pages/SignInPage'))
const SignUpPage = lazyRouteComponent(() => import('../pages/SignUpPage'))
const OrganizationPage = lazyRouteComponent(() => import('../pages/OrganizationPage'))
const CompaniesPage = lazyRouteComponent(() => import('../pages/CompaniesPage'))
const LocationsPage = lazyRouteComponent(() => import('../pages/LocationsPage'))
const StructurePage = lazyRouteComponent(() => import('../pages/StructurePage'))
const NoOrganizationPage = lazyRouteComponent(() => import('../pages/NoOrganizationPage'))
const HomePage = lazyRouteComponent(() => import('../pages/HomePage'))

interface RouterContext { auth: AuthClient; api: Services['api'] }

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

// Pages that need sign-in: signed-out people sign in first, then come back. Without an
// organization, every such page leads to the organization step, which decides (below).
const signedInWithOrganization = async ({ context, location }: { context: RouterContext; location: { href: string } }) => {
  await context.auth.start()
  if (context.auth.status() !== 'signedIn') throw redirect({ to: '/signin', search: { redirect: location.href } })
  if (!context.auth.organizationId()) throw redirect({ to: '/setup/organization' })
}

/*
 * Signed in but no organization. Who gets the organization step is decided by the server
 * (/me canOnboardOrganization): only people who signed up themselves (users.signed_up_via = 'self')
 * and haven't onboarded one yet, however much later they come back. Everyone else without an
 * organization sees "You're not part of an organization":
 *  - invited colleagues and employees normally have one (AUTH-08, ACC-03, AUTH-06); if they were
 *    removed, they land here and ask an administrator to add them back
 *  - Pesaroll staff (BO-01) go to the back office and partners (PRN-01) to their partner area;
 *    those tasks route them before these guards, never to the organization step
 */
async function canOnboard(context: RouterContext) {
  const { data } = await context.api.GET('/api/auth/me')
  return data?.canOnboardOrganization === true
}

// The organization step: to name the organization (if allowed to onboard one), or, once it exists,
// to see it set up and go on to the dashboard.
const organizationStep = async ({ context, location }: { context: RouterContext; location: { href: string } }) => {
  await context.auth.start()
  if (context.auth.status() !== 'signedIn') throw redirect({ to: '/signin', search: { redirect: location.href } })
  if (context.auth.organizationId()) return
  if (!(await canOnboard(context))) throw redirect({ to: '/no-organization' })
}

// "You're not part of an organization": signed in, no organization, and not allowed to onboard one.
const noOrganization = async ({ context, location }: { context: RouterContext; location: { href: string } }) => {
  await context.auth.start()
  if (context.auth.status() !== 'signedIn') throw redirect({ to: '/signin', search: { redirect: location.href } })
  if (context.auth.organizationId()) throw redirect({ to: '/' })
  if (await canOnboard(context)) throw redirect({ to: '/setup/organization' })
}

const validateRedirect = (search: Record<string, unknown>): { redirect?: string } => ({ redirect: safeRedirect(search.redirect) })

const homeRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', beforeLoad: signedInWithOrganization, component: HomePage })
const signInRoute = createRoute({ getParentRoute: () => rootRoute, path: '/signin', validateSearch: validateRedirect, beforeLoad: signedOutOnly, component: SignInPage })
const signUpRoute = createRoute({ getParentRoute: () => rootRoute, path: '/signup', validateSearch: validateRedirect, beforeLoad: signedOutOnly, component: SignUpPage })
const organizationRoute = createRoute({ getParentRoute: () => rootRoute, path: '/setup/organization', beforeLoad: organizationStep, component: OrganizationPage })
const companiesRoute = createRoute({ getParentRoute: () => rootRoute, path: '/setup/companies', beforeLoad: signedInWithOrganization, component: CompaniesPage })
const locationsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/setup/locations', beforeLoad: signedInWithOrganization, component: LocationsPage })
const structureRoute = createRoute({ getParentRoute: () => rootRoute, path: '/setup/structure', beforeLoad: signedInWithOrganization, component: StructurePage })
const noOrganizationRoute = createRoute({ getParentRoute: () => rootRoute, path: '/no-organization', beforeLoad: noOrganization, component: NoOrganizationPage })
// The old placeholder's address, kept so bookmarks still work.
const welcomeRoute = createRoute({ getParentRoute: () => rootRoute, path: '/welcome', beforeLoad: () => { throw redirect({ to: '/' }) } })

// Development only; production builds leave the components page out.
const devRoutes = import.meta.env.DEV
  ? [createRoute({ getParentRoute: () => rootRoute, path: '/dev/components', component: lazyRouteComponent(() => import('../dev/ComponentsPage')) })]
  : []

const routeTree = rootRoute.addChildren([homeRoute, signInRoute, signUpRoute, organizationRoute, companiesRoute, locationsRoute, structureRoute, noOrganizationRoute, welcomeRoute, ...devRoutes])

export function createAppRouter(auth: AuthClient, api: Services['api'], initialPath?: string) {
  return createRouter({
    routeTree,
    context: { auth, api },
    history: initialPath ? createMemoryHistory({ initialEntries: [initialPath] }) : undefined,
    defaultPendingComponent: () => <div className="mx-auto max-w-md p-8"><Loading /></div>,
  })
}

declare module '@tanstack/react-router' {
  interface Register { router: ReturnType<typeof createAppRouter> }
}
