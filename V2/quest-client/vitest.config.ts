import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';

export default defineConfig({
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
