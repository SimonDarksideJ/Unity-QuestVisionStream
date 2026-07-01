/**
 * App configuration, resolved from (in priority order): a `?server=` query
 * param, a Vite env var, then a default. Keeping this in one place makes the
 * signaling target easy to change per-deployment without touching code.
 */

interface ImportMetaEnvLike {
  readonly VITE_SIGNALING_URL?: string;
}

function readEnv(): ImportMetaEnvLike {
  // Vite replaces import.meta.env at build time; guard for non-Vite contexts.
  return (import.meta as unknown as { env?: ImportMetaEnvLike }).env ?? {};
}

function resolveSignalingUrl(): string {
  const fromQuery =
    typeof window !== 'undefined'
      ? new URLSearchParams(window.location.search).get('server')
      : null;
  const fromEnv = readEnv().VITE_SIGNALING_URL;
  return fromQuery ?? fromEnv ?? 'ws://localhost:3000';
}

export const AppConfig = {
  /** WebSocket signaling URL of QuestVisionStreamServer. */
  signalingUrl: resolveSignalingUrl(),
  /** Camera capture request (server downsamples further). */
  camera: { facing: 'back' as const, width: 1280, height: 960, frameRate: 30 },
  /** Flip Y when mapping stream boxes to the viewport (matches server v-flip). */
  invertY: true,
  /** Fixed placement distance (m) along the detection ray when no depth hit. */
  placementDistanceMeters: 2.0,
  /** Deduplicate one tag per class (matches the Unity default). */
  dedupPolicy: 'per-class' as const,
};
