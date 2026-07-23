# Changelog

All notable changes to `com.ethar.uxtraining` are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.1.0-pre.1] - 2026-07-23

### Added

- `UxSettings` ScriptableObject + `UxSettingsService` (RealityCollective
  Service Framework) — one cached asset for the kit's tuning numbers: world
  label scale, leader-line width, placement-dot diameter, window placement
  mode and follow behaviour. Loaded once at app start (profile asset →
  `Resources/UxSettings` → built-in defaults).
- `WindowFollower` — window placement for UX windows with two modes: `Fixed`
  (anchor in front of the user when shown — previous behaviour) and
  `HeadLocked` (lazy smooth-follow of the user's view). Head-locked windows
  are label-aware: they slide to a stop at a configurable clearance boundary
  around any active world label and resume following when the user looks back.
- `TrainingStepView.Image` — a host-resolved `Texture2D` rendered in the step
  form's image area. The area now renders only when an image is actually
  provided; the shared grey placeholder is gone.

### Changed

- World labels default to the new settings sizes: pill scale 3×, leader line
  2× wider, placement dot 2× larger than 1.0.0-pre.1.
- The step form's placement runs through `WindowFollower` (mode from
  `UxSettings`; `Fixed` matches the previous per-step anchoring).
- The package now depends on `com.realitycollective.service-framework`.

## [1.0.0-pre.1] - 2026-07-12

### Added

- Initial extraction of the training UX kit from the QuestVisionStream Unity
  Quest client into a standalone, headset-agnostic UPM package.
- `TrainingUxController` — world-space step form, palm-up hand menu with live
  step readout, and world location indicator (hotspot marker + leader line +
  billboarded label pill), driven by the presentation-only `TrainingStepView`.
- `XRUiPointer` (formerly the client's `ControllerUiPointer`) — code-configured
  `InputSystemUIInputModule` pointer with laser + reticle UX feedback, built
  purely on Unity Input System generic `<XRController>` bindings with a
  configurable pointing hand; Editor mouse supported through the same module.
- Theme system (`ThemePalette`, `ThemeLibrary`, `ThemeManager`) with three
  built-in palettes (Dark·Cyan, Light·Teal, Hi-Vis·Orange).
- uGUI kit: `UIFactory`, `RoundedSprite`, `HandMenu`, `ButtonHoverGlow`,
  `Pulser`.

### Changed

- Namespaces moved from `XRTraining.*` to `Ethar.UXTraining.*`.
- The training UX no longer references the QuestVisionStream detection/state
  services — hosts map their own scenario model onto `TrainingStepView`.
