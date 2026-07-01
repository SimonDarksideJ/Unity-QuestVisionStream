import { BaseService } from '@realitycollective/service-framework-ts';
import { Emitter } from '../../util/Emitter';
import { createLogger } from '../../util/logger';
import type { IWebRTCService } from '../webrtc/IWebRTCService';
import type { IDetectionService } from './IDetectionService';
import { isDetectionsPayload, type DetectionsPayload } from './types';

const log = createLogger('Detection');

/**
 * Consumes raw detection messages from the {@link IWebRTCService}, validates and
 * republishes them as typed {@link DetectionsPayload}s, and tracks frame size +
 * throughput. Depends on the WebRTC service (constructor-injected).
 */
export class DetectionService extends BaseService implements IDetectionService {
  readonly detections = new Emitter<DetectionsPayload>();

  private readonly webrtc: IWebRTCService;
  private _lastFrameSize: { width: number; height: number } | undefined;
  private _dps = 0;
  private windowCount = 0;
  private windowStart = 0;
  private unsub: (() => void) | undefined;

  constructor(webrtc: IWebRTCService) {
    // Priority 30: after the WebRTC service that feeds it.
    super('DetectionService', 30);
    this.webrtc = webrtc;
  }

  get lastFrameSize(): { readonly width: number; readonly height: number } | undefined {
    return this._lastFrameSize;
  }

  get detectionsPerSecond(): number {
    return this._dps;
  }

  override async start(): Promise<void> {
    await super.start();
    this.unsub = this.webrtc.detectionMessages.on((raw) => this.handle(raw));
  }

  override update(_delta: number): void {
    // Roll the throughput window once per second.
    const now = performance.now();
    if (this.windowStart === 0) this.windowStart = now;
    if (now - this.windowStart >= 1000) {
      this._dps = this.windowCount;
      this.windowCount = 0;
      this.windowStart = now;
    }
  }

  override async destroy(): Promise<void> {
    this.unsub?.();
    this.detections.clear();
    await super.destroy();
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

    const payload = parsed;
    if (payload.width > 0 && payload.height > 0) {
      this._lastFrameSize = { width: payload.width, height: payload.height };
    }
    this.windowCount += 1;
    this.detections.emit(payload);
  }
}
