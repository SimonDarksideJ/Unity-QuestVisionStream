import { BaseService } from '@realitycollective/service-framework-ts';
import { Emitter } from '../../util/Emitter';
import type {
  IImageQualifierService,
  QualifierFrameProvider,
} from './IImageQualifierService';
import type { IImageQualifierModule, QualifierMetric, QualityReport } from './types';

export interface ImageQualifierConfig {
  /** Sampling interval in ms. Default 100 (10 Hz). */
  readonly sampleIntervalMs?: number;
}

/**
 * Orchestrates qualifier modules (data providers). Each `sampleIntervalMs` it
 * pulls a downsampled frame from the provider, runs every active module, and
 * emits an aggregate {@link QualityReport}. The aggregate `ok` is a logical AND
 * and the aggregate score is the weakest module's score.
 */
export class ImageQualifierService extends BaseService implements IImageQualifierService {
  readonly qualityChanged = new Emitter<QualityReport>();

  private readonly sampleIntervalMs: number;
  private provider: QualifierFrameProvider | undefined;
  private _lastReport: QualityReport | undefined;
  private sinceSample = 0;

  constructor(config: ImageQualifierConfig = {}) {
    // Priority 15: between signaling (10) and webrtc (20) — the gate is consulted
    // before frames are streamed.
    super('ImageQualifierService', 15);
    this.sampleIntervalMs = config.sampleIntervalMs ?? 100;
  }

  /** Attach an analyzer module (data provider). Chainable. */
  use(module: IImageQualifierModule): this {
    this.registerModule(module);
    return this;
  }

  get lastReport(): QualityReport | undefined {
    return this._lastReport;
  }

  get shouldStream(): boolean {
    // Default to true before the first sample so streaming isn't blocked at start.
    return this._lastReport?.ok ?? true;
  }

  setFrameProvider(provider: QualifierFrameProvider): void {
    this.provider = provider;
  }

  override update(delta: number): void {
    super.update(delta); // drive modules' own update hooks
    if (!this.provider) return;

    this.sinceSample += delta * 1000;
    if (this.sinceSample < this.sampleIntervalMs) return;
    this.sinceSample = 0;

    const frame = this.provider();
    if (!frame) return;

    const metrics: QualifierMetric[] = [];
    for (const m of this.activeModules) {
      metrics.push((m as IImageQualifierModule).evaluate(frame));
    }
    if (metrics.length === 0) return;

    const report: QualityReport = {
      ok: metrics.every((m) => m.ok),
      score: metrics.reduce((min, m) => Math.min(min, m.score), 1),
      metrics,
      timestamp: performance.now(),
    };
    this._lastReport = report;
    this.qualityChanged.emit(report);
  }

  override async destroy(): Promise<void> {
    this.qualityChanged.clear();
    this.provider = undefined;
    await super.destroy();
  }
}
