import { createSystem } from '@iwsdk/core';
import * as THREE from 'three';
import {
  IDetectionService,
  DetectionDeduper,
  toRenderBatch,
  type IDetectionRenderer,
  type NormalizedPoint,
  type RenderBatch,
} from '@questvisionstream/client';
import { AppConfig } from '../config';
import { getServiceManager } from '../runtime';
import { LatencyEstimator } from '../rendering/LatencyEstimator';
import { PoseHistory, unprojectThroughSnapshot } from '../rendering/PoseHistory';
import { createTagObject, disposeTagObject } from '../rendering/TagFactory';

/**
 * Host-side implementation of the library's {@link IDetectionRenderer} seam.
 * Resolves the {@link IDetectionService} by token and subscribes to its
 * `detections` event, placing world-anchored tags (the WebXR analogue of the
 * Unity `DetectionSpawnerManager`).
 *
 * Placement uses the **capture-time pose**, not the arrival-time pose: the
 * per-frame update records the camera matrices into a {@link PoseHistory} and
 * detections are unprojected through the pose from
 * `AppConfig.assumedLatencyMs` ago — the Unity reference's P0 "pose-freeze"
 * behaviour. Falls back to the live camera when no history exists yet.
 */
export class DetectionRenderSystem
  extends createSystem({})
  implements IDetectionRenderer
{
  private readonly deduper = new DetectionDeduper({ policy: AppConfig.dedupPolicy });
  private readonly tags: THREE.Object3D[] = [];
  private readonly poseHistory = new PoseHistory();
  private readonly latency = new LatencyEstimator(AppConfig.assumedLatencyMs);
  private unsub: (() => void) | undefined;

  override init(): void {
    const detection = getServiceManager().resolve(IDetectionService);
    this.unsub = detection.on('detections', (payload) => {
      this.latency.observe(performance.now(), payload.pts);
      this.renderDetections(toRenderBatch(payload, { invertY: AppConfig.invertY }));
    });
    this.watchReferenceSpaceReset();
  }

  /**
   * A recenter moves the reference space out from under every world-anchored
   * tag — clear them (they'd all be misplaced). `world.renderer` and
   * `world.session` are verified members of the installed @iwsdk/core 0.4.2;
   * the `reset` event is the standard WebXR reference-space API.
   */
  private watchReferenceSpaceReset(): void {
    const xr = (
      this.world as unknown as {
        renderer?: { xr?: THREE.WebXRManager };
      }
    )?.renderer?.xr;
    if (!xr?.addEventListener) return;
    xr.addEventListener('sessionstart', () => {
      const referenceSpace = xr.getReferenceSpace?.();
      referenceSpace?.addEventListener?.('reset', () => {
        this.clear();
        this.poseHistory.clear();
      });
    });
  }

  override update(): void {
    const camera = this.world?.camera;
    if (camera) this.poseHistory.record(performance.now(), camera);
  }

  renderDetections(batch: RenderBatch): void {
    for (const d of batch.detections) {
      const worldPos = this.placeOnRay(d.center);
      if (!worldPos) continue;
      if (!this.deduper.shouldPlace(d.label, worldPos)) continue;

      const tag = createTagObject(d.label);
      tag.position.copy(worldPos);
      this.scene.add(tag);
      this.tags.push(tag);
    }
  }

  clear(): void {
    for (const tag of this.tags) {
      this.scene.remove(tag);
      disposeTagObject(tag);
    }
    this.tags.length = 0;
    this.deduper.reset();
  }

  override destroy(): void {
    this.unsub?.();
    this.clear();
    this.poseHistory.clear();
  }

  /**
   * Unproject a normalized viewport point to a world point — through the
   * recorded capture-time pose when available, else the live camera.
   */
  private placeOnRay(center: NormalizedPoint): THREE.Vector3 | null {
    const snapshot = this.poseHistory.lookup(performance.now() - this.latency.latencyMs());
    if (snapshot) {
      return unprojectThroughSnapshot(snapshot, center, AppConfig.placementDistanceMeters);
    }

    const camera = this.world?.camera;
    if (!camera) return null;
    const ndc = new THREE.Vector3(center.x * 2 - 1, center.y * 2 - 1, 0.5);
    ndc.unproject(camera);
    const origin = new THREE.Vector3();
    camera.getWorldPosition(origin);
    const dir = ndc.sub(origin).normalize();
    return origin.add(dir.multiplyScalar(AppConfig.placementDistanceMeters));
  }
}
