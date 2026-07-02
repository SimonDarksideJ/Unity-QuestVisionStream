/**
 * Shared test doubles for the quest-client suite.
 *
 * happy-dom provides the DOM but not a 2D canvas context, so tests that build
 * tags stub `HTMLCanvasElement.getContext`. IWSDK systems resolve services by
 * token through `runtime.ts`, so a stub ServiceManager with a token→fake map
 * stands in for the real one.
 */
import type { ServiceManager } from '@realitycollective/service-framework';
import { setServiceManager } from '../src/runtime';

/** Minimal 2D context good enough for TagFactory's text rendering. */
export function stubCanvas2d(): void {
  const proto = HTMLCanvasElement.prototype as unknown as {
    getContext: (kind: string, opts?: unknown) => unknown;
  };
  proto.getContext = function getContext(this: HTMLCanvasElement) {
    return {
      canvas: this,
      font: '',
      textBaseline: '',
      fillStyle: '',
      measureText: (text: string) => ({ width: Math.max(1, text.length) * 20 }),
      fillText: () => {},
      beginPath: () => {},
      moveTo: () => {},
      arcTo: () => {},
      closePath: () => {},
      fill: () => {},
      drawImage: () => {},
      getImageData: (_x: number, _y: number, w: number, h: number) => ({
        width: w,
        height: h,
        data: new Uint8ClampedArray(w * h * 4),
      }),
    };
  };
}

type Handler = (payload: unknown) => void;

/** Tiny event emitter matching the library services' on/emit surface. */
export class FakeEventService {
  private readonly handlers = new Map<string, Set<Handler>>();
  on(event: string, handler: Handler): () => void {
    const set = this.handlers.get(event) ?? new Set<Handler>();
    set.add(handler);
    this.handlers.set(event, set);
    return () => set.delete(handler);
  }
  emit(event: string, payload: unknown): void {
    for (const handler of this.handlers.get(event) ?? []) handler(payload);
  }
}

export class FakeWebRTCService extends FakeEventService {
  stream: MediaStream | undefined;
  connectCalls = 0;
  connectRejection: Error | undefined;
  connectionState = 'new';
  setVideoStream(stream: MediaStream): void {
    this.stream = stream;
  }
  async connect(): Promise<void> {
    this.connectCalls += 1;
    if (this.connectRejection) throw this.connectRejection;
  }
  close(): void {}
}

export class FakeQualifierService extends FakeEventService {
  shouldStream = true;
  provider: (() => unknown) | undefined;
  lastReport = undefined;
  setFrameProvider(provider: () => unknown): void {
    this.provider = provider;
  }
}

export class FakeDetectionService extends FakeEventService {
  lastFrameSize = undefined;
  detectionsPerSecond = 0;
}

export class FakeSignalingService extends FakeEventService {
  isConnected = false;
  async connect(): Promise<void> {}
  disconnect(): void {}
  send(): void {}
}

/** Install a stub ServiceManager resolving tokens from the given map.
 * Returns per-token resolve counts (to catch hot-path re-resolution). */
export function installServiceManager(services: Map<unknown, unknown>): Map<unknown, number> {
  const resolveCounts = new Map<unknown, number>();
  const manager = {
    resolve: (token: unknown) => {
      resolveCounts.set(token, (resolveCounts.get(token) ?? 0) + 1);
      const service = services.get(token);
      if (!service) throw new Error('service not registered in test manager');
      return service;
    },
  } as unknown as ServiceManager;
  setServiceManager(manager);
  return resolveCounts;
}

export const flushMicrotasks = async (rounds = 8): Promise<void> => {
  for (let i = 0; i < rounds; i += 1) await Promise.resolve();
};
