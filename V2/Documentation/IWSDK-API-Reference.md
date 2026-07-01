# IWSDK (`@iwsdk/core`) API Reference — v0.4.2

Source-verified snapshot (2026-07-01) used to build this client. Verbatim from
the official docs (`iwsdk.dev/guides`, `/concepts/ecs`), the published API
reference, and real `examples/*/src/index.ts` in `facebook/immersive-web-sdk`.
Items that could not be fully verified are flagged at the end.

## World.create

```ts
static async create(container: HTMLDivElement, options?: WorldOptions): Promise<World>
```

Returns a `Promise<World>`. Two distinct feature blocks:
- `xr.features` — raw WebXR session features: `hitTest`, `handTracking`,
  `anchors`, `planeDetection`, `meshDetection`, `layers`.
- top-level `features` — IWSDK subsystems: `grabbing`, `locomotion`, `physics`,
  `camera`, `sceneUnderstanding`, `environmentRaycast`, `spatialUI`.

```ts
World.create(container, {
  assets,
  render: { near: 0.001, far: 300 },
  xr: {
    sessionMode: SessionMode.ImmersiveAR,
    offer: 'always',
    features: { hitTest: { required: true }, layers: true },
  },
  features: { camera: true, environmentRaycast: true },
}).then((world) => { /* register systems/components, create entities */ });
```

## Systems

`createSystem(queries, configSchema?)` returns a base class you extend.

```ts
export class MySystem extends createSystem(
  { targets: { required: [CompA], excluded: [CompB], where: [eq(CompA, 'f', 1)] } },
  { speed: { type: Types.Float32, default: 5 } },
) {
  init(): void {}
  update(delta: number, time: number): void {
    for (const e of this.queries.targets.entities) {
      e.getValue(CompA, 'f'); e.setValue(CompA, 'f', 2);
    }
  }
}
// registration (on the world instance, not World.create):
world.registerSystem(MySystem, { priority: 0, configData: { speed: 2 } });
```

System members: `this.world`, `this.scene` (THREE.Scene), `this.input.gamepads.right`,
`this.config` (fields are **signals** — `.peek()`, `.subscribe()`), `this.createEntity()`,
`this.queries.<name>.entities`, `this.queries.<name>.subscribe('qualify'|'disqualify', fn)`.
`update` is the entrypoint (not `execute`). `where` ops: `eq ne lt le gt ge isin nin`.

## Components

`createComponent(name, schema, description?)`. `Types`: `Float32 Float64 Int8 Int16
Int32 Uint32 Boolean String Vec3 Vec4 Color Entity Enum Object`.

```ts
export const Health = createComponent('Health', {
  current: { type: Types.Float32, default: 100 },
  mode: { type: Types.Enum, enum: { Idle: 'idle' }, default: 'idle' },
});
entity.addComponent(Health, { current: 50 });
entity.getValue(Health, 'current'); entity.setValue(Health, 'current', 60);
entity.getVectorView(Comp, 'vec3Field'); // zero-copy TypedArray
world.registerComponent(Health);
```

## Entities & transforms

```ts
world.createEntity();                       // raw ECS entity
world.createTransformEntity(object3D?, { parent?, persistent? }); // + Transform + Object3D
entity.object3D!.position.set(0, 1, -2);
entity.getValue(Transform, 'position');     // auto-synced [x,y,z]
world.camera.position.set(0, 1, 0.5);       // THREE camera
```

## Camera (`features: { camera: true }`)

```ts
import { CameraSource, CameraUtils, CameraState, CameraFacing } from '@iwsdk/core';
const cam = world.createEntity();
cam.addComponent(CameraSource, { facing: 'back', width: 1280, height: 960, frameRate: 30 });
const stream = cam.getValue(CameraSource, 'stream');        // MediaStream
const video  = cam.getValue(CameraSource, 'videoElement');  // HTMLVideoElement
const tex    = cam.getValue(CameraSource, 'texture');       // THREE.VideoTexture
const state  = cam.getValue(CameraSource, 'state');         // CameraState.Active ...
await CameraUtils.getDevices();          // request permission early
CameraUtils.captureFrame(cam);           // HTMLCanvasElement | null
```
`CameraState: Inactive|Starting|Active|Error`. `CameraFacing: Back|Front|Unknown`.

## Environment raycast (world placement)

Enable `xr.features.hitTest:{required:true}` + `features:{environmentRaycast:true}`.

```ts
import { EnvironmentRaycastTarget, RaycastSpace } from '@iwsdk/core';
target.addComponent(EnvironmentRaycastTarget, { space: RaycastSpace.Viewer, maxDistance: 10 });
const hit = target.getValue(EnvironmentRaycastTarget, 'xrHitTestResult'); // XRHitTestResult | undefined
```
`RaycastSpace: Left|Right|Viewer|Screen`. The system auto-positions/orients the
target entity to the hit surface. (Controller/viewer-oriented; for arbitrary
per-detection viewport points we unproject through `world.camera` — see
`DetectionRenderSystem`.) Scene understanding: `XRPlane`, `XRMesh`
(`semanticLabel`, `dimensions`), `XRAnchor` via `SceneUnderstandingSystem`.

## ⚠️ Not fully verified
Exhaustive `WorldOptions` type (API page 404); `camera` object sub-config (docs
only show `camera: true`); no `systems` option on `World.create` (register on the
instance); `CameraUtils` exact signatures from docs not source; `SessionMode`
beyond `ImmersiveVR`/`ImmersiveAR`. Validate against `npm create @iwsdk@latest`
output when the toolchain is available.
