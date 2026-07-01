/**
 * Wire protocol types — kept byte-identical to what `webrtc_server.py` sends over
 * the `detections` data channel and to the Unity `DetectionsPayload`/`Detection`
 * classes, so the same server serves the Unity client and this WebXR client
 * unchanged.
 */

/** A single detection. `bbox` is `[x1, y1, x2, y2]` in STREAM pixels. */
export interface Detection {
  readonly label: string;
  readonly conf: number;
  readonly bbox: [number, number, number, number];
}

/** The per-frame payload pushed by the server over the `detections` channel. */
export interface DetectionsPayload {
  readonly type: 'detections';
  /** Server frame counter. */
  readonly frame: number;
  /** Width of the frame the boxes were computed against (server ramps this up). */
  readonly width: number;
  /** Height of the frame the boxes were computed against. */
  readonly height: number;
  readonly detections: Detection[];
}

/** The `{ "type": "ready" }` handshake the server sends when the channel opens. */
export interface ReadyMessage {
  readonly type: 'ready';
}

export type ServerMessage = DetectionsPayload | ReadyMessage;

/** Runtime guard: validate an arbitrary parsed JSON object is a DetectionsPayload. */
export function isDetectionsPayload(value: unknown): value is DetectionsPayload {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  if (v.type !== 'detections') return false;
  if (!Array.isArray(v.detections)) return false;
  return v.detections.every(
    (d) =>
      typeof d === 'object' &&
      d !== null &&
      typeof (d as Detection).label === 'string' &&
      typeof (d as Detection).conf === 'number' &&
      Array.isArray((d as Detection).bbox) &&
      (d as Detection).bbox.length === 4,
  );
}
