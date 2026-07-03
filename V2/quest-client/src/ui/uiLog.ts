/**
 * Subtle on-page activity log.
 *
 * A small translucent panel pinned to the **bottom-left** that fills **upward**
 * (newest line at the bottom). Its job is debugging visibility — "is data
 * actually flowing?" — during bring-up: frames leaving the client, detection
 * payloads arriving back, and connection state.
 *
 * Scope note: DOM overlays are only visible in the flat browser / pre-AR view,
 * not inside an immersive session. In AR the **red detection boxes**
 * (DetectionRenderSystem) are the equivalent "it's working" signal, and the
 * head-locked StatusSpriteSystem shows the current headline.
 */
export class UiLog {
  private el: HTMLElement | null = null;
  private readonly lines: string[] = [];
  private readonly lastAt = new Map<string, number>();

  constructor(private readonly max = 12) {}

  /** Create the panel and attach it (idempotent). No-op without a DOM. */
  mount(container?: HTMLElement): void {
    if (this.el || typeof document === 'undefined') return;
    const el = document.createElement('div');
    el.id = 'uilog';
    Object.assign(el.style, {
      position: 'fixed',
      left: '12px',
      bottom: '12px',
      maxWidth: '48vw',
      maxHeight: '42vh',
      overflow: 'hidden',
      font: '11px/1.4 ui-monospace, SFMono-Regular, Menlo, monospace',
      color: 'rgba(220, 232, 244, 0.82)',
      textShadow: '0 1px 2px rgba(0, 0, 0, 0.75)',
      whiteSpace: 'pre-wrap',
      wordBreak: 'break-word',
      pointerEvents: 'none',
      zIndex: '30',
    } satisfies Partial<CSSStyleDeclaration>);
    (container ?? document.body).appendChild(el);
    this.el = el;
    this.render();
  }

  /** Append a line; oldest lines fall off the top once past {@link max}. */
  push(message: string): void {
    this.lines.push(message);
    while (this.lines.length > this.max) this.lines.shift();
    this.render();
  }

  /**
   * Rate-limited push: emits `message` at most once per `intervalMs` for a
   * given `key`. Used to keep the high-frequency streaming/receipt lines calm
   * (frames every 5 s, detections every 1 s).
   */
  throttle(key: string, intervalMs: number, message: string): void {
    const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
    const last = this.lastAt.get(key) ?? Number.NEGATIVE_INFINITY;
    if (now - last < intervalMs) return;
    this.lastAt.set(key, now);
    this.push(message);
  }

  /** True at most once per `intervalMs` for `key` — gate a multi-line block. */
  allow(key: string, intervalMs: number): boolean {
    const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
    const last = this.lastAt.get(key) ?? Number.NEGATIVE_INFINITY;
    if (now - last < intervalMs) return false;
    this.lastAt.set(key, now);
    return true;
  }

  private render(): void {
    if (this.el) this.el.textContent = this.lines.join('\n');
  }
}

/** App-wide activity log instance. */
export const uiLog = new UiLog();
