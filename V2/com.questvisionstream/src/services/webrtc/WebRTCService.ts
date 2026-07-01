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
}

const DEFAULT_ICE: IceServerConfig[] = [{ urls: 'stun:stun.l.google.com:19302' }];

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
  private unsub: (() => void) | undefined;

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

  get connectionState(): WebRTCConnectionState {
    return this._state;
  }

  override start(): void {
    this.unsub = this.signaling.on('message', (msg) => {
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
    this.channel?.close();
    this.pc?.close();
    this.channel = undefined;
    this.pc = undefined;
    this.setState('closed');
  }

  override destroy(): void {
    this.unsub?.();
    this.close();
    super.destroy();
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
  }
}
