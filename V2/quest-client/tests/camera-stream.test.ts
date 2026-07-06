/**
 * CameraStreamSystem: camera-error handling (C1 — a denied permission
 * currently stalls the app silently, forever), connect-failure surfacing, and
 * the streaming/qualifier wiring baselines. @iwsdk/core is mocked at the
 * module seam; the system's logic runs unmodified.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppConfig } from '../src/config';

vi.mock('@iwsdk/core', () => {
  class SystemBase {
    world: unknown;
    scene: unknown;
    init(): void {}
    update(): void {}
    destroy(): void {}
  }
  return {
    createSystem: () => SystemBase,
    CameraState: { Inactive: 'inactive', Starting: 'starting', Active: 'active', Error: 'error' },
    CameraSource: { componentName: 'CameraSource' },
    CameraUtils: { getDevices: vi.fn(async () => []), captureFrame: vi.fn(() => null) },
  };
});

import { CameraState } from '@iwsdk/core';
import { IImageQualifierService, IWebRTCService } from '@questvisionstream/client';
import { CameraStreamSystem } from '../src/systems/CameraStreamSystem';
import {
  FakeQualifierService,
  FakeWebRTCService,
  flushMicrotasks,
  installServiceManager,
  stubCanvas2d,
} from './helpers';

stubCanvas2d();

interface FakeEntityValues {
  state: unknown;
  stream: MediaStream | null;
}

function makeFakeEntity(values: FakeEntityValues) {
  return {
    values,
    getValueCalls: 0,
    addComponent: vi.fn(),
    getValue(_component: unknown, key: keyof FakeEntityValues) {
      this.getValueCalls += 1;
      return this.values[key];
    },
  };
}

function makeSystem(values: FakeEntityValues) {
  const entity = makeFakeEntity(values);
  const webrtc = new FakeWebRTCService();
  const qualifier = new FakeQualifierService();
  const resolveCounts = installServiceManager(
    new Map<unknown, unknown>([
      [IWebRTCService, webrtc],
      [IImageQualifierService, qualifier],
    ]),
  );
  // The real base-class constructor takes ECS wiring args; the mocked one is
  // parameterless — cast at the seam so typecheck (against real types) passes.
  const SystemCtor = CameraStreamSystem as unknown as new () => object;
  const system = new SystemCtor() as {
    world: unknown;
    init(): void;
    update(): void;
  };
  system.world = { createEntity: () => entity };
  system.init();
  return { system, entity, webrtc, qualifier, resolveCounts };
}

const fakeStream = (): MediaStream => {
  const track = { kind: 'video', enabled: true }; // one instance — identity matters
  return { getVideoTracks: () => [track] } as unknown as MediaStream;
};

beforeEach(async () => {
  // Reset the app-wide status singleton between tests (once it exists).
  try {
    const { status } = await import('../src/ui/status');
    status.reset();
  } catch {
    /* red phase: module not created yet */
  }
});

describe('camera error handling (C1)', () => {
  it('stops polling after CameraState.Error instead of spinning forever', () => {
    const { system, entity } = makeSystem({ state: CameraState.Error, stream: null });

    system.update(); // observes the error
    const callsAfterError = entity.getValueCalls;
    system.update();
    system.update();

    expect(entity.getValueCalls).toBe(callsAfterError); // must not keep polling silently
  });

  it('reports the camera error on the status surface', async () => {
    const { system } = makeSystem({ state: CameraState.Error, stream: null });
    system.update();

    const { status } = await import('../src/ui/status');
    expect(status.get('camera')?.toLowerCase()).toContain('error');
  });
});

describe('streaming baselines', () => {
  it('hands the stream to WebRTC and wires the qualifier once Active', async () => {
    const stream = fakeStream();
    const { system, webrtc, qualifier } = makeSystem({ state: CameraState.Active, stream });

    system.update();
    await flushMicrotasks();

    expect(webrtc.stream).toBe(stream);
    expect(webrtc.connectCalls).toBe(1);
    expect(qualifier.provider).toBeTypeOf('function');
  });

  it('toggles the outbound track from the quality gate when gating is enabled', async () => {
    AppConfig.camera.gateStreamOnQuality = true;
    try {
      const stream = fakeStream();
      const track = stream.getVideoTracks()[0]!;
      const { system, qualifier } = makeSystem({ state: CameraState.Active, stream });

      system.update(); // begins streaming
      await flushMicrotasks();

      qualifier.shouldStream = false;
      system.update();
      expect(track.enabled).toBe(false);

      qualifier.shouldStream = true;
      system.update();
      expect(track.enabled).toBe(true);
    } finally {
      AppConfig.camera.gateStreamOnQuality = false;
    }
  });

  it('leaves the track enabled when the gate is off (default — ungated stream)', async () => {
    // A disabled track sends black frames and blacks the qualifier's own sample,
    // which deadlocks; the default keeps the stream flowing regardless of light.
    const stream = fakeStream();
    const track = stream.getVideoTracks()[0]!;
    const { system, qualifier } = makeSystem({ state: CameraState.Active, stream });

    system.update();
    await flushMicrotasks();

    qualifier.shouldStream = false;
    system.update();
    expect(track.enabled).toBe(true);
  });

  it('does not re-resolve services on every frame (hot path)', async () => {
    const { system, resolveCounts } = makeSystem({ state: CameraState.Active, stream: fakeStream() });

    system.update(); // begins streaming
    await flushMicrotasks();
    for (let i = 0; i < 100; i += 1) system.update();

    expect(resolveCounts.get(IImageQualifierService) ?? 0).toBeLessThanOrEqual(2);
  });
});

describe('connect failure surfacing', () => {
  it('reports a rejected connect() on the status surface instead of swallowing it', async () => {
    const stream = fakeStream();
    const { system, webrtc } = makeSystem({ state: CameraState.Active, stream });
    webrtc.connectRejection = new Error('server unreachable');

    system.update();
    await flushMicrotasks();

    const { status } = await import('../src/ui/status');
    expect(status.get('connection')?.toLowerCase()).toContain('failed');
  });
});
