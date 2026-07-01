import { defineConfig } from 'vite';
import { fileURLToPath, URL } from 'node:url';

/**
 * Vite config for the WebXR client. WebXR requires a secure context, so the dev
 * server binds to all interfaces; use an HTTPS tunnel (or IWSDK's managed dev
 * runtime) to reach it from a headset over LAN.
 *
 * The two file-linked workspace libraries are aliased to their TypeScript source
 * so Vite bundles them directly (instant HMR when editing the library, and no
 * per-package `node_modules` needed for transitive resolution).
 */
export default defineConfig({
  resolve: {
    alias: {
      '@questvisionstream/client': fileURLToPath(
        new URL('../com.questvisionstream/src/index.ts', import.meta.url),
      ),
      '@realitycollective/service-framework-ts': fileURLToPath(
        new URL('../service-framework/src/index.ts', import.meta.url),
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
