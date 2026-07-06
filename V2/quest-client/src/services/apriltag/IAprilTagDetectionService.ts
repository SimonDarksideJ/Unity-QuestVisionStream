import { createServiceToken, type IService } from '@realitycollective/service-framework';
import type { DetectedMarker } from '../../apriltags/detector';

/** A raw RGBA frame to decode (as produced by a canvas `getImageData`). */
export interface DetectorFrame {
  readonly width: number;
  readonly height: number;
  readonly data: Uint8ClampedArray;
}

/**
 * **Detection** responsibility: turn pixels into markers, nothing more. Owns the
 * js-aruco2 detector instance; knows nothing about the world, the scene, or what
 * a tag *means* (that's routing). Kept side-effect-free so it's trivially unit
 * testable and swappable (e.g. a WASM AprilTag backend later).
 */
export interface IAprilTagDetectionService extends IService {
  /** True once the underlying detector loaded (vendored scripts present). */
  readonly available: boolean;
  /** Decode all tags in a frame; `[]` if unavailable or none found. */
  detect(frame: DetectorFrame): DetectedMarker[];
}

export const IAprilTagDetectionService =
  createServiceToken<IAprilTagDetectionService>('IAprilTagDetectionService');
