import { World, SessionMode, VisibilityState, createSystem, launchXR } from '@iwsdk/core';
import {
  startServiceRuntime,
  makeServiceBridgeSystem,
  type CreateSystemLike,
} from '@realitycollective/service-framework-iwsdk';
import {
  createQuestVisionStreamProfile,
  IDetectionService,
  IImageQualifierService,
  ISignalingService,
  IWebRTCService,
} from '@questvisionstream/client';
import { resolveSignalingUrl } from './config';
import { setServiceManager } from './runtime';
import { CameraStreamSystem } from './systems/CameraStreamSystem';
import { DetectionRenderSystem } from './systems/DetectionRenderSystem';
import { StatusSpriteSystem } from './systems/StatusSpriteSystem';
import { bindStatusDom, status, wireStatusServices } from './ui/status';
import { uiLog } from './ui/uiLog';

/** Host portion of a ws(s):// URL for compact display, or the raw value. */
function hostOf(url: string): string {
  try {
    return new URL(url).host || url;
  } catch {
    return url;
  }
}

/** Append a `?cid=` query param (stable per page load) to the signaling URL. */
function withConnectionId(url: string, cid: string): string {
  try {
    const u = new URL(url);
    u.searchParams.set('cid', cid);
    return u.toString();
  } catch {
    return url;
  }
}

/**
 * Wire the flat-page welcome screen. "Enter XR" is enabled only once BOTH
 * immersive AR is available AND the signaling socket is connected (the socket
 * connects on page load; the camera does not — that waits for the session). A
 * failed/dropped socket shows a red hint. Launching needs a click (the user
 * gesture `requestSession` requires); the intro hides while the session runs.
 */
function wireIntro(world: World, signaling: ISignalingService): void {
  const intro = document.getElementById('intro');
  const button = document.getElementById('enter-xr') as HTMLButtonElement | null;
  const note = document.getElementById('intro-note');
  if (!button) return;

  let xrChecked = false;
  let xrSupported = false;
  let lastCloseCode = 0;
  let conn: 'connecting' | 'connected' | 'failed' = signaling.isConnected
    ? 'connected'
    : 'connecting';

  const setNote = (text: string, error = false): void => {
    if (!note) return;
    note.textContent = text;
    note.classList.toggle('error', error);
  };

  const render = (): void => {
    if (!xrChecked) {
      button.disabled = true;
      button.textContent = 'Checking XR…';
      return;
    }
    if (!xrSupported) {
      button.disabled = true;
      button.textContent = 'XR unavailable';
      setNote('Immersive AR isn’t supported here — open this page on a Meta Quest.');
      return;
    }
    button.textContent = conn === 'connecting' ? 'Connecting…' : 'Enter XR';
    button.disabled = conn !== 'connected';
    if (conn === 'connected') setNote('');
    else if (conn === 'connecting') setNote('Connecting to server…');
    else if (lastCloseCode === 4000)
      setNote('Another tab or device is already connected — close it, then reload to take over.', true);
    else setNote('Unable to connect to server, have you connected the VPN and started the server?', true);
  };

  const showIntro = (show: boolean): void => {
    intro?.classList.toggle('hidden', !show);
  };
  const xr = (world as unknown as { renderer?: { xr?: EventTarget } }).renderer?.xr;
  xr?.addEventListener?.('sessionstart', () => showIntro(false));
  xr?.addEventListener?.('sessionend', () => showIntro(true));

  signaling.on('connected', () => {
    conn = 'connected';
    render();
  });
  signaling.on('disconnected', (code) => {
    conn = 'failed';
    lastCloseCode = code;
    render();
  });

  button.addEventListener('click', () => {
    if (button.disabled) return;
    button.disabled = true;
    button.textContent = 'Starting…';
    setNote('');
    try {
      launchXR(world, { sessionMode: SessionMode.ImmersiveAR });
    } catch (err) {
      setNote(`Could not start XR: ${err instanceof Error ? err.message : String(err)}`, true);
      render();
    }
  });

  render();
  const query = navigator.xr?.isSessionSupported?.('immersive-ar');
  if (!query) {
    xrChecked = true;
    xrSupported = false;
    render();
    return;
  }
  void query
    .then((supported) => {
      xrChecked = true;
      xrSupported = supported;
      render();
    })
    .catch(() => {
      xrChecked = true;
      xrSupported = false;
      render();
    });
}

/**
 * Quest WebXR client entry point.
 *
 *  1. Create the IWSDK world (camera + environment raycast).
 *  2. `startServiceRuntime` builds the RealityCollective `ServiceManager` from the
 *     QuestVisionStream service profile, using the IWSDK adapter as the per-frame
 *     source.
 *  3. Register the framework's `ServiceBridgeSystem` (pumps ticks + focus/pause
 *     from the XR session) and the host systems, which resolve services by token.
 */
