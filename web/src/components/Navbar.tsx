import { Link } from '@tanstack/react-router'
import { Logo } from './Logo'
import { ThemeMenu } from './ThemeMenu'

/** The bar across the top of the public pages: logo on the left, theme menu on the right. */
export function Navbar() {
  return (
    <header className="sticky top-0 z-20 flex h-16 items-center justify-between border-b border-line bg-card px-4 md:px-8">
      <Link to="/" className="rounded-control no-underline">
        <Logo />
      </Link>
      <ThemeMenu />
    </header>
  )
}
