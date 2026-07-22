# Training Builder — Mermaid ⇄ Configuration ⇄ CSV

Visual authoring for training scenarios: **the mermaid diagram IS the
configuration**. Draw the flow in a markdown document, convert it to the
training configuration; or take any existing configuration and generate the
diagram back for review. A CSV path accelerates imports from spreadsheets.
Every conversion runs a **validation report** (errors, warnings, info) so a
broken chain is caught at the desk, not on the headset.

**There is ONE conversion tool — the Python builder CLI** — deliberately, so
the dialect has a single implementation and nothing to drift against. It is an
editor-independent VS Code tool (Python 3.8+, stdlib only, no Unity, no build
step). **Scenario JSON is the interchange format**: the Unity
`TrainingScenarioAsset` inspector imports/exports exactly that JSON, and the
C# runtime loads it — so the builder output is bit-for-bit what the headset
runs.

```bash
cd V2/com.ethar.trainingstatemachine.python

python builder.py md2json  flow.md       -o scenario.json        # diagram → wire format
python builder.py json2md  scenario.json -o flow.md              # wire format → diagram
python builder.py csv2md   steps.csv     -o flow.md --name "Line 4 Training"
python builder.py csv2json steps.csv     -o scenario.json --config --confidence 0.5

# Exit codes: 0 = OK (warnings allowed), 2 = validation errors (nothing written).
# The validation report prints to stderr.
```

### The Unity round trip (JSON is the seam)

| Direction | Flow |
|---|---|
| **Author → headset** | write `flow.md` (any mermaid-rendering editor: VS Code, GitHub, Obsidian) → `builder.py md2json flow.md -o scenario.json` → **Import JSON…** on the `TrainingScenarioAsset` inspector (or assign at runtime via `ITrainingStateService.LoadScenarioJson`) |
| **Unity asset → diagram** | **Export JSON…** on the asset inspector → `builder.py json2md scenario.json -o flow.md` → review the rendered diagram |
| **Spreadsheet → headset** | `builder.py csv2md steps.csv -o flow.md` → *review the diagram* → `md2json` (or straight `csv2json`) → Import JSON… |

The inspector shows the **same chain validation live** (the shared
`TrainingScenarioValidator` in the C# package mirrors the builder's
`validate_scenario` — the one deliberate piece of C#/Python twinning, because
both sides need to judge a scenario). Library APIs for embedding in other
tooling: Python `try_parse_markdown` / `to_markdown` / `try_parse_csv` /
`validate_scenario`.

`examples/ethar_demo.md` in the Python package is the demo scenario generated
by the builder — a working sample of everything below.

---

## The mermaid dialect

A standard markdown document; the **first `# heading`** is the scenario name,
and the **first ` ```mermaid ` fenced block** is the flow (a `flowchart`
subset — it renders anywhere mermaid renders: GitHub, VS Code, Obsidian…).

```mermaid
flowchart TD
  %% minimumDetectionConfidence: 0.5
  welcome["Welcome|Ready to begin your Ethar training?|!camera"]
  lookfor["Look for a Monitor|Search your space and locate the monitor|!camera"]
  foundtv["Found TV|Great - that is the monitor|@tv: This is a tv|!camera"]
  bridge[" "]
  person["Found Person|Now find a person|@person: This is a person|!camera"]
  station["Station 1|Fit the pump housing|@station1: Station 1|#pumpAssembly"]
  done["Complete|Training complete|!camera"]

  welcome -->|Begin @begintraining| lookfor
  lookfor -->|Search @tv| foundtv
  foundtv -->|Next @foundtv| bridge
  bridge -->|@person| person
  person -->|Continue @station1| station
  station -->|Finish @finishtraining| done
  done -->|End| END
```

### Nodes = steps (in first-appearance order)

Node text is split on `|` into segments:

| Segment | Meaning |
|---|---|
| 1st | `title` — empty (`" "` or a leading `\|`) makes a **pass-through** step |
| 2nd (plain) | `description` |
| `@class: text` | `detectedClass` = `class`, world `label` = `text` (`@class` alone sets only the class) |
| `#key` | `modelRef` — the model catalog key spawned aligned to the step's tag |
| `!ref` | `imageRef` — the form image reference |
| `?class` | explicit `waitingClass` override — only needed for steps no edge reaches (the export emits it automatically in that case) |

