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
    unsubs.push(services.signaling.on('connected', () => model.set('signaling', 'connected')));
    unsubs.push(
      services.signaling.on('disconnected', (code) =>
        model.set('signaling', `disconnected (code ${code}) — retrying`),
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
