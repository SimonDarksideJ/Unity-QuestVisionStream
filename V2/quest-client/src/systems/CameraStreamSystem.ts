import { createSystem, CameraSource, CameraState, CameraUtils, type Entity } from '@iwsdk/core';
import {
  IWebRTCService,
  IImageQualifierService,
  type IImageQualifierService as QualifierService,
  type QualifierFrame,
} from '@questvisionstream/client';
import { AppConfig } from '../config';
import { getServiceManager } from '../runtime';
import { status } from '../ui/status';

/**
 * Owns the passthrough camera. Creates the `CameraSource` entity, and once the
 * camera is `Active`, hands its `MediaStream` to the {@link IWebRTCService} and
 * wires a downsampled frame provider into the {@link IImageQualifierService}.
 *
 * Also enforces the **edge quality gate**: each frame it toggles the outbound
 * video track's `enabled` flag from the qualifier's verdict.
 *
 * Failure paths report to the status surface — a denied camera permission
 * (`CameraState.Error`) or a rejected connect() must never be console-only.
 *
 * Services are resolved by interface token from the RealityCollective
 * `ServiceManager`; the system never imports a concrete service class.
 */
export class CameraStreamSystem extends createSystem({}) {
  private cameraEntity: Entity | undefined;
  private videoTrack: MediaStreamTrack | undefined;
  private qualifier: QualifierService | undefined;
  private connected = false;
  private cameraFailed = false;

  private readonly sampleCanvas = document.createElement('canvas');
  private readonly sampleW = 64;
  private readonly sampleH = 48;

  override init(): void {
    this.sampleCanvas.width = this.sampleW;
    this.sampleCanvas.height = this.sampleH;

    void CameraUtils.getDevices(); // request permission early
    status.set('camera', 'starting…');

    this.cameraEntity = this.world.createEntity();
    this.cameraEntity.addComponent(CameraSource, {
      facing: AppConfig.camera.facing,
      width: AppConfig.camera.width,
      height: AppConfig.camera.height,
      frameRate: AppConfig.camera.frameRate,
    });
  }

  override update(): void {
    if (!this.cameraEntity || this.cameraFailed) return;

    if (!this.connected) {
      const state = this.cameraEntity.getValue(CameraSource, 'state');
      if (state === CameraState.Error) {
        // Terminal: without this branch a denied permission spins the poll
        // forever with no feedback anywhere.
        this.cameraFailed = true;
        status.set('camera', 'error — camera unavailable (check the browser permission)');
        console.error('[CameraStream] CameraSource entered Error state (permission denied?)');
        return;
      }
      if (state !== CameraState.Active) return;
      const stream = this.cameraEntity.getValue(CameraSource, 'stream') as MediaStream | null;
      if (!stream) return;
      this.beginStreaming(stream);
      return;
    }

    // Edge gate: pause/resume the outbound track based on image quality.
    if (this.videoTrack && this.qualifier) {
      const desired = this.qualifier.shouldStream;
      if (this.videoTrack.enabled !== desired) this.videoTrack.enabled = desired;
    }
  }

  private beginStreaming(stream: MediaStream): void {
    this.connected = true;
    this.videoTrack = stream.getVideoTracks()[0];
    status.set('camera', 'active');

    const webrtc = getServiceManager().resolve(IWebRTCService);
    webrtc.setVideoStream(stream);
    webrtc.connect().catch((err) => {
      // The library auto-recovers when signaling comes back; surface the
      // failure instead of swallowing it.
      status.set('connection', `failed: ${err instanceof Error ? err.message : String(err)}`);
      console.error('[CameraStream] connect() failed', err);
    });

    // Cache the handle — resolving by token every frame is a wasted lookup.
    this.qualifier = getServiceManager().resolve(IImageQualifierService);
    this.qualifier.setFrameProvider(() => this.grabQualifierFrame());
  }

  /** Capture a downsampled RGBA frame for the qualifier modules. */
  private grabQualifierFrame(): QualifierFrame | null {
    if (!this.cameraEntity) return null;
    const source = CameraUtils.captureFrame(this.cameraEntity);
    if (!source) return null;

    const ctx = this.sampleCanvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;
    ctx.drawImage(source, 0, 0, this.sampleW, this.sampleH);
    const image = ctx.getImageData(0, 0, this.sampleW, this.sampleH);
    return { width: image.width, height: image.height, data: image.data };
  }
}
