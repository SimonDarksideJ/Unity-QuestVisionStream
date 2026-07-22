# Training Flow — State Service + Presentation Service

> 📖 The canonical scenario **wire format and capability reference** (aligned
> against both the C# package and its standalone Python export) is
> [Training-Configuration-Reference.md](Training-Configuration-Reference.md).
> This document covers the Unity client's end-to-end flow and UX.

The authoritative training flow for the Quest client: a **Training State
Service** that manages a queue of expected detection classes, and a **Training
Presentation Service** that turns each activated step into UX — a display-menu
step form, a hand menu with the current step, and a location indicator (label +
connector) at the detected box centre.

```
                         ┌────────────────────────────────────────────┐
detections data channel  │            DetectionService                │
{"type":"detections",…} ─▶  parse → validate → normalize (RenderBatch) │
                         └───────────────┬────────────────────────────┘
                                         │ DetectionsReceived (always firing)
                                         ▼
                         ┌────────────────────────────────────────────┐
                         │          TrainingStateService              │
                         │  cached ExpectedClass — one string compare │
                         │  per detection; everything else DISCARDED  │
                         │  match → move next → cache next expected   │
                         └───────┬───────────────────────▲────────────┘
                     StepActivated│                        │ CompleteStep(TrainingStepResult)
              CurrentClassSighted │                        │   result class re-enters as a
                ScenarioCompleted ▼                        │   synthetic detections payload
                         ┌────────────────────────────────┴───────────┐
                         │       TrainingPresentationService          │
                         │  step form (display menu) · hand menu step │
                         │  readout · world label + connector at the  │
                         │  detected box centre (capture-pose ray)    │
                         └────────────────────────────────────────────┘
```

## The state queue

A scenario is an ordered queue of steps, authored as a **`TrainingScenarioAsset`
ScriptableObject** (Create → QuestVisionStream → Training Scenario) and edited
in its custom inspector; JSON (`TrainingScenarioParser`) remains the wire
format for pushing scenarios at runtime, and the inspector imports/exports it.
Each step:

| Field | Meaning |
|-------|---------|
| `waitingClass` | The class whose arrival **activates** this step (empty on the entry step — activated by `Begin()`). |
| `title` / `description` | Form copy. A step with no title and no options is a **pass-through**: no UX, the flow immediately waits on its `result`. |
| `options` | Action labels (default one). Pressing **any** option emits the step's `result` class. |
| `detectedClass` | Class to annotate in the world while the step is active. |
| `label` | Text for the location indicator placed at that class's box centre. |
| `imageRef` | Client-side image reference (currently the shared camera-on-grey placeholder). |
| `modelRef` | Model catalog key: while this step is active, the host spawns the mapped prefab **aligned to the AprilTag** whose registry class name matches `detectedClass` (falling back to `waitingClass`). Empty = no model. |
| `result` | **The next expected class.** Arrives either as a real detection or as the synthetic "detected class from pressing an action". Empty = final step; its action completes the scenario. |

Because each step's `result` is the next step's `waitingClass`, the state
service never scans the table at runtime: on every transition it advances the
index and **statically caches the next expected class** — the per-detection hot
path is a single case-insensitive string comparison, and everything that
doesn't match is discarded (counted in `DiscardedCount`).

### Both paths are the same path

Training form responses deliberately follow the same route as detections. When
an action is pressed, the presentation service feeds a `TrainingStepResult`
back to the state service, which wraps the result class in a **wire-shaped
detections payload** (`TrainingResponseMessage`, `frame = -1`, conf 1.0) and
pushes it through `DetectionChannelParser` → `DetectionMath.ToRenderBatch` →
the same batch handler a server payload uses. An action press *is* a detected
class.

## The demo scenario (Training_Scenario.xlsx)

Shipped as `Assets/QuestVisionStream/Resources/EtharTrainingScenario.asset` (and as
the built-in fallback `TrainingScenarioLibrary.EtharDemo()`):

| # | Waits for | Form | Action → result | World label |
|---|-----------|------|-----------------|-------------|
| 1 | — (Begin) | Welcome — "Ready to begin your Ethar training?" | Begin → `begintraining` | |
| 2 | `begintraining` | Look for a Monitor | Search → `tv` | |
| 3 | `tv` *(detected)* | Found TV | Next → `foundtv` | "This is a tv" |
| 4 | `foundtv` | *(pass-through — no UX, move next)* | → `person` | |
| 5 | `person` *(detected)* | Found Person | Finish → `finishtraining` | "This is a person" |
| 6 | `finishtraining` | Complete | End → *(scenario complete)* | |

Note step 2: its result `tv` can only arrive from the detector — pressing
"Search" is not what advances it; the button is a prompt. (Every option emits
the step's result class, so on this step the Search press *does* emit `tv` —
acting as a manual override if the detector can see the monitor but the user
wants to move on. If a step must be detection-only, give it no options.)

## The services

### `ITrainingStateService` (package — `Runtime/Services/Training/`)

- Wraps the pure, EditMode-tested `TrainingStateMachine` from the
  engine-agnostic **`com.ethar.trainingstatemachine`** package (mirrored 1:1 by
  `com.ethar.trainingstatemachine.python`).
- Subscribes to `IDetectionService.DetectionsReceived` — the detection service
  is always sending; the queue filter makes the flow authoritative.
- Events: `ScenarioLoaded`, `StepActivated` (with the triggering
  `TrainingDetectionMatch` when a detection caused the transition),
  `CurrentClassSighted` (re-sightings of the active step's `detectedClass`, so
  the world label tracks the object), `ScenarioCompleted`.
- `CompleteStep(TrainingStepResult)` — the custom response class fed back by
  the presentation layer; routes the result class down the detections path.
- Profile: the `TrainingScenarioAsset` to run, minimum detection confidence
  (default 0.5), verbose logging (`[QVS:Training]` for logcat filtering).

### `ITrainingPresentationService` (interface in package, implementation in the client app)

Receives UX requests from the state flow and owns the training UI. The glue
service (`TrainingPresentationService` in
`Unity-Quest-Client/Assets/QuestVisionStream/Scripts/Training/`) maps each
step activation onto a presentation-only `TrainingStepView` and drives the
reusable **`com.ethar.uxtraining`** package (`V2/com.ethar.uxtraining/` —
headset-agnostic, Unity Input System only), which owns `TrainingUxController`
and the whole uGUI kit:

- **Display menu** — a world-space step form built from the Ethar UX Training
  uGUI kit: eyebrow step counter, progress ticks, title,
  description, the shared camera/grey-backdrop image placeholder, and the
  step's action buttons. Re-anchored ~1.25 m in front of the user per step.
- **Hand menu** — the design's vertical 1d strip with a live current-step
  readout, lazily following the left controller and gated on the **palm-up
  pose**: it fades in when the palm rolls toward the face (controller up-axis
  toward the head, with show/hide hysteresis so it doesn't flicker) and fades
  out — raycasts disabled — when the palm rolls away. In the Editor's
  untracked fallback it stays visible at the lower left for mouse testing.
  HOME/REDO restart the scenario, TASKS re-anchors the form, HINT pulses the
  location indicator, EXIT resets to idle.
- **Location indicator** — when a step carries `detectedClass` + `label`, a
  pulsing marker is placed at the detection's box centre by unprojecting the
  normalized centre through the **capture-time pose snapshot** (same
  pose-freeze primitive as the box renderer, same fixed distance so label and
  box coincide), with a leader-line connector up to a billboarded label pill.
  Each indicator is linked to its step's dialog: when the dialog progresses,
  the next `StepActivated` re-binds the indicator to the new step's label or
  hides it (pass-through steps, completion, restart, exit), and the state
  machine clears its cached detected class on every advance so stale
  re-sightings can never resurrect an old indicator.
- **Interaction** — controller-only for now (hand tracking comes later). One
  `XRUiPointer` (package `com.ethar.uxtraining`, formerly the client's
  `ControllerUiPointer`) owns the EventSystem: an `InputSystemUIInputModule`
  configured in code with the pointing controller's OpenXR **aim pose** as a
  tracked pointer, **trigger** as select, and a laser beam so the user can see
  what they're aiming at (no interaction-toolkit or vendor-SDK dependency —
  generic Input System `<XRController>` bindings, so the same pointer runs on
  Quest, Magic Leap 2 or any OpenXR headset; the Editor mouse works through
  the same module). Every interactive world-space canvas — the warm-up Enter
  card, the step form, the hand menu — registers a `TrackedDeviceRaycaster`
  through `XRUiPointer.RegisterCanvas`.
  Selection is pointer-only: there are no hardware-button shortcuts. The beam
  is clamped to the UI raycast hit (it stops on what a click would land on)
  with a circular reticle laid flat on the surface, and every button carries
  `ButtonHoverGlow` — a slight expand plus accent glow while the ray is on it.

## AprilTags in the training flow — offline detections and tag-aligned models

Printed AprilTags are unified under the same **ClassName architecture** as
server detections, with a strict delineation:

1. **Tag detects** — the on-device tag pipeline is unchanged
   (`ITagDetectionService` → `ITagRoutingService`, priorities 40/41).
2. **Bridge translates** — `ITagDetectionBridgeService` (43) republishes each
   sighting through `IDetectionService.PublishLocal` as a wire-shaped
   detections payload: `label` = the tag's registry **Class Name**
   (`TagDefinition.ClassName`, falling back to its display name), `conf` = 1.0,
   box projected around the tag's viewport position (`frame = -2` marks tag
   payloads; action responses use `-1`). One parser, one batch handler — a tag
   sighting *is* a detected class, exactly like an action press.
