import type { Detection } from '../services/detection/types';

/** Simple 3D vector, framework-agnostic (no Three.js dependency in the library). */
export interface Vec3 {
  x: number;
  y: number;
  z: number;
}

/** A point in normalized viewport space, both axes in [0,1], origin top-left. */
export interface NormalizedPoint {
  x: number;
  y: number;
}

/** A rectangle in normalized viewport space. */
export interface NormalizedRect {
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * A detection transformed into renderer-ready, resolution-independent form. The
 * host renderer turns `center` into a world-anchored tag (via hit-test /
 * unprojection) and/or draws `rect` as a 2D overlay box.
 */
export interface RenderableDetection {
  readonly label: string;
  readonly conf: number;
  /** Normalized center of the box (for world placement). */
  readonly center: NormalizedPoint;
  /** Normalized box rectangle (for 2D overlay drawing). */
  readonly rect: NormalizedRect;
  /** Original detection (stream-pixel bbox) for reference. */
  readonly source: Detection;
}

/** A batch of renderable detections for a single server frame. */
export interface RenderBatch {
  readonly frame: number;
  readonly frameWidth: number;
  readonly frameHeight: number;
  readonly detections: readonly RenderableDetection[];
}
