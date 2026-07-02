/**
 * WebRTCService: the offer-drop race (L1, end-to-end with the real
 * SignalingService), early-candidate queueing (L2), unhandled rejections
 * (L3b), session recovery, and ICE-prefix baselines.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SignalingService } from '../src/services/signaling/SignalingService';
import { WebRTCService } from '../src/services/webrtc/WebRTCService';
import {
  FakeSignaling,
  MockRTCPeerConnection,
  MockWebSocket,
  collectUnhandledRejections,
  fakeMediaStream,
  flushMicrotasks,
  makeContext,
  nextMacrotask,
} from './helpers';

const makeWebRTC = (signaling: FakeSignaling, config: Record<string, unknown> = {}) =>
  new WebRTCService(makeContext('webrtc', { ...config }), signaling.asService());

beforeEach(() => {
  vi.stubGlobal('WebSocket', MockWebSocket);
  vi.stubGlobal('RTCPeerConnection', MockRTCPeerConnection);
  MockWebSocket.reset();
  MockRTCPeerConnection.reset();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('offer delivery across the connect race (L1)', () => {
  it('delivers the offer even when connect() starts while signaling is still CONNECTING', async () => {
    // Real SignalingService with autoConnect (as the shipped profile configures it).
    const signaling = new SignalingService(
      makeContext('signaling', { url: 'ws://test:3000', autoConnect: true }),
    );
    signaling.start(); // socket now CONNECTING — the race window
    const webrtc = new WebRTCService(makeContext('webrtc', {}), signaling);
    webrtc.start();
    webrtc.setVideoStream(fakeMediaStream());

    const connecting = webrtc.connect(); // host connects immediately (camera became Active)
    connecting.catch(() => {}); // outcome asserted via the wire below
    await flushMicrotasks();

    const socket = MockWebSocket.latest();
    socket.open(); // handshake completes *after* the offer was created
    await flushMicrotasks();
    await nextMacrotask();

    const offers = socket.sentJson().filter((m) => (m as { type: string }).type === 'offer');
    expect(offers).toHaveLength(1); // the offer must reach the wire, not be dropped
  });
});

describe('early remote ICE candidates (L2)', () => {
  it('queues candidates that arrive before the answer is applied', async () => {
    const signaling = new FakeSignaling();
    const webrtc = makeWebRTC(signaling);
    webrtc.start();
    webrtc.setVideoStream(fakeMediaStream());
    await webrtc.connect();

    const pc = MockRTCPeerConnection.latest();
    pc.deferRemoteDescription = true; // answer application is in flight...

    signaling.emitMessage({ type: 'answer', sdp: 'v=0 mock-answer' });
    await flushMicrotasks();
    // ...and candidates trickle in before setRemoteDescription resolves.
    signaling.emitMessage({ type: 'candidate', candidate: 'a 1', sdpMid: '0', sdpMLineIndex: 0 });
    signaling.emitMessage({ type: 'candidate', candidate: 'b 2', sdpMid: '0', sdpMLineIndex: 0 });
    signaling.emitMessage({ type: 'candidate', candidate: 'c 3', sdpMid: '0', sdpMLineIndex: 0 });
    await flushMicrotasks();
    expect(pc.candidates).toHaveLength(0); // nothing can apply yet

    pc.resolveRemote();
    await flushMicrotasks();
    await nextMacrotask();

    // All three must be applied (in order) once the remote description lands.
    expect(pc.candidates.map((c) => c.candidate)).toEqual([
      'candidate:a 1',
      'candidate:b 2',
      'candidate:c 3',
    ]);
  });
});

describe('unhandled rejections (L3b)', () => {
  it('a malformed answer SDP does not produce an unhandled rejection', async () => {
    const collector = collectUnhandledRejections();
    try {
      const signaling = new FakeSignaling();
      const webrtc = makeWebRTC(signaling);
      webrtc.start();
      webrtc.setVideoStream(fakeMediaStream());
      await webrtc.connect();

      MockRTCPeerConnection.latest().rejectRemoteDescription = true;
      signaling.emitMessage({ type: 'answer', sdp: 'garbage' });
      await flushMicrotasks();
      await nextMacrotask();
      await nextMacrotask();

      expect(collector.events).toHaveLength(0);
    } finally {
      collector.stop();
    }
  });
});

describe('session recovery', () => {
  it('re-offers automatically after the peer connection fails', async () => {
    vi.useFakeTimers();
    const signaling = new FakeSignaling();
    const webrtc = makeWebRTC(signaling, { reconnectDelayMs: 500 });
    webrtc.start();
    webrtc.setVideoStream(fakeMediaStream());
    await webrtc.connect();
    expect(signaling.sent.filter((m) => m.type === 'offer')).toHaveLength(1);

    MockRTCPeerConnection.latest().setConnectionState('failed'); // mid-session media death
    await vi.advanceTimersByTimeAsync(500);
    await flushMicrotasks();

    expect(MockRTCPeerConnection.instances).toHaveLength(2); // fresh pc
    expect(signaling.sent.filter((m) => m.type === 'offer')).toHaveLength(2); // fresh offer
  });

  it('re-offers when signaling reconnects and the session never completed', async () => {
    vi.useFakeTimers();
    const signaling = new FakeSignaling();
    const webrtc = makeWebRTC(signaling, { reconnectDelayMs: 500 });
    webrtc.start();
    webrtc.setVideoStream(fakeMediaStream());
    await webrtc.connect(); // offer sent; answer never arrives (server restarted)

    signaling.emit('connected', undefined); // signaling layer restored the socket
    await vi.advanceTimersByTimeAsync(500);
    await flushMicrotasks();

    expect(signaling.sent.filter((m) => m.type === 'offer')).toHaveLength(2);
  });

  it('does not auto-reconnect after an intentional close()', async () => {
    vi.useFakeTimers();
    const signaling = new FakeSignaling();
    const webrtc = makeWebRTC(signaling, { reconnectDelayMs: 500 });
    webrtc.start();
    webrtc.setVideoStream(fakeMediaStream());
    await webrtc.connect();

    webrtc.close();
    signaling.emit('connected', undefined);
    await vi.advanceTimersByTimeAsync(60_000);
    await flushMicrotasks();

    expect(signaling.sent.filter((m) => m.type === 'offer')).toHaveLength(1);
    expect(MockRTCPeerConnection.instances).toHaveLength(1);
  });
});

describe('ICE prefix + channel routing baselines', () => {
  it('strips the candidate: prefix on send and re-adds it on receive', async () => {
    const signaling = new FakeSignaling();
    const webrtc = makeWebRTC(signaling);
    webrtc.start();
    webrtc.setVideoStream(fakeMediaStream());
    await webrtc.connect();

    const pc = MockRTCPeerConnection.latest();
    pc.onicecandidate?.({
      candidate: { candidate: 'candidate:local 1', sdpMid: '0', sdpMLineIndex: 0 },
    });
    const sent = signaling.sent.find((m) => m.type === 'candidate');
    expect(sent && 'candidate' in sent ? sent.candidate : undefined).toBe('local 1');

    signaling.emitMessage({ type: 'answer', sdp: 'v=0' });
    await flushMicrotasks();
    signaling.emitMessage({ type: 'candidate', candidate: 'remote 2', sdpMid: '0', sdpMLineIndex: 0 });
    await flushMicrotasks();
    expect(pc.candidates[0]?.candidate).toBe('candidate:remote 2');
  });

  it('routes ready and detection messages from the data channel', async () => {
    const signaling = new FakeSignaling();
    const webrtc = makeWebRTC(signaling);
    webrtc.start();
    webrtc.setVideoStream(fakeMediaStream());
    await webrtc.connect();

    const ready = vi.fn();
    const detections = vi.fn();
    webrtc.on('ready', ready);
    webrtc.on('detectionMessage', detections);

    const channel = MockRTCPeerConnection.latest().channels[0]!;
    channel.onmessage?.({ data: JSON.stringify({ type: 'ready' }) });
    channel.onmessage?.({ data: '{"type":"detections","detections":[]}' });

    expect(ready).toHaveBeenCalledTimes(1);
    expect(detections).toHaveBeenCalledTimes(1);
  });
});
