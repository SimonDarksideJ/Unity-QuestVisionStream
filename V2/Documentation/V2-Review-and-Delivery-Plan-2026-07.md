# V2 End-to-End Review & Delivery Plan — July 2026

Scope: the `V2/` tree only (V1 at the repo root is reference-only). Review +
plan only — **no code was changed**. Every finding was verified against the
actual source with file/line references. This document covers four asks:

1. End-to-end review of V2 deliverables — inconsistencies and improvements.
2. Verify the **package breakup** is clean enough to split into standalone
   repos, with simple accompanying documentation.
3. Plan **operationalising delivery**: a mermaid-authored workflow that turns
   into a real training/demonstration setup, so new training workflows are
   simple to build.
4. Assess whether the **AprilTags workflow follows the YOLO detection path**
   (especially for training) so it can augment or replace YOLO offline, with
   everything unified under the **ClassName** architecture.

---

## Executive summary

- **The code split is clean; the story around it has drifted.** The five
  packages have an acyclic, name-referenced, correctly-declared dependency
  graph and no duplicated logic — they are structurally ready to split. What
  is *not* ready is packaging hygiene (licenses, links, versions, Python
  packaging) and, above all, **documentation**: the docs hub, the V2 README's
  lower half, root README, and CI still present the retired TypeScript/WebXR
  stack as the current product, while the actual product (the Unity client)
  is untested in CI.
- **AprilTags does NOT follow the YOLO path today.** It was successfully
  ported to the Unity client, but as a fully parallel pipeline: integer tag
  IDs enriched to phonetic names (Alpha…Juliet) with 6-DoF poses, on its own
  event bus, never touching `DetectionService` or `TrainingStateService`. A
  tag sighting can never satisfy a `waitingClass` step. The fix is small and
  the pattern already exists (the synthetic action-response bridge) — an
  adapter plus a registry vocabulary change, not new architecture.
- **"ClassName" is real but unnamed on the wire.** The entire training
  harness keys on a case-insensitive class-name string, but the wire field is
  `label` and nothing documents the contract. Unification means writing the
  contract down and making every source (server detector, action press,
  AprilTag) emit through it — not renaming the wire field.
- **The training model is strictly linear** (single `result` per step, shared
  by all options; forward-only advance). A mermaid toolchain for the common
  linear case is buildable *today* against the existing JSON schema, parser,
  validator and Python simulator. True branching (per-option results), loops,
  and either-of waits need a deliberate, versioned model extension.

Recommended order: **Phase 0 truth & hygiene → Phase 1 split readiness →
Phase 2 ClassName unification (AprilTags) → Phase 3 mermaid toolchain →
Phase 4 delivery kit.** Phases 2 and 3 are independent of each other and can
run in parallel after Phase 1.

---

## Part I — Review findings

### A. Package breakup (verdict: clean, with hygiene gaps)

Dependency graph as verified (all one-directional, no cycles, all asmdef
references **name-based** — zero GUID refs — so portable across repos):

```
com.ethar.trainingstatemachine   (leaf; pure C#, noEngineReferences: true)
        ▲
com.questvisionstream.unity ───► com.ethar.trainingstatemachine
        ▲
com.ethar.debugdrawingbbox  ───► com.questvisionstream.unity
com.ethar.uxtraining             (leaf; Input System + uGUI only)
com.ethar.trainingstatemachine.python  (standalone 1:1 port)
Unity-Quest-Client (app)    ───► all four Unity packages via file: links
```

**Confirmed clean:**

- Every cross-package `using` is backed by a declared `package.json`
  dependency; inter-package version pins match actual versions.
- `TrainingStateMachine` was **moved, not duplicated** — the engine-agnostic
  machine lives only in `com.ethar.trainingstatemachine`;
  `com.questvisionstream.unity/Runtime/Training/` retains only the
  `TrainingScenarioAsset` authoring wrapper and `TrainingResponseMessage`.
