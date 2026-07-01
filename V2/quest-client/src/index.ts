import { World, SessionMode } from '@iwsdk/core';
import { QuestVisionStreamClient } from '@questvisionstream/client';
import { ServiceManager } from '@realitycollective/service-framework-ts';
import { resolveSignalingUrl } from './config';
import { ServicePumpSystem } from './systems/ServicePumpSystem';
import { CameraStreamSystem } from './systems/CameraStreamSystem';
import { DetectionRenderSystem } from './systems/DetectionRenderSystem';

/**
 * Quest WebXR client entry point.
 *
 * Two-phase bootstrap:
 *  1. Construct and start the {@link QuestVisionStreamClient}, which builds the
 *     streaming service graph (signaling → qualifier → webrtc → detection) and
 *     registers it on the shared `ServiceManager` under interface tokens.
 *  2. Create the IWSDK world (camera + environment raycast features) and register
 *     the host systems. Systems resolve services by token from the manager —
 *     they never import concrete service classes.
 */
async function bootstrap(): Promise<void> {
  // Resolve which streaming server to use (query param → Cloudflare Pages env var
  // via /api/config → build-time env → localhost). See src/config.ts.
  const signalingUrl = await resolveSignalingUrl();

  const qvs = new QuestVisionStreamClient({
    signalingUrl,
    logLevel: 'info',
  });
  await qvs.start();

  // Forward app focus/pause into the service lifecycle.
  document.addEventListener('visibilitychange', () => {
    ServiceManager.instance.onApplicationPause(document.hidden);
    ServiceManager.instance.onApplicationFocus(!document.hidden);
  });

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

  world
    // Pump services first each frame (priority < 0), then host systems.
    .registerSystem(ServicePumpSystem, { priority: -100 })
    .registerSystem(CameraStreamSystem)
    .registerSystem(DetectionRenderSystem);

  console.info('[QuestClient] Ready. Streaming to', signalingUrl);
}

bootstrap().catch((err) => console.error('[QuestClient] bootstrap failed', err));
