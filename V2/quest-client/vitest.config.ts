import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';

// Mirror vite.config.ts's build-time __CLIENT_VERSION__ so modules that read it
// resolve under test too (see vite.config.ts for the rationale).
const { version } = JSON.parse(
  readFileSync(fileURLToPath(new URL('./package.json', import.meta.url)), 'utf8'),
) as { version: string };

export default defineConfig({
  define: {
    __CLIENT_VERSION__: JSON.stringify(version),
  },
  resolve: {
    // Same source alias as vite.config.ts so the file-linked library resolves
    // without a built dist/.
    alias: {
      '@questvisionstream/client': fileURLToPath(
        new URL('../com.questvisionstream/src/index.ts', import.meta.url),
      ),
    },
  },
  test: {
    environment: 'happy-dom',
    include: ['tests/**/*.test.ts'],
  },
});
