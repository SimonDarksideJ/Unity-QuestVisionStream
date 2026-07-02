/**
 * User-facing status surface.
 *
 * The architecture review's biggest UX finding: every failure signal (camera
 * permission denied, server unreachable, WebRTC failed, quality gate) reached
 * only the console — invisible on a headset. The library services are already
 * fully instrumented with events; this module aggregates them into named
 * fields (StatusModel), renders them to the DOM overlay (visible on the 2D
 * page before entering AR, and in desktop debugging), and exposes a single
 * app-wide instance for systems to report into.
 *
 * A world-space (in-AR) rendering of the same model is the follow-up step —
 * it needs on-headset validation (see the improvements log).
 */
import type {
  IDetectionService,
  IImageQualifierService,
  ISignalingService,
  IWebRTCService,
} from '@questvisionstream/client';

export type StatusField =
  | 'server'
  | 'camera'
  | 'signaling'
  | 'connection'
  | 'quality'
  | 'detections';

/**
 * Human-readable meaning for a WebSocket close code. The raw number (esp. the
 * opaque `1006`) is useless on a headset; this turns it into an actionable
 * cause. Codes: RFC 6455 §7.4 for the 1xxx range; 4xxx are this server's own
 * (see QuestVisionStreamServer/webrtc_server.py — 4401 auth, 4403 origin, 4000
 * superseded).
 */
export function describeCloseCode(code: number): string {
  switch (code) {
    case 1000:
      return 'closed normally';
    case 1001:
      return 'server going away';
    case 1006:
      return 'no response — server unreachable (TLS/proxy/network); is `tailscale serve` up?';
    case 1011:
      return 'server error';
    case 1015:
      return 'TLS handshake failed (bad/missing cert)';
    case 4000:
      return 'superseded by a newer connection';
    case 4401:
      return 'unauthorized — bad/missing token';
    case 4403:
      return 'origin not allowed';
    default:
      return `code ${code}`;
  }
}

/** Host portion of a ws(s):// URL for compact display, or the raw value. */
function hostOf(url: string | undefined): string {
  if (!url) return '(unset)';
  try {
    return new URL(url).host || url;
  } catch {
    return url;
  }
}

/** Display order for {@link StatusModel.lines}. */
const FIELD_ORDER: readonly StatusField[] = [
  'server',
  'camera',
  'signaling',
  'connection',
  'quality',
  'detections',
];

export class StatusModel {
  private readonly fields = new Map<StatusField, string>();
  private readonly listeners = new Set<() => void>();

  set(field: StatusField, value: string): void {
    if (this.fields.get(field) === value) return;
    this.fields.set(field, value);
    for (const listener of this.listeners) listener();
  }

  get(field: StatusField): string | undefined {
    return this.fields.get(field);
  }

  /** Stable-ordered `field: value` lines for display. */
  lines(): string[] {
    return FIELD_ORDER.filter((f) => this.fields.has(f)).map((f) => `${f}: ${this.fields.get(f)}`);
  }

  onChange(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /**
   * The single most important thing to show the user right now, or null when
   * everything is healthy. Priority: camera failure → connection problem →
   * signaling drop → quality gate → connection progress.
   */
  headline(): string | null {
    const camera = this.fields.get('camera');
    if (camera?.includes('error')) return `camera: ${camera}`;
    const connection = this.fields.get('connection');
    if (connection && (connection.includes('failed') || connection.includes('disconnected'))) {
      return `connection: ${connection}`;
    }
    const signaling = this.fields.get('signaling');
    if (signaling?.includes('retrying') || signaling?.includes("can't reach")) {
      return `signaling: ${signaling}`;
    }
    const quality = this.fields.get('quality');
    if (quality?.includes('paused')) return `quality: ${quality}`;
    if (connection && connection !== 'ready' && connection !== 'connected') {
      return `connection: ${connection}`;
    }
    return null;
  }

  reset(): void {
    this.fields.clear();
    for (const listener of this.listeners) listener();
  }
}

/** The app-wide status instance systems report into. */
export const status = new StatusModel();

/** Render the model into a DOM container, one line per field, kept live. */
export function bindStatusDom(model: StatusModel, container: HTMLElement): () => void {
  const render = (): void => {
    container.textContent = model.lines().join('\n');
  };
  render();
  return model.onChange(render);
}

export interface StatusServices {
  readonly signaling?: ISignalingService;
  readonly webrtc?: IWebRTCService;
  readonly detection?: IDetectionService;
  readonly qualifier?: IImageQualifierService;
}

/**
 * Subscribe the model to the library services' events. Returns a single
 * unsubscribe for everything.
 */
export function wireStatusServices(model: StatusModel, services: StatusServices): () => void {
  const unsubs: Array<() => void> = [];

  if (services.signaling) {
    unsubs.push(
      services.signaling.on('connected', () =>
        model.set('signaling', `connected → ${hostOf(model.get('server'))}`),
      ),
    );
    unsubs.push(
      services.signaling.on('disconnected', (code) =>
        // Include the target host + a decoded cause so the AR HUD says *what*
        // it can't reach and *why*, not just an opaque number.
        model.set(
          'signaling',
          `can't reach ${hostOf(model.get('server'))} — ${describeCloseCode(code)} — retrying`,
        ),
      ),
    );
  }
  if (services.webrtc) {
    unsubs.push(services.webrtc.on('stateChange', (state) => model.set('connection', state)));
    unsubs.push(services.webrtc.on('ready', () => model.set('connection', 'ready')));
  }
  if (services.qualifier) {
    unsubs.push(
      services.qualifier.on('quality', (report) =>
        model.set(
          'quality',
          report.ok ? 'ok' : `paused (${report.metrics.map((m) => m.detail ?? m.name).join(', ')})`,
        ),
      ),
    );
  }
  if (services.detection) {
    unsubs.push(
      services.detection.on('detections', (payload) =>
        model.set('detections', `${payload.detections.length} @ frame ${payload.frame}`),
      ),
    );
  }

  return () => {
    for (const unsub of unsubs.splice(0)) unsub();
  };
}