- The app is genuinely thin-host: only `MetaPassthroughCameraCaptureModule`
  (platform glue) and `TrainingPresentationService` (Ethar-UX glue) live in
  `Assets/QuestVisionStream/Scripts/` beyond bootstrap/UI/utility scripts. No
  reusable logic is stranded app-side.
- `com.ethar.uxtraining` has zero QVS/Ethar.Training coupling — the
  `TrainingStepView` seam holds.
- The Keijiro AprilTag module is a correctly define-gated optional module
  (`QVS_APRILTAG_KEIJIRO` + versionDefine on `jp.keijiro.apriltag`).

**Gaps to close before splitting (Phase 1 backlog):**

| # | Gap | Where |
|---|-----|-------|
| P1 | **No per-package LICENSE file** in any of the five packages; C# headers say "See LICENSE in the repository root", which won't exist post-split. | all packages |
| P2 | **Monorepo-relative links** that break on split: `com.questvisionstream.unity/README.md:12` → `../Documentation/Unity-Client.md`; `README.md:38` → `V2/tools/generate-apriltags.py`; CHANGELOG → `V2/Documentation/Training-Flow.md`. | com.questvisionstream.unity |
| P3 | **`documentationUrl` in all four Unity package.json files** points at the monorepo branch subpath (`…/tree/QuestV2/V2/Documentation`) — 404 after split. | all Unity packages |
| P4 | **Version skew / unreleased breaking changes**: `com.questvisionstream.unity` CHANGELOG carries an `[Unreleased]` block (the state-machine + renderer extractions — breaking) with no version bump; deps sit at `1.0.0-pre.1` while `com.ethar.trainingstatemachine` is stable `1.0.0`. | com.questvisionstream.unity |
| P5 | **Python package has no packaging manifest** — no `pyproject.toml`, no version, no CHANGELOG; source-tree only, not pip-installable. | com.ethar.trainingstatemachine.python |
| P6 | **No tests** in `com.ethar.uxtraining` or `com.ethar.debugdrawingbbox` (the other three packages ship suites). | those two packages |
| P7 | Missing install docs: `com.questvisionstream.unity` README lacks the scoped-registry setup (OpenUPM + Keijiro) that the app's `manifest.json:61-79` currently supplies implicitly. | com.questvisionstream.unity |
| P8 | Newtonsoft is used but not listed in asmdef references (relies on auto-reference) in both `QuestVisionStream.Unity` and `Ethar.TrainingStateMachine` asmdefs — works, but fragile once standalone. | both |
| P9 | The app `manifest.json` file-links must be swapped for registry/git refs at split time (five `file:../../…` lines). | Unity-Quest-Client |

### B. Documentation & CI drift (the biggest inconsistency cluster)

The Unity client and `Training-Flow.md` are accurate; the *surrounding* docs
still describe the retired TS/WebXR stack as current:

