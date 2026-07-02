import type { IEventService } from '@realitycollective/service-framework';
import { createServiceToken } from '@realitycollective/service-framework';

export type WebRTCConnectionState =
  | 'new'
  | 'connecting'
  | 'connected'
  | 'disconnected'
  | 'failed'
  | 'closed';

export type WebRTCEventMap = {
  /** Connection-state transitions (for UI/status). */
  stateChange: WebRTCConnectionState;
  /** Raw string messages received on the `detections` data channel. */
  detectionMessage: string;
  /** Server signalled `{ "type": "ready" }`. */
  ready: undefined;
};

/**
 * Owns the `RTCPeerConnection`. Client is the offerer, creates the `detections`
 * data channel, and sends the camera video track — the exact flow
 * `webrtc_server.py` expects. Depends (constructor-injected) on
 * {@link ISignalingService}.
 */
export interface IWebRTCService extends IEventService<WebRTCEventMap> {
  readonly connectionState: WebRTCConnectionState;
  setVideoStream(stream: MediaStream): void;
  connect(): Promise<void>;
  close(): void;
}

export const IWebRTCService = createServiceToken<IWebRTCService>('IWebRTCService');
