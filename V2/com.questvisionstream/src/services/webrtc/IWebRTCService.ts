import type { IService } from '@realitycollective/service-framework-ts';
import { createServiceToken } from '@realitycollective/service-framework-ts';
import type { Emitter } from '../../util/Emitter';

export type WebRTCConnectionState =
  | 'new'
  | 'connecting'
  | 'connected'
  | 'disconnected'
  | 'failed'
  | 'closed';

/**
 * Owns the `RTCPeerConnection` and speaks the exact WebRTC flow expected by
 * `webrtc_server.py`:
 *  - the **client creates** the `detections` `RTCDataChannel` (the server only
 *    listens for it);
 *  - the client **adds the camera video track**; the server runs detection and
 *    pushes results back over the data channel;
 *  - the client is the **offerer** (creates the SDP offer).
 *
 * ICE-candidate prefix quirk: aiortc carries candidate lines WITHOUT the
 * `candidate:` prefix that the browser's `RTCIceCandidate` uses. This service
 * strips the prefix on send and re-adds it on receive.
 */
export interface IWebRTCService extends IService {
  readonly connectionState: WebRTCConnectionState;

  /** Connection-state transitions (for UI/status). */
  readonly stateChanged: Emitter<WebRTCConnectionState>;
  /** Raw string messages received on the `detections` data channel. */
  readonly detectionMessages: Emitter<string>;
  /** Fires when the server signals `{ "type": "ready" }` on the channel. */
  readonly ready: Emitter<void>;

  /** Provide the outbound camera media stream (its video track is sent). */
  setVideoStream(stream: MediaStream): void;

  /** Build the peer connection, add the track/channel, and offer. */
  connect(): Promise<void>;

  /** Close the peer connection. */
  close(): void;
}

export const IWebRTCService = createServiceToken<IWebRTCService>('IWebRTCService');