### Edges = flow

| Edge | Meaning |
|---|---|
| `a -->\|Begin\| b` | Action button **Begin**; the result class is **minted** from the target node id (here `b`) — use meaningful node ids |
| `a -->\|@tv\| b` | **Detection-advanced**: result class `tv`, no button — only the detector (or a bridged AprilTag) advances it |
| `a -->\|Search @tv\| b` | Button **Search** *and* result class `tv` — pressing Search emits `tv` as a manual override, and a real `tv` detection advances it too (the demo's pattern) |
| `a -->\|Go, Skip\| b` | Two buttons, both emitting the same result (the current model's rule) |
| `a -->\|End\| END` | Edge to the `END` pseudo-node = final step (empty result) |

Each target's `waitingClass` is derived from its incoming edge's class, so the
chain rule (*each step's result is a later step's waiting class*) holds by
construction. `%% minimumDetectionConfidence: 0.5` inside the block sets the
config's confidence gate. Ordinary mermaid styling (`classDef`, `style`,
`linkStyle`, `direction`) is ignored on import, so diagrams can be prettified
freely.

### What the dialect cannot express (by design, today)

The runtime model is a **linear, forward-only queue** — so the dialect rejects
with an **error**:

- **Branching** — one node with edges to different targets. Per-option results
  are the planned schema-v2 extension (see the delivery plan).
- **Merges with conflicting classes** — two edges into one node must carry the
  same class (forward *skips* to the same class are fine).

Loops/backward edges aren't expressible either (the machine only scans
forward). Titles/labels: `"` becomes `'` and `|` becomes `/` on export; class
tokens must be single words.

---

## CSV import (spreadsheet accelerator)

One header row + one row per step. Column matching is case-insensitive and
ignores spaces/underscores (`Waiting Class` = `waiting_class` = `waitingClass`):

```csv
waitingClass,title,description,options,detectedClass,label,imageRef,modelRef,result
,Welcome,Ready to begin?,Begin,,,camera,,begintraining
begintraining,Look for a Monitor,Search your space,Search,,,camera,,tv
tv,Found TV,That is the monitor,Next,tv,This is a tv,camera,,foundtv
foundtv,,,,,,,,person
person,Found Person,Now find a person,Finish,person,This is a person,camera,,finishtraining
finishtraining,Complete,Training complete,End,,,camera,,
```

- `options` are `;`-separated inside the cell (the CSV comma stays the field
  delimiter). Quoted cells, embedded commas and newlines are handled.
- Unknown columns are reported as **info** and ignored (spreadsheet leftovers
  are fine); a missing `waitingClass`/`result` column is a **warning**.
- Recommended flow: `csv2md` first, **review the rendered diagram**, then
  import/convert — the diagram is the verification step.

## The validation report

Every conversion ends with the chain validator (`validate_scenario`; mirrored
as `TrainingScenarioValidator` for the Unity inspector's live checks):

| Level | Examples |
|---|---|
| **ERROR** (nothing produced) | no mermaid block, unrecognised line, branching, conflicting merge classes, no recognised CSV columns |
| **WARNING** (loads, but check it) | unreachable step (no waiting class / never targeted), early completion (empty result mid-queue), dead-end result, missing CSV column |
| **INFO** | ignored spreadsheet columns, the resolved expected-class queue (`begin → begintraining → tv → …`) |

## Round-trip guarantee

`config → markdown → config` is lossless (pinned by the builder test suite,
including `modelRef`, pass-through steps and empty-title+description).
`markdown → config → markdown` normalizes node ids to `s1…sN` and always
writes explicit `@result` tokens — semantically identical, textually
canonical.

Cross-references: [Training-Configuration-Reference.md](Training-Configuration-Reference.md)
(the wire format the builder targets), [Training-Flow.md](Training-Flow.md)
(how the Unity client runs the result), and
[V2-Review-and-Delivery-Plan-2026-07.md](V2-Review-and-Delivery-Plan-2026-07.md)
(Phase 3b: per-option results, loops — the dialect grows with the schema).
