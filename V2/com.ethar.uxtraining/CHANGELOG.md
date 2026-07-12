# Changelog

All notable changes to `com.ethar.uxtraining` are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