| # | Finding | Location |
|---|---------|----------|
| D1 | **Root `README.md` is V1-only** — never mentions V2 or the direction change. First thing any visitor reads. | `/README.md` |
| D2 | **`Architecture-and-Hosting.md`** (the hub's "Read this first") describes the IWSDK/Three.js client end-to-end; the Unity client is never mentioned. | `V2/Documentation/Architecture-and-Hosting.md:16-83` |
| D3 | **`Configuration-and-Connectivity.md`** is entirely WebXR-client config (`config.ts`, `?server=`, Horizon Browser) — and `Unity-Client.md:147` links readers straight into it for the protocol. | `V2/Documentation/Configuration-and-Connectivity.md` |
| D4 | **`V2/README.md` lower half is stale**: "Build & run" says `cd service-framework` (folder doesn't exist — contradicting its own line 29), builds the retired TS client, and "Architecture at a glance" + "Verification status" describe the TS stack. | `V2/README.md:48-105` |
| D5 | **CI never touches the current product**: `v2-tests.yml` runs server pytest (good) plus tsc/vitest on the two **retired** TS packages; a C# change triggers CI that builds only retired code. `quest-client-deploy.yml` still deploys the retired WebXR client. No Unity/C# job, no Python state-machine job. | `.github/workflows/` |
| D6 | **Retired folders don't say they're retired** — `quest-client/README.md` and `com.questvisionstream/README.md` present themselves as live; only `V2/README.md:23-24` marks them. 3 of 4 `improvements/` logs document the retired stack with no historical marker. | retired folders, `Documentation/improvements/` |
| D7 | **`Unity-Client.md` architecture diagram omits Training services** (45/46) even though `enableTraining` defaults on — training is the headline feature. | `Unity-Client.md:22-33` |
| D8 | **Warm-up gating described two different ways**: `Unity-Client.md:97-108` (Enter at signaling-connected) vs `Training-Flow.md:161-164` (Enter at peer-connection Connected, with a `Negotiating…` tier). Reconcile against `StartupFlowController.cs`. | both docs |
| D9 | **`Training-Flow.md:91` points the state machine at a file that moved** (`Runtime/Training/TrainingStateMachine.cs` in the QVS package → now in `com.ethar.trainingstatemachine`). | `Training-Flow.md` |
| D10 | **`V2/package.json` root scripts** build/typecheck only the two retired TS packages; description still says "IWSDK Quest WebXR client". | `V2/package.json:4-10` |
| D11 | **`Documentation/README.md`** bills `Architecture-Review-2026-07.md` as covering "the whole V2 tree" — its own scope line says TS library + IWSDK app + server; it predates the Unity client. | `Documentation/README.md:26-28` |
| D12 | **Branding split unexplained**: company=Ethar, product=TrainingSimulator, Android appId=`com.zenithmoon.questvisionstream` (also hardcoded in `DeviceLogRecorder.cs`), repo/namespaces=QuestVisionStream, packages=`com.ethar.*`. Four brands, no doc explaining which is which. | `ProjectSettings.asset`, docs |
| D13 | `HANDOVER.md` / `EVALUATION.md` still recommend the (since-reversed) WebXR direction with no "superseded" note, yet are cited as "the analysis that drove this rebuild". | repo root |

### C. Server findings

The July hardening pass is real: S1 (inference on a dedicated worker via
`run_in_executor`, `video_processor.py:248-250`), latest-frame-wins
backpressure, S4 log-interval clamp, and S5 multi-track teardown are all
implemented **and tested**. Remaining issues:

| # | Finding | Location |
|---|---------|----------|
| S-a | **"Per-connection detector state" is overstated.** The `VideoProcessor` is per-connection; the **detector model is shared** across connections (`server.py:56`), with safety coming from `QVS_MAX_CONNECTIONS=1` + the single inference worker. Code comments say so; any doc claiming per-connection detector isolation should be corrected. | `server.py:51-63`, `webrtc_server.py:123-128` |
| S-b | **Stale README defaults**: `QVS_YOLO_CONF` documented `0.6`, code default `0.35` (`yolo_detector.py:32`); `QVS_YOLO_IGNORE` documented "people/vehicles", code default empty (`yolo_detector.py:15`). | `QuestVisionStreamServer/README.md:61-62` |
| S-c | **Env-var table incomplete**: `QVS_YOLO_MODEL`, all `QVS_FLORENCE_*`, `QVS_OWLV2_*`, `QVS_DINO_*`, `QVS_FFMPEG_LOG_LEVEL`, `QVS_DUMP_DIR` missing from the table. Users switching detectors get no guidance on the knobs that drive them. | README |
| S-d | **No detector unit tests** — every `detect()` body (yolo/florence2/owlv2/grounding_dino/body) is stubbed in the suite; label mapping, ignore filtering, frame-skip caching unverified. | `tests/` |
| S-e | **Personal email as a test fixture** (`simon.darkside.jackson@googlemail.com`) in `tests/test_detection_log.py:124-128`. Replace with a synthetic address. | tests |
| S-f | No server-side AprilTag detector exists (by design today — see Part D); no `requirements-apriltags.txt`. Decide deliberately whether one is wanted (see Phase 2 options). | `detectors/__init__.py` |

Positive confirmations: V2 server has **zero** references into the V1 tree;
the detector registry remains the single source of truth; requirements-file
mapping matches `run-local.sh`; protocol docs match the code (including
`pts`).

### D. AprilTags vs the ClassName architecture (verdict: divergent)

AprilTags did **not** die with the WebXR client — the Unity port is complete
(`Runtime/Services/Tags/` + `Runtime/Modules/AprilTagKeijiro/`, bootstrap
priorities 40/41/42, tagStandard41h12 via `jp.keijiro.apriltag`). But it is a
**parallel subsystem** end to end:

| Aspect | YOLO/detections path | AprilTag path |
|---|---|---|
| Identity | `label` string (COCO token) | `int id` → phonetic `TagName` (Alpha…Juliet) |
| Payload | `DetectionsPayload{label, conf, bbox}` | `DetectedTag{Id, WorldPose}` / `TagObservation` |
| Confidence | `conf` 0..1, gated by profile | none |
| Geometry | normalized 2D bbox | 6-DoF world pose (metres) |
| Entry point | `DetectionService.DetectionsReceived` → `TrainingStateService.ProcessBatch` | `TagsDetected` → `TagRoutingService` → placement |
| Training | drives `waitingClass` matching | **never reaches the training harness** |

Verified: no file under `Runtime/Services/Tags/` or `Runtime/Modules/`
references `DetectionsPayload`, `IDetectionService`, or the training service.
`TagName="Alpha"` can never equal `waitingClass="tv"`. So today AprilTags
**cannot** augment or replace YOLO for offline training.

The unification pattern already exists and is proven:
`TrainingResponseMessage.ToDetectionsJson(resultClass)` wraps an action press
into a wire-shaped detections payload (`{type:"detections", frame:-1,
detections:[{label, conf:1.0, bbox:[1,1,1,1]}]}`) and pushes it through the
same parser and batch handler (`TrainingStateService.cs:186-194`). "An action
press *is* a detected class" — a tag sighting should be too. Plan in Phase 2.

Also noted: the "ClassName" concept is nowhere named on the wire — the
identity field is `label` (defined `detectors/base.py:17-22`; consumed as
`Detection.Label` client-side). That is fine, but the contract deserves a
short canonical write-up (see Phase 2, item 2.1).

### E. Training model & authoring (verdict: solid core, strictly linear)

Confirmed single-source data model: the engine-agnostic
`Ethar.Training.TrainingStep` (8 fields), mirrored 1:1 by the Unity authoring
asset and the Python port; **identical camelCase JSON wire format** in both
parsers (both tolerant of missing fields, both requiring non-empty `steps`);
demo scenario reproduced row-for-row in C#, Python and the JSON example.
Editor-side validation (`TrainingScenarioAssetEditor.Validate`) covers
unreachable steps, early completion, and dead-end results, and is pure/static
— liftable into a headless tool. The Python `console.py` is a ready-made
headless simulator.

Expressiveness limits (all verified in code, they gate the mermaid plan):

1. **One `result` per step, shared by every option** — `TriggerOption` emits
   `step.Result` regardless of the option pressed (`OptionLabel` is
   audit-only). No decision branching.
2. **Single cached `ExpectedClass`** — no either-of/parallel waits.
3. **Forward-only advance** — `Offer` scans from `CurrentStepIndex+1`; no
   loops or backward jumps. Forward *skips* are expressible.
4. Progress ticks derive from queue position — ill-defined in a general
   graph.

Drift risks: demo scenario is hand-duplicated in three places (C# library,
Python library, JSON example); graph validation exists only in the C# Editor
(Python has structural parsing only); the referenced `Training_Scenario.xlsx`
is not in the repo (historical source, hand-transcribed).

---

## Part II — The plan

No code changes were made in this pass. The following is the recommended
sequence of work, sized and ordered so each phase leaves the tree shippable.

### Phase 0 — Truth & hygiene pass (docs + CI, ~no code)

Goal: a newcomer following the default reading path lands on the Unity
client, and CI exercises the product that ships.

1. **Root `README.md`**: add a prominent direction-change banner + link to
   `V2/` (fixes D1).
2. **Rewrite `Architecture-and-Hosting.md` and
   `Configuration-and-Connectivity.md` for the Unity client** (server +
   hosting content is still valid — the client half needs replacing). Fix the
   onward link at `Unity-Client.md:147`. (D2, D3)
3. **`V2/README.md`**: replace the lower half — Unity-first architecture
   diagram, build/run for Unity + server, current verification status. Kill
   `cd service-framework`. (D4)
4. **Retire markers**: "RETIRED — reference only" banners at the top of
   `quest-client/README.md`, `com.questvisionstream/README.md`, and the three
   TS improvement logs; note in `improvements/README.md` index. (D6)
5. **CI**: add jobs for (a) Unity C# — at minimum EditMode tests of
   `com.ethar.trainingstatemachine` + `com.questvisionstream.unity` via
   `game-ci/unity-test-runner` (needs a Unity licence secret), and (b)
   `python -m unittest` for `com.ethar.trainingstatemachine.python`. Decide
   the fate of `quest-client-deploy.yml` (recommend: disable trigger, keep
   file with a retired note). Scope path triggers so TS jobs only run on TS
   paths. (D5)
6. **Small doc fixes**: add Training (45/46) to the `Unity-Client.md`
   diagram (D7); reconcile warm-up tiers against `StartupFlowController.cs`
   (D8); fix the moved state-machine path in `Training-Flow.md:91` (D9);
   correct `Documentation/README.md`'s framing of the July review (D11);
   correct server README YOLO defaults + complete the env table (S-b, S-c);
   fix any doc claiming per-connection detector state (S-a).
7. **Hygiene**: replace the personal email fixture in
   `test_detection_log.py` (S-e); update or retire the root `V2/package.json`
   scripts (D10); add a short "Branding" note (Ethar = company/packages,
   QuestVisionStream = streaming tech, TrainingSimulator = product) and
   decide whether `com.zenithmoon.*` stays as the appId (D12); add
   "superseded by the Unity direction" notes to HANDOVER/EVALUATION (D13).

### Phase 1 — Package-separation readiness

Goal: each package can be lifted into its own repo with `git mv` + a registry
publish, and nothing breaks.

1. **Per-package LICENSE** files (P1) and standalone READMEs: replace
   monorepo-relative links with absolute URLs or in-package content (P2);
   add an Installation section (scoped registries, optional
   `jp.keijiro.apriltag` for the tags module) to `com.questvisionstream.unity`
   (P7).
2. **Versioning discipline**: roll the `[Unreleased]` block in
   `com.questvisionstream.unity` into a dated `1.0.0-pre.2` (or `1.1.0-pre.1`)
   release; align dependents (P4). Decide the target registry (OpenUPM under
   the existing scopes is the natural fit) and set real `documentationUrl`s
   per future repo (P3).
3. **Python packaging**: add `pyproject.toml` (name
   `ethar-training-state-machine`, version matching the C# package, py≥3.8),
   a CHANGELOG, and console entry point (P5).
4. **Explicit Newtonsoft asmdef references** in the two asmdefs that use it
   (P8).
5. **Minimal test suites** for `com.ethar.uxtraining` (theme/view logic) and
   `com.ethar.debugdrawingbbox` (math/module selection) so every package has
   at least a smoke suite before living alone (P6).
6. **Split mechanics** (when the time comes): per package — new repo, copy
   tree + LICENSE, publish to registry, then swap the app `manifest.json`
   `file:` links to registry versions (P9). Keep `com.ethar.trainingstatemachine`
   first (leaf), then `com.questvisionstream.unity`, then
   `com.ethar.debugdrawingbbox`; `com.ethar.uxtraining` and the Python
   package can go any time. A shared wire-protocol fixture (one detections
   JSON asserted by pytest, NUnit and the Python SM tests) should land
   *before* the split so the contract survives repo boundaries.

### Phase 2 — ClassName unification (AprilTags joins the detection path)

Goal: **one identity — the class name — one ingress — the detections
pipeline** — regardless of source (server model, action press, AprilTag).

1. **Write the ClassName contract down** (new short doc,
   `Documentation/ClassName-Contract.md`): a detection identity is a
   case-insensitive class token carried in the wire field `label`;
   sources are `Detection` (server), `ActionResponse` (synthetic),
   and — new — `AprilTag`; geometry is optional (degenerate bbox allowed, as
   the synthetic path already proves); confidence defaults 1.0 for
   non-model sources. Extend the existing `TrainingClassSource` enum with
   `AprilTag` in both C# and Python.
2. **Registry vocabulary**: replace/extend Alpha…Juliet in
   `tag-registry.json` + `TagRegistryAsset.CreateDefault()` with **class
   tokens** (e.g. `tv`, `person`, or scenario-specific tokens like
   `station1`). Keep name+color for placement UX; add an explicit
   `className` field so display name and class token can differ.
   `generate-apriltags.py` already reads the same registry — printed sheets
   then carry the class token automatically.
3. **Tag→detections adapter** (the core change): a small module —
   e.g. `TagDetectionBridgeModule` on the tag routing service — that on
   `TagEntered`/`TagUpdated` synthesizes the standard detections payload
   (modelled exactly on `TrainingResponseMessage.ToDetectionsJson`) with
   `label = className`, `conf = 1.0`, and either the projected tag quad as a
   normalized bbox (preferred — the box renderer and world-label placement
   then work unchanged) or a degenerate bbox (training-only). Route through
   `DetectionChannelParser` → the shared batch handler so **every** consumer
   (renderer, HUD, training) sees tags as detections. Gate behind a profile
   toggle.
4. **Offline mode**: a bootstrap/profile switch (`DetectionSourceMode:
   Server | AprilTag | Both`) so a demo can run with **no server at all** —
   signaling/WebRTC services simply not registered (or idle), tags feeding
   the training harness alone. `Both` = augmentation (tags for stations,
   YOLO for objects).
5. **Server-side AprilTag detector — recommend NO (for now).** On-device
   detection is lower-latency, offline-capable, and already built; a server
   `apriltag` registry entry would only serve non-Unity clients. Revisit only
   if a thin client appears. Document the decision (closes S-f deliberately).
6. **Tests**: adapter unit tests (tag → payload → state-machine advance,
   including case-insensitivity and confidence gating) in both the Unity
   package and, for the shared fixture, the Python suite.

### Phase 3 — Mermaid workflow toolchain ("draw the training, run the training")

Goal: author a training/demonstration flow as a mermaid flowchart, convert it
to a scenario, validate it, simulate it headlessly, and ship it to the
headset — all without touching Unity for the common case.

**3a. Converter for the current linear model (buildable now, zero model
changes).** A Python CLI in the state-machine package (it already owns the
parser + console simulator): `mermaid2scenario flow.mmd -o scenario.json`.
Authoring convention (all constructs verified to map onto the current model):

```mermaid
flowchart TD
  welcome["Welcome|Ready to begin your Ethar training?"]
  lookfor["Look for a Monitor|Search your space"]
  foundtv["Found TV|@tv: This is a tv"]
  done["Complete|Well done"]

  welcome -->|Begin| lookfor
  lookfor -->|@tv| foundtv
  foundtv -->|Next| done
```

- Node = step; node text `Title|Description`; a `@class: label` annotation
  sets `detectedClass` + world `label`.
- Edge label starting with `@` = **detection-advanced** (the edge label is
  the detector class ⇒ source step's `result`, target's `waitingClass`).
- Plain edge label = **action-advanced** (label becomes the button text in
  `options`, the converter mints the synthetic token, e.g. from the target
  node id).
- Node with no title ⇒ pass-through step. Forward skips = edges jumping
  ahead in declaration order.
- The converter **emits the exact existing JSON schema** (all eight keys per
  step), then runs a ported copy of `TrainingScenarioAssetEditor.Validate`
  (unreachable / dead-end / early-completion) and refuses on errors. Add a
  reverse `scenario2mermaid` for docs/review round-trips, and use it to
  render existing scenarios into the docs (replacing hand-drawn ASCII).
- Only a small mermaid subset needs parsing (flowchart nodes + labelled
  edges) — a ~200-line parser, no dependency on mermaid itself.
- **Simulate before shipping**: `console.py` already drives a scenario by
  typed class names; add a scripted mode (`--run "Begin,tv,Next"`) so CI can
  assert a generated scenario completes along its intended path.
- **Delivery to device**: the output JSON is already consumable three ways —
  Editor Import button, `Resources` asset replacement, and
  `ITrainingStateService.LoadScenarioJson` at runtime. For operational use,
  add a fourth: a `scenario` message over the existing signaling channel or a
  simple HTTP fetch at boot (small client change, Phase 4).

**3b. Model extension for real graphs (versioned, breaking — do second).**
Mermaid earns its keep with branching; the current model can't express it.
Extend deliberately, as wire-format v2 with `"schemaVersion": 2` and
back-compat parsing of v1:

1. **Per-option results**: `options: ["Begin"]` →
   `options: [{label, result}]` (v1 shorthand still accepted). Touches
   `TrainingStep`/`Data`/`Definition`, both parsers, both demo libraries, the
   inspector, and `TriggerOption` + `TrainingStepResult`. This alone unlocks
   mermaid decision nodes.
2. **Class-keyed lookup instead of forward-only scan**: replace `Offer`'s
   forward scan with a `waitingClass → stepIndex` dictionary over the whole
   queue (O(1) hot path preserved) — unlocks loops/backward edges. Relax the
   editor validation accordingly.
3. **Either-of waits**: `ExpectedClass` string → set (`result` may list
   alternatives). Optional; do last.
4. **Progress semantics in a graph**: authored ordinals or longest-path
   depth for the "STEP n/m" UI; define the convention in the contract doc.
5. Port every change to the Python SM **in the same PR** (it exists to be
   the parity/CI harness) and extend the shared JSON fixtures to v2.

### Phase 4 — Operationalising delivery

Goal: a repeatable "new customer demo in an afternoon" kit.

1. **Scenario delivery channel**: runtime scenario push (signaling `scenario`
   message or HTTP fetch keyed by device/config) so demos change without
   rebuilds; falls back to the shipped asset exactly as today.
2. **Demo-in-a-box checklist doc**: print tag sheet
   (`generate-apriltags.py` from the same registry the headset uses), start
   server (`run-local.sh` / docker), author flow (mermaid → validate →
   simulate), push scenario, run. Include the offline (AprilTag-only) recipe
   from Phase 2.4 — that is the resilient conference/demo mode.
3. **Scenario asset pipeline in CI**: a `scenarios/` folder of `.mmd`
   sources; CI converts, validates, simulates, and publishes the JSON as
   build artifacts consumed by the Unity build. Single source of truth ends
   the three-way demo duplication (C#/Python/JSON) — regenerate the demo
   libraries from the canonical JSON or delete the duplicates.
4. **Authoring UX (later, optional)**: the Unity inspector already validates
   live; a small web page rendering the mermaid + running the validator (both
   pure logic) would let non-Unity authors build flows. Only worth it once
   3a/3b are proven.

### Suggested milestone cut

| Milestone | Contents | Risk |
|---|---|---|
| M1 "Tell the truth" | Phase 0 | none (docs/CI) |
| M2 "Ready to split" | Phase 1 | low |
| M3 "Tags are detections" | Phase 2 | low-med (one adapter + registry change; pattern proven) |
| M4 "Draw-to-run (linear)" | Phase 3a + 4.2 | low (greenfield tool, existing schema) |
| M5 "Real graphs" | Phase 3b | med (breaking schema v2, cross-language) |
| M6 "Delivery kit" | Phase 4.1/4.3 | low |

M3 and M4 are independent; either can precede the other. The actual repo
split (Phase 1.6) can happen any time after M2 — recommended after M3, so the
`TrainingClassSource.AprilTag` and registry changes ship once, in-monorepo.
