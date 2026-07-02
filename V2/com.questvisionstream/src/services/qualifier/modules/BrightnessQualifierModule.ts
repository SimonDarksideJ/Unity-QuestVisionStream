import {
  BaseServiceModule,
  createServiceToken,
  type ServiceActivationContext,
} from '@realitycollective/service-framework';
import type { IImageQualifierService } from '../IImageQualifierService';
import type { IImageQualifierModule, QualifierFrame, QualifierMetric } from '../types';

export interface BrightnessQualifierConfig {
  /** Minimum acceptable mean luminance (0..255). Default 40 (too dark below). */
  readonly minLuma?: number;
  /** Maximum acceptable mean luminance (0..255). Default 230 (blown out above). */
  readonly maxLuma?: number;
  /** Smoothing window (frames). Default 10. */
  readonly bufferSize?: number;
}

/** Token for the brightness module (registered under the qualifier service). */
export const IBrightnessQualifierModule =
  createServiceToken<IImageQualifierModule>('IBrightnessQualifierModule');

/**
 * Scores frame brightness using Rec.709 luminance (weights 0.2126 R / 0.7152 G /
 * 0.0722 B), smoothed over a ring buffer — ported from the Unity
 * `BrightnessEstimationManager`. Flags too-dark / over-exposed frames.
 */
export class BrightnessQualifierModule
  extends BaseServiceModule<IImageQualifierService, BrightnessQualifierConfig>
  implements IImageQualifierModule
{
  private readonly history: number[] = [];

  constructor(
    context: ServiceActivationContext<BrightnessQualifierConfig, IImageQualifierService>,
  ) {
    super(context);
  }

  private get cfg(): BrightnessQualifierConfig {
    return this.serviceConfig;
  }

  evaluate(frame: QualifierFrame): QualifierMetric {
    const minLuma = this.cfg.minLuma ?? 40;
    const maxLuma = this.cfg.maxLuma ?? 230;
    const bufferSize = this.cfg.bufferSize ?? 10;

    const { data } = frame;
    let sum = 0;
    const pixels = data.length / 4;
    for (let i = 0; i < data.length; i += 4) {
      sum += 0.2126 * data[i]! + 0.7152 * data[i + 1]! + 0.0722 * data[i + 2]!;
    }
    const luma = pixels > 0 ? sum / pixels : 0;

    this.history.push(luma);
    if (this.history.length > bufferSize) this.history.shift();
    const smoothed = this.history.reduce((a, b) => a + b, 0) / this.history.length;

    const tooDark = smoothed < minLuma;
    const tooBright = smoothed > maxLuma;
    const ok = !tooDark && !tooBright;

    let score: number;
    if (tooDark) score = Math.max(0, smoothed / minLuma);
    else if (tooBright) score = Math.max(0, (255 - smoothed) / (255 - maxLuma));
    else score = 1;

    return {
      name: 'brightness',
      value: smoothed,
      score,
      ok,
      ...(tooDark ? { detail: 'too dark' } : tooBright ? { detail: 'over-exposed' } : {}),
    };
  }

  override reset(): void {
    this.history.length = 0;
  }
}
