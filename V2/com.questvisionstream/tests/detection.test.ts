/**
 * DetectionService + wire-payload guard: numeric validation gaps and the
 * happy-path baselines. A hostile/buggy payload must never reach the renderer
 * as NaN coordinates.
 */
import { describe, expect, it, vi } from 'vitest';
import { DetectionService } from '../src/services/detection/DetectionService';
import { isDetectionsPayload } from '../src/services/detection/types';
import type { IWebRTCService } from '../src/services/webrtc/IWebRTCService';
import { makeContext } from './helpers';

const validPayload = {
  type: 'detections',
  frame: 1,
  width: 640,
  height: 480,
  detections: [{ label: 'cup', conf: 0.9, bbox: [1, 2, 3, 4] }],
};

describe('isDetectionsPayload validation', () => {
  it('accepts a well-formed payload (with or without the additive pts field)', () => {
    expect(isDetectionsPayload(validPayload)).toBe(true);
    expect(isDetectionsPayload({ ...validPayload, pts: 3000 })).toBe(true);
    expect(isDetectionsPayload({ ...validPayload, pts: null })).toBe(true);
    expect(isDetectionsPayload({ ...validPayload, detections: [] })).toBe(true);
  });

  it('rejects bbox entries that are not finite numbers', () => {
    expect(
      isDetectionsPayload({
        ...validPayload,
        detections: [{ label: 'cup', conf: 0.9, bbox: ['a', 'b', 'c', 'd'] }],
      }),
    ).toBe(false);
    expect(
      isDetectionsPayload({
        ...validPayload,
        detections: [{ label: 'cup', conf: 0.9, bbox: [1, 2, 3, null] }],
      }),
    ).toBe(false);
  });

  it('rejects non-numeric frame/width/height', () => {
    expect(isDetectionsPayload({ ...validPayload, width: '640' })).toBe(false);
    expect(isDetectionsPayload({ ...validPayload, height: undefined })).toBe(false);
    expect(isDetectionsPayload({ ...validPayload, frame: 'x' })).toBe(false);
  });

  it('rejects wrong bbox lengths and non-detection shapes', () => {
    expect(
      isDetectionsPayload({
        ...validPayload,
        detections: [{ label: 'cup', conf: 0.9, bbox: [1, 2, 3] }],
      }),
    ).toBe(false);
    expect(isDetectionsPayload({ type: 'ready' })).toBe(false);
    expect(isDetectionsPayload(null)).toBe(false);
    expect(isDetectionsPayload('detections')).toBe(false);
  });
});

// Minimal IWebRTCService double: just the event we consume.
class FakeWebRTC {
  private handler: ((raw: string) => void) | undefined;
  on(_event: string, handler: (raw: string) => void): () => void {
    this.handler = handler;
    return () => {
      this.handler = undefined;
    };
  }
  push(raw: string): void {
    this.handler?.(raw);
  }
  asService(): IWebRTCService {
    return this as unknown as IWebRTCService;
  }
}

describe('DetectionService', () => {
  it('emits validated payloads and tracks the last frame size', () => {
    const webrtc = new FakeWebRTC();
    const service = new DetectionService(makeContext('detection', {}), webrtc.asService());
    service.start();
    const seen = vi.fn();
    service.on('detections', seen);

    webrtc.push(JSON.stringify(validPayload));
    expect(seen).toHaveBeenCalledTimes(1);
    expect(service.lastFrameSize).toEqual({ width: 640, height: 480 });
  });

  it('drops malformed payloads instead of forwarding NaN math downstream', () => {
    const webrtc = new FakeWebRTC();
    const service = new DetectionService(makeContext('detection', {}), webrtc.asService());
    service.start();
    const seen = vi.fn();
    service.on('detections', seen);

    webrtc.push('not json');
    webrtc.push(JSON.stringify({ ...validPayload, width: 'wide' }));
    webrtc.push(
      JSON.stringify({
        ...validPayload,
        detections: [{ label: 'cup', conf: 0.9, bbox: ['a', 'b', 'c', 'd'] }],
      }),
    );

    expect(seen).not.toHaveBeenCalled();
    expect(service.lastFrameSize).toBeUndefined();
  });
});
