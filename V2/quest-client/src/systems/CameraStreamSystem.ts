import { createSystem, CameraSource, CameraState, CameraUtils, type Entity } from '@iwsdk/core';
import {
  IWebRTCService,
  IImageQualifierService,
  type IImageQualifierService as QualifierService,
  type QualifierFrame,
} from '@questvisionstream/client';
import { AppConfig } from '../config';
import { getServiceManager } from '../runtime';
import { status } from '../ui/status';
import { uiLog } from '../ui/uiLog';

/**
 * Owns the passthrough camera. Creates the `CameraSource` entity, and once the
 * camera is `Active`, hands its `MediaStream` to the {@link IWebRTCService} and
 * wires a downsampled frame provider into the {@link IImageQualifierService}.
 *
 * Also enforces the **edge quality gate**: each frame it toggles the outbound
 * video track's `enabled` flag from the qualifier's verdict.
 *
 * Failure paths report to the status surface — a denied camera permission
 * (`CameraState.Error`) or a rejected connect() must never be console-only.
 *
 * Services are resolved by interface token from the RealityCollective
 * `ServiceManager`; the system never imports a concrete service class.
 */
export class CameraStreamSystem extends createSystem({}) {
  private cameraEntity: Entity | undefined;
  private videoTrack: MediaStreamTrack | undefined;
  private qualifier: QualifierService | undefined;
  private connected = false;
  private cameraFailed = false;
  private startedAt = 0;
  private stallReported = false;
  private readonly stallDeadlineMs = 8000;

  private readonly sampleCanvas = document.createElement('canvas');
  private readonly sampleW = 64;
  private readonly sampleH = 48;

  override init(): void {
    this.sampleCanvas.width = this.sampleW;
    this.sampleCanvas.height = this.sampleH;

    status.set('camera', 'idle — enter XR to start');

    // Gate ALL capture on an ACTIVE XR session: nothing starts in the flat
    // browser view (the 2D page is just the welcome screen), and — helpfully —
    // Quest passthrough getUserMedia tends to require an active session anyway.
    const xr = (this.world as unknown as { renderer?: { xr?: EventTarget } })?.renderer?.xr;
    if (xr?.addEventListener) {
      const onStart = (): void => {
        xr.removeEventListener?.('sessionstart', onStart);
        this.startCapture();
      };
      xr.addEventListener('sessionstart', onStart);
    } else {
      // No XR manager (tests / non-XR host) — start immediately so existing
      // behaviour and unit tests are unchanged.
      this.startCapture();
    }
  }

  /** Begin capture: run diagnostics and create the CameraSource. Idempotent. */
  private startCapture(): void {
    if (this.cameraEntity) return;
    status.set('camera', 'starting…');
    this.startedAt =
      typeof performance !== 'undefined' ? performance.now() : Date.now();
    // Diagnostics also grant permission (via the getUserMedia probe), so the
    // redundant early CameraUtils.getDevices() is intentionally omitted.
    void this.runCameraDiagnostics();

    this.cameraEntity = this.world.createEntity();
    this.cameraEntity.addComponent(CameraSource, {
      facing: AppConfig.camera.facing,
      width: AppConfig.camera.width,
      height: AppConfig.camera.height,
      frameRate: AppConfig.camera.frameRate,
    });
  }

  override update(): void {
    if (!this.cameraEntity || this.cameraFailed) return;

    if (!this.connected) {
      const state = this.cameraEntity.getValue(CameraSource, 'state');
      if (state === CameraState.Error) {
        // Terminal: without this branch a denied permission spins the poll
        // forever with no feedback anywhere.
        this.cameraFailed = true;
        status.set('camera', 'error — camera unavailable (check the browser permission)');
        console.error('[CameraStream] CameraSource entered Error state (permission denied?)');
        return;
      }
      if (state !== CameraState.Active) {
        this.maybeWarnStall(state);
        return;
      }
      const stream = this.cameraEntity.getValue(CameraSource, 'stream') as MediaStream | null;
      if (!stream) return;
      this.beginStreaming(stream);
      return;
    }

    // Edge quality gate — OFF by default (see AppConfig.camera.gateStreamOnQuality).
    // Disabling a track transmits black frames AND blacks the local <video> the
    // qualifier samples, so a single "too dark" verdict deadlocks. The stream is
    // ungated; the qualifier still reports brightness (status 'quality').
    if (AppConfig.camera.gateStreamOnQuality && this.videoTrack && this.qualifier) {
      const desired = this.qualifier.shouldStream;
      if (this.videoTrack.enabled !== desired) this.videoTrack.enabled = desired;
    }

    // Streaming heartbeat (every 5 s): a minimal "data is leaving the client"
    // signal — deliberately terse (the connection/resolution detail lives in
    // the connect line and the server's detection replies).
    const streaming = this.videoTrack?.enabled ?? false;
    uiLog.throttle(
      'tx',
      5000,
      streaming
        ? `↑ streaming camera frames (${AppConfig.camera.width}×${AppConfig.camera.height})`
        : '↑ frames paused — low light (edge quality gate)',
    );
  }

  private beginStreaming(stream: MediaStream): void {
    this.connected = true;
    this.videoTrack = stream.getVideoTracks()[0];
    status.set('camera', 'active');

    const webrtc = getServiceManager().resolve(IWebRTCService);
    webrtc.setVideoStream(stream);
    webrtc.connect().catch((err) => {
      // The library auto-recovers when signaling comes back; surface the
      // failure instead of swallowing it.
      status.set('connection', `failed: ${err instanceof Error ? err.message : String(err)}`);
      console.error('[CameraStream] connect() failed', err);
    });

    // Cache the handle — resolving by token every frame is a wasted lookup.
    this.qualifier = getServiceManager().resolve(IImageQualifierService);
    this.qualifier.setFrameProvider(() => this.grabQualifierFrame());
  }

