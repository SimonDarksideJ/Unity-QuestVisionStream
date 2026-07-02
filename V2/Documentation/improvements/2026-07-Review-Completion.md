# 2026-07 — Review completion pass (client follow-ups + deploy pipeline)

**Scope:** the items every earlier pass *deliberately deferred*, closing out
the [2026-07 architecture review](../Architecture-Review-2026-07.md):
the in-AR status HUD, recenter cleanup, per-session latency estimation from
`pts`, the `?server=` confirmation, and the deploy-pipeline smoke check.
**Method:** red → green per the [strategy guide](README.md) — plus a step the
earlier passes didn't need: **de-risking deferred items by verifying the
platform API from the installed package** before writing any code.

## The unblocker: verify APIs from the shipped typings

Two deferrals (recenter cleanup, in-AR HUD) existed only because the IWSDK
surface they needed wasn't in the verified reference. Rather than guessing,
this pass read `node_modules/@iwsdk/core/dist/ecs/world.d.ts` (the actual
installed 0.4.2) and confirmed:

- `world.renderer: WebGLRenderer` → `renderer.xr` (three.js `WebXRManager`)
  → `'sessionstart'` event + `getReferenceSpace()` → the standard WebXR
  `'reset'` event on recenter;
- `world.playerHeadEntity: Entity` — a **persistent head Group**, the ideal
  parent for head-locked HUD content (no per-frame repositioning needed);
- `world.session`, `world.visibilityState` (Signal).

Both deferrals became one-evening wirings. The findings are recorded in
[`../IWSDK-API-Reference.md`](../IWSDK-API-Reference.md) (new
"source-verified World members" section), shrinking the "not fully verified"
caveat for the next person.

## The red run

```
3 failed | 32 passed; 2 test files unloadable (LatencyEstimator,
StatusSpriteSystem modules not yet created)
```

Green: **49 passed** (client suite grew 30 → 49), stable across repeated
runs; typecheck and `vite build` clean; library (36) and server (18) suites
unaffected.

## What was completed

### 1. In-AR status HUD (`StatusSpriteSystem`)

The DOM status panel (previous pass) is invisible inside an immersive
session. The new system parents a text sprite to `playerHeadEntity.object3D`
(~1 m out, slightly below gaze) showing `StatusModel.headline()` — the
single highest-priority problem (camera error → connection failure →
signaling drop → quality pause → connection progress), and **hides entirely
when healthy**. It re-renders the canvas texture only when the headline
actually changes, and disposes fully on destroy (verified via THREE dispose
events). `headline()` is unit-tested for the priority ordering.

*Remaining hardware caveat:* sprite legibility/placement tuning needs a real
headset — but that is now a tuning task, not a missing feature.

### 2. Recenter cleanup (`DetectionRenderSystem`)

On `sessionstart`, the system attaches to the reference space's `reset`
event; a recenter clears all tags **and** the pose history (both are
expressed in the old space, so both are garbage after a reset), and resets
dedup so objects re-place in the new space. Tested with scripted
`renderer.xr` / reference-space fakes. Guarded with optional chaining, so
hosts/tests without a renderer are unaffected.

### 3. Per-session latency estimation (`LatencyEstimator`)

The previous pass placed detections through the pose from a **constant**
`assumedLatencyMs` ago. The estimator makes that dynamic using the server's
`pts` field (added in the server pass): `offset_i = arrival_i − pts_i(ms)` is
constant when frames flow smoothly and rises when a frame queued behind slow
inference — so `latency_i = base + (offset_i − min(offset over 5 s))`.
Queuing spikes now shift the pose lookup correspondingly; the windowed
minimum re-anchors as conditions drift; frames without `pts` fall back to
the base. Fully unit-tested (constant offset, spike, recovery, window
eviction, null pts). The absolute base remains configured — only a
clock-synchronized protocol could remove it, deliberately out of scope.

### 4. `?server=` confirmation (the camera-redirect guard, completed)

The previous pass validated schemes and surfaced the target; this pass adds
the user-consent step: a non-localhost `?server=` override now asks
`Stream the headset camera to "<host>"?` before being accepted — declined
overrides fall through to the deployment-configured tier. Localhost dev
targets and the KV/env tiers (deployment-trusted) never prompt, so the dev
loop is unchanged. Tested for all four paths.

### 5. Deploy pipeline: `/api/config` smoke + documented fragility

`quest-client-deploy.yml`: both wrangler steps now carry a comment explaining
that the Pages Function deploys **implicitly** from the checkout's
`workingDirectory` (not from the tested dist artifact) and what breaks if
that changes; the staging smoke check now also fetches `/api/config` and
warns (non-fatally, consistent with the existing smoke philosophy) if the
Function didn't deploy.

## Review disposition — final state

Every finding from the architecture review is now either **fixed with test
evidence** (see the three hardening entries + this one) or **explicitly
dispositioned**:

| Still open | Why | Where it's tracked |
|---|---|---|
| Surface anchoring via hit-test / scene-understanding depth (P0) | Genuinely requires on-headset iteration; the ray math, pose history, and renderer seam it builds on are all in place and tested | quest-client README "Notes / limitations" |
| HUD legibility / `assumedLatencyMs` base tuning | Hardware tuning, not missing code | this entry |
| DI factory typing (`profile.ts` casts) | Belongs upstream in `@realitycollective/service-framework` | library entry |
| Server config split-brain (detector env vars outside `ServerConfig`) | Cosmetic consolidation; documented behaviour | server entry §6 |

## Addendum: post-merge CI failure (the field found a masked bug)

After the PR merged, the first *real* GitHub runs of the client jobs failed —
both the v2-tests client job and the deploy build job — with a cascade of
`TS2307: Cannot find module '@realitycollective/service-framework'` errors
from the **library source** the client type-checks via its alias.

**Root cause:** `npm ci` in `quest-client` symlinks the `file:` library but
does **not** install the library's own `node_modules`. Node/TS resolve
imports from a symlink's *real path*, so the framework package must exist
under `com.questvisionstream/` — and on a fresh checkout it doesn't. Every
local verification in this session had passed only because the library's
`node_modules` already existed from an earlier direct `npm ci` there.
Reproduced deterministically by deleting the library's `node_modules` and
running the exact CI steps; fixed by adding an
"Install library dependencies" step (`npm ci` in `V2/com.questvisionstream`)
before the client install in **both** workflows — the same order the V2
monorepo's `install:all` script documents. The server and library CI jobs
were unaffected (each installs its own tree).

## Lessons for the guide

- **"Deferred" should mean "deferred pending a named unblocker", not
  "dropped".** Both API-gated deferrals listed exactly what needed verifying;
  when the unblocker turned out to be sitting in `node_modules`, completion
  was cheap. Write deferrals so the next session can test the unblocker in
  minutes.
- **Check the installed package before trusting a docs-gap.** The reference
  doc said "not fully verified" because the *web docs* 404'd — the shipped
  `.d.ts` files had the authoritative answer all along.
- **Derive what you can from data you already ship.** Absolute latency needs
  clock sync, but latency *variation* falls out of `pts` arithmetic — a
  windowed minimum turns an "impossible" measurement into a useful one.
