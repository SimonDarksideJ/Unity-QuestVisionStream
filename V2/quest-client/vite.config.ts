import { defineConfig } from 'vite';
import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';

/**
 * Vite config for the WebXR client. WebXR requires a secure context, so the dev
 * server binds to all interfaces; use an HTTPS tunnel (or IWSDK's managed dev
 * runtime) to reach it from a headset over LAN.
 *
 * The file-linked library (`@questvisionstream/client`) is aliased to its
 * TypeScript source so Vite bundles it directly (instant HMR when editing the
 * library). Its `@realitycollective/service-framework` import resolves from
 * node_modules like any other npm dependency.
 */

// Single source of truth for the reported build: package.json's version. Injected
// as __CLIENT_VERSION__ so the client always tells the server its ACTUAL deployed
// version (no hand-edited string to drift). Bumping the version — which already
// triggers the deploy workflow — updates what's reported automatically.
const { version } = JSON.parse(
  readFileSync(fileURLToPath(new URL('./package.json', import.meta.url)), 'utf8'),
) as { version: string };

export default defineConfig({
  define: {
    __CLIENT_VERSION__: JSON.stringify(version),
  },
  resolve: {
    alias: {
      '@questvisionstream/client': fileURLToPath(
        new URL('../com.questvisionstream/src/index.ts', import.meta.url),
      ),
    },
  },
  server: {
    host: true,
    port: 5173,
  },
  build: {
    target: 'es2022',
    sourcemap: true,
  },
});
