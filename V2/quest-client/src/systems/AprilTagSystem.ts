import { createSystem, CameraSource, CameraState, CameraUtils } from '@iwsdk/core';
import type * as THREE from 'three';
import { getServiceManager } from '../runtime';
import { PoseHistory } from '../rendering/PoseHistory';
import {
  IAprilTagDetectionService,
  type DetectorFrame,
} from '../services/apriltag/IAprilTagDetectionService';
import { IAprilTagConfigService } from '../services/apriltag/IAprilTagConfigService';
import { IAprilTagRoutingService } from '../services/apriltag/IAprilTagRoutingService';
import { IAprilTagPlacementService } from '../services/apriltag/IAprilTagPlacementService';
import { uiLog } from '../ui/uiLog';

/**
 * Thin composition root for the AprilTag pipeline. It owns nothing but the
 * per-frame plumbing that *requires* IWSDK access — capturing the passthrough
 * frame and recording the camera pose — and delegates every decision to the
 * services: it captures → asks **detection** for markers → hands them to
 * **routing**, which emits lifecycle events that **placement** turns into planes.
 * All logic (what a tag means, TTL, rules, rendering) lives in those services;
 * this class just moves data across the seam only a System can reach.
 */
export class AprilTagSystem extends createSystem({
  cameras: { required: [CameraSource] },
}) {
  private readonly poseHistory = new PoseHistory();
  private readonly canvas = document.createElement('canvas');
  private cfg!: IAprilTagConfigService;
  private detection!: IAprilTagDetectionService;
  private routing!: IAprilTagRoutingService;
  private placement!: IAprilTagPlacementService;
  private enabled = false;
  private lastDetectAt = 0;

  override init(): void {
    const manager = getServiceManager();
    this.cfg = manager.resolve(IAprilTagConfigService);
    this.enabled = this.cfg.enabled;
    if (!this.enabled) return;

    this.detection = manager.resolve(IAprilTagDetectionService);
    this.routing = manager.resolve(IAprilTagRoutingService);
    this.placement = manager.resolve(IAprilTagPlacementService);

    this.canvas.width = this.cfg.sampleWidth;
    this.canvas.height = this.cfg.sampleHeight;

    // Give placement the render surface + a capture-time pose source. Routing
    // emits synchronously inside update(), so `lookup(now)` is the current frame.
    this.placement.attach(this.scene, () => this.poseHistory.lookup(performance.now()));

    uiLog.push(
      this.detection.available
        ? `▣ AprilTag detector ready (${this.cfg.dictionary})`
        : '▲ AprilTag detector unavailable (js-aruco2 vendor scripts not loaded)',
    );
    this.watchReferenceSpaceReset();
  }

  override update(): void {
    if (!this.enabled) return;

    // Pose is recorded every frame (dense history); decoding is throttled.
    const now = performance.now();
    const camera = this.world?.camera;
    if (camera) this.poseHistory.record(now, camera);
    if (now - this.lastDetectAt < this.cfg.detectIntervalMs) return;

    const entity = this.firstCamera();
    if (!entity || entity.getValue(CameraSource, 'state') !== CameraState.Active) return;
    this.lastDetectAt = now;

    const frame = this.grabFrame(entity);
    if (!frame) return;
    const markers = this.detection.detect(frame);
    this.routing.ingest(markers, now); // routing emits → placement draws/removes
    if (markers.length) {
      uiLog.throttle('apriltag', 1000, `▣ tags: ${markers.map((m) => `#${m.id}`).join(', ')}`);
    }
  }

  /** First entity carrying a CameraSource (the query yields a Set, not an array). */
  private firstCamera(): Parameters<typeof CameraUtils.captureFrame>[0] | undefined {
    for (const entity of this.queries.cameras.entities) return entity;
    return undefined;
  }

  /** Sample the passthrough frame down to the configured decode resolution. */
  private grabFrame(entity: Parameters<typeof CameraUtils.captureFrame>[0]): DetectorFrame | null {
    const source = CameraUtils.captureFrame(entity);
    if (!source) return null;
    const ctx = this.canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;
    const { width, height } = this.canvas;
    ctx.drawImage(source, 0, 0, width, height);
    const image = ctx.getImageData(0, 0, width, height);
    return { width, height, data: image.data };
  }

  private watchReferenceSpaceReset(): void {
    const xr = (this.world as unknown as { renderer?: { xr?: THREE.WebXRManager } })?.renderer?.xr;
    if (!xr?.addEventListener) return;
    xr.addEventListener('sessionstart', () => {
      const referenceSpace = xr.getReferenceSpace?.();
      referenceSpace?.addEventListener?.('reset', () => {
        this.placement.clear();
        this.routing.forgetAll();
        this.poseHistory.clear();
      });
    });
  }

  override destroy(): void {
    this.placement?.clear();
    this.poseHistory.clear();
  }
}
