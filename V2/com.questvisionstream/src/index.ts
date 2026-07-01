/**
 * @questvisionstream/client
 *
 * Reusable, host-agnostic streaming client for QuestVisionStream, built as a
 * graph of RealityCollective Service Framework services. It speaks the exact
 * WebRTC + data-channel protocol of `QuestVisionStreamServer` and exposes a
 * framework-agnostic rendering seam so any WebXR/browser host (IWSDK, a DOM
 * overlay, tests) can consume detections.
 *
 * Quick start:
 * ```ts
 * const qvs = new QuestVisionStreamClient({ signalingUrl: 'ws://host:3000' });
 * await qvs.start();
 * await qvs.connect(cameraMediaStream);
 * qvs.onDetections((payload) => renderer.renderDetections(toRenderBatch(payload)));
 * // each frame: qvs.update(deltaSeconds);
 * ```
 */

// Orchestration facade
export {
  QuestVisionStreamClient,
  type QuestVisionStreamConfig,
} from './orchestration/QuestVisionStreamClient';

// Services + interface tokens
export { ISignalingService } from './services/signaling/ISignalingService';
export type {
  OfferMessage,
  AnswerMessage,
  CandidateMessage,
  SignalingInbound,
  SignalingOutbound,
} from './services/signaling/ISignalingService';
export { SignalingService, type SignalingConfig } from './services/signaling/SignalingService';

export { IWebRTCService, type WebRTCConnectionState } from './services/webrtc/IWebRTCService';
export {
  WebRTCService,
  type WebRTCConfig,
  type IceServerConfig,
} from './services/webrtc/WebRTCService';

export { IDetectionService } from './services/detection/IDetectionService';
export { DetectionService } from './services/detection/DetectionService';
export {
  isDetectionsPayload,
  type Detection,
  type DetectionsPayload,
  type ServerMessage,
} from './services/detection/types';

export { IImageQualifierService } from './services/qualifier/IImageQualifierService';
export type { QualifierFrameProvider } from './services/qualifier/IImageQualifierService';
export {
  ImageQualifierService,
  type ImageQualifierConfig,
} from './services/qualifier/ImageQualifierService';
export type {
  QualifierFrame,
  QualifierMetric,
  QualityReport,
  IImageQualifierModule,
} from './services/qualifier/types';
export {
  BrightnessQualifierModule,
  type BrightnessQualifierConfig,
} from './services/qualifier/modules/BrightnessQualifierModule';

// Rendering seam + math
export type { IDetectionRenderer } from './rendering/IDetectionRenderer';
export type {
  Vec3,
  NormalizedPoint,
  NormalizedRect,
  RenderableDetection,
  RenderBatch,
} from './rendering/types';
export {
  normalizeDetection,
  toRenderBatch,
  DetectionDeduper,
  type DedupPolicy,
  type DedupOptions,
  type NormalizeOptions,
} from './rendering/DetectionMath';

// Utilities
export { Emitter, type Listener } from './util/Emitter';
export { createLogger, setLogLevel, type LogLevel, type Logger } from './util/logger';
