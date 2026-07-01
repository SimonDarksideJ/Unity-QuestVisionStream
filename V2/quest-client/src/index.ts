import { World, SessionMode, VisibilityState, createSystem } from '@iwsdk/core';
import {
  startServiceRuntime,
  makeServiceBridgeSystem,
  type CreateSystemLike,
} from '@realitycollective/service-framework-iwsdk';
import { createQuestVisionStreamProfile } from '@questvisionstream/client';
import { resolveSignalingUrl } from './config';
import { setServiceManager } from './runtime';
import { CameraStreamSystem } from './systems/CameraStreamSystem';
import { DetectionRenderSystem } from './systems/DetectionRenderSystem';

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
    .registerSystem(DetectionRenderSystem);

  console.info('[QuestClient] Ready. Streaming to', signalingUrl);
}

bootstrap().catch((err) => console.error('[QuestClient] bootstrap failed', err));
