/**
 * ImageQualifierService + BrightnessQualifierModule: the silently-inert
 * misconfiguration must be loud, and the gate math baselines.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { FrameSource, FrameTick } from '../src/frame-source';
import { BrightnessQualifierModule } from '../src/services/qualifier/modules/BrightnessQualifierModule';
import { ImageQualifierService } from '../src/services/qualifier/ImageQualifierService';
import type { QualifierFrame } from '../src/services/qualifier/types';
import { makeContext } from './helpers';

class ManualFrameSource implements FrameSource {
  private handler: ((tick: FrameTick) => void) | undefined;
  onFrame(handler: (tick: FrameTick) => void): () => void {
    this.handler = handler;
    return () => {
      this.handler = undefined;
    };
  }
  tick(deltaSeconds: number): void {
    this.handler?.({ delta: deltaSeconds, timestamp: 0 });
  }
}

const solidFrame = (value: number, pixels = 16): QualifierFrame => ({
  width: pixels,
  height: 1,
  data: new Uint8ClampedArray(
    Array.from({ length: pixels * 4 }, (_, i) => (i % 4 === 3 ? 255 : value)),
  ),
});

const makeService = (frameSource: FrameSource, sampleIntervalMs = 100) =>
  new ImageQualifierService(makeContext('qualifier', { frameSource, sampleIntervalMs }));

afterEach(() => {
  vi.restoreAllMocks();
});

describe('silently-inert misconfiguration', () => {
  it('warns (once) when ticks arrive but no frame provider was wired', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const source = new ManualFrameSource();
    const service = makeService(source);
    service.start();

    for (let i = 0; i < 50; i += 1) source.tick(0.1); // 5s of ticks, no provider

    const providerWarnings = warn.mock.calls.filter((call) =>
      call.some((arg) => typeof arg === 'string' && arg.toLowerCase().includes('provider')),
    );
    expect(providerWarnings).toHaveLength(1); // loud exactly once, not spam
    expect(service.shouldStream).toBe(true); // and the gate stays open (fail-safe)
  });
});

describe('quality gate baselines', () => {
  const wire = (service: ImageQualifierService, value: number) => {
    const module = new BrightnessQualifierModule(
      makeContext('brightness', { bufferSize: 1 }, service) as never,
    );
    service.registerServiceModule(module);
    service.setFrameProvider(() => solidFrame(value));
  };

  it('flags a too-dark frame and closes the gate', () => {
    const source = new ManualFrameSource();
    const service = makeService(source);
    service.start();
    wire(service, 5); // near-black
    source.tick(0.2); // past the 100ms sample interval

    expect(service.lastReport?.ok).toBe(false);
    expect(service.shouldStream).toBe(false);
  });

  it('passes a normal frame and reopens the gate', () => {
    const source = new ManualFrameSource();
    const service = makeService(source);
    service.start();
    wire(service, 128); // mid-grey
    source.tick(0.2);

    expect(service.lastReport?.ok).toBe(true);
    expect(service.shouldStream).toBe(true);
    expect(service.lastReport?.metrics[0]?.name).toBe('brightness');
  });

  it('defaults to streaming before the first sample', () => {
    const source = new ManualFrameSource();
    const service = makeService(source);
    service.start();
    expect(service.shouldStream).toBe(true);
  });
});