  /**
   * Report what the browser exposes for capture, so a stuck "starting…" is
   * explainable. IWSDK's camera uses `navigator.mediaDevices.getUserMedia` (a
   * standard webcam) — NOT Meta's passthrough camera API — so on a Quest this
   * commonly finds no usable device. Best-effort; guarded for environments
   * without the APIs (and for tests).
   */
  private async runCameraDiagnostics(): Promise<void> {
    let perm = 'unknown';
    try {
      const permissions = navigator.permissions as
        | { query?: (d: { name: string }) => Promise<{ state: string }> }
        | undefined;
      const result = await permissions?.query?.({ name: 'camera' });
      if (result?.state) perm = result.state;
    } catch {
      /* Permissions API / the 'camera' name is unsupported on some browsers. */
    }

    let cameras = -1;
    let labels = '';
    try {
      const devices = (await navigator.mediaDevices?.enumerateDevices?.()) ?? [];
      const video = devices.filter((d) => d.kind === 'videoinput');
      cameras = video.length;
      labels = video.map((d) => d.label || '(unlabeled)').join(', ');
    } catch (err) {
      console.warn('[CameraStream] enumerateDevices failed', err);
    }

    const summary =
      cameras < 0
        ? `perm=${perm}, devices unavailable`
        : `perm=${perm}, ${cameras} camera(s)${labels ? ` — ${labels}` : ''}`;
    status.set('device', summary);
    uiLog.push(`▣ camera: ${summary}`);
    if (cameras === 0) {
      const hint =
        'no getUserMedia camera on this device — Quest passthrough needs WebXR camera-access, not getUserMedia';
      status.set('device', `${summary} · ${hint}`);
      uiLog.push(`▲ ${hint}`);
      console.warn('[CameraStream] ' + hint);
      return;
    }
    await this.probeCapture();
  }

  /**
   * Decisively test whether a camera stream actually OPENS — enumerate/labels
   * succeed whenever permission was ever granted, but they don't prove
   * `getUserMedia` resolves. Runs first (before IWSDK's own capture) with a
   * timeout, so a hang is reported instead of leaving "starting…" forever. The
   * outcome pinpoints the failure:
   *   - timed out  → the stream never opens (Quest passthrough often needs an
   *                  ACTIVE immersive session — tap Enter AR first)
   *   - NotReadableError / in-use → already opened elsewhere; the hang is later
   *   - OK w/ dims → getUserMedia is fine; the problem is downstream
   */
  private async probeCapture(): Promise<void> {
    const md = navigator.mediaDevices;
    if (!md?.getUserMedia) {
      uiLog.push('▲ getUserMedia unavailable in this browser');
      return;
    }
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<'timeout'>((resolve) => {
      timer = setTimeout(() => resolve('timeout'), 6000);
    });
    try {
      const result = await Promise.race([md.getUserMedia({ video: true }), timeout]);
      if (result === 'timeout') {
        const msg =
          'getUserMedia timed out (6s) — stream will not open; on Quest passthrough usually needs an ACTIVE AR session (tap Enter AR)';
        status.set('device', msg);
        uiLog.push(`▲ ${msg}`);
        console.warn('[CameraStream] ' + msg);
        return;
      }
      const stream = result as MediaStream;
      const track = stream.getVideoTracks()[0];
      const s = track?.getSettings?.() ?? {};
      const info = `getUserMedia OK — ${s.width ?? '?'}×${s.height ?? '?'} facing=${s.facingMode ?? '?'}`;
      status.set('device', info);
      uiLog.push(`▣ ${info}`);
      console.info('[CameraStream] ' + info);
      stream.getTracks().forEach((t) => t.stop());
    } catch (err) {
      const name = err instanceof Error ? err.name : String(err);
      const msg = `getUserMedia failed: ${name}`;
      status.set('device', msg);
      uiLog.push(`▲ ${msg}`);
      console.warn('[CameraStream] ' + msg, err);
    } finally {
      if (timer) clearTimeout(timer);
    }
  }

  /**
   * Watchdog: if the camera never leaves `starting`/`inactive`, say so instead
   * of leaving the user staring at "starting…" forever — usually a pending
   * permission prompt or no capture device (see {@link runCameraDiagnostics}).
   */
  private maybeWarnStall(state: unknown): void {
    if (this.stallReported) return;
    if (state !== CameraState.Starting && state !== CameraState.Inactive) return;
    const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
    if (now - this.startedAt < this.stallDeadlineMs) return;
    this.stallReported = true;
    const msg =
      'camera still starting after 8s — pending permission prompt, or no capture device (see camera diag above)';
    status.set('camera', `starting… — ${msg}`);
    uiLog.push(`▲ ${msg}`);
    console.warn('[CameraStream] ' + msg);
  }

  /** Capture a downsampled RGBA frame for the qualifier modules. */
  private grabQualifierFrame(): QualifierFrame | null {
    if (!this.cameraEntity) return null;
    const source = CameraUtils.captureFrame(this.cameraEntity);
    if (!source) return null;

    const ctx = this.sampleCanvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;
    ctx.drawImage(source, 0, 0, this.sampleW, this.sampleH);
    const image = ctx.getImageData(0, 0, this.sampleW, this.sampleH);
    return { width: image.width, height: image.height, data: image.data };
  }
}
