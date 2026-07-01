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
import { createTagObject } from '../rendering/TagFactory';

/**
 * Host-side implementation of the library's {@link IDetectionRenderer} seam.
 * Resolves the {@link IDetectionService} by token and subscribes to its
 * `detections` event, placing world-anchored tags (the WebXR analogue of the
 * Unity `DetectionSpawnerManager`).
 */
export class DetectionRenderSystem
  extends createSystem({})
  implements IDetectionRenderer
{
  private readonly deduper = new DetectionDeduper({ policy: AppConfig.dedupPolicy });
  private readonly tags: THREE.Object3D[] = [];
  private unsub: (() => void) | undefined;

  override init(): void {
    const detection = getServiceManager().resolve(IDetectionService);
    this.unsub = detection.on('detections', (payload) => {
      this.renderDetections(toRenderBatch(payload, { invertY: AppConfig.invertY }));
    });
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
      disposeObject(tag);
    }
    this.tags.length = 0;
    this.deduper.reset();
  }

  override destroy(): void {
    this.unsub?.();
    this.clear();
  }

  /** Unproject a normalized viewport point through the XR camera to a world point. */
  private placeOnRay(center: NormalizedPoint): THREE.Vector3 | null {
    const camera = this.world.camera;
    if (!camera) return null;

    const ndc = new THREE.Vector3(center.x * 2 - 1, center.y * 2 - 1, 0.5);
    ndc.unproject(camera);

    const origin = new THREE.Vector3();
    camera.getWorldPosition(origin);
    const dir = ndc.sub(origin).normalize();
    return origin.add(dir.multiplyScalar(AppConfig.placementDistanceMeters));
  }
}

function disposeObject(root: THREE.Object3D): void {
  root.traverse((obj) => {
    const mesh = obj as THREE.Mesh & { material?: THREE.Material | THREE.Material[] };
    mesh.geometry?.dispose?.();
    const mat = mesh.material;
    if (Array.isArray(mat)) mat.forEach((m) => m.dispose());
    else mat?.dispose?.();
  });
}
