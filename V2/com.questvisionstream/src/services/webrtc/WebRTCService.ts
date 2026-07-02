import {
  BaseEventService,
  type ServiceActivationContext,
} from '@realitycollective/service-framework';
import { createLogger } from '../../util/logger';
import type { CandidateMessage, ISignalingService } from '../signaling/ISignalingService';
import type { IWebRTCService, WebRTCConnectionState, WebRTCEventMap } from './IWebRTCService';

const log = createLogger('WebRTC');

export interface IceServerConfig {
  readonly urls: string | string[];
  readonly username?: string;
  readonly credential?: string;
}

export interface WebRTCConfig {
  readonly iceServers?: readonly IceServerConfig[];
  readonly detectionsChannelLabel?: string;
  /** Re-offer automatically after a failed session / restored signaling. Default true. */
  readonly autoReconnect?: boolean;
  /** Delay before an automatic re-offer, in ms. Default 2000. */
  readonly reconnectDelayMs?: number;
}

const DEFAULT_ICE: IceServerConfig[] = [{ urls: 'stun:stun.l.google.com:19302' }];
const DEFAULT_RECONNECT_DELAY_MS = 2000;

/** aiortc carries candidate lines without the `candidate:` prefix; strip it on send. */
function stripCandidatePrefix(candidate: string): string {
  return candidate.startsWith('candidate:') ? candidate.slice('candidate:'.length) : candidate;
}
/** …and re-add it on receive, since the browser's `RTCIceCandidate` expects it. */
function addCandidatePrefix(candidate: string): string {
  return candidate.startsWith('candidate:') ? candidate : `candidate:${candidate}`;
}

