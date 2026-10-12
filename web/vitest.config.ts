import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config.ts'

// Unit tests run in jsdom: components through React Testing Library, plus checks on the
// design tokens and translation files. See docs/standards/ui.md.
export default mergeConfig(viteConfig, defineConfig({
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
    testTimeout: 30_000, // page tests start the whole app; a busy machine shouldn't fail them
    // Four at a time: each worker starts its own jsdom app, and more than four ran Docker's 4 GB out of memory and timed tests out.
    maxWorkers: 4,
  },
}))
