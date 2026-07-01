import type { IService } from '@realitycollective/service-framework-ts';
import { createServiceToken } from '@realitycollective/service-framework-ts';
import type { Emitter } from '../../util/Emitter';

/**
 * Signaling wire messages. These mirror exactly what `webrtc_server.py` exchanges
 * over the WebSocket. Note the ICE `candidate` string is carried **without** the
 * `candidate:` SDP prefix — aiortc emits and expects it that way. The
 * {@link IWebRTCService} is responsible for stripping the prefix on send and
 * re-adding it on receive; the signaling layer is a dumb JSON transport.
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
  /** SDP candidate line WITHOUT the leading `candidate:` token. */
  candidate: string;
  sdpMid: string | null;
  sdpMLineIndex: number;
}

export type SignalingOutbound = OfferMessage | CandidateMessage;
export type SignalingInbound = AnswerMessage | CandidateMessage;

/**
 * Transport for the WebRTC handshake. Owns the WebSocket connection and provides
 * typed send + inbound event streams. Reconnection is handled internally.
 */
export interface ISignalingService extends IService {
  readonly isConnected: boolean;

  /** Inbound messages from the server (answer / candidate). */
  readonly messages: Emitter<SignalingInbound>;
  /** Fires when the socket opens. */
  readonly connected: Emitter<void>;
  /** Fires when the socket closes (with the close code). */
  readonly disconnected: Emitter<number>;

  /** Open the socket (no-op if already connecting/open). */
  connect(): Promise<void>;
  /** Close the socket and stop reconnecting. */
  disconnect(): void;
  /** Send a signaling message to the server. */
  send(message: SignalingOutbound): void;
}

export const ISignalingService = createServiceToken<ISignalingService>('ISignalingService');
