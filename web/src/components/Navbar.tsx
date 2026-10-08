import { Link } from '@tanstack/react-router'
import { Logo } from './Logo'

/** The bar across the top of the public pages: logo on the left. (ThemeMenu goes on the right when dark mode is switched on.) */
export function Navbar() {
  return (
    <header className="sticky top-0 z-20 flex h-16 items-center justify-between border-b border-line bg-card px-4 md:px-8">
      <Link to="/" className="rounded-control no-underline">
        <Logo />
      </Link>
    </header>
  )
}
