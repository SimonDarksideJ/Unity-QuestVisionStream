import {
  BaseEventService,
  type ServiceActivationContext,
} from '@realitycollective/service-framework';
import { createLogger } from '../../util/logger';
import type {
  ISignalingService,
  SignalingEventMap,
  SignalingInbound,
  SignalingOutbound,
} from './ISignalingService';

const log = createLogger('Signaling');

export interface SignalingConfig {
  /** WebSocket URL, e.g. `ws://192.168.1.20:3000`. */
  readonly url: string;
  /** Auto-connect on service start (default true). */
  readonly autoConnect?: boolean;
  /** Reconnect backoff in ms; empty disables reconnect. */
  readonly reconnectBackoffMs?: readonly number[];
}

const DEFAULT_BACKOFF = [1000, 2000, 4000, 8000] as const;

/**
 * WebSocket signaling transport. Speaks `webrtc_server.py`'s JSON protocol:
 * sends `offer`/`candidate`, receives `answer`/`candidate`. Reconnects with
 * bounded exponential backoff.
 */
export class SignalingService
  extends BaseEventService<SignalingEventMap, SignalingConfig>
  implements ISignalingService
{
  private socket: WebSocket | undefined;
  private reconnectAttempt = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | undefined;
  private closedByUser = false;

  constructor(context: ServiceActivationContext<SignalingConfig>) {
    super(context);
  }

  private get cfg(): SignalingConfig {
    return this.serviceConfig;
  }

  get isConnected(): boolean {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  override start(): void {
    if (this.cfg.autoConnect ?? true) void this.connect();
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
      log.info(`Connecting to ${this.cfg.url}`);
      const socket = new WebSocket(this.cfg.url);
      this.socket = socket;

      socket.onopen = () => {
        log.info('Connected');
        this.reconnectAttempt = 0;
        this.emit('connected', undefined);
        resolve();
      };
      socket.onmessage = (event) => this.handleMessage(event.data);
      socket.onerror = () => {
        if (this.socket === socket && socket.readyState !== WebSocket.OPEN) {
          reject(new Error('Signaling socket error'));
        }
      };
      socket.onclose = (event) => {
        log.info(`Disconnected (code ${event.code})`);
        this.emit('disconnected', event.code);
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

  override destroy(): void {
    this.disconnect();
    super.destroy();
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
      this.emit('message', parsed);
    }
  }

  private scheduleReconnect(): void {
    const backoff = this.cfg.reconnectBackoffMs ?? DEFAULT_BACKOFF;
    if (backoff.length === 0) return;
    const delay = backoff[Math.min(this.reconnectAttempt, backoff.length - 1)]!;
    this.reconnectAttempt += 1;
    log.info(`Reconnecting in ${delay}ms (attempt ${this.reconnectAttempt})`);
    this.reconnectTimer = setTimeout(() => {
      void this.connect().catch(() => {
        /* onclose reschedules */
      });
    }, delay);
  }
}
