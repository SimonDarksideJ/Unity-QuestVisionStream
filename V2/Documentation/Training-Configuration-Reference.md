# Training Configuration Reference — C# & Python (Unified)

**The single source of truth for the training state machine's capabilities and
configuration wire format**, aligned against both implementations:

| | C# (Unity ecosystem) | Python (server-side export) |
|---|---|---|
| Package | `V2/com.ethar.trainingstatemachine` | `V2/com.ethar.trainingstatemachine.python` |
| Namespace / module | `Ethar.Training` | `training_state_machine` |
| Consumed by | `com.questvisionstream.unity` → the Quest client | Server-side Python repos (standalone export — **not** part of the Unity project, never referenced by it) |
| Dependencies | Newtonsoft.Json only (`noEngineReferences`) | Python 3.8+ stdlib only |
| Naming | PascalCase (`WaitingClass`, `ProcessClass`) | snake_case (`waiting_class`, `process_class`) |
| Version | 1.0.0 (+ unreleased: `ModelRef`, `AprilTag`) | `__version__ = "1.0.0"` (parity incl. `model_ref`, `APRIL_TAG`) |

**Parity policy:** the Python package exists as a 1:1 behavioural port with an
**identical JSON wire format**. Any change to the wire format, the enums, or
the machine's semantics must land in **both packages and this document in the
same change**, with a round-trip test on each side. The JSON keys are the
contract; the language-local member names differ only by naming convention.

---

## 1. Configuration wire format (JSON)

