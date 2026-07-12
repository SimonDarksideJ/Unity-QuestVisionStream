# Changelog

## [1.0.0-pre.1]

- Initial extraction from the QuestVisionStream V2 Unity client
  (`com.questvisionstream.unity` + `Unity-Quest-Client`) into a standalone UPM
  package:
  - `IDetectionRendererService` / `IDetectionRenderModule` contract and
    `DetectionRendererService` (from `com.questvisionstream.unity`).
  - `EphemeralBoxRenderModule` — pose-frozen outline boxes, the default mode
    (from `com.questvisionstream.unity`).
  - `AnchoredTagRenderModule` — persistent depth-anchored tags (from the
    Quest client app).
  - `EnvironmentDepthProvider` — Meta Depth API sampling for anchored
    placement (from the Quest client app).
  - `UnlitMaterialFactory` — URP + single-pass-instanced XR safe unlit
    materials (from `com.questvisionstream.unity` Core).
  - Editor tooling: `DetectionMaterialGenerator` and the
    `DrawingTestHarness`/`DrawingTestMenu` drawing test scene (from the Quest
    client app). Menus now live under `Tools ▸ Ethar ▸ Debug Drawing BBox`.
- All types moved to the `Ethar.DebugDrawingBBox` namespace
  (assemblies `Ethar.DebugDrawingBBox` / `Ethar.DebugDrawingBBox.Editor`).
