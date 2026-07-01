import type { IService } from '@realitycollective/service-framework-ts';
import { createServiceToken } from '@realitycollective/service-framework-ts';
import type { Emitter } from '../../util/Emitter';
import type { DetectionsPayload } from './types';

/**
 * Parses raw `detections` data-channel messages into validated, typed payloads
 * and republishes them. Also tracks the most recent frame dimensions the server
 * reports (which the server ramps up over time — adaptive resolution), so
 * renderers can normalize boxes against the correct frame size.
 */
export interface IDetectionService extends IService {
  /** Validated detection payloads, in arrival order. */
  readonly detections: Emitter<DetectionsPayload>;

  /** Most recent frame size reported by the server, or undefined before first frame. */
  readonly lastFrameSize: { readonly width: number; readonly height: number } | undefined;

  /** Detections received per second (rolling), for diagnostics. */
  readonly detectionsPerSecond: number;
}

export const IDetectionService = createServiceToken<IDetectionService>('IDetectionService');