Two accepted root shapes, parsed by `TrainingScenarioParser` (C#) /
`parser.py` (Python):

**Bare scenario** — `TryParse` / `try_parse_scenario`:

```json
{ "name": "…", "steps": [ { …step… } ] }
```

**Full machine config** — `TryParseConfig` / `try_parse_config`:

```json
{ "minimumDetectionConfidence": 0.5, "scenario": { "name": "…", "steps": [ … ] } }
```

### Parsing rules (identical in both)

- **Tolerant of missing fields** — every absent step field defaults to empty
  (the project-wide additive wire rule). Unknown keys are ignored, so older
  parsers safely read newer files (dropping fields they don't know — e.g. a
  pre-`modelRef` parser reads a `modelRef` file fine but strips the key when
  re-serializing).
- **Strict about shape** — `steps` must be a non-empty array and every element
  must be an object, else the whole parse is rejected (no partial loads).
- `options` entries that are not strings are silently dropped.
- `minimumDetectionConfidence` defaults to `0.0` when absent/invalid; the
  machine clamps it to `[0, 1]`.
- Serializers always emit **all** step keys (empty strings included) plus
  `name`/`steps`, so output from either language is byte-equivalent in content.
- **Class-name comparison is case-insensitive** everywhere
  (`OrdinalIgnoreCase` / `casefold`), so configurations need not normalize
  case — but be consistent for readability.

### Step schema — all fields, both languages

| JSON key | C# (`TrainingStep`) | Python (`TrainingStep`) | Default | Meaning |
|---|---|---|---|---|
| `waitingClass` | `WaitingClass` | `waiting_class` | `""` | The class whose arrival **activates** this step. Empty only on the entry step (activated by `Begin()`/`begin()`). |
| `title` | `Title` | `title` | `""` | Form headline. No title **and** no options ⇒ **pass-through** step (`HasPresentation`/`has_presentation` false): no UX, flow immediately waits on `result`. |
| `description` | `Description` | `description` | `""` | Form body text. |
| `options` | `Options` | `options` | `[]` | Action button labels. Pressing **any** option emits the step's `result` (option label is audit-only). |
| `detectedClass` | `DetectedClass` | `detected_class` | `""` | Class annotated in the world while the step is active; re-sightings fire `CurrentClassSighted`/`current_class_sighted`. Also the **model alignment target** (see `modelRef`). |
| `label` | `Label` | `label` | `""` | World label text at the detected box centre. `HasWorldLabel`/`has_world_label` = `detectedClass` and `label` both set. |
| `imageRef` | `ImageRef` | `image_ref` | `""` | Host-resolved image reference for the form. Opaque to the machine. |
| `modelRef` | `ModelRef` | `model_ref` | `""` | Host-resolved **model catalog key**: while the step is active the host's placement layer spawns the mapped model aligned to the physical marker (AprilTag) whose class name matches `detectedClass` (falling back to `waitingClass`). Opaque to the machine. `HasModel`/`has_model` = non-empty. |
| `result` | `Result` | `result` | `""` | **The next expected class** — should equal a later step's `waitingClass` (the chain rule). Empty = final step: its action completes the scenario. |

---

## 2. Class pipeline capabilities

### Class sources — `TrainingClassSource`

| Value | C# | Python | Origin | Confidence gate |
|---|---|---|---|---|
| 0 | `Detection` | `DETECTION` | The semantic detection pipeline (inference server over the WebRTC data channel) | **Gated**: arrivals below `MinimumDetectionConfidence` are discarded (`BelowConfidence`) |
| 1 | `ActionResponse` | `ACTION_RESPONSE` | Pressing an action on a training form — the result class re-enters the pipeline as a synthetic detection | Always passes (conf 1.0) |
| 2 | `AprilTag` | `APRIL_TAG` | An on-device fiducial sighting bridged into the detection pipeline | Always passes — deterministic, conf 1.0 |

Every source travels the **same path**: one class-arrival entry point
(`ProcessClass` / `process_class`), one cached-string hot-path comparison.
"An action press is a detected class" and "a tag sighting is a detected
class" are the same design rule.

**Unity-client wire conventions** (host-level, not part of the core
libraries): synthetic action-response payloads mark `frame = -1`
(`TrainingResponseMessage`), bridged AprilTag payloads mark `frame = -2`
(`TagDetectionMessage`); server frames count up from 0. A Python host
consuming logged payloads can use the same markers to distinguish sources.

### Flow semantics (identical in both)

- **Chain rule:** each step's `result` is cached as the single
  `ExpectedClass`/`expected_class`; on a match the machine scans **forward
  only** (from the next index) for the first step whose `waitingClass` equals
  the arrived class, and activates it. Forward **skips** are legal; loops and
  backward jumps are not expressible.
- An expected class that matches **no later** step's `waitingClass` is
  **terminal** — its arrival completes the scenario.
- Everything that doesn't match the expected class is discarded and counted
  (`DiscardedCount`/`discarded_count`).
- Lifecycle: `Idle → Running → Completed` (`TrainingFlowStatus`); `Reset` /
  `reset` returns to Idle keeping the loaded scenario.
- Per-arrival outcomes (`TrainingProcessOutcome`): `NotRunning`,
  `BelowConfidence`, `Ignored`, `StaleStep`, `Advanced`, `Completed`
  (Python: SCREAMING_SNAKE_CASE, same values 0–5).

---

## 3. State machine API — member-by-member parity

| Capability | C# (`TrainingStateMachine`) | Python (`TrainingStateMachine`) |
|---|---|---|
| Configure from serializable data | `Initialize(TrainingStateMachineConfig)` / ctor | `initialize(config)` / ctor |
| Load scenario (reset to Idle) | `Load(TrainingScenario)` | `load(scenario)` |
| Back to Idle, keep scenario | `Reset()` | `reset()` |
| Start (activate entry step) | `Begin() : bool` | `begin() -> bool` |
| Single class-arrival entry point | `ProcessClass(className, confidence, source)` | `process_class(class_name, confidence, source)` |
| Form action feedback | `CompleteStep(TrainingStepResult)` | `complete_step(result)` |
| Offer class to the queue | `Offer(className, source)` | `offer(class_name, source)` |
| Complete final step | `CompleteScenario(source)` | `complete_scenario(source)` |
| Hot-path checks | `MatchesExpected`, `MatchesCurrentDetectedClass` | `matches_expected`, `matches_current_detected_class` |
| State | `Scenario`, `Status`, `CurrentStep`, `CurrentStepIndex`, `ExpectedClass`, `CurrentDetectedClass`, `IsRunning`, `MinimumDetectionConfidence`, `DiscardedCount` | `scenario`, `status`, `current_step`, `current_step_index`, `expected_class`, `current_detected_class`, `is_running`, `minimum_detection_confidence`, `discarded_count` |
| Events | `ScenarioLoaded`, `StepActivated`, `CurrentClassSighted`, `ScenarioCompleted` (.NET events) | `scenario_loaded`, `step_activated`, `current_class_sighted`, `scenario_completed` (`Event.subscribe/unsubscribe/fire`) |
| Result/report types | `TrainingStepResult`, `TrainingAdvance`, `TrainingStepActivated`, `TrainingClassSighting`, `TrainingCompletion`, `TrainingProcessResult` | Same names, snake_case fields |
| Demo library | `TrainingScenarioLibrary.EtharDemo/EtharDemoData/EtharDemoConfig` | `ethar_demo/ethar_demo_data/ethar_demo_config` |
| Parser | `TrainingScenarioParser.TryParse/ToJson/TryParseConfig/ConfigToJson` | `try_parse_scenario/scenario_to_json/try_parse_config/config_to_json` |

---

## 4. What the core owns vs what the host owns

The machine interprets **only** `waitingClass`, `result`, `options` (count),
`detectedClass` and the confidence gate. Everything else is host territory:

| Concern | Owner | Unity host (current) | Python host (current) |
|---|---|---|---|
| Step forms / hand menu | Host presentation | `TrainingPresentationService` + `com.ethar.uxtraining` | `console.py` prints step + options |
| `imageRef` resolution | Host | Shared placeholder image | Ignored |
| `modelRef` resolution + spawn | Host placement | `TrainingModelPlacementService` (47): model catalog → prefab aligned to the step's AprilTag, kept aligned by `TagPoseFollower` (smoothed follow, freeze on tag loss) | Ignored (no scene) |
| World labels (`detectedClass` + `label`) | Host | Capture-pose unprojection at the detected box centre | Ignored |
| Class arrivals — server | Host ingress | `DetectionService` (WebRTC data channel) | Type a class in `console.py` |
| Class arrivals — action press | Host ingress | `TrainingResponseMessage` → same channel parser (`frame -1`) | `press [n]` in `console.py` |
| Class arrivals — AprilTag | Host ingress | `TagDetectionBridgeService` (43) → `IDetectionService.PublishLocal` (`frame -2`); registry `TagDefinition.ClassName` maps tag id → class token | n/a (tags are on-device) |
| Authoring | Host | `TrainingScenarioAsset` (ScriptableObject) + inspector with live chain validation + JSON/Markdown/CSV import & export ([Training-Builder.md](Training-Builder.md)) | Mermaid/CSV/JSON via the `builder.py` CLI ([Training-Builder.md](Training-Builder.md)) |
| Graph validation (unreachable / dead-end / early-completion) | **Core** (both packages) | `TrainingScenarioValidator` (inspector + builder) | `validate_scenario` (builder CLI report) |

**Offline capability (Unity host):** with `bridgeTagsToDetections` enabled and
registry class names matching scenario tokens, printed AprilTags advance the
flow with **no server and no ML model** — tags can augment the detector when
connected or replace it entirely offline.

---

## 5. The built-in demo scenario (shared)

Reproduced identically in `TrainingScenarioLibrary` (C#), `library.py`
(Python) and `examples/ethar_demo.json` — **all three must stay in sync**:

| # | `waitingClass` | Title | Options → `result` | `detectedClass` / `label` |
|---|---|---|---|---|
| 1 | *(empty — Begin)* | Welcome | Begin → `begintraining` | — |
| 2 | `begintraining` | Look for a Monitor | Search → `tv` | — |
| 3 | `tv` | Found TV | Next → `foundtv` | `tv` / "This is a tv" |
| 4 | `foundtv` | *(pass-through)* | → `person` | — |
| 5 | `person` | Found Person | Finish → `finishtraining` | `person` / "This is a person" |
| 6 | `finishtraining` | Complete | End → *(complete)* | — |

No demo step sets `modelRef` — it defaults to empty everywhere.

---

## 6. Parity checklist (update with every capability change)

| Capability | Wire key(s) | C# | Python | Tests |
|---|---|---|---|---|
| Scenario queue + chain rule | `name`, `steps`, `waitingClass`, `result` | ✅ | ✅ | both |
| Confidence gate (Detection only) | `minimumDetectionConfidence` | ✅ | ✅ | both |
| Pass-through steps | *(derived)* | ✅ | ✅ | both |
| World-label annotation | `detectedClass`, `label` | ✅ | ✅ | both |
| Form content | `title`, `description`, `options`, `imageRef` | ✅ | ✅ | both |
| Action-response source (1) | — | ✅ | ✅ | both |
| **AprilTag source (2)** | — | ✅ | ✅ | both |
| **Tag-aligned models** | `modelRef` | ✅ | ✅ (data only — no scene host) | both (round-trip) |
| Case-insensitive class match | — | ✅ | ✅ | both |
| Graph validation (`TrainingScenarioValidator` / `validate_scenario`) | — | ✅ | ✅ | both |
| **Mermaid builder** (markdown ⇄ config, shared dialect) | — | ✅ (`TrainingMermaidBuilder` + inspector buttons + `Builder~` dotnet CLI) | ✅ (`mermaid.py` + `builder.py` CLI) | both (round-trip; CLI outputs byte-identical) |
| **CSV import** (spreadsheet → config, `;`-separated options) | — | ✅ (`TrainingCsvBuilder` + inspector button + CLI) | ✅ (`csv_io.py` + CLI) | both |

Cross-references: [Training-Flow.md](Training-Flow.md) (the Unity client's
end-to-end flow and UX), the package READMEs
(`com.ethar.trainingstatemachine`, `com.ethar.trainingstatemachine.python`),
and `V2-Review-and-Delivery-Plan-2026-07.md` (planned evolution: per-option
results, loops, mermaid authoring toolchain — all of which must land in both
implementations per the parity policy above).