export class WebRTCService
  extends BaseEventService<WebRTCEventMap, WebRTCConfig>
  implements IWebRTCService
{
  private readonly signaling: ISignalingService;
  private pc: RTCPeerConnection | undefined;
  private channel: RTCDataChannel | undefined;
  private stream: MediaStream | undefined;
  private _state: WebRTCConnectionState = 'new';
  private readonly unsubs: Array<() => void> = [];
  /** Remote candidates that arrived before the answer was applied (they must
   * be queued: `addIceCandidate` throws while the remote description is null). */
  private pendingCandidates: CandidateMessage[] = [];
  private remoteDescriptionSet = false;
  private reconnectTimer: ReturnType<typeof setTimeout> | undefined;
  private intentionalClose = false;
  private everAttempted = false;

  constructor(context: ServiceActivationContext<WebRTCConfig>, signaling: ISignalingService) {
    super(context);
    this.signaling = signaling;
  }

  private get iceServers(): readonly IceServerConfig[] {
    return this.serviceConfig.iceServers ?? DEFAULT_ICE;
  }
  private get channelLabel(): string {
    return this.serviceConfig.detectionsChannelLabel ?? 'detections';
  }
  private get autoReconnect(): boolean {
    return this.serviceConfig.autoReconnect ?? true;
  }
  private get reconnectDelayMs(): number {
    return this.serviceConfig.reconnectDelayMs ?? DEFAULT_RECONNECT_DELAY_MS;
  }

  get connectionState(): WebRTCConnectionState {
    return this._state;
  }

  override start(): void {
    this.unsubs.push(
      this.signaling.on('message', (msg) => {
        if (msg.type === 'answer') void this.onAnswer(msg.sdp);
        else if (msg.type === 'candidate') void this.onRemoteCandidate(msg);
      }),
    );
    // Signaling came back after a drop: if our session never completed (or
    // died with it), the server side no longer knows us — re-offer.
    this.unsubs.push(
      this.signaling.on('connected', () => {
        if (this.everAttempted && this._state !== 'connected') {
          this.scheduleReconnect('signaling restored');
        }
      }),
    );
  }

  setVideoStream(stream: MediaStream): void {
    this.stream = stream;
  }

  async connect(): Promise<void> {
    this.clearReconnectTimer();
    this.intentionalClose = false;
    if (this.pc) {
      log.warn('connect() called with an existing peer connection; closing old one');
      this.teardown();
    }
    if (!this.stream) throw new Error('setVideoStream() must be called before connect()');
    this.everAttempted = true;
    // SignalingService.connect() resolves only when the socket is OPEN, so the
    // offer below cannot race a still-connecting socket.
    if (!this.signaling.isConnected) await this.signaling.connect();

    const pc = new RTCPeerConnection({ iceServers: this.iceServers as RTCIceServer[] });
    this.pc = pc;
    this.setState('connecting');

    const channel = pc.createDataChannel(this.channelLabel);
    this.channel = channel;
    channel.onopen = () => log.info('Detections channel open');
    channel.onmessage = (e) => this.onChannelMessage(e.data);

    for (const track of this.stream.getVideoTracks()) pc.addTrack(track, this.stream);

    pc.onicecandidate = (event) => {
      if (!event.candidate) return;
      const payload: CandidateMessage = {
        type: 'candidate',
        candidate: stripCandidatePrefix(event.candidate.candidate),
        sdpMid: event.candidate.sdpMid,
        sdpMLineIndex: event.candidate.sdpMLineIndex ?? 0,
      };
      this.signaling.send(payload);
    };
    pc.onconnectionstatechange = () => {
      log.info(`PC state: ${pc.connectionState}`);
      this.setState(pc.connectionState as WebRTCConnectionState);
    };

    const offer = await pc.createOffer({ offerToReceiveVideo: false });
    await pc.setLocalDescription(offer);
    this.signaling.send({ type: 'offer', sdp: offer.sdp! });
    log.info('Offer sent');
  }

  close(): void {
    this.intentionalClose = true;
    this.clearReconnectTimer();
    this.teardown();
    this.setState('closed');
  }

  override destroy(): void {
    for (const unsub of this.unsubs.splice(0)) unsub();
    this.close();
    super.destroy();
  }

  /** Release the current session's resources without touching reconnect intent. */
  private teardown(): void {
    this.channel?.close();
    this.pc?.close();
    this.channel = undefined;
    this.pc = undefined;
    this.remoteDescriptionSet = false;
    this.pendingCandidates = [];
  }

  private async onAnswer(sdp: string): Promise<void> {
    const pc = this.pc;
    if (!pc) return;
    log.info('Answer received');
    try {
      await pc.setRemoteDescription({ type: 'answer', sdp });
    } catch (err) {
      // Never let this become an unhandled rejection (the caller is a
      // fire-and-forget event handler). A bad answer is a protocol error, not
      // a transient network fault — surface it, don't auto-retry into a loop.
      log.error('Failed to apply answer SDP', err);
      return;
    }
    if (this.pc !== pc) return; // session was replaced while applying
    this.remoteDescriptionSet = true;
    for (const queued of this.pendingCandidates.splice(0)) {
      await this.applyCandidate(queued);
    }
  }

  private async onRemoteCandidate(msg: CandidateMessage): Promise<void> {
    if (!this.pc) return;
    if (!this.remoteDescriptionSet) {
      this.pendingCandidates.push(msg); // applied in order after the answer
      return;
    }
    await this.applyCandidate(msg);
  }

  private async applyCandidate(msg: CandidateMessage): Promise<void> {
    if (!this.pc) return;
    try {
      await this.pc.addIceCandidate({
        candidate: addCandidatePrefix(msg.candidate),
        sdpMid: msg.sdpMid,
        sdpMLineIndex: msg.sdpMLineIndex,
      });
    } catch (err) {
      log.warn('Failed to add remote ICE candidate', err);
    }
  }

  private onChannelMessage(data: unknown): void {
    if (typeof data !== 'string') return;
    if (data.includes('"ready"')) {
      try {
        if ((JSON.parse(data) as { type?: string }).type === 'ready') {
          log.info('Server ready');
          this.emit('ready', undefined);
          return;
        }
      } catch {
        /* fall through */
      }
    }
    this.emit('detectionMessage', data);
  }

  private setState(state: WebRTCConnectionState): void {
    if (state === this._state) return;
    this._state = state;
    this.emit('stateChange', state);
    // 'failed' is terminal (unlike 'disconnected', which ICE can self-heal);
    // recover by renegotiating a fresh session.
    if (state === 'failed') this.scheduleReconnect('peer connection failed');
  }

  private scheduleReconnect(reason: string): void {
    if (!this.autoReconnect || this.intentionalClose || !this.stream) return;
    if (this.reconnectTimer) return;
    log.info(`Reconnecting in ${this.reconnectDelayMs}ms (${reason})`);
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = undefined;
      this.connect().catch((err) => log.warn('Reconnect attempt failed', err));
    }, this.reconnectDelayMs);
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
  }
}
