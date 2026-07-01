import { BaseServiceModule, type IService } from '@realitycollective/service-framework-ts';
import type { IImageQualifierModule, QualifierFrame, QualifierMetric } from '../types';

export interface BrightnessQualifierConfig {
  /** Minimum acceptable mean luminance (0..255). Default 40 (too dark below). */
  readonly minLuma?: number;
  /** Maximum acceptable mean luminance (0..255). Default 230 (blown out above). */
  readonly maxLuma?: number;
  /** Smoothing window (frames). Default 10. */
  readonly bufferSize?: number;
}

/**
 * Scores frame brightness using Rec.709 luminance, mirroring the Unity
 * `BrightnessEstimationManager` (weights 0.2126 R, 0.7152 G, 0.0722 B), smoothed
 * over a ring buffer. Flags frames that are too dark or over-exposed for
 * reliable detection.
 */
export class BrightnessQualifierModule extends BaseServiceModule implements IImageQualifierModule {
  private readonly minLuma: number;
  private readonly maxLuma: number;
  private readonly bufferSize: number;
  private readonly history: number[] = [];

  constructor(parent: IService, config: BrightnessQualifierConfig = {}) {
    super('BrightnessQualifierModule', parent, 10);
    this.minLuma = config.minLuma ?? 40;
    this.maxLuma = config.maxLuma ?? 230;
    this.bufferSize = config.bufferSize ?? 10;
  }

  evaluate(frame: QualifierFrame): QualifierMetric {
    const { data } = frame;
    let sum = 0;
    const pixels = data.length / 4;
    for (let i = 0; i < data.length; i += 4) {
      sum += 0.2126 * data[i]! + 0.7152 * data[i + 1]! + 0.0722 * data[i + 2]!;
    }
    const luma = pixels > 0 ? sum / pixels : 0;

    // Smooth over the ring buffer.
    this.history.push(luma);
    if (this.history.length > this.bufferSize) this.history.shift();
    const smoothed = this.history.reduce((a, b) => a + b, 0) / this.history.length;

    const tooDark = smoothed < this.minLuma;
    const tooBright = smoothed > this.maxLuma;
    const ok = !tooDark && !tooBright;

    // Score: 1.0 in the comfortable middle, ramping to 0 at the limits.
    let score: number;
    if (smoothed < this.minLuma) score = Math.max(0, smoothed / this.minLuma);
    else if (smoothed > this.maxLuma) score = Math.max(0, (255 - smoothed) / (255 - this.maxLuma));
    else score = 1;

    return {
      name: 'brightness',
      value: smoothed,
      score,
      ok,
      detail: tooDark ? 'too dark' : tooBright ? 'over-exposed' : undefined,
    };
  }

  override reset(): void {
    this.history.length = 0;
  }
}
