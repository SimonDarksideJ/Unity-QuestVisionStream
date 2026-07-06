import { createSystem } from '@iwsdk/core';
import * as THREE from 'three';
import {
  IDetectionService,
  toRenderBatch,
  type IDetectionRenderer,
  type NormalizedPoint,
  type NormalizedRect,
  type RenderBatch,
} from '@questvisionstream/client';
import { AppConfig } from '../config';
import { getServiceManager } from '../runtime';
import { LatencyEstimator } from '../rendering/LatencyEstimator';
import {
  PoseHistory,
  unprojectThroughSnapshot,
  type CameraPoseSnapshot,
} from '../rendering/PoseHistory';
import { createDetectionBox, disposeTagObject } from '../rendering/TagFactory';
import { uiLog } from '../ui/uiLog';

/**
 * Host-side implementation of the library's {@link IDetectionRenderer} seam.
 * Resolves the {@link IDetectionService} by token and, per server frame, draws a
 * **red hollow bounding box + label** around every detection — the WebXR
 * analogue of the Unity reference's 2D outline boxes
 * (`SentisInferenceUiManager`), placed in 3D.
 *
 * Boxes are **ephemeral**: each payload clears the previous set and redraws the
 * current one, so what's on screen is always "what's seen right now" (no
 * cross-frame dedup — that was for the persistent-tag mode).
 *
 * Placement uses the **capture-time pose**, not the arrival-time pose: the
 * per-frame update records the camera matrices into a {@link PoseHistory} and
 * each box corner is unprojected through the pose from `AppConfig.assumedLatencyMs`
 * ago — the Unity reference's P0 "pose-freeze" behaviour. Falls back to the live
 * camera when no history exists yet.
 *
 * It also drives the on-page activity log: once per second it reports the frame
 * resolution + what was found ("nothing found" or "class x [bbox]").
 */
export class DetectionRenderSystem
  extends createSystem({})
  implements IDetectionRenderer
{
  private readonly boxes: THREE.Object3D[] = [];
  private readonly poseHistory = new PoseHistory();
  private readonly latency = new LatencyEstimator(AppConfig.assumedLatencyMs);
  private unsub: (() => void) | undefined;
  /** Detection payloads received since the last (1 s-throttled) log line. */
  private payloadsSinceLog = 0;

  override init(): void {
    const detection = getServiceManager().resolve(IDetectionService);
    this.unsub = detection.on('detections', (payload) => {
      this.latency.observe(performance.now(), payload.pts);
      this.logPayload(payload);
      this.renderDetections(toRenderBatch(payload, { invertY: AppConfig.invertY }));
    });
    this.watchReferenceSpaceReset();
  }

  /**
   * A recenter moves the reference space out from under every placed box — clear
   * them (they'd all be misplaced). `world.renderer` and `world.session` are
   * verified members of the installed @iwsdk/core 0.4.2; the `reset` event is
   * the standard WebXR reference-space API.
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
    // Ephemeral: redraw the current frame's boxes from scratch.
    this.clear();

    // One pose lookup per frame so all corners of all boxes share it.
    const snapshot = this.poseHistory.lookup(performance.now() - this.latency.latencyMs());
    const camera = this.world?.camera ?? null;

    for (const d of batch.detections) {
      const corners = this.rectCorners(d.rect).map((p) => this.unproject(p, snapshot, camera));
      if (corners.some((c) => c === null)) continue;

      const label = `${d.label} ${Math.round(d.conf * 100)}%`;
      const box = createDetectionBox(corners as THREE.Vector3[], label);
      this.scene.add(box);
      this.boxes.push(box);
    }
  }

  clear(): void {
    for (const box of this.boxes) {
      this.scene.remove(box);
      disposeTagObject(box);
    }
    this.boxes.length = 0;
  }

  override destroy(): void {
    this.unsub?.();
    this.clear();
    this.poseHistory.clear();
  }

  /** The four corners of a normalized rect: top-left, top-right, bottom-right, bottom-left. */
  private rectCorners(rect: NormalizedRect): NormalizedPoint[] {
    const { x, y, w, h } = rect;
    return [
      { x, y },
      { x: x + w, y },
      { x: x + w, y: y + h },
      { x, y: y + h },
    ];
  }

  /**
   * Unproject a normalized viewport point to a world point — through the recorded
   * capture-time `snapshot` when available, else the live `camera`.
   */
  private unproject(
    point: NormalizedPoint,
    snapshot: CameraPoseSnapshot | null,
    camera: THREE.Camera | null,
  ): THREE.Vector3 | null {
    if (snapshot) {
      return unprojectThroughSnapshot(snapshot, point, AppConfig.placementDistanceMeters);
    }
    if (!camera) return null;
    const ndc = new THREE.Vector3(point.x * 2 - 1, point.y * 2 - 1, 0.5);
    ndc.unproject(camera);
    const origin = new THREE.Vector3();
    camera.getWorldPosition(origin);
    const dir = ndc.sub(origin).normalize();
    return origin.add(dir.multiplyScalar(AppConfig.placementDistanceMeters));
  }

  /**
   * Log the detection payload to the activity panel, at most once per second:
   * resolution + frame, then "nothing found" or "class x [bbox]" per detection.
   */
  private logPayload(payload: {
    width: number;
    height: number;
    frame: number;
    detections: readonly { label: string; conf: number; bbox: readonly number[] }[];
  }): void {
    // Count every payload; report the per-second RATE (not the cumulative frame
    // index) when the 1 s throttle opens, so the log reflects actual throughput.
    this.payloadsSinceLog += 1;
    if (!uiLog.allow('detections', 1000)) return;
    const rate = this.payloadsSinceLog;
    this.payloadsSinceLog = 0;
    const { width, height, detections } = payload;
    if (detections.length === 0) {
      uiLog.push(`↓ ${width}×${height} · ${rate} frames/s · nothing found`);
      return;
    }
    uiLog.push(`↓ ${width}×${height} · ${rate} frames/s · ${detections.length} found`);
    for (const d of detections) {
      const bbox = d.bbox.map((n) => Math.round(n)).join(', ');
      uiLog.push(`   • ${d.label} ${Math.round(d.conf * 100)}% [${bbox}]`);
    }
  }
}
