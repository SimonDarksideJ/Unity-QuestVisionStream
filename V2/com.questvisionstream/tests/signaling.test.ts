/**
 * SignalingService: the connect() contract (L1), unhandled rejections (L3a),
 * stale-socket close handling (L4), and reconnect baselines.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SignalingService } from '../src/services/signaling/SignalingService';
import {
  MockWebSocket,
  collectUnhandledRejections,
  flushMicrotasks,
  makeContext,
  nextMacrotask,
} from './helpers';

const makeService = (config: Record<string, unknown> = {}) =>
  new SignalingService(
    makeContext('signaling', { url: 'ws://test:3000', autoConnect: false, ...config }),
  );

beforeEach(() => {
  vi.stubGlobal('WebSocket', MockWebSocket);
  MockWebSocket.reset();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('connect() contract (L1)', () => {
  it('does not resolve while the socket is still CONNECTING', async () => {
    const service = makeService({ autoConnect: true });
    service.start(); // autoConnect creates a CONNECTING socket
    const socket = MockWebSocket.latest();
    expect(socket.readyState).toBe(MockWebSocket.CONNECTING);

    let resolved = false;
    void service.connect().then(() => {
      resolved = true;
    });
    await flushMicrotasks();
    expect(resolved).toBe(false); // awaiting connect() must mean "socket is OPEN"

    socket.open();
    await flushMicrotasks();
    expect(resolved).toBe(true);
    expect(service.isConnected).toBe(true);
  });

  it('resolves immediately when already OPEN', async () => {
    const service = makeService();
    const first = service.connect();
    MockWebSocket.latest().open();
    await first;

    await service.connect(); // second call must not create a new socket
    expect(MockWebSocket.instances).toHaveLength(1);
  });

  it('rejects when the socket errors before opening', async () => {
    const service = makeService();
    const pending = service.connect();
    MockWebSocket.latest().fail();
    await expect(pending).rejects.toThrow();
  });
});

describe('unhandled rejections (L3a)', () => {
  it('autoConnect failure on start() produces no unhandled rejection', async () => {
    const collector = collectUnhandledRejections();
    try {
      const service = makeService({ autoConnect: true, reconnectBackoffMs: [] });
      service.start();
      MockWebSocket.latest().fail();
      await flushMicrotasks();
      await nextMacrotask();
      await nextMacrotask();
      expect(collector.events).toHaveLength(0);
    } finally {
      collector.stop();
    }
  });
});

describe('stale sockets (L4)', () => {
  it('ignores a late close event from a superseded socket', async () => {
    vi.useFakeTimers();
    const service = makeService();
    let disconnects = 0;
    service.on('disconnected', () => {
      disconnects += 1;
    });

    const first = service.connect();
    const socketA = MockWebSocket.latest();
    socketA.open();
    await first;

    service.disconnect();
    const second = service.connect();
    const socketB = MockWebSocket.latest();
    expect(socketB).not.toBe(socketA);
    socketB.open();
    await second;

    // The old socket's close event finally arrives (network latency).
    socketA.serverClose(1006);
    await vi.advanceTimersByTimeAsync(0);

    expect(disconnects).toBe(0); // stale close must not look like a live disconnect
    // ...and must not schedule a reconnect that replaces the healthy socket B.
    await vi.advanceTimersByTimeAsync(10_000);
    expect(MockWebSocket.instances).toHaveLength(2);
    expect(service.isConnected).toBe(true);
  });
});

describe('reconnect baseline', () => {
  it('reconnects with backoff after a live socket drops', async () => {
    vi.useFakeTimers();
    const service = makeService({ reconnectBackoffMs: [1000, 2000] });
    const first = service.connect();
    MockWebSocket.latest().open();
    await first;

    MockWebSocket.latest().serverClose(1006);
    expect(MockWebSocket.instances).toHaveLength(1);

    await vi.advanceTimersByTimeAsync(1000);
    expect(MockWebSocket.instances).toHaveLength(2); // new socket after backoff
  });

  it('does not reconnect after a user disconnect()', async () => {
    vi.useFakeTimers();
    const service = makeService();
    const first = service.connect();
    const socket = MockWebSocket.latest();
    socket.open();
    await first;

    service.disconnect();
    socket.serverClose(1000);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(MockWebSocket.instances).toHaveLength(1);
  });
});

describe('send()', () => {
  it('sends JSON when open and drops (without throwing) when not', async () => {
    const service = makeService();
    service.send({ type: 'offer', sdp: 'x' }); // no socket yet — must not throw

    const pending = service.connect();
    MockWebSocket.latest().open();
    await pending;
    service.send({ type: 'offer', sdp: 'y' });
    expect(MockWebSocket.latest().sentJson()).toEqual([{ type: 'offer', sdp: 'y' }]);
  });
});
