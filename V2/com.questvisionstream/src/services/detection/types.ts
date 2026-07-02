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
  /** Server counter of frames RECEIVED (gaps = frames the server skipped). */
  readonly frame: number;
  /**
   * Media timestamp of the processed frame (RTP clock units, 90 kHz), or
   * null/absent when the source frame carried none. Additive (2026-07 server
   * hardening); use it to correlate a detection with the captured frame.
   */
  readonly pts?: number | null;
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

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

/**
 * Runtime guard: validate an arbitrary parsed JSON object is a
 * DetectionsPayload. Every numeric field is checked (bbox elements included) —
 * a payload that passes here must never produce NaN downstream in
 * `DetectionMath`.
 */
export function isDetectionsPayload(value: unknown): value is DetectionsPayload {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  if (v.type !== 'detections') return false;
  if (!isFiniteNumber(v.frame) || !isFiniteNumber(v.width) || !isFiniteNumber(v.height)) {
    return false;
  }
  if (!Array.isArray(v.detections)) return false;
  return v.detections.every((d) => {
    if (typeof d !== 'object' || d === null) return false;
    const det = d as Record<string, unknown>;
    return (
      typeof det.label === 'string' &&
      isFiniteNumber(det.conf) &&
      Array.isArray(det.bbox) &&
      det.bbox.length === 4 &&
      det.bbox.every(isFiniteNumber)
    );
  });
}
