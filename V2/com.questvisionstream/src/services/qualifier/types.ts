import type { IServiceModule } from '@realitycollective/service-framework-ts';

/** A downsampled RGBA frame handed to qualifier modules for analysis. */
export interface QualifierFrame {
  readonly width: number;
  readonly height: number;
  /** RGBA pixels, length = width * height * 4. */
  readonly data: Uint8ClampedArray;
}

/** One analyzer's verdict on a frame. */
export interface QualifierMetric {
  /** Metric name, e.g. 'brightness'. */
  readonly name: string;
  /** Raw measured value (metric-specific units). */
  readonly value: number;
  /** Normalized quality in [0,1] (1 = ideal). */
  readonly score: number;
  /** Whether this metric passes its own threshold. */
  readonly ok: boolean;
  /** Optional human-readable explanation ('too dark', 'over-exposed'…). */
  readonly detail?: string;
}

/** Aggregated verdict across all active qualifier modules. */
export interface QualityReport {
  /** True when every module passed (logical AND) — safe to stream. */
  readonly ok: boolean;
  /** Aggregate score (minimum of module scores — the weakest link). */
  readonly score: number;
  readonly metrics: readonly QualifierMetric[];
  readonly timestamp: number;
}

/**
 * A qualifier module (data provider) that scores one property of a frame.
 * Modules are owned and driven by the {@link IImageQualifierService}.
 */
export interface IImageQualifierModule extends IServiceModule {
  evaluate(frame: QualifierFrame): QualifierMetric;
}
