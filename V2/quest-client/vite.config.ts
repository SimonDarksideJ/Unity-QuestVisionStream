import { defineConfig } from 'vite';
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
export default defineConfig({
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