async function bootstrap(): Promise<void> {
  const signalingUrl = await resolveSignalingUrl();
  const serverHost = hostOf(signalingUrl);
  // Stable per-page connection id: lets the server treat a reconnect of THIS
  // page as a clean replace rather than a second client tripping the cap.
  const connectionId =
    typeof crypto !== 'undefined' && 'randomUUID' in crypto
      ? crypto.randomUUID()
      : Math.random().toString(36).slice(2);
  const signalingUrlForProfile = withConnectionId(signalingUrl, connectionId);
  status.set('server', signalingUrl);
  const statusPanel = document.getElementById('status');
  if (statusPanel) bindStatusDom(status, statusPanel);

  // Subtle bottom-left activity log (flat-browser / pre-AR debugging visibility).
  uiLog.mount();
  uiLog.push(`● server: ${serverHost}`);

  const container = document.getElementById('scene-container') as HTMLDivElement;
  const world = await World.create(container, {
    render: { near: 0.01, far: 100 },
    xr: {
      sessionMode: SessionMode.ImmersiveAR,
      // No auto-offer: the flat page is a welcome screen with our own "Enter XR"
      // button (see wireIntro), so nothing XR/camera-related starts in-browser.
      offer: 'none',
      features: {
        hitTest: { required: false },
        layers: true,
      },
    },
    features: {
      camera: true,
      environmentRaycast: true,
      grabbing: false,
      locomotion: false,
    },
  });

  const { manager, adapter } = startServiceRuntime(world, (frameSource) =>
    createQuestVisionStreamProfile(
      'quest-vision-stream',
      { signalingUrl: signalingUrlForProfile, qualifier: { enabled: true }, logLevel: 'info' },
      frameSource,
    ),
  );
  setServiceManager(manager);

  // Every failure signal (camera, signaling, WebRTC, quality gate) is surfaced
  // on the status panel — never console-only.
  const signaling = manager.resolve(ISignalingService);
  wireStatusServices(status, {
    signaling,
    webrtc: manager.resolve(IWebRTCService),
    detection: manager.resolve(IDetectionService),
    qualifier: manager.resolve(IImageQualifierService),
  });

  // Device-status uplink: forward status fields to the server over the signaling
  // socket, so device-side problems (camera permission, no capture device) are
  // visible in the SERVER console too — even before/without a WebRTC connection.
  let sentStatus: Record<string, string> = {};
  const sendStatus = (field: string, value: string): void => {
    if (!signaling.isConnected || sentStatus[field] === value) return;
    sentStatus[field] = value;
    signaling.send({ type: 'status', field, value });
  };
  const flushStatus = (): void => {
    for (const [field, value] of Object.entries(status.snapshot())) {
      if (value) sendStatus(field, value);
    }
  };
  status.onChange(flushStatus);

  // Diagnostic: on each (re)connect, report the PREVIOUS link's close code +
  // how long it lived — surfaced in the server console as `link = …`. A short
  // lifetime + code 1006 points to the socket being dropped (proxy/network);
  // this is how we tell a real drop from a one-off.
  let connectedAt = 0;
  let prevClose = '';
  // App-level keepalive: regular traffic keeps a proxy (tailscale serve) from
  // treating the socket as idle and closing it. `__`-prefixed status fields are
  // NOT logged by the server (see webrtc_server.py) — they just keep it warm.
  let keepaliveSeq = 0;
  const KEEPALIVE_MS = 15000;

  signaling.on('connected', () => {
    uiLog.push(`● connected to ${serverHost}`);
    connectedAt = performance.now();
    sentStatus = {}; // (re)connect → resend the full current state
    flushStatus();
    if (prevClose) {
      sendStatus('link', prevClose);
      prevClose = '';
    }
  });
  signaling.on('disconnected', (code) => {
    const lifetimeMs = connectedAt ? Math.round(performance.now() - connectedAt) : 0;
    connectedAt = 0;
    const info = signaling.lastCloseInfo;
    const clean = info ? (info.wasClean ? 'clean' : 'abrupt') : '?';
    const reason = info?.reason ? ` "${info.reason}"` : '';
    // Reported to the SERVER console on the next connect (as `link = …`), so we
    // can see who closed it: abrupt+1006 = transport drop; clean = deliberate.
    prevClose = `code ${code} (${clean}${reason}) after ${lifetimeMs}ms`;
    uiLog.push(`● disconnected from ${serverHost} (${prevClose}) — retrying`);
  });

  setInterval(() => {
    if (signaling.isConnected) {
      signaling.send({ type: 'status', field: '__keepalive', value: String(++keepaliveSeq) });
    }
  }, KEEPALIVE_MS);

  // The -iwsdk shim uses minimal structural contracts (it never imports
  // @iwsdk/core), so we bridge the concrete IWSDK types here: `createSystem` and
  // the returned bridge class are cast at this documented interop seam.
  const bridgeSystem = makeServiceBridgeSystem({
    adapter,
    manager,
    world,
    createSystem: createSystem as unknown as CreateSystemLike,
    visibleState: VisibilityState.Visible,
  });

  world
    .registerSystem(bridgeSystem as never)
    .registerSystem(CameraStreamSystem)
    .registerSystem(DetectionRenderSystem)
    .registerSystem(StatusSpriteSystem); // in-AR headline HUD (head-locked)

  // Flat-page welcome screen + our own "Enter XR" button (no auto-offer).
  // Enabled only once the signaling socket connects (camera still waits for XR).
  wireIntro(world, signaling);

  console.info('[QuestClient] Ready. Streaming to', signalingUrl);
}

bootstrap().catch((err) => console.error('[QuestClient] bootstrap failed', err));
