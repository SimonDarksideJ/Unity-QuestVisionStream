import { createSystem } from '@iwsdk/core';
import * as THREE from 'three';
import { ServiceManager } from '@realitycollective/service-framework-ts';
import {
  IDetectionService,
  DetectionDeduper,
  toRenderBatch,
  type IDetectionRenderer,
  type NormalizedPoint,
  type RenderBatch,
} from '@questvisionstream/client';
import { AppConfig } from '../config';
import { createTagObject } from '../rendering/TagFactory';

/**
 * The host-side implementation of the library's {@link IDetectionRenderer} seam.
 * Subscribes (via DI) to the {@link IDetectionService}, transforms each payload
 * into a resolution-independent {@link RenderBatch}, and places world-anchored
 * tags — the WebXR analogue of the Unity `DetectionSpawnerManager`.
 *
 * World placement: the normalized viewport center is unprojected through the XR
 * camera to a ray, and the tag is placed at a fixed distance along it. (A future
 * enhancement raycasts the ray against IWSDK scene-understanding meshes /
 * environment depth for true surface anchoring — see README.)
 */
export class DetectionRenderSystem
  extends createSystem({})
  implements IDetectionRenderer
{
  private readonly deduper = new DetectionDeduper({ policy: AppConfig.dedupPolicy });
  private readonly tags: THREE.Object3D[] = [];
  private unsub: (() => void) | undefined;

  override init(): void {
    const detection = ServiceManager.instance.getService(IDetectionService);
    this.unsub = detection.detections.on((payload) => {
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

    // Normalized [0,1] (bottom-up after invertY) → NDC [-1,1].
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
