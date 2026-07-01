import type { IEventService } from '@realitycollective/service-framework';
import { createServiceToken } from '@realitycollective/service-framework';
import type { DetectionsPayload } from './types';

export type DetectionEventMap = {
  /** Validated detection payloads, in arrival order. */
  detections: DetectionsPayload;
};

/**
 * Parses raw `detections` data-channel messages into validated payloads and
 * republishes them, tracking the most recent (adaptive) frame size and rolling
 * throughput. Depends (constructor-injected) on {@link IWebRTCService}.
 */
export interface IDetectionService extends IEventService<DetectionEventMap> {
  readonly lastFrameSize: { readonly width: number; readonly height: number } | undefined;
  readonly detectionsPerSecond: number;
}

export const IDetectionService = createServiceToken<IDetectionService>('IDetectionService');
