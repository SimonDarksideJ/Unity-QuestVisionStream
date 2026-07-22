# Ethar Training State Machine — Python (`com.ethar.trainingstatemachine.python`)

A standalone Python port of the `com.ethar.trainingstatemachine` Unity package —
the same training flow state machine, no service framework, no dependencies
beyond the Python 3.8+ standard library. The API mirrors the C# implementation
1:1 (`TrainingStateMachine`, `TrainingStateMachineConfig`, the JSON wire format
— including the optional per-step `modelRef` model catalog key — the
`APRIL_TAG` class source, and the built-in Ethar demo scenario), so behaviour
is identical across both runtimes.

> This package is a standalone **export for server-side Python use** — it is
> not referenced by the Unity project in any way. 📖 The unified wire-format
> and capability reference for both implementations is
> [`V2/Documentation/Training-Configuration-Reference.md`](../Documentation/Training-Configuration-Reference.md);
> changes must land in both packages and that document together (parity policy).

## Layout

```
training_state_machine/   the package (enums, scenario model, config data,
                          machine, JSON parser, demo library, validation,
                          mermaid + CSV builders)
console.py                interactive console harness
builder.py                training builder CLI (md2json / json2md / csv2md / csv2json)
examples/ethar_demo.json  the demo configuration, serialized (shared wire format)
examples/ethar_demo.md    the same configuration as a mermaid document (builder output)
tests/                    unittest suite (valid / ignore / result processing / builder)
```

## Training builder

**This is THE conversion tool for the project** (one implementation by
design). Author scenarios as mermaid diagrams and convert both ways, or
import from a spreadsheet CSV — every conversion prints a validation report;
the resulting scenario JSON is what the Unity asset imports/exports (dialect
and usage: `V2/Documentation/Training-Builder.md`):

```bash
python builder.py md2json  flow.md       -o scenario.json
python builder.py json2md  scenario.json -o flow.md
python builder.py csv2md   steps.csv     -o flow.md --name "Line 4 Training"
python builder.py csv2json steps.csv     -o scenario.json --config --confidence 0.5
```

## Console harness

Run the engine interactively and drive it by typing class names — exactly what
the semantic detection pipeline would send:

```
python console.py                       # built-in Ethar demo configuration
python console.py examples/ethar_demo.json  # or any config/scenario JSON
```

```
> begin
» step 1/6 activated
  ┌─ STEP 1 OF 6 — Welcome
  │  Ready to begin your Ethar training?
  │  action 0: [Begin]  (press 0)
  └─ expecting 'begintraining'
> person            ← ignored: not the expected class
> press             ← presses [Begin]; the result class flows like a detection
> tv 0.3            ← ignored: below the confidence gate
> tv 0.9            ← advances to Found TV
```

Commands: `begin`, `press [n]`, `reset`, `status`, `help`, `quit`; anything
else is offered to the engine as a detected class (`<class> [confidence]`,
default confidence 1.0).

## Library usage

```python
from training_state_machine import (
    TrainingStateMachine, TrainingClassSource, ethar_demo_config)

machine = TrainingStateMachine(ethar_demo_config())
machine.step_activated.subscribe(
    lambda a: print(f"step {a.step_index + 1}/{a.step_count}: {a.step.title}"))

machine.begin()
result = machine.process_class("tv", 0.92, TrainingClassSource.DETECTION)
```

Configuration is plain serializable data (`TrainingStateMachineConfig`), loaded
from JSON with `try_parse_config` / saved with `config_to_json` — the same wire
format the Unity packages use.

## Tests

```
python -m unittest discover -v
```

The suite validates, using the built-in demo configuration: valid processing
(expected classes advance the queue), ignore processing (unexpected or
low-confidence classes are discarded and counted), and result processing (form
action results flow back through the same path, stale results rejected).
