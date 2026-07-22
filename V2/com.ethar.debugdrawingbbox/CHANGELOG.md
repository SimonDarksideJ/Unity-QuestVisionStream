# Changelog

## [Unreleased]

### Fixed

- Missing `Unity.XR.CoreUtils` asmdef reference (+ explicit
  `com.unity.xr.core-utils` package dependency): `EnvironmentDepthProvider`'s
  occlusion-frame accessors (`TryGetPoses`/`TryGetFovs`/`TryGetNearFarPlanes`)
  return `ReadOnlyList<>` from CoreUtils, which compiled only while the type
  happened to resolve transitively — a clean recompile failed with CS0012 and
  cascading errors.

### Added

- `IDetectionRendererService.RenderingEnabled` — master visibility switch for
  the debug drawing: when false, incoming batches are ignored and the active
  module's visuals are cleared, while the module selection is kept so
  re-enabling resumes on the next batch. Hosts bind this to their debug
  toggle (the Quest client binds it — with the HUD window and connection dot
  — to the left controller MENU button, off by default).

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
