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
 * WebSocket close code the server sends when its connection cap evicts this
 * session in favour of a newer one (see `webrtc_server.py` `_enforce_connection_cap`).
 * We deliberately do NOT reconnect on this — see the onclose handler.
 */
const SUPERSEDED_CLOSE_CODE = 4000;

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
  /** Open-promise for the in-flight CONNECTING socket, shared by all callers. */
  private pendingOpen: Promise<void> | undefined;
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
    // Attach a catch: an initial connect failure must not surface as an
    // unhandled rejection (onclose schedules the retry).
    if (this.cfg.autoConnect ?? true) {
      void this.connect().catch((err) => log.warn('Initial connect failed', err));
    }
  }

  /**
   * Contract: the returned promise resolves only once the socket is OPEN (so
   * `await connect()` always means "safe to send"), and rejects if this
   * connection attempt fails. Concurrent calls share the in-flight attempt.
   */
  connect(): Promise<void> {
    this.closedByUser = false;
    if (this.socket) {
      if (this.socket.readyState === WebSocket.OPEN) return Promise.resolve();
      if (this.socket.readyState === WebSocket.CONNECTING && this.pendingOpen) {
        return this.pendingOpen;
      }
    }

    const promise = new Promise<void>((resolve, reject) => {
      log.info(`Connecting to ${this.cfg.url}`);
      const socket = new WebSocket(this.cfg.url);
      this.socket = socket;
      let settled = false;

      socket.onopen = () => {
        if (this.socket !== socket) return; // superseded while connecting
        log.info('Connected');
        this.pendingOpen = undefined;
        this.reconnectAttempt = 0;
        this.emit('connected', undefined);
        settled = true;
        resolve();
      };
      socket.onmessage = (event) => {
        if (this.socket === socket) this.handleMessage(event.data);
      };
      socket.onerror = () => {
        if (this.socket === socket && socket.readyState !== WebSocket.OPEN) {
          this.pendingOpen = undefined;
          settled = true;
          reject(new Error('Signaling socket error'));
        }
      };
      socket.onclose = (event) => {
        // Settle the open-promise regardless of staleness so no caller hangs.
        if (!settled) {
          settled = true;
          if (this.pendingOpen === promise) this.pendingOpen = undefined;
          reject(new Error(`Signaling socket closed before opening (code ${event.code})`));
        }
        // A late close from a superseded socket must not look like a live
        // disconnect (and must not double-schedule reconnects).
        if (this.socket !== socket) return;
        this.socket = undefined;
        log.info(`Disconnected (code ${event.code})`);
        this.emit('disconnected', event.code);
        // 4000 = the server's connection cap superseded us with a NEWER
        // connection. Reconnecting would just supersede that one back — an
        // endless "who's connected" war between overlapping clients/tabs. Stay
        // down; the newest connection wins. (Reload to deliberately take over.)
        if (event.code === SUPERSEDED_CLOSE_CODE) {
          log.warn('Superseded by a newer connection — not reconnecting.');
          return;
        }
        if (!this.closedByUser) this.scheduleReconnect();
      };
    });
    this.pendingOpen = promise;
    return promise;
  }

  disconnect(): void {
    this.closedByUser = true;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
    this.pendingOpen = undefined;
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
