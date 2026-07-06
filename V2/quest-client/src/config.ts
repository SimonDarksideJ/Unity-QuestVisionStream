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
  /**
   * Camera capture request (server downsamples further). `facing: 'unknown'`
   * means "use whatever camera is available" — on Quest the passthrough camera
   * enumerates as a single front-labelled device, so requesting `back` fails
   * ("No back-facing camera available"). Use the available one.
   */
  camera: {
    facing: 'unknown' as const,
    width: 1280,
    height: 960,
    frameRate: 30,
    /**
     * Let the edge quality gate pause the OUTBOUND track on low light. Default
     * OFF: a disabled MediaStreamTrack transmits black frames AND blacks the
     * local <video> the qualifier samples, so one "too dark" verdict deadlocks
     * (black → too dark → stays paused). Keeping the stream ungated makes
     * detections fluid; the qualifier still reports brightness for diagnostics.
     */
    gateStreamOnQuality: false,
  },
  /** Flip Y when mapping stream boxes to the viewport (matches server v-flip). */
  invertY: true,
  /** Fixed placement distance (m) along the detection ray when no depth hit. */
  placementDistanceMeters: 2.0,
  /** Deduplicate one tag per class (matches the Unity default). */
  dedupPolicy: 'per-class' as const,
  /**
   * Edge quality gate thresholds (mean luma, 0..255). A frame darker than
   * `minLuma` pauses the outbound stream ("too dark"). The Quest passthrough
   * camera's RAW getUserMedia feed is much darker than the tone-mapped view you
   * see, so `40` can trip in a room that looks bright — lower this once you've
   * confirmed the real luma from the server log / client status.
   */
  qualifier: { minLuma: 40, maxLuma: 230 },
  /**
   * Assumed capture→detection round-trip (ms). Detections are placed through
   * the camera pose from this long ago (see PoseHistory), not the pose at
   * arrival. Tune per deployment; the wire `pts` field enables estimating it
   * dynamically later.
   */
  assumedLatencyMs: 200,

  /**
   * Client-side AprilTag detection (runs locally on the passthrough frame — see
   * AprilTagSystem). Independent of the server object-detection stream.
   *   - `detectIntervalMs`: throttle detection to bound CPU (native ~30fps).
   *   - `width`/`height`: resolution the frame is sampled to before decoding
   *     (36h11 needs enough pixels on the tag; too large is just slower).
   *   - `flipX`/`flipY`: correct a mirrored/rotated passthrough feed on-device —
   *     if planes land mirrored or upside down, toggle these (no rebuild needed
   *     to try: they're read here, but calibrate once and commit).
   *   - `tagTtlMs`: keep a placed plane this long after a tag was last seen.
   */
  apriltag: {
    enabled: true,
    detectIntervalMs: 120,
    width: 640,
    height: 480,
    flipX: false,
    flipY: false,
    tagTtlMs: 8000,
    /**
     * Only place tags present in the registry. AprilTag decoders occasionally
     * misread clutter (even a tag's own printed caption) as a stray id; for a
     * known test set, ignoring unregistered ids removes those ghost planes. Set
     * false to surface every detected tag.
     */
    knownTagsOnly: true,
  },
};

const DEFAULT_SIGNALING_URL = 'ws://localhost:3000';

/** Boot must not hang on a stuck /api/config — cap the fetch and fall through. */
export const CONFIG_FETCH_TIMEOUT_MS = 4000;

/**
 * A signaling target must be a WebSocket URL. Rejecting everything else keeps
 * a crafted `?server=` link (or a poisoned config value) from silently
 * pointing the camera stream at an arbitrary non-signaling endpoint.
 */
export function isValidSignalingUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'ws:' || url.protocol === 'wss:';
  } catch {
    return false;
  }
}

/**
 * A `?server=` override redirects the camera stream — since the whole UX is
 * "scan a QR", a crafted link could silently point the passthrough feed at an
 * attacker's server. Ask the user for non-localhost override targets; the
 * KV/env-configured tiers are deployment-trusted and never prompt.
 */
function confirmOverrideTarget(candidate: string): boolean {
  if (typeof confirm !== 'function') return true; // non-interactive context
  const host = new URL(candidate).hostname;
  if (host === 'localhost' || host === '127.0.0.1') return true; // dev flow
  return confirm(`Stream the headset camera to "${host}"?\n\n(${candidate})`);
}

function acceptSignalingUrl(candidate: string, source: string): string | null {
  if (!isValidSignalingUrl(candidate)) {
    console.warn(`[QuestClient] Ignoring invalid signaling URL from ${source}:`, candidate);
    return null;
  }
  if (source === '?server=' && !confirmOverrideTarget(candidate)) {
    console.warn('[QuestClient] ?server= override declined by the user; ignoring it.');
    return null;
  }
  if (
    typeof window !== 'undefined' &&
    window.location.protocol === 'https:' &&
    candidate.startsWith('ws://') &&
    !candidate.startsWith('ws://localhost')
  ) {
    console.warn(
      `[QuestClient] ${source} uses ws:// on an https page — the browser will block it ` +
        'as mixed content. Use wss:// (TLS tunnel/reverse proxy) for hosted clients.',
    );
  }
  return candidate;
}

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
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), CONFIG_FETCH_TIMEOUT_MS);
  try {
    const res = await fetch('/api/config', { cache: 'no-store', signal: controller.signal });
    if (!res.ok) return null;
    const data = (await res.json()) as { server?: unknown };
    return typeof data.server === 'string' && data.server ? data.server : null;
  } catch {
    // Dev server / no Function deployed / timed out — fall through.
    return null;
  } finally {
    clearTimeout(timeout);
  }
}

/** Resolve the signaling server URL (see the priority order above). */
export async function resolveSignalingUrl(): Promise<string> {
  const fromQuery =
    typeof window !== 'undefined'
      ? new URLSearchParams(window.location.search).get('server')
      : null;
  if (fromQuery) {
    const accepted = acceptSignalingUrl(fromQuery, '?server=');
    if (accepted) return accepted;
  }

  const fromRuntime = await fetchRuntimeServer();
  if (fromRuntime) {
    const accepted = acceptSignalingUrl(fromRuntime, '/api/config');
    if (accepted) return accepted;
  }

  const fromEnv = readEnv().VITE_SIGNALING_URL;
  if (fromEnv) {
    const accepted = acceptSignalingUrl(fromEnv, 'VITE_SIGNALING_URL');
    if (accepted) return accepted;
  }

  return DEFAULT_SIGNALING_URL;
}
