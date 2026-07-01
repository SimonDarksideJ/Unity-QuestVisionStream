import { ServiceManager } from '@realitycollective/service-framework-ts';
import { setLogLevel, type LogLevel } from '../util/logger';
import { ISignalingService } from '../services/signaling/ISignalingService';
import { SignalingService } from '../services/signaling/SignalingService';
import { IWebRTCService } from '../services/webrtc/IWebRTCService';
import { WebRTCService, type IceServerConfig } from '../services/webrtc/WebRTCService';
import { IDetectionService } from '../services/detection/IDetectionService';
import { DetectionService } from '../services/detection/DetectionService';
import { IImageQualifierService } from '../services/qualifier/IImageQualifierService';
import { ImageQualifierService } from '../services/qualifier/ImageQualifierService';
import {
  BrightnessQualifierModule,
  type BrightnessQualifierConfig,
} from '../services/qualifier/modules/BrightnessQualifierModule';
import type { QualifierFrameProvider } from '../services/qualifier/IImageQualifierService';
import type { DetectionsPayload } from '../services/detection/types';
import type { Listener } from '../util/Emitter';

export interface QuestVisionStreamConfig {
  /** WebSocket signaling URL, e.g. `ws://192.168.1.20:3000`. */
  readonly signalingUrl: string;
  /** ICE servers. Defaults to Google STUN (LAN direct). Add TURN for remote. */
  readonly iceServers?: readonly IceServerConfig[];
  /** Client-side image qualifier gate. Enabled by default. */
  readonly qualifier?: {
    readonly enabled?: boolean;
    readonly sampleIntervalMs?: number;
    readonly brightness?: BrightnessQualifierConfig;
  };
  /** Log verbosity. Default 'info'. */
  readonly logLevel?: LogLevel;
}

/**
 * High-level facade over the streaming service graph. Constructs every service,
 * wires their dependencies via constructor injection (proper DI), and registers
 * them on a {@link ServiceManager} under their interface tokens. The host then
 * pumps `update(delta)` each frame and consumes typed events.
 *
 * The graph and its priority-ordered lifecycle:
 *   SignalingService(10) → ImageQualifierService(15) → WebRTCService(20)
 *      → DetectionService(30)
 */
export class QuestVisionStreamClient {
  readonly manager: ServiceManager;
  readonly signaling: SignalingService;
  readonly qualifier: ImageQualifierService;
  readonly webrtc: WebRTCService;
  readonly detection: DetectionService;

  constructor(config: QuestVisionStreamConfig, manager: ServiceManager = ServiceManager.instance) {
    if (config.logLevel) setLogLevel(config.logLevel);
    this.manager = manager;

    // --- construct with dependency injection ---
    this.signaling = new SignalingService({ url: config.signalingUrl, autoConnect: false });

    this.qualifier = new ImageQualifierService({
      sampleIntervalMs: config.qualifier?.sampleIntervalMs,
    });
    if (config.qualifier?.enabled ?? true) {
      this.qualifier.use(new BrightnessQualifierModule(this.qualifier, config.qualifier?.brightness));
    }

    this.webrtc = new WebRTCService(
      { iceServers: config.iceServers },
      this.signaling,
    );
    this.detection = new DetectionService(this.webrtc);

    // --- register by interface token ---
    this.manager.registerService(ISignalingService, this.signaling);
    this.manager.registerService(IImageQualifierService, this.qualifier);
    this.manager.registerService(IWebRTCService, this.webrtc);
    this.manager.registerService(IDetectionService, this.detection);
  }

  /** Initialize + start all services (opens the signaling socket). */
  async start(): Promise<void> {
    await this.manager.start();
  }

  /** Per-frame pump. Call from the host's render/update loop. */
  update(deltaSeconds: number): void {
    this.manager.update(deltaSeconds);
  }

  /** Provide the camera media stream and open the peer connection. */
  async connect(stream: MediaStream): Promise<void> {
    this.webrtc.setVideoStream(stream);
    await this.webrtc.connect();
  }

  /** Wire the qualifier's frame source (downsampled camera frames). */
  setQualifierFrameProvider(provider: QualifierFrameProvider): void {
    this.qualifier.setFrameProvider(provider);
  }

  /** Subscribe to typed detection payloads. Returns an unsubscribe function. */
  onDetections(listener: Listener<DetectionsPayload>): () => void {
    return this.detection.detections.on(listener);
  }

  /** True when the qualifier gate currently permits streaming. */
  get shouldStream(): boolean {
    return this.qualifier.shouldStream;
  }

  /** Tear everything down. */
  async dispose(): Promise<void> {
    await this.manager.destroy();
  }
}
