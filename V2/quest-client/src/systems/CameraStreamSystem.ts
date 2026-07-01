import { createSystem, CameraSource, CameraState, CameraUtils, type Entity } from '@iwsdk/core';
import {
  IWebRTCService,
  IImageQualifierService,
  type QualifierFrame,
} from '@questvisionstream/client';
import { AppConfig } from '../config';
import { getServiceManager } from '../runtime';

/**
 * Owns the passthrough camera. Creates the `CameraSource` entity, and once the
 * camera is `Active`, hands its `MediaStream` to the {@link IWebRTCService} and
 * wires a downsampled frame provider into the {@link IImageQualifierService}.
 *
 * Also enforces the **edge quality gate**: each frame it toggles the outbound
 * video track's `enabled` flag from the qualifier's verdict.
 *
 * Services are resolved by interface token from the RealityCollective
 * `ServiceManager`; the system never imports a concrete service class.
 */
export class CameraStreamSystem extends createSystem({}) {
  private cameraEntity: Entity | undefined;
  private videoTrack: MediaStreamTrack | undefined;
  private connected = false;

  private readonly sampleCanvas = document.createElement('canvas');
  private readonly sampleW = 64;
  private readonly sampleH = 48;

  override init(): void {
    this.sampleCanvas.width = this.sampleW;
    this.sampleCanvas.height = this.sampleH;

    void CameraUtils.getDevices(); // request permission early

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
      const qualifier = getServiceManager().resolve(IImageQualifierService);
      const desired = qualifier.shouldStream;
      if (this.videoTrack.enabled !== desired) this.videoTrack.enabled = desired;
    }
  }

  private beginStreaming(stream: MediaStream): void {
    this.connected = true;
    this.videoTrack = stream.getVideoTracks()[0];

    const webrtc = getServiceManager().resolve(IWebRTCService);
    webrtc.setVideoStream(stream);
    void webrtc.connect();

    const qualifier = getServiceManager().resolve(IImageQualifierService);
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
