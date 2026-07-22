# Ethar Training State Machine — Python (`com.ethar.trainingstatemachine.python`)

A standalone Python port of the `com.ethar.trainingstatemachine` Unity package —
the same training flow state machine, no service framework, no dependencies
beyond the Python 3.8+ standard library. The API mirrors the C# implementation
1:1 (`TrainingStateMachine`, `TrainingStateMachineConfig`, the JSON wire format
— including the optional per-step `modelRef` model catalog key — the
`APRIL_TAG` class source, and the built-in Ethar demo scenario), so behaviour
is identical across both runtimes.

## Layout

```
training_state_machine/   the package (enums, scenario model, config data,
                          machine, JSON parser, demo library)
console.py                interactive console harness
examples/ethar_demo.json  the demo configuration, serialized (shared wire format)
tests/                    unittest suite (valid / ignore / result processing)
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
