import type { IEventService } from '@realitycollective/service-framework';
import { createServiceToken } from '@realitycollective/service-framework';

/**
 * Signaling wire messages — mirror exactly what `webrtc_server.py` exchanges over
 * the WebSocket. The ICE `candidate` string is carried WITHOUT the `candidate:`
 * SDP prefix (aiortc's form); {@link IWebRTCService} strips/re-adds it.
 */
export interface OfferMessage {
  type: 'offer';
  sdp: string;
}
export interface AnswerMessage {
  type: 'answer';
  sdp: string;
}
export interface CandidateMessage {
  type: 'candidate';
  candidate: string;
  sdpMid: string | null;
  sdpMLineIndex: number;
}

export type SignalingOutbound = OfferMessage | CandidateMessage;
export type SignalingInbound = AnswerMessage | CandidateMessage;

/**
 * Typed event surface (RealityCollective `IEventService`). Declared as a `type`
 * (not `interface`) so it satisfies the framework's `Record<string, unknown>`
 * event-map constraint.
 */
export type SignalingEventMap = {
  /** Inbound answer/candidate from the server. */
  message: SignalingInbound;
  /** Socket opened. */
  connected: undefined;
  /** Socket closed (with close code). */
  disconnected: number;
};

/**
 * WebSocket transport for the WebRTC handshake. A RealityCollective service:
 * lifecycle + events come from `BaseEventService`.
 */
export interface ISignalingService extends IEventService<SignalingEventMap> {
  readonly isConnected: boolean;
  connect(): Promise<void>;
  disconnect(): void;
  send(message: SignalingOutbound): void;
}

export const ISignalingService = createServiceToken<ISignalingService>('ISignalingService');
