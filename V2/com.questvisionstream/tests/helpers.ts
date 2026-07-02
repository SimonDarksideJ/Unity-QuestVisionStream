/**
 * Shared test doubles. The library targets browser APIs (WebSocket,
 * RTCPeerConnection) that don't exist in Node, so the suite stubs them at the
 * global seam — which also makes every network/WebRTC behaviour scriptable.
 */
import type { ServiceActivationContext } from '@realitycollective/service-framework';
import type {
  ISignalingService,
  SignalingInbound,
  SignalingOutbound,
} from '../src/services/signaling/ISignalingService';

/** Minimal activation context — the framework constructor only stores fields.
 * Service *modules* additionally require the owning service as `parent`. */
export function makeContext<TConfig>(
  name: string,
  config: TConfig,
  parent?: unknown,
): ServiceActivationContext<TConfig> {
  return {
    name,
    priority: 0,
    config,
    manager: {} as never,
    scheduler: { subscribe: () => () => {}, emit: () => {}, dispose: () => {} },
    environment: { name: 'test', capabilities: new Set<string>(), hasCapability: () => false },
    signal: new AbortController().signal,
    ...(parent !== undefined ? { parent } : {}),
  } as unknown as ServiceActivationContext<TConfig>;
}

export const flushMicrotasks = async (rounds = 8): Promise<void> => {
  for (let i = 0; i < rounds; i += 1) await Promise.resolve();
};

export const nextMacrotask = (): Promise<void> => new Promise((r) => setTimeout(r, 0));

/** Collects unhandled promise rejections while active. */
export function collectUnhandledRejections(): { events: unknown[]; stop: () => void } {
  const events: unknown[] = [];
  const handler = (reason: unknown) => {
    events.push(reason);
  };
  process.on('unhandledRejection', handler);
  return { events, stop: () => process.off('unhandledRejection', handler) };
}

// ---------------------------------------------------------------- WebSocket

type WsHandler<T> = ((event: T) => void) | null;

export class MockWebSocket {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;
  static instances: MockWebSocket[] = [];
  static reset(): void {
    MockWebSocket.instances = [];
  }
  static latest(): MockWebSocket {
    const ws = MockWebSocket.instances[MockWebSocket.instances.length - 1];
    if (!ws) throw new Error('no MockWebSocket was created');
    return ws;
  }

  readyState: number = MockWebSocket.CONNECTING;
  readonly sent: string[] = [];
  onopen: WsHandler<unknown> = null;
  onmessage: WsHandler<{ data: unknown }> = null;
  onerror: WsHandler<unknown> = null;
  onclose: WsHandler<{ code: number }> = null;

  constructor(public readonly url: string) {
    MockWebSocket.instances.push(this);
  }

  send(data: string): void {
    this.sent.push(data);
  }

  /** Client-initiated close — the close *event* arrives later via serverClose. */
  close(_code?: number, _reason?: string): void {
    this.readyState = MockWebSocket.CLOSING;
  }

  // -- test controls --
  open(): void {
    this.readyState = MockWebSocket.OPEN;
    this.onopen?.({});
  }
  serverMessage(payload: unknown): void {
    this.onmessage?.({ data: JSON.stringify(payload) });
  }
  fail(): void {
    this.onerror?.({});
  }
  serverClose(code = 1006): void {
    this.readyState = MockWebSocket.CLOSED;
    this.onclose?.({ code });
  }
  sentJson(): unknown[] {
    return this.sent.map((s) => JSON.parse(s));
  }
}

// -------------------------------------------------------- RTCPeerConnection

export class MockDataChannel {
  onopen: (() => void) | null = null;
  onmessage: ((e: { data: unknown }) => void) | null = null;
  closed = false;
  constructor(public readonly label: string) {}
  close(): void {
    this.closed = true;
  }
}

export class MockRTCPeerConnection {
  static instances: MockRTCPeerConnection[] = [];
  static reset(): void {
    MockRTCPeerConnection.instances = [];
  }
  static latest(): MockRTCPeerConnection {
    const pc = MockRTCPeerConnection.instances[MockRTCPeerConnection.instances.length - 1];
    if (!pc) throw new Error('no MockRTCPeerConnection was created');
    return pc;
  }

