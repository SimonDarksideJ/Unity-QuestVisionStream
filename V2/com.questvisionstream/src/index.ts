/**
 * @questvisionstream/client
 *
 * Reusable, host-agnostic streaming client for QuestVisionStream, built as a
 * graph of RealityCollective Service Framework services
 * (`@realitycollective/service-framework`). Speaks the exact WebRTC +
 * data-channel protocol of `QuestVisionStreamServer` and exposes a
 * framework-agnostic rendering seam.
 *
 * Quick start (IWSDK host):
 * ```ts
 * import { startServiceRuntime } from '@realitycollective/service-framework-iwsdk';
 * import { createQuestVisionStreamProfile } from '@questvisionstream/client';
 * const { manager, adapter } = startServiceRuntime(world, (adapter) =>
 *   createQuestVisionStreamProfile('quest-vision-stream', { signalingUrl }, adapter));
 * ```
 */

// Orchestration — the service profile / registrations
export {
  createQuestVisionStreamProfile,
  createQuestVisionStreamRegistrations,
  type QuestVisionStreamOptions,
} from './orchestration/profile';

// Frame-source contract (structural; satisfied by the IWSDK adapter)
export type { FrameSource, FrameTick } from './frame-source';

// Services — interface tokens, event maps, classes + configs
export { ISignalingService } from './services/signaling/ISignalingService';
export type {
  OfferMessage,
  AnswerMessage,
  CandidateMessage,
  SignalingInbound,
  SignalingOutbound,
  SignalingEventMap,
} from './services/signaling/ISignalingService';
export { SignalingService, type SignalingConfig } from './services/signaling/SignalingService';

export { IWebRTCService } from './services/webrtc/IWebRTCService';
export type { WebRTCConnectionState, WebRTCEventMap } from './services/webrtc/IWebRTCService';
export {
  WebRTCService,
  type WebRTCConfig,
  type IceServerConfig,
} from './services/webrtc/WebRTCService';

export { IDetectionService } from './services/detection/IDetectionService';
export type { DetectionEventMap } from './services/detection/IDetectionService';
export { DetectionService, type DetectionConfig } from './services/detection/DetectionService';
export {
  isDetectionsPayload,
  type Detection,
  type DetectionsPayload,
  type ServerMessage,
} from './services/detection/types';

export { IImageQualifierService } from './services/qualifier/IImageQualifierService';
export type {
  QualifierEventMap,
  QualifierFrameProvider,
} from './services/qualifier/IImageQualifierService';
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
  IBrightnessQualifierModule,
  type BrightnessQualifierConfig,
} from './services/qualifier/modules/BrightnessQualifierModule';

// Rendering seam + math (framework-agnostic)
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
export { createLogger, setLogLevel, type LogLevel, type Logger } from './util/logger';
