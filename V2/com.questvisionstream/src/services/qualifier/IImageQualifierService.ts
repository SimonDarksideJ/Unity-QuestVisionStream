import type { IEventService } from '@realitycollective/service-framework';
import { createServiceToken } from '@realitycollective/service-framework';
import type { QualifierFrame, QualityReport } from './types';

/** Supplies frames for analysis. Returns null when no frame is available. */
export type QualifierFrameProvider = () => QualifierFrame | null;

export type QualifierEventMap = {
  quality: QualityReport;
};

/**
 * Client-side image quality gate. On a periodic frame tick it samples a
 * downscaled camera frame, runs analyzer modules, and publishes an aggregate
 * {@link QualityReport}. When quality is poor (`shouldStream === false`) the host
 * can pause the outbound track — an edge gate that saves bandwidth + inference.
 */
export interface IImageQualifierService extends IEventService<QualifierEventMap> {
  readonly lastReport: QualityReport | undefined;
  readonly shouldStream: boolean;
  /** Wire the frame source (host provides downscaled camera frames). */
  setFrameProvider(provider: QualifierFrameProvider): void;
}

export const IImageQualifierService =
  createServiceToken<IImageQualifierService>('IImageQualifierService');