  connectionState = 'new';
  localDescription: unknown = null;
  remoteDescription: unknown = null;
  readonly candidates: RTCIceCandidateInit[] = [];
  readonly tracks: unknown[] = [];
  readonly channels: MockDataChannel[] = [];
  onicecandidate: ((e: { candidate: RTCIceCandidateInit | null }) => void) | null = null;
  onconnectionstatechange: (() => void) | null = null;
  closed = false;

  /** When true, setRemoteDescription stays pending until resolveRemote(). */
  deferRemoteDescription = false;
  /** When true, setRemoteDescription rejects (malformed SDP). */
  rejectRemoteDescription = false;
  private deferred: { resolve: () => void; reject: (e: Error) => void } | undefined;

  constructor(public readonly config: unknown) {
    MockRTCPeerConnection.instances.push(this);
  }

  createDataChannel(label: string): MockDataChannel {
    const channel = new MockDataChannel(label);
    this.channels.push(channel);
    return channel;
  }

  addTrack(track: unknown, _stream: unknown): void {
    this.tracks.push(track);
  }

  async createOffer(_options?: unknown): Promise<{ type: string; sdp: string }> {
    return { type: 'offer', sdp: 'v=0 mock-offer' };
  }

  async setLocalDescription(description: unknown): Promise<void> {
    this.localDescription = description;
  }

  setRemoteDescription(description: unknown): Promise<void> {
    if (this.rejectRemoteDescription) {
      return Promise.reject(new Error('Failed to parse SessionDescription'));
    }
    if (this.deferRemoteDescription) {
      return new Promise((resolve, reject) => {
        this.deferred = {
          resolve: () => {
            this.remoteDescription = description;
            resolve();
          },
          reject,
        };
      });
    }
    this.remoteDescription = description;
    return Promise.resolve();
  }

  resolveRemote(): void {
    this.deferred?.resolve();
    this.deferred = undefined;
  }

  async addIceCandidate(candidate: RTCIceCandidateInit): Promise<void> {
    // Mirrors the browser: candidates cannot be applied before the remote
    // description is set.
    if (!this.remoteDescription) {
      throw new Error('The remote description was null');
    }
    this.candidates.push(candidate);
  }

  close(): void {
    this.closed = true;
    this.connectionState = 'closed';
  }

  setConnectionState(state: string): void {
    this.connectionState = state;
    this.onconnectionstatechange?.();
  }
}

export const fakeMediaStream = (): MediaStream =>
  ({ getVideoTracks: () => [{ kind: 'video' }] }) as unknown as MediaStream;

// ------------------------------------------------------------ FakeSignaling

type Handler = (payload: unknown) => void;

/** Hand-rolled ISignalingService double for WebRTCService tests. */
export class FakeSignaling {
  private readonly handlers = new Map<string, Set<Handler>>();
  readonly sent: SignalingOutbound[] = [];
  isConnected = true;
  connectCalls = 0;

  on(event: string, handler: Handler): () => void {
    const set = this.handlers.get(event) ?? new Set<Handler>();
    set.add(handler);
    this.handlers.set(event, set);
    return () => set.delete(handler);
  }
  off(event: string, handler: Handler): void {
    this.handlers.get(event)?.delete(handler);
  }
  once(event: string, handler: Handler): () => void {
    const unsub = this.on(event, (p) => {
      unsub();
      handler(p);
    });
    return unsub;
  }
  emit(event: string, payload: unknown): void {
    for (const handler of this.handlers.get(event) ?? []) handler(payload);
  }
  listenerCount(event: string): number {
    return this.handlers.get(event)?.size ?? 0;
  }

  async connect(): Promise<void> {
    this.connectCalls += 1;
    this.isConnected = true;
  }
  disconnect(): void {
    this.isConnected = false;
  }
  send(message: SignalingOutbound): void {
    this.sent.push(message);
  }

  emitMessage(message: SignalingInbound): void {
    this.emit('message', message);
  }

  asService(): ISignalingService {
    return this as unknown as ISignalingService;
  }
}
