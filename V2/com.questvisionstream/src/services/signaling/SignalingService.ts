import { BaseService } from '@realitycollective/service-framework-ts';
import { Emitter } from '../../util/Emitter';
import { createLogger } from '../../util/logger';
import type {
  ISignalingService,
  SignalingInbound,
  SignalingOutbound,
} from './ISignalingService';

const log = createLogger('Signaling');

export interface SignalingConfig {
  /** WebSocket URL, e.g. `ws://192.168.1.20:3000`. */
  readonly url: string;
  /** Auto-connect on service start (default true). */
  readonly autoConnect?: boolean;
  /** Reconnect backoff in ms; empty array disables reconnect. Default [1000,2000,4000,8000]. */
  readonly reconnectBackoffMs?: readonly number[];
}

/**
 * WebSocket signaling transport. Speaks the exact JSON protocol of
 * `webrtc_server.py`: sends `offer`/`candidate`, receives `answer`/`candidate`.
 * Handles reconnection with bounded exponential backoff.
 */
export class SignalingService extends BaseService implements ISignalingService {
  readonly messages = new Emitter<SignalingInbound>();
  readonly connected = new Emitter<void>();
  readonly disconnected = new Emitter<number>();

  private readonly url: string;
  private readonly autoConnect: boolean;
  private readonly backoff: readonly number[];

  private socket: WebSocket | undefined;
  private reconnectAttempt = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | undefined;
  private closedByUser = false;

  constructor(config: SignalingConfig) {
    // Priority 10: signaling comes up before the peer connection (priority 20).
    super('SignalingService', 10);
    this.url = config.url;
    this.autoConnect = config.autoConnect ?? true;
    this.backoff = config.reconnectBackoffMs ?? [1000, 2000, 4000, 8000];
  }

  get isConnected(): boolean {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  override async start(): Promise<void> {
    await super.start();
    if (this.autoConnect) await this.connect();
  }

  connect(): Promise<void> {
    this.closedByUser = false;
    if (
      this.socket &&
      (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING)
    ) {
      return Promise.resolve();
    }

    return new Promise<void>((resolve, reject) => {
      log.info(`Connecting to ${this.url}`);
      const socket = new WebSocket(this.url);
      this.socket = socket;

      socket.onopen = () => {
        log.info('Connected');
        this.reconnectAttempt = 0;
        this.connected.emit();
        resolve();
      };

      socket.onmessage = (event) => this.handleMessage(event.data);

      socket.onerror = (event) => {
        log.warn('Socket error', event);
        // Defer resolution/rejection to onclose so reconnect logic runs once.
        if (this.socket === socket && socket.readyState !== WebSocket.OPEN) reject(new Error('Signaling socket error'));
      };

      socket.onclose = (event) => {
        log.info(`Disconnected (code ${event.code})`);
        this.disconnected.emit(event.code);
        if (!this.closedByUser) this.scheduleReconnect();
      };
    });
  }

  disconnect(): void {
    this.closedByUser = true;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
    this.socket?.close();
    this.socket = undefined;
  }

  send(message: SignalingOutbound): void {
    if (!this.isConnected) {
      log.warn('Dropping message; socket not open', message.type);
      return;
    }
    this.socket!.send(JSON.stringify(message));
  }

  override async destroy(): Promise<void> {
    this.disconnect();
    this.messages.clear();
    this.connected.clear();
    this.disconnected.clear();
    await super.destroy();
  }

  private handleMessage(data: unknown): void {
    if (typeof data !== 'string') return;
    let parsed: SignalingInbound;
    try {
      parsed = JSON.parse(data) as SignalingInbound;
    } catch {
      log.warn('Ignoring non-JSON signaling message');
      return;
    }
    if (parsed?.type === 'answer' || parsed?.type === 'candidate') {
      this.messages.emit(parsed);
    }
  }

  private scheduleReconnect(): void {
    if (this.backoff.length === 0) return;
    const delay = this.backoff[Math.min(this.reconnectAttempt, this.backoff.length - 1)]!;
    this.reconnectAttempt += 1;
    log.info(`Reconnecting in ${delay}ms (attempt ${this.reconnectAttempt})`);
    this.reconnectTimer = setTimeout(() => {
      void this.connect().catch(() => {
        /* onclose reschedules */
      });
    }, delay);
  }
}