3. **Engine decides** — the state machine sees the class arrive with source
   `AprilTag` and advances `waitingClass` steps / re-sights `detectedClass`
   like any other arrival. Give a registry entry `ClassName = "tv"` and the
   demo's "Look for a Monitor" step advances from the printed tag with **no
   server and no ML model** — augmenting YOLO when connected, replacing it
   offline (`bridgeTagsToDetections` on the bootstrap).
4. **Placement instantiates** — when an activated step carries `modelRef`,
   `ITrainingModelPlacementService` (47) resolves it through the host's model
   catalog (Bootstrap ▸ Training Models) and instantiates the prefab aligned
   to the tag whose class name matches the step's `detectedClass` (or
   `waitingClass`) — immediately if the tag is tracked, else the moment it
   enters view.

The **connecting component** is `TagPoseFollower`: bound to one tag id, it
snaps to the first observed pose and then smooths toward every fresh sighting
(SmoothDamp position + slerped rotation), freezing at the last pose when the
tag leaves view (TTL exit) and self-healing when it re-enters — including
after a recenter, since re-entry carries the corrected world pose. Chosen over
parenting to the debug tag markers (ties model lifetime to a disableable
visual) and per-sighting re-instantiation (allocation churn, no smoothing).

## Launch pattern

The whole connection warms up **behind the intro card**, so entering is
instant:

