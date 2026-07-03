import {
  BaseEventService,
  type ServiceActivationContext,
} from '@realitycollective/service-framework';
import { createLogger } from '../../util/logger';
import type {
  CloseInfo,
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

/** Close code the server sends when OUR OWN reconnect replaces a prior session. */
const REPLACED_CLOSE_CODE = 4001;

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
  private _lastCloseInfo: CloseInfo | undefined;

  constructor(context: ServiceActivationContext<SignalingConfig>) {
    super(context);
  }

  private get cfg(): SignalingConfig {
    return this.serviceConfig;
  }

  get isConnected(): boolean {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  get lastCloseInfo(): CloseInfo | undefined {
    return this._lastCloseInfo;
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
    // We're connecting now — cancel any pending reconnect so it can't fire a
    // second, overlapping socket later.
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
    if (this.socket) {
      if (this.socket.readyState === WebSocket.OPEN) return Promise.resolve();
      // Never open a SECOND socket while one is still CONNECTING — even if
      // pendingOpen was cleared by an onerror that hasn't reached onclose yet
      // (that window is exactly how overlapping connections were created).
      if (this.socket.readyState === WebSocket.CONNECTING) {
        return this.pendingOpen ?? Promise.resolve();
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
        // Capture the full close picture — code + reason + wasClean is what tells
        // us WHO closed it: wasClean=false + 1006 = the transport dropped it (no
        // close frame); a clean code = the server/proxy closed deliberately.
        this._lastCloseInfo = {
          code: event.code,
          reason: (event as CloseEvent).reason ?? '',
          wasClean: (event as CloseEvent).wasClean ?? false,
        };
        log.warn(
          `Disconnected: code=${event.code} clean=${this._lastCloseInfo.wasClean} reason=${JSON.stringify(this._lastCloseInfo.reason)}`,
        );
        this.emit('disconnected', event.code);
        // 4000 = superseded by a different client; 4001 = replaced by our own
        // reconnect. Either way the server deliberately closed us in favour of
        // another connection — reconnecting would just fight it. Stay down.
        if (event.code === SUPERSEDED_CLOSE_CODE || event.code === REPLACED_CLOSE_CODE) {
          log.warn(`Closed by server (code ${event.code}) — not reconnecting.`);
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
    if (this.reconnectTimer) return; // one reconnect in flight at a time — never stack
    const backoff = this.cfg.reconnectBackoffMs ?? DEFAULT_BACKOFF;
    if (backoff.length === 0) return;
    const delay = backoff[Math.min(this.reconnectAttempt, backoff.length - 1)]!;
    this.reconnectAttempt += 1;
    log.info(`Reconnecting in ${delay}ms (attempt ${this.reconnectAttempt})`);
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = undefined;
      void this.connect().catch(() => {
        /* onclose reschedules */
      });
    }, delay);
  }
}
