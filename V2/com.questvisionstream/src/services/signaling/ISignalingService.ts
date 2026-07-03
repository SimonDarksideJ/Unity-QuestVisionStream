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

/**
 * Client → server device/telemetry status, carried over the signaling socket so
 * it works BEFORE (or without) a WebRTC connection — the point at which most
 * device problems (camera permission, no passthrough device) actually happen.
 * The server logs these; they are not part of the WebRTC handshake.
 */
export interface StatusMessage {
  type: 'status';
  field: string;
  value: string;
}

export type SignalingOutbound = OfferMessage | CandidateMessage | StatusMessage;
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
/** Details of the most recent socket close — for diagnosing *why* it dropped. */
export interface CloseInfo {
  /** WebSocket close code (1006 = abnormal/no close frame; 1000/1001 = clean). */
  readonly code: number;
  /** Close reason string, if any. */
  readonly reason: string;
  /** True only if a proper close handshake happened (a close frame was received). */
  readonly wasClean: boolean;
}

export interface ISignalingService extends IEventService<SignalingEventMap> {
  readonly isConnected: boolean;
  /** The most recent close, or undefined before the first disconnect. */
  readonly lastCloseInfo?: CloseInfo;
  connect(): Promise<void>;
  disconnect(): void;
  send(message: SignalingOutbound): void;
}

export const ISignalingService = createServiceToken<ISignalingService>('ISignalingService');
