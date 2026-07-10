# Changelog

## [Unreleased]

- **Training scenarios are now ScriptableObject assets**:
  `TrainingScenarioAsset` (Create → QuestVisionStream → Training Scenario) with
  a custom inspector in the new `QuestVisionStream.Unity.Editor` assembly —
  live chain validation (unreachable steps, dead-end results, early completion)
  and JSON import/export via `TrainingScenarioParser`, which stays as the wire
  format. `TrainingStateServiceProfile` takes the asset instead of a JSON
  `TextAsset`; an asset with no steps falls back to the built-in demo.
- **Breaking**: `TrainingStateServiceProfile.ScenarioJson` (TextAsset) replaced
  by `TrainingStateServiceProfile.Scenario` (`TrainingScenarioAsset`).
  `ITrainingStateService.LoadScenarioJson` is unchanged.

- **Fix: eager negotiation no longer self-destructs (stuck "Negotiating…")**.
  Three related repairs in `WebRTCService`/the Android transport:
  - The "signaling restored" renegotiation now fires only after a REAL
    signaling drop while a session was in flight. Previously the first-connect
    event could race the eagerly-started session and kill it mid-negotiation.
  - `RestartSession()` cycles the signaling socket: the server accepts exactly
    one offer per socket (extra offers are silently ignored), so any
    renegotiation on the same socket hung at "negotiating" forever. A fresh
    socket reads as a same-client reconnect and cleanly replaces the session
    server-side. (Also fixes the peer-Failed recovery path.)
  - An OPEN data channel now synthesizes `Connected` in case a `pcState`
    event is lost — a status surface can never sit at "negotiating" over a
    live link.
  - Consent audit: the first pushed camera frame is logged
    (`first camera frame pushed`) so the device log proves no pixels leave
    before Enter.
- **Warm connection launch pattern**: `WebRTCService` now negotiates the
  session eagerly (as soon as camera + signaling are ready — i.e. behind the
  warm-up screen) and `AutoStartSession`/`BeginStreaming()` gate only the
  frame pump: no camera pixels leave the device until the user confirms, but
  the peer connection, data channel and ICE are warmed and READY by the time
  Enter lights up. New `IWebRTCService.StreamingBegan` event signals the
  user's confirm.

- **Training flow** (`Runtime/Training/`, `Runtime/Services/Training/`): the
  authoritative training state queue driven by detections.
  - `TrainingScenario` / `TrainingScenarioParser` — the scenario queue's JSON
    format (waiting class, form copy, options, detected class, world label,
    image ref, result) plus the built-in Ethar demo scenario mirroring
    `Training_Scenario.xlsx`.
  - `TrainingStateMachine` — pure, EditMode-tested queue: the next expected
    class is statically cached so the per-detection hot path is one string
    comparison; non-matching detections are discarded.
  - `TrainingStateService` (`ITrainingStateService`) — subscribes to the
    detection service, advances the queue on the expected class, and routes
    training form responses through the SAME detections path
    (`TrainingResponseMessage` → `DetectionChannelParser` → batch handler),
    so an action press is literally a detected class arriving.
  - `ITrainingPresentationService` — the UX seam (step form, hand-menu step
    readout, world label + connector); uGUI implementation lives in the
    client app, same split as the render modules.
- EditMode tests: scenario parsing/round-trip and the full demo flow
  (`TrainingScenarioTests`, `TrainingStateMachineTests`).
- Docs: `V2/Documentation/Training-Flow.md`.

## [1.0.0-pre.1] - 2026-07-06

Initial V2 rebuild of the Unity client library as RealityCollective Service
Framework services with swappable service modules.

- Protocol layer wire-identical to the V2 `QuestVisionStreamServer` (nullable
  `pts`, additive-field tolerance, strict numeric validation, aiortc candidate
  prefix handling, server close-code semantics).
- Core: detection normalization math, dedup policies, pose history +
  capture-pose unprojection (pose-freeze), pts-based latency estimation,
  status model.
- Services: signaling (C#, com.utilities.websockets), camera stream, image
  qualifier (brightness module), WebRTC orchestration + media-only Android
  plugin transport module, pose tracking, detection parsing, runtime-switchable
  detection render modules (ephemeral pose-frozen boxes included), AprilTag
  detection/routing/placement with a Keijiro tagStandard41h12 detector module,
  status service with server uplink.
- EditMode test suite for the pure core (math, dedup, latency, pose, protocol,
  status).
