import {
  BaseEventService,
  type ServiceActivationContext,
} from '@realitycollective/service-framework';
import { createLogger } from '../../util/logger';
import type { IWebRTCService } from '../webrtc/IWebRTCService';
import type { DetectionEventMap, IDetectionService } from './IDetectionService';
import { isDetectionsPayload, type DetectionsPayload } from './types';

const log = createLogger('Detection');

export interface DetectionConfig {
  /** Rolling window (ms) for the detections-per-second estimate. Default 1000. */
  readonly throughputWindowMs?: number;
}

/**
 * Consumes raw detection messages from the {@link IWebRTCService}, validates and
 * republishes them, and tracks frame size + throughput (from arrival timestamps,
 * so no per-frame tick is needed).
 */
export class DetectionService
  extends BaseEventService<DetectionEventMap, DetectionConfig>
  implements IDetectionService
{
  private readonly webrtc: IWebRTCService;
  private _lastFrameSize: { width: number; height: number } | undefined;
  private readonly arrivals: number[] = [];
  private unsub: (() => void) | undefined;

  constructor(context: ServiceActivationContext<DetectionConfig>, webrtc: IWebRTCService) {
    super(context);
    this.webrtc = webrtc;
  }

  get lastFrameSize(): { readonly width: number; readonly height: number } | undefined {
    return this._lastFrameSize;
  }

  get detectionsPerSecond(): number {
    this.trim();
    return this.arrivals.length;
  }

  override start(): void {
    this.unsub = this.webrtc.on('detectionMessage', (raw) => this.handle(raw));
  }

  override destroy(): void {
    this.unsub?.();
    super.destroy();
  }

  private handle(raw: string): void {
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      log.warn('Dropping non-JSON detection message');
      return;
    }
    if (!isDetectionsPayload(parsed)) return;

    const payload: DetectionsPayload = parsed;
    if (payload.width > 0 && payload.height > 0) {
      this._lastFrameSize = { width: payload.width, height: payload.height };
    }
    this.arrivals.push(performance.now());
    this.trim();
    this.emit('detections', payload);
  }

  private trim(): void {
    const windowMs = this.serviceConfig.throughputWindowMs ?? 1000;
    const cutoff = performance.now() - windowMs;
    while (this.arrivals.length && this.arrivals[0]! < cutoff) this.arrivals.shift();
  }
}
