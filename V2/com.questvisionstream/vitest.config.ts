import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['tests/**/*.test.ts'],
    // Unhandled rejections are asserted EXPLICITLY (tests attach their own
    // process listener and expect zero events) — vitest's implicit
    // fail-on-unhandled would double-report the red cases non-deterministically.
    dangerouslyIgnoreUnhandledErrors: true,
  },
});
