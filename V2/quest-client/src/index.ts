import { World, SessionMode, VisibilityState, createSystem } from '@iwsdk/core';
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
      offer: 'always',
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
      { signalingUrl, qualifier: { enabled: true }, logLevel: 'info' },
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

  // Connection state → activity log, naming the server it's talking to.
  signaling.on('connected', () => {
    uiLog.push(`● connected to ${serverHost}`);
    sentStatus = {}; // (re)connect → resend the full current state
    flushStatus();
  });
  signaling.on('disconnected', (code) =>
    uiLog.push(`● disconnected from ${serverHost} (code ${code}) — retrying`),
  );

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

  console.info('[QuestClient] Ready. Streaming to', signalingUrl);
}

bootstrap().catch((err) => console.error('[QuestClient] bootstrap failed', err));