1. **App launch** — signaling socket connects, passthrough camera activates,
   and the WebRTC session negotiates immediately (peer connection, `detections`
   data channel, ICE). No camera frames leave the device yet — consent gates
   the frames, not the plumbing.
2. **Warm-up card** — button tiers: `Checking camera…` → `Connecting…`
   (signaling) → `Negotiating…` (session warming) → **`Enter`**, which only
   lights up when the peer connection reports Connected. By the time the user
   can press it, the pipeline is READY.
3. **Enter** (laser click) — the frame pump starts (first pixels leave
   the device now), the card hides, and the training scenario auto-begins:
   the Welcome form appears immediately. Auto-begin requires both gates —
   user entered **and** session connected — with the server `ready` handshake
   as an extra trigger for unusual orderings.
4. **Begin** on the Welcome form — detections are already flowing, so step 2
   ("Look for a Monitor") is live detection from the first second.

Both services are registered code-first in `QuestVisionStreamBootstrap`
(priorities 45/46) behind the `enableTraining` toggle, alongside a
`trainingScenario` asset override and a theme index (Dark·Cyan / Light·Teal /
Hi-Vis·Orange — the swappable Ethar UX Training palettes).

## Authoring a new scenario

**Visual route (recommended):** draw the flow as a mermaid diagram and
convert it with the **training builder**
([Training-Builder.md](Training-Builder.md)) — `builder.py md2json flow.md -o
scenario.json` (or start from a spreadsheet with `csv2md`/`csv2json`), review
the validation report, then **Import JSON…** on the asset below.

**In-inspector route:**

1. **Create → QuestVisionStream → Training Scenario** and edit the queue in the
   inspector — keep the chain rule: *each step's `result` is a later step's
   `waitingClass`*, and the final step's `result` empty. The inspector
   validates the chain live via the shared `TrainingScenarioValidator`
   (unreachable steps, dead-end results, early completion) and shows the
   expected-class queue — the same report the builder CLI prints.
2. Use detector class names (`tv`, `person`, …) — or AprilTag registry class
   names — for steps advanced by vision; use any unique token
   (`begintraining`, `foundtv`, …) for steps advanced by an action press.
   Give a step a `modelRef` to spawn its catalog model aligned to the step's
   tag.
3. Assign the asset to **Bootstrap ▸ Training Scenario** (or replace
   `Assets/QuestVisionStream/Resources/EtharTrainingScenario.asset`).

The inspector's **Import JSON… / Export JSON…** buttons round-trip the wire
format (`TrainingScenarioParser`) — JSON is the interchange with the builder
(`json2md` turns an exported asset back into a reviewable diagram); an asset
with no steps is rejected at load and the built-in demo is the last-resort
fallback.

## Tests

- `com.ethar.trainingstatemachine/Tests/Editor/` — the core suite
  (`TrainingScenarioParserTests`, `TrainingStateMachineTests`,
  `TrainingProcessingTests`, `TrainingValidatorTests`): parsing/round-trip,
  the full demo flow, discard behaviour, case-insensitivity, reset/restart,
  and the chain validator. Runs in the Unity Test Runner **or** headless via
  `dotnet test Tests~` (no Unity required).
- `com.questvisionstream.unity/Tests/Editor/` — the wire-protocol bridges
  (`TrainingResponseMessageTests`, `TagDetectionMessageTests`) and the
  authoring asset (`TrainingScenarioAssetTests`), including the synthetic
  response-as-detection path through the real channel parser.
- `com.ethar.trainingstatemachine.python/tests/` — the Python export's
  mirror suite plus the builder tests (`python -m unittest discover`).
