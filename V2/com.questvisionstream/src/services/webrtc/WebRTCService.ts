import { BaseService } from '@realitycollective/service-framework-ts';
import { Emitter } from '../../util/Emitter';
import { createLogger } from '../../util/logger';
import type { ISignalingService, CandidateMessage } from '../signaling/ISignalingService';
import type { IWebRTCService, WebRTCConnectionState } from './IWebRTCService';

const log = createLogger('WebRTC');

export interface IceServerConfig {
  readonly urls: string | string[];
  readonly username?: string;
  readonly credential?: string;
}

export interface WebRTCConfig {
  /** STUN/TURN servers. Defaults to Google STUN. */
  readonly iceServers?: readonly IceServerConfig[];
  /** Label of the data channel the client opens for detections. Default 'detections'. */
  readonly detectionsChannelLabel?: string;
}

const DEFAULT_ICE: IceServerConfig[] = [{ urls: 'stun:stun.l.google.com:19302' }];

/**
 * Strip aiortc-incompatible `candidate:` prefix before sending to the Python
 * server (which parses the bare SDP candidate line).
 */
function stripCandidatePrefix(candidate: string): string {
  return candidate.startsWith('candidate:') ? candidate.slice('candidate:'.length) : candidate;
}

/**
 * Re-add the `candidate:` prefix the browser's `RTCIceCandidate` expects, since
 * aiortc sends it bare.
 */
function addCandidatePrefix(candidate: string): string {
  return candidate.startsWith('candidate:') ? candidate : `candidate:${candidate}`;
}

/**
 * Owns the RTCPeerConnection. Client is the offerer, creates the `detections`
 * data channel, and sends the camera video track. Depends on an
 * {@link ISignalingService} (constructor-injected) for the handshake.
 */
export class WebRTCService extends BaseService implements IWebRTCService {
  readonly stateChanged = new Emitter<WebRTCConnectionState>();
  readonly detectionMessages = new Emitter<string>();
  readonly ready = new Emitter<void>();

  private readonly signaling: ISignalingService;
  private readonly iceServers: readonly IceServerConfig[];
  private readonly channelLabel: string;

  private pc: RTCPeerConnection | undefined;
  private channel: RTCDataChannel | undefined;
  private stream: MediaStream | undefined;
  private _state: WebRTCConnectionState = 'new';
  private unsub: (() => void) | undefined;

  constructor(config: WebRTCConfig, signaling: ISignalingService) {
    // Priority 20: comes up after signaling (priority 10).
    super('WebRTCService', 20);
    this.signaling = signaling;
    this.iceServers = config.iceServers ?? DEFAULT_ICE;
    this.channelLabel = config.detectionsChannelLabel ?? 'detections';
  }

  get connectionState(): WebRTCConnectionState {
    return this._state;
  }

  override async start(): Promise<void> {
    await super.start();
    // Subscribe to inbound signaling; connect() is driven by the host once a
    // camera stream is available.
    this.unsub = this.signaling.messages.on((msg) => {
      if (msg.type === 'answer') void this.onAnswer(msg.sdp);
      else if (msg.type === 'candidate') void this.onRemoteCandidate(msg);
    });
  }

  setVideoStream(stream: MediaStream): void {
    this.stream = stream;
  }

  async connect(): Promise<void> {
    if (this.pc) {
      log.warn('connect() called with an existing peer connection; closing old one');
      this.close();
    }
    if (!this.stream) throw new Error('setVideoStream() must be called before connect()');
    if (!this.signaling.isConnected) await this.signaling.connect();

    const pc = new RTCPeerConnection({ iceServers: this.iceServers as RTCIceServer[] });
    this.pc = pc;
    this.setState('connecting');

    // Client creates the detections data channel; server only listens.
    const channel = pc.createDataChannel(this.channelLabel);
    this.channel = channel;
    channel.onopen = () => log.info('Detections channel open');
    channel.onmessage = (e) => this.onChannelMessage(e.data);

    // Send the camera video track.
    for (const track of this.stream.getVideoTracks()) {
      pc.addTrack(track, this.stream);
    }

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
    this.channel?.close();
    this.pc?.close();
    this.channel = undefined;
    this.pc = undefined;
    this.setState('closed');
  }

  override async destroy(): Promise<void> {
    this.unsub?.();
    this.close();
    this.stateChanged.clear();
    this.detectionMessages.clear();
    this.ready.clear();
    await super.destroy();
  }

  private async onAnswer(sdp: string): Promise<void> {
    if (!this.pc) return;
    log.info('Answer received');
    await this.pc.setRemoteDescription({ type: 'answer', sdp });
  }

  private async onRemoteCandidate(msg: CandidateMessage): Promise<void> {
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
    // Fast-path the ready handshake without a full parse in DetectionService.
    if (data.includes('"ready"')) {
      try {
        if ((JSON.parse(data) as { type?: string }).type === 'ready') {
          log.info('Server ready');
          this.ready.emit();
          return;
        }
      } catch {
        /* fall through */
      }
    }
    this.detectionMessages.emit(data);
  }

  private setState(state: WebRTCConnectionState): void {
    if (state === this._state) return;
    this._state = state;
    this.stateChanged.emit(state);
  }
}
