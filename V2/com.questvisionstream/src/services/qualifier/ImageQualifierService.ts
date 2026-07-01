import {
  BaseEventService,
  type IServiceModule,
  type ServiceActivationContext,
} from '@realitycollective/service-framework';
import type { FrameSource } from '../../frame-source';
import type {
  IImageQualifierService,
  QualifierEventMap,
  QualifierFrameProvider,
} from './IImageQualifierService';
import type { IImageQualifierModule, QualifierMetric, QualityReport } from './types';

export interface ImageQualifierConfig {
  /** Per-frame tick source (the IWSDK adapter). */
  readonly frameSource: FrameSource;
  /** Sampling interval in ms. Default 100 (10 Hz). */
  readonly sampleIntervalMs?: number;
}

function isQualifierModule(m: IServiceModule): m is IImageQualifierModule {
  return typeof (m as IImageQualifierModule).evaluate === 'function';
}

/**
 * Orchestrates qualifier modules (data providers). On each frame tick it
 * accumulates time and, every `sampleIntervalMs`, pulls a downsampled frame from
 * the provider, runs every module, and emits an aggregate {@link QualityReport}
 * (ok = logical AND, score = weakest module).
 */
export class ImageQualifierService
  extends BaseEventService<QualifierEventMap, ImageQualifierConfig>
  implements IImageQualifierService
{
  private provider: QualifierFrameProvider | undefined;
  private _lastReport: QualityReport | undefined;
  private sinceSampleMs = 0;
  private unsub: (() => void) | undefined;

  constructor(context: ServiceActivationContext<ImageQualifierConfig>) {
    super(context);
  }

  get lastReport(): QualityReport | undefined {
    return this._lastReport;
  }

  get shouldStream(): boolean {
    // Default true before the first sample so streaming isn't blocked at start.
    return this._lastReport?.ok ?? true;
  }

  setFrameProvider(provider: QualifierFrameProvider): void {
    this.provider = provider;
  }

  override start(): void {
    this.unsub = this.serviceConfig.frameSource.onFrame((tick) => this.onTick(tick.delta));
  }

  override destroy(): void {
    this.unsub?.();
    super.destroy();
  }

  private get qualifierModules(): IImageQualifierModule[] {
    return this.serviceModules.filter(isQualifierModule);
  }

  private onTick(deltaSeconds: number): void {
    if (!this.provider) return;
    this.sinceSampleMs += deltaSeconds * 1000;
    const interval = this.serviceConfig.sampleIntervalMs ?? 100;
    if (this.sinceSampleMs < interval) return;
    this.sinceSampleMs = 0;

    const frame = this.provider();
    if (!frame) return;

    const metrics: QualifierMetric[] = this.qualifierModules.map((m) => m.evaluate(frame));
    if (metrics.length === 0) return;

    const report: QualityReport = {
      ok: metrics.every((m) => m.ok),
      score: metrics.reduce((min, m) => Math.min(min, m.score), 1),
      metrics,
      timestamp: performance.now(),
    };
    this._lastReport = report;
    this.emit('quality', report);
  }
}
