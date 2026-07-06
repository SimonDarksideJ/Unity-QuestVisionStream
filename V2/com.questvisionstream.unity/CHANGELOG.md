# Changelog

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
