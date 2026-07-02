/**
 * Capture-pose history — groundwork for the Unity reference's P0
 * "pose-freeze" behaviour.
 *
 * A detection arrives one full round-trip (capture → encode → WebRTC →
 * inference → data channel) after the frame it describes. Placing it through
 * the camera pose at *arrival* time plants tags wherever the head happens to
 * point when the reply lands. Instead, the render system records a short
 * history of camera matrices each frame and places detections through the
 * pose from ~capture time (arrival − assumed latency).
 *
 * The lookup key is wall-clock only for now; the server already reports the
 * frame's media timestamp (`pts`), so a future refinement can estimate the
 * true per-session latency instead of assuming a constant.
 */
import * as THREE from 'three';
import type { NormalizedPoint } from '@questvisionstream/client';

export interface CameraPoseSnapshot {
  readonly timeMs: number;
  readonly matrixWorld: THREE.Matrix4;
  readonly projectionMatrixInverse: THREE.Matrix4;
}

export class PoseHistory {
  private readonly snapshots: CameraPoseSnapshot[] = [];

  constructor(private readonly maxAgeMs = 2000) {}

  get size(): number {
    return this.snapshots.length;
  }

  /** Record the camera's current matrices (cloned — snapshots never move). */
  record(timeMs: number, camera: THREE.Camera): void {
    this.snapshots.push({
      timeMs,
      matrixWorld: camera.matrixWorld.clone(),
      projectionMatrixInverse: camera.projectionMatrixInverse.clone(),
    });
    const cutoff = timeMs - this.maxAgeMs;
    while (this.snapshots.length > 1 && this.snapshots[0]!.timeMs < cutoff) {
      this.snapshots.shift();
    }
  }

  /** The snapshot nearest in time, or null when none recorded. */
  lookup(timeMs: number): CameraPoseSnapshot | null {
    let best: CameraPoseSnapshot | null = null;
    let bestDelta = Number.POSITIVE_INFINITY;
    for (const snapshot of this.snapshots) {
      const delta = Math.abs(snapshot.timeMs - timeMs);
      if (delta < bestDelta) {
        bestDelta = delta;
        best = snapshot;
      }
    }
    return best;
  }

  clear(): void {
    this.snapshots.length = 0;
  }
}

/**
 * Unproject a normalized viewport point through a recorded pose and walk
 * `distanceM` along the ray — the historical-matrices equivalent of
 * `THREE.Vector3.unproject(camera)` + the fixed-distance placement.
 */
export function unprojectThroughSnapshot(
  snapshot: CameraPoseSnapshot,
  center: NormalizedPoint,
  distanceM: number,
): THREE.Vector3 {
  const world = new THREE.Vector3(center.x * 2 - 1, center.y * 2 - 1, 0.5)
    .applyMatrix4(snapshot.projectionMatrixInverse)
    .applyMatrix4(snapshot.matrixWorld);
  const origin = new THREE.Vector3().setFromMatrixPosition(snapshot.matrixWorld);
  const direction = world.sub(origin).normalize();
  return origin.add(direction.multiplyScalar(distanceM));
}
