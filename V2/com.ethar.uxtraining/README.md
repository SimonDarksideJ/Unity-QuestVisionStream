# Ethar UX Training (`com.ethar.uxtraining`)

A headset-agnostic XR training UX kit. Everything is built at runtime from code
(no prefabs, no imported art) on top of uGUI, themed through a swappable
palette, and driven entirely through the **Unity Input System** with generic
`<XRController>` bindings — so the same package runs on Meta Quest,
Magic Leap 2 or any other OpenXR headset without changes, and stays fully
mouse-testable in the Editor.

## What's in the box

| Area | Types | Purpose |
| --- | --- | --- |
| `Ethar.UXTraining` | `TrainingUxController`, `TrainingStepView` | The training UX: a world-space step form (eyebrow, progress ticks, title, description, an image area shown only when the host resolves a step image, action buttons), a palm-up hand menu with a live step readout, and a world **location indicator** (pulsing hotspot marker + leader line + billboarded label pill). |
| `Ethar.UXTraining.Interaction` | `XRUiPointer` | Pointer input + UX feedback for world-space canvases: an `InputSystemUIInputModule` configured in code, a laser clamped to the UI hit, and a surface-flattened reticle. Editor mouse works through the same module. |
| `Ethar.UXTraining.Theme` | `ThemePalette`, `ThemeLibrary`, `ThemeManager` | ~12 core colour tokens per palette, everything else derived. Three built-ins: Dark·Cyan, Light·Teal, Hi-Vis·Orange. Author your own via *Create ▸ Ethar ▸ UX Training ▸ Theme Palette*. |
| `Ethar.UXTraining.UI` | `UIFactory`, `RoundedSprite` | Terse, themed uGUI construction helpers and runtime-baked 9-sliced rounded sprites. |
| `Ethar.UXTraining.Components` | `HandMenu`, `ButtonHoverGlow`, `Pulser`, `WindowFollower` | The vertical hand-menu strip, pointer-hover glow feedback, dot/ring pulse animations, and window placement: `Fixed` (anchor in front of the user when shown) or `HeadLocked` (lazy smooth-follow that slides to a stop at a clearance boundary around active world labels and resumes when the user looks back). |
| `Ethar.UXTraining.Settings` | `UxSettings`, `UxSettingsService` (+ `IUxSettingsService`, profile) | The kit's tuning asset — world label scale / leader-line width / placement-dot size, window placement mode and follow behaviour — cached at app start by a [RealityCollective Service Framework](https://github.com/realitycollective/com.realitycollective.service-framework) service (profile asset → `Resources/UxSettings` → built-in defaults). Author via *Create ▸ Ethar ▸ UX Training ▸ UX Settings*. |

## Quick start

```csharp
using Ethar.UXTraining;
using Ethar.UXTraining.Components;
using Ethar.UXTraining.Interaction;
using Ethar.UXTraining.Theme;

// 1. One-time pointer setup (safe to call from every UI owner).
XRUiPointer.EnsureSetup();

// 2. Create the training UX and wire your callbacks.
var host = new GameObject("TrainingUx");
var ux = host.AddComponent<TrainingUxController>();
ux.Initialize(
    ThemeLibrary.DarkCyan(),
    formDistance: 1.25f,
    optionPressed: index => Debug.Log($"Option {index} pressed"),
    menuActions: new HandMenu.Actions
    {
        home = () => { /* restart */ },
        tasks = () => ux.RepresentForm(),
        hint = () => ux.PulseWorldLabel(),
        redo = () => { /* restart */ },
        exit = () => { /* leave */ }
    },
    brandText: "MY TRAINING");

// 3. Feed it steps from YOUR detection / state source.
ux.ShowStep(new TrainingStepView(
    stepIndex: 0, stepCount: 5,
    title: "Look for a Monitor",
    description: "Search your space and locate the monitor",
    options: new[] { "Search" },
    imageRef: "camera"));

// 4. Hotspots and labels: place / refresh / pulse a world label.
ux.ShowWorldLabel(worldPoint, "Monitor");
```

The package deliberately has **no dependency on any detection pipeline** —
`TrainingStepView` is the seam. Map your own step/scenario model onto it and
feed world points for the location indicator from wherever your detections
come from.

## Input model (why it ports to other headsets)

All input goes through Unity Input System actions bound to the generic
XR layouts, never a vendor SDK:

- **Pointing** — the OpenXR aim pose: `<XRController>{RightHand}/pointerPosition`
  / `pointerRotation`, with `triggerPressed`/`triggerButton` as select.
- **Hand menu** — `<XRController>{LeftHand}/devicePosition` / `deviceRotation`
  drive the palm-up show/hide gate and lazy follow.
- **Editor** — `<Mouse>/position` + `<Mouse>/leftButton` through the same
  `InputSystemUIInputModule`; the hand menu rests at the lower-left of view
  when no controller is tracked.

Swap the pointing hand with `XRUiPointer.Hand = XRUiPointer.PointerHand.Left;`
(the hand menu automatically follows the opposite hand). On any headset whose
runtime exposes the standard OpenXR controller profiles (Quest, Magic Leap 2,
Pico, HTC, …) these bindings resolve without modification.

## Requirements

- Unity 6000.0+
- `com.unity.inputsystem` (Active Input Handling set to Input System or Both)
- `com.unity.ugui`
- `com.realitycollective.service-framework` (for the `UxSettingsService`; via the OpenUPM scoped registry)
