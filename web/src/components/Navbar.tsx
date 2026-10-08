import { Link } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { Logo } from './Logo'

/** The bar across the top: logo on the left, anything passed in (who is signed in, Sign out) on the right. */
export function Navbar({ children }: { children?: ReactNode }) {
  return (
    <header className="sticky top-0 z-20 flex h-16 items-center justify-between border-b border-line bg-card px-4 md:px-8">
      <Link to="/" className="rounded-control no-underline">
        <Logo />
      </Link>
      {children}
    </header>
  )
}
