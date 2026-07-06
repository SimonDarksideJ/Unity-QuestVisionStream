import type { NormalizedPoint } from '@questvisionstream/client';

/**
 * Thin typed seam over the vendored **js-aruco2** library, which is loaded as a
 * classic script (see index.html) and attaches a global `AR`. We can't `import`
 * it — it self-registers dictionaries onto the global — so we read `window.AR`
 * here and keep the rest of the app in typed TS.
 */

/** A marker corner, in pixels of the image passed to `detectImage`. */
export interface MarkerCorner {
  readonly x: number;
  readonly y: number;
}

/** One detected tag: family id + its four corners (image pixels). */
export interface DetectedMarker {
  readonly id: number;
  readonly corners: readonly MarkerCorner[];
  readonly hammingDistance: number;
}

interface ArucoDetector {
  detectImage(width: number, height: number, data: Uint8ClampedArray): DetectedMarker[];
}

interface ArucoGlobal {
  Detector: new (config: { dictionaryName: string }) => ArucoDetector;
}

declare global {
  // eslint-disable-next-line no-var
  interface Window {
    AR?: ArucoGlobal;
  }
}

/**
 * Build a detector for `dictionaryName`, or null if the vendored scripts didn't
 * load (offline, blocked, or a headless test) or the dictionary is unknown —
 * the caller degrades gracefully instead of throwing.
 */
export function createAprilTagDetector(dictionaryName: string): ArucoDetector | null {
  const AR = typeof window !== 'undefined' ? window.AR : undefined;
  if (!AR?.Detector) return null;
  try {
    return new AR.Detector({ dictionaryName });
  } catch {
    return null;
  }
}

export type { ArucoDetector };

/**
 * Map a marker corner (image pixels, origin top-left) to a normalized viewport
 * point (0..1, origin bottom-left) for {@link unprojectThroughSnapshot}. The Y
 * axis flips because image space is top-down while NDC/viewport is bottom-up;
 * `flipX`/`flipY` correct a mirrored or rotated passthrough feed on-device
 * (Quest's camera orientation is not guaranteed) and are exposed in AppConfig so
 * they can be calibrated without a rebuild.
 */
export function imagePointToViewport(
  x: number,
  y: number,
  width: number,
  height: number,
  flipX: boolean,
  flipY: boolean,
): NormalizedPoint {
  const nx = width > 0 ? x / width : 0;
  const ny = height > 0 ? y / height : 0;
  return {
    x: flipX ? 1 - nx : nx,
    // Default (flipY=false): image-top → viewport-top. Image is top-down and the
    // viewport is bottom-up, so the base mapping already inverts (1 - ny).
    y: flipY ? ny : 1 - ny,
  };
}
