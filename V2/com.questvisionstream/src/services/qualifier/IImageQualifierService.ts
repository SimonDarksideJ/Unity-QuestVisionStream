import type { IService } from '@realitycollective/service-framework-ts';
import { createServiceToken } from '@realitycollective/service-framework-ts';
import type { Emitter } from '../../util/Emitter';
import type { QualifierFrame, QualityReport } from './types';

/** Supplies frames for analysis. Returns null when no frame is available. */
export type QualifierFrameProvider = () => QualifierFrame | null;

/**
 * Client-side image quality gate. Periodically samples a downsampled camera
 * frame, runs it through analyzer modules (brightness, blur, exposure…), and
 * publishes an aggregate {@link QualityReport}. Acts as an **edge gate**: when
 * quality is poor (e.g. too dark to detect reliably), `shouldStream` goes false
 * so the host can pause sending frames — saving bandwidth and server inference,
 * which directly serves the server's "validate & qualify images" goal but does
 * it at the edge.
 */
export interface IImageQualifierService extends IService {
  /** Latest aggregate verdict, or undefined before the first sample. */
  readonly lastReport: QualityReport | undefined;
  /** Convenience: whether the latest report passed all gates. */
  readonly shouldStream: boolean;
  /** Fires whenever a new report is produced. */
  readonly qualityChanged: Emitter<QualityReport>;

  /** Wire the frame source (called by the host once the camera is ready). */
  setFrameProvider(provider: QualifierFrameProvider): void;
}

export const IImageQualifierService = createServiceToken<IImageQualifierService>(
  'IImageQualifierService',
);
