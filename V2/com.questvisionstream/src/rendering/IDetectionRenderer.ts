import type { RenderBatch } from './types';

/**
 * Framework-agnostic rendering seam. The library computes resolution-independent
 * {@link RenderBatch}es from server detections; the host (IWSDK/Three.js, a DOM
 * overlay, a test double…) implements this interface to actually place them.
 *
 * This is the port of the Unity `DetectionSpawnerManager` responsibility, split
 * so the transform/dedup logic stays reusable and only the world-placement is
 * host-specific.
 */
export interface IDetectionRenderer {
  /** Render (or update) the tags/boxes for one frame's detections. */
  renderDetections(batch: RenderBatch): void;

  /** Remove all currently rendered detections (e.g. on reference-space reset). */
  clear(): void;
}
