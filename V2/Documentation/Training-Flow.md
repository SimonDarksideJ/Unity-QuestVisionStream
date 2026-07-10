# Training Flow — State Service + Presentation Service

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

A scenario is an ordered queue of steps (JSON, or authored in code). Each step:

| Field | Meaning |
|-------|---------|
| `waitingClass` | The class whose arrival **activates** this step (empty on the entry step — activated by `Begin()`). |
| `title` / `description` | Form copy. A step with no title and no options is a **pass-through**: no UX, the flow immediately waits on its `result`. |
| `options` | Action labels (default one). Pressing **any** option emits the step's `result` class. |
| `detectedClass` | Class to annotate in the world while the step is active. |
| `label` | Text for the location indicator placed at that class's box centre. |
| `imageRef` | Client-side image reference (currently the shared camera-on-grey placeholder). |
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

Shipped as `Assets/XRTraining/Resources/EtharTrainingScenario.json` (and as the
built-in fallback `TrainingScenarioLibrary.EtharDemo()`):

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

- Wraps the pure, EditMode-tested `TrainingStateMachine`
  (`Runtime/Training/TrainingStateMachine.cs`).
- Subscribes to `IDetectionService.DetectionsReceived` — the detection service
  is always sending; the queue filter makes the flow authoritative.
- Events: `ScenarioLoaded`, `StepActivated` (with the triggering
  `TrainingDetectionMatch` when a detection caused the transition),
  `CurrentClassSighted` (re-sightings of the active step's `detectedClass`, so
  the world label tracks the object), `ScenarioCompleted`.
- `CompleteStep(TrainingStepResult)` — the custom response class fed back by
  the presentation layer; routes the result class down the detections path.
- Profile: scenario `TextAsset`, minimum detection confidence (default 0.5),
  verbose logging (`[QVS:Training]` for logcat filtering).

### `ITrainingPresentationService` (interface in package, implementation in the client app)

Receives UX requests from the state flow and owns the training UI
(`TrainingPresentationService` + `TrainingUxController` in
`Unity-Quest-Client/Assets/QuestVisionStream/Scripts/Training/`):

- **Display menu** — a world-space step form built from the XRTraining uGUI kit
  (`Assets/XRTraining/`): eyebrow step counter, progress ticks, title,
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
  `ControllerUiPointer` owns the EventSystem: an `InputSystemUIInputModule`
  configured in code with the right controller's OpenXR **aim pose** as a
  tracked pointer, **trigger** as select, and a laser beam so the user can see
  what they're aiming at (no interaction-toolkit dependency; the Editor mouse
  works through the same module). Every interactive world-space canvas — the
  warm-up Enter card, the step form, the hand menu — registers a
  `TrackedDeviceRaycaster` through `ControllerUiPointer.RegisterCanvas`. The
  right-controller **A** remains a shortcut for the form's first action.

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
3. **Enter** (laser click or A) — the frame pump starts (first pixels leave
   the device now), the card hides, and the training scenario auto-begins:
   the Welcome form appears immediately. Auto-begin requires both gates —
   user entered **and** session connected — with the server `ready` handshake
   as an extra trigger for unusual orderings.
4. **Begin** on the Welcome form — detections are already flowing, so step 2
   ("Look for a Monitor") is live detection from the first second.

Both services are registered code-first in `QuestVisionStreamBootstrap`
(priorities 45/46) behind the `enableTraining` toggle, alongside a
`trainingScenarioJson` override and a theme index (Dark·Cyan / Light·Teal /
Hi-Vis·Orange — the swappable XRTraining palettes).

## Authoring a new scenario

1. Copy `EtharTrainingScenario.json` and edit the queue — keep the chain rule:
   *each step's `result` is the next step's `waitingClass`*, and the final
   step's `result` empty.
2. Use detector class names (`tv`, `person`, …) for steps advanced by vision;
   use any unique token (`begintraining`, `foundtv`, …) for steps advanced by
   an action press.
3. Assign the asset to **Bootstrap ▸ Training Scenario Json** (or replace the
   Resources asset).

Malformed JSON is rejected at load (the current scenario is kept and the
built-in demo is the last-resort fallback), and
`TrainingScenarioParser.ToJson` round-trips a scenario for tooling.

## Tests

`com.questvisionstream.unity/Tests/Editor/`:

- `TrainingScenarioTests` — JSON parsing, queue chain rule, round-trip,
  malformed-input rejection, demo-vs-Excel fidelity.
- `TrainingStateMachineTests` — the full Excel flow end to end, discard
  behaviour, case-insensitivity, reset/restart, and the synthetic
  response-as-detection path through the real channel parser.
