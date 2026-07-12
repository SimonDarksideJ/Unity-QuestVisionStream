# Ethar Debug Drawing (Bounding Boxes)

World-space debug drawing for vision detection events. Renders the bounding
boxes received from a detection stream as in-headset visuals — a hollow outline
box, a centre-marker probe and a billboarded `label 82%` text — each placed by
unprojecting the box corners through the **capture-time** camera pose
(`CameraPoseSnapshot`, the pose-freeze primitive), so boxes land on the pixels
they describe even after the capture → inference → return round trip.

Extracted from the QuestVisionStream V2 Unity client.

## Render modes

Exactly one render module is active at a time; `DetectionRendererService`
switches between them at runtime (the outgoing module fully cleans up its scene
objects and GPU resources).

| Module | Behaviour |
| ------ | --------- |
| **Ephemeral Boxes** *(default)* | WebXR-client parity: each payload clears the previous boxes and redraws "what's seen right now" at a fixed distance along the capture-pose ray. Visuals are pooled — no per-frame allocation. |
| **Anchored Tags** | One persistent world-placed box + centre marker + label per class. Distance comes from real geometry: Meta environment depth first (`EnvironmentDepthProvider`, no room scan needed), then a scene-mesh `Physics.Raycast`, then a fixed fallback. Runtime-switchable between persistent world-pins and update-in-place tracking. |

## Contents

- `IDetectionRendererService` / `IDetectionRenderModule` — the render-module
  contract (activation, `RenderDetections(DetectionArrival, CameraPoseSnapshot?)`,
  clear-on-recenter).
- `DetectionRendererService` — routes detection batches to the single active
  module with the capture-time pose applied, and clears everything when the XR
  tracking origin recenters.
- `EphemeralBoxRenderModule` / `AnchoredTagRenderModule` — the two modes above.
- `EnvironmentDepthProvider` — raw per-frame metric depth from Meta's Depth API
  (AR Foundation occlusion), used by the anchored module.
- `UnlitMaterialFactory` — builds unlit materials that actually render under
  URP + single-pass-instanced XR (ships URP/Unlit shader variants via a
  material asset in `Resources/QuestVisionStream/DetectionUnlit`).
- Editor tooling:
  - `Tools ▸ Ethar ▸ Debug Drawing BBox ▸ Regenerate Detection Material` —
    generates the Resources material asset that forces the URP/Unlit stereo
    variants into device builds (also runs automatically on editor load).
  - `Tools ▸ Ethar ▸ Debug Drawing BBox ▸ Open Drawing Test Scene` — a
    self-contained drawing test board (`DrawingTestHarness`) that reproduces
    shader/rendering faults in the editor Game view, no device build needed.

## Usage

Register the service and modules with the RealityCollective Service Framework
(code-first shown; profile assets work too — Create ▸ Ethar ▸ Debug Drawing BBox):

```csharp
var rendererProfile = ScriptableObject.CreateInstance<DetectionRendererServiceProfile>();
rendererProfile.InitialActiveModule = "Ephemeral Boxes";
serviceManager.TryCreateAndRegisterService<IDetectionRendererService>(
    typeof(DetectionRendererService), out var rendererService, "Detection Renderer", 35u, rendererProfile);

serviceManager.TryCreateAndRegisterService<IEphemeralBoxRenderModule>(
    typeof(EphemeralBoxRenderModule), out _, "Ephemeral Boxes", 0u,
    ScriptableObject.CreateInstance<EphemeralBoxRenderModuleProfile>(), rendererService);

serviceManager.TryCreateAndRegisterService<IAnchoredTagRenderModule>(
    typeof(AnchoredTagRenderModule), out _, "Anchored Tags", 1u,
    ScriptableObject.CreateInstance<AnchoredTagRenderModuleProfile>(), rendererService);
```

Switch modes at runtime with
`rendererService.SetActiveModule("Anchored Tags")`.

## Dependencies

- `com.questvisionstream.unity` — detection/pose data types
  (`DetectionArrival`, `RenderableDetection`, `CameraPoseSnapshot`) and the
  `IDetectionService` / `IPoseTrackingService` seams.
- `com.realitycollective.service-framework` — service/module registration.
- `com.unity.xr.arfoundation` — environment depth (anchored mode).
- `com.unity.ugui` — TextMeshPro for the drawing test harness.
