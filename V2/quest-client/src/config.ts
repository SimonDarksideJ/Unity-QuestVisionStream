/**
 * App configuration.
 *
 * The signaling server URL is resolved at runtime (see {@link resolveSignalingUrl})
 * in priority order:
 *   1. `?server=` query param        — explicit per-open override.
 *   2. `/api/config`                 — a Cloudflare Pages Function returning the
 *                                      project's `QVS_SIGNALING_URL` env var,
 *                                      editable ONLINE in the Cloudflare dashboard
 *                                      (per project, no rebuild/redeploy).
 *   3. `VITE_SIGNALING_URL`          — baked at build time.
 *   4. `ws://localhost:3000`         — dev default.
 */

export const AppConfig = {
  /** Camera capture request (server downsamples further). */
  camera: { facing: 'back' as const, width: 1280, height: 960, frameRate: 30 },
  /** Flip Y when mapping stream boxes to the viewport (matches server v-flip). */
  invertY: true,
  /** Fixed placement distance (m) along the detection ray when no depth hit. */
  placementDistanceMeters: 2.0,
  /** Deduplicate one tag per class (matches the Unity default). */
  dedupPolicy: 'per-class' as const,
};

const DEFAULT_SIGNALING_URL = 'ws://localhost:3000';

interface ImportMetaEnvLike {
  readonly VITE_SIGNALING_URL?: string;
}

function readEnv(): ImportMetaEnvLike {
  // Vite replaces import.meta.env at build time; guard for non-Vite contexts.
  return (import.meta as unknown as { env?: ImportMetaEnvLike }).env ?? {};
}

/** Fetch the per-deployment server from the Cloudflare Pages Function. */
async function fetchRuntimeServer(): Promise<string | null> {
  if (typeof fetch !== 'function') return null;
  try {
    const res = await fetch('/api/config', { cache: 'no-store' });
    if (!res.ok) return null;
    const data = (await res.json()) as { server?: unknown };
    return typeof data.server === 'string' && data.server ? data.server : null;
  } catch {
    // Dev server / no Function deployed — fall through to the next tier.
    return null;
  }
}

/** Resolve the signaling server URL (see the priority order above). */
export async function resolveSignalingUrl(): Promise<string> {
  const fromQuery =
    typeof window !== 'undefined'
      ? new URLSearchParams(window.location.search).get('server')
      : null;
  if (fromQuery) return fromQuery;

  const fromRuntime = await fetchRuntimeServer();
  if (fromRuntime) return fromRuntime;

  const fromEnv = readEnv().VITE_SIGNALING_URL;
  if (fromEnv) return fromEnv;

  return DEFAULT_SIGNALING_URL;
}
