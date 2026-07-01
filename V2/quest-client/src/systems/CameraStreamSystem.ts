import { createSystem, CameraSource, CameraState, CameraUtils, type Entity } from '@iwsdk/core';
import { ServiceManager } from '@realitycollective/service-framework-ts';
import {
  IWebRTCService,
  IImageQualifierService,
  type QualifierFrame,
} from '@questvisionstream/client';
import { AppConfig } from '../config';

/**
 * Owns the passthrough camera. Creates the `CameraSource` entity, and once the
 * camera is `Active`, hands its `MediaStream` to the {@link IWebRTCService} and
 * wires a downsampled frame provider into the {@link IImageQualifierService}.
 *
 * It also enforces the **edge quality gate**: each frame it toggles the outbound
 * video track's `enabled` flag from the qualifier's verdict, so frames that are
 * too dark/over-exposed are not streamed — saving bandwidth and server inference.
 *
 * Services are resolved by interface token from the `ServiceManager` (DI); the
 * system never imports concrete service classes.
 */
export class CameraStreamSystem extends createSystem({}) {
  private cameraEntity: Entity | undefined;
  private videoTrack: MediaStreamTrack | undefined;
  private connected = false;

  // Small reusable canvas for cheap luminance sampling.
  private readonly sampleCanvas = document.createElement('canvas');
  private readonly sampleW = 64;
  private readonly sampleH = 48;

  override init(): void {
    this.sampleCanvas.width = this.sampleW;
    this.sampleCanvas.height = this.sampleH;

    // Request camera permission early (before XR session), per IWSDK guidance.
    void CameraUtils.getDevices();

    this.cameraEntity = this.world.createEntity();
    this.cameraEntity.addComponent(CameraSource, {
      facing: AppConfig.camera.facing,
      width: AppConfig.camera.width,
      height: AppConfig.camera.height,
      frameRate: AppConfig.camera.frameRate,
    });
  }

  override update(): void {
    if (!this.cameraEntity) return;

    if (!this.connected) {
      const state = this.cameraEntity.getValue(CameraSource, 'state');
      if (state !== CameraState.Active) return;
      const stream = this.cameraEntity.getValue(CameraSource, 'stream') as MediaStream | null;
      if (!stream) return;
      this.beginStreaming(stream);
      return;
    }

    // Edge gate: pause/resume the outbound track based on image quality.
    if (this.videoTrack) {
      const qualifier = ServiceManager.instance.getService(IImageQualifierService);
      const desired = qualifier.shouldStream;
      if (this.videoTrack.enabled !== desired) this.videoTrack.enabled = desired;
    }
  }

  private beginStreaming(stream: MediaStream): void {
    this.connected = true;
    this.videoTrack = stream.getVideoTracks()[0];

    const webrtc = ServiceManager.instance.getService(IWebRTCService);
    webrtc.setVideoStream(stream);
    void webrtc.connect();

    const qualifier = ServiceManager.instance.getService(IImageQualifierService);
    qualifier.setFrameProvider(() => this.grabQualifierFrame());
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
