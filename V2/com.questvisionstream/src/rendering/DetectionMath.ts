import type { Detection, DetectionsPayload } from '../services/detection/types';
import type { NormalizedPoint, NormalizedRect, RenderBatch, RenderableDetection, Vec3 } from './types';

export interface NormalizeOptions {
  /**
   * Flip the Y axis. The Python server sends v-flipped frames (see
   * `VideoProcessor.flip_vertical`), so the Unity client used `invertY = true`.
   * Keep configurable to match the capture pipeline.
   */
  readonly invertY?: boolean;
  /**
   * Flip the X axis — for mirrored streams (front-facing cameras, or a server
   * running with `QVS_FLIP_HORIZONTAL=true`). Default false.
   */
  readonly invertX?: boolean;
}

/**
 * Convert a stream-pixel detection into a normalized viewport center + rect,
 * porting `DetectionSpawnerManager.ComputeWorldPosition`'s first stage:
 *
 *   nx = cx / frameW ; ny = cy / frameH ; if invertY: ny = 1 - ny
 *
 * The result is resolution-independent, so it stays correct as the server ramps
 * its frame size up over time (adaptive resolution).
 */
export function normalizeDetection(
  detection: Detection,
  frameWidth: number,
  frameHeight: number,
  options: NormalizeOptions = {},
): { center: NormalizedPoint; rect: NormalizedRect } {
  const invertY = options.invertY ?? true;
  const invertX = options.invertX ?? false;
  const w = Math.max(1, frameWidth);
  const h = Math.max(1, frameHeight);
  const [x1, y1, x2, y2] = detection.bbox;

  const cx = (x1 + x2) * 0.5;
  const cy = (y1 + y2) * 0.5;
  let nx = cx / w;
  let ny = cy / h;
  if (invertY) ny = 1 - ny;
  if (invertX) nx = 1 - nx;

  let rx = Math.min(x1, x2) / w;
  const rw = Math.abs(x2 - x1) / w;
  let ry = Math.min(y1, y2) / h;
  const rh = Math.abs(y2 - y1) / h;
  if (invertY) ry = 1 - ry - rh;
  if (invertX) rx = 1 - rx - rw;

  return { center: { x: nx, y: ny }, rect: { x: rx, y: ry, w: rw, h: rh } };
}

/** Build a full {@link RenderBatch} from a server payload. */
export function toRenderBatch(
  payload: DetectionsPayload,
  options: NormalizeOptions = {},
): RenderBatch {
  const detections: RenderableDetection[] = payload.detections.map((d) => {
    const { center, rect } = normalizeDetection(d, payload.width, payload.height, options);
    return { label: d.label, conf: d.conf, center, rect, source: d };
  });
  return {
    frame: payload.frame,
    frameWidth: payload.width,
    frameHeight: payload.height,
    detections,
  };
}

export type DedupPolicy = 'none' | 'per-class' | 'spatial-per-class';

export interface DedupOptions {
  readonly policy?: DedupPolicy;
  /** For 'spatial-per-class': min separation (metres) between same-class tags. */
  readonly minDistanceMeters?: number;
}

/**
 * Deduplication policy ported from the Unity client, which supported both
 * one-tag-per-class and spatial-per-class (`minDistanceMeters`) modes. Tracks
 * what has already been placed so repeat detections don't spawn duplicates.
 */
export class DetectionDeduper {
  private readonly policy: DedupPolicy;
  private readonly minDistance: number;
  private readonly placedClasses = new Set<string>();
  private readonly placedByClass = new Map<string, Vec3[]>();

  constructor(options: DedupOptions = {}) {
    this.policy = options.policy ?? 'per-class';
    this.minDistance = options.minDistanceMeters ?? 0.3;
  }

  /**
   * Should a detection of `label` at `worldPos` be placed? Records placement
   * when it returns true. `worldPos` is required only for 'spatial-per-class'.
   */
  shouldPlace(label: string, worldPos?: Vec3): boolean {
    switch (this.policy) {
      case 'none':
        return true;
      case 'per-class': {
        if (this.placedClasses.has(label)) return false;
        this.placedClasses.add(label);
        return true;
      }
      case 'spatial-per-class': {
        if (!worldPos) return true;
        const existing = this.placedByClass.get(label) ?? [];
        for (const p of existing) {
          if (distance(p, worldPos) < this.minDistance) return false;
        }
        existing.push(worldPos);
        this.placedByClass.set(label, existing);
        return true;
      }
    }
  }

  reset(): void {
    this.placedClasses.clear();
    this.placedByClass.clear();
  }
}

function distance(a: Vec3, b: Vec3): number {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  const dz = a.z - b.z;
  return Math.sqrt(dx * dx + dy * dy + dz * dz);
}
