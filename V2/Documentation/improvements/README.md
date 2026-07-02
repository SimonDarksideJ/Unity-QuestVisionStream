# Improvements — strategy & method guide

This folder is the running record of deliberate improvement passes over the V2
codebase: what was changed, **why**, and the measured evidence that the change
actually improved things. It doubles as a learning guide — each entry shows the
method in action so future passes (by anyone, human or agent) follow the same
discipline.

## The method: prove it broken, fix it, prove it fixed

Every improvement pass follows the same three-step loop:

1. **RED — write the test first, watch it fail.** Before touching production
   code, write a test that encodes the *desired* behaviour and run it against
   the current code. The failure is the proof that the defect is real (not a
   review-time guess) and the failure message/stats are the "before" baseline.
   If you cannot make a test fail, you have not understood the defect yet —
   stop and re-investigate.
2. **GREEN — make the smallest change that passes.** Fix the production code.
   Do not touch the test to make it pass (pacing/fixture bugs in the *test*
   are the one exception — and note them in the entry when it happens, as in
   entry 2026-07, where latest-frame-wins legitimately changed how test fakes
   must feed frames).
3. **MEASURE — capture before/after stats.** For behavioural bugs the stat is
   binary (crashed → survives). For performance work the test itself should
   *print* the measured quantity (loop stall, lag, throughput) so every future
   run re-verifies the number, and the entry records both sides of the change.

### Rules of thumb learned so far

- **Test against fakes at the seams, not the heavy stack.** The server suite
  runs without model weights, GPU, or a real WebRTC session: detectors are
  plain functions, tracks are scripted objects with `recv()`, and signaling is
  driven through a fake websocket. This keeps the suite ~2.5s and runnable
  anywhere — which is the difference between tests that run and tests that rot.
- **Make performance tests print their measurement.** An assertion threshold
  says pass/fail; the printed `[STATS]` line is what goes in the entry and what
  lets you spot gradual regressions before they cross the threshold.
- **Watch for defects masking each other.** The event-loop-blocking bug hid
  the backpressure bug: with the loop blocked, the test's frame *producer*
  starved too, so queue lag looked tiny. Only after fixing the first defect
  could the second one be measured honestly. When a "before" number looks
  suspiciously good, ask what else is broken.
- **Hold everything else constant when measuring.** The backpressure numbers
  compare FIFO vs latest-frame-wins *with the executor fix applied to both* —
  otherwise the stat conflates two changes.
- **Behaviour changes ripple into test fixtures.** Latest-frame-wins means a
  fake track that serves frames instantly will (correctly) have frames
  dropped; tests that need every frame processed must pace the feed slower
  than inference. Expect to revisit fixtures when you change scheduling
  semantics — that is not "fudging the test" as long as the *assertion* stays
  honest.
- **Additive wire changes only.** New payload fields (like `pts`) must be
  ignorable by existing clients (Unity `JsonUtility` and the TS guard both
  ignore unknown fields). Never rename or re-type an existing field without a
  coordinated client change.
- **Mock the platform seam, not the library.** For browser-API code
  (WebSocket, RTCPeerConnection), stub the globals with scriptable doubles
  that *enforce the platform's contracts* (e.g. `addIceCandidate` throws
  before the remote description is set). Timing races become deterministic
  red tests instead of flaky field bugs.
- **Lock baselines before fixing.** Write passing tests for the behaviour a
  review verified as correct *first* — they pin it so the fixes can't regress
  it unnoticed. In the library pass, 21 of 36 tests were baseline locks.
- **Fix shared-layer gaps in the shared layer.** Session recovery could have
  been patched in the quest-client, but every future host would inherit the
  gap; the re-offer state machine belongs in the library next to the state it
  reasons about.

## How to add an entry

1. Copy the structure of an existing entry (`2026-07-Server-Hardening.md`):
   context → defect table → per-change sections (problem / evidence / change /
   result) → stats table → lessons.
2. Name it `YYYY-MM-<Topic>.md` and link it in the index below.
3. Keep the red-run output (or its key lines) in the entry — the failing
   evidence is the most instructive part for the next reader.

## Index

| Entry | Scope | Headline results |
|-------|-------|------------------|
| [2026-07 — Server hardening](2026-07-Server-Hardening.md) | `QuestVisionStreamServer` (Python) | Event-loop stall 316ms → 8.4ms; live-edge lag 1631ms (unbounded) → 60ms (bounded); 6 robustness/security gaps closed; first test suite (18 tests) |
| [2026-07 — Library hardening](2026-07-Library-Hardening.md) | `com.questvisionstream` (TypeScript) | Offer-drop connect race fixed; early ICE candidates 3/3 lost → 3/3 applied; mid-session drop permanent → auto re-offer; 2 unhandled-rejection paths → 0; strict payload validation; first test suite (36 tests) |
