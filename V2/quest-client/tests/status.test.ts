/**
 * Status surface (new in this pass): the review found every failure signal
 * lands only in the console — invisible on a headset. StatusModel aggregates
 * named fields, bindStatusDom renders them, wireStatusServices feeds them
 * from the library's already-instrumented events.
 */
import { describe, expect, it } from 'vitest';
import { StatusModel, bindStatusDom, wireStatusServices } from '../src/ui/status';
import {
  FakeDetectionService,
  FakeQualifierService,
  FakeSignalingService,
  FakeWebRTCService,
} from './helpers';

describe('StatusModel', () => {
  it('stores fields and notifies on change only', () => {
    const model = new StatusModel();
    let notified = 0;
    model.onChange(() => {
      notified += 1;
    });

    model.set('camera', 'active');
    model.set('camera', 'active'); // no-op — same value
    model.set('connection', 'connecting');

    expect(model.get('camera')).toBe('active');
    expect(notified).toBe(2);
    expect(model.lines()).toEqual(['camera: active', 'connection: connecting']);
  });
});

describe('bindStatusDom', () => {
  it('renders lines into the container and updates live', () => {
    const model = new StatusModel();
    const container = document.createElement('div');
    bindStatusDom(model, container);

    model.set('server', 'wss://host:3000');
    expect(container.textContent).toContain('server: wss://host:3000');

    model.set('connection', 'failed');
    expect(container.textContent).toContain('connection: failed');
  });
});

describe('wireStatusServices', () => {
  it('reflects signaling, webrtc, quality, and detection events', () => {
    const model = new StatusModel();
    const signaling = new FakeSignalingService();
    const webrtc = new FakeWebRTCService();
    const qualifier = new FakeQualifierService();
    const detection = new FakeDetectionService();

    wireStatusServices(model, {
      signaling: signaling as never,
      webrtc: webrtc as never,
      qualifier: qualifier as never,
      detection: detection as never,
    });

    signaling.emit('connected', undefined);
    expect(model.get('signaling')).toBe('connected');

    signaling.emit('disconnected', 1006);
    expect(model.get('signaling')).toContain('1006');

    webrtc.emit('stateChange', 'connecting');
    expect(model.get('connection')).toBe('connecting');
    webrtc.emit('ready', undefined);
    expect(model.get('connection')).toBe('ready');

    qualifier.emit('quality', { ok: false, score: 0.1, metrics: [], timestamp: 0 });
    expect(model.get('quality')).toContain('paused');
    qualifier.emit('quality', { ok: true, score: 1, metrics: [], timestamp: 0 });
    expect(model.get('quality')).toBe('ok');

    detection.emit('detections', {
      type: 'detections',
      frame: 42,
      width: 640,
      height: 480,
      detections: [{ label: 'cup', conf: 0.9, bbox: [0, 0, 1, 1] }],
    });
    expect(model.get('detections')).toContain('1');
  });

  it('returns an unsubscribe that detaches everything', () => {
    const model = new StatusModel();
    const webrtc = new FakeWebRTCService();
    const unwire = wireStatusServices(model, { webrtc: webrtc as never });
    unwire();
    webrtc.emit('stateChange', 'failed');
    expect(model.get('connection')).toBeUndefined();
  });
});
