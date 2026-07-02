import type { IServiceModule } from '@realitycollective/service-framework';

/** A downsampled RGBA frame handed to qualifier modules for analysis. */
export interface QualifierFrame {
  readonly width: number;
  readonly height: number;
  /** RGBA pixels, length = width * height * 4. */
  readonly data: Uint8ClampedArray;
}

/** One analyzer's verdict on a frame. */
export interface QualifierMetric {
  readonly name: string;
  readonly value: number;
  /** Normalized quality in [0,1] (1 = ideal). */
  readonly score: number;
  readonly ok: boolean;
  readonly detail?: string;
}

/** Aggregated verdict across all active qualifier modules. */
export interface QualityReport {
  /** True when every module passed (logical AND) — safe to stream. */
  readonly ok: boolean;
  /** Aggregate score (minimum module score — the weakest link). */
  readonly score: number;
  readonly metrics: readonly QualifierMetric[];
  readonly timestamp: number;
}

/**
 * A qualifier module (RealityCollective service module / data provider) that
 * scores one property of a frame. Owned and driven by the qualifier service.
 */
export interface IImageQualifierModule extends IServiceModule {
  evaluate(frame: QualifierFrame): QualifierMetric;
}
