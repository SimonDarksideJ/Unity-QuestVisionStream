# Ethar Training State Machine (`com.ethar.trainingstatemachine`)

An engine-agnostic training flow state machine: a queue of expected semantic
classes driven by detection events. It orchestrates the training procedure —
each step activates when its waiting class arrives, and the machine checks
state (expected class + confidence gate) before any new action is surfaced to
the client. Everything that doesn't match the statically cached expected class
is discarded, keeping the per-detection hot path to a single string comparison.

The package uses **only basic C# types** — no `UnityEngine` references
(`noEngineReferences: true`) — so it can be ported 1:1 to other runtimes. A
standalone Python implementation lives alongside this package in
`com.ethar.trainingstatemachine.python`.

## Concepts

| Type | Role |
| --- | --- |
| `TrainingStateMachineConfig` | Serializable struct configuration input (scenario + minimum detection confidence). |
| `TrainingScenarioData` / `TrainingStepData` | Serializable plain-data scenario snapshot. |
| `TrainingScenario` / `TrainingStep` | Immutable runtime model consumed by the machine. |
| `TrainingStateMachine` | The component: state queue, confidence gate, class processing, events. |
| `TrainingScenarioParser` | JSON parse/serialize for scenarios and configs (wire format). |
| `TrainingScenarioLibrary` | Built-in Ethar demo scenario (also the test configuration). |

## Usage

```csharp
using Ethar.Training;

// Hosts convert their own authoring format (e.g. a Unity ScriptableObject)
// into the serializable config struct for initialization.
var machine = new TrainingStateMachine(new TrainingStateMachineConfig
{
    Scenario = TrainingScenarioLibrary.EtharDemoData(),
    MinimumDetectionConfidence = 0.5f
});

machine.StepActivated += activation =>
    Console.WriteLine($"step {activation.StepIndex + 1}/{activation.StepCount}: {activation.Step.Title}");
machine.ScenarioCompleted += completion => Console.WriteLine("done!");

machine.Begin();

// Feed every detected class through the machine — it checks state before
// sending out a new action; non-matching classes are discarded and counted.
var result = machine.ProcessClass("tv", confidence: 0.92f, TrainingClassSource.Detection);

// Training form actions feed back the step's result class the same way.
var step = machine.CurrentStep;
machine.CompleteStep(new TrainingStepResult(
    machine.CurrentStepIndex, step.WaitingClass, step.Result, step.Options[0]));
```

`ProcessClass` returns a `TrainingProcessResult` describing what happened
(`NotRunning`, `BelowConfidence`, `Ignored`, `StaleStep`, `Advanced`,
`Completed`), whether the active step's annotated detection class was sighted,
and the `TrainingAdvance` when a transition occurred — so hosts can enrich
their own events (e.g. attach detection geometry) without re-deriving state.

## Scenario JSON wire format

```json
{
  "name": "Ethar Training Demo",
  "steps": [
    { "waitingClass": "", "title": "Welcome", "description": "…",
      "options": ["Begin"], "detectedClass": "", "label": "",
      "imageRef": "camera", "result": "begintraining" }
  ]
}
```

A full machine config wraps a scenario:

```json
{ "minimumDetectionConfidence": 0.5, "scenario": { … } }
```

## Tests

EditMode tests live in `Tests/Editor` (Unity Test Runner) and validate, using
the built-in demo configuration: valid processing (expected classes advance the
queue), ignore processing (unexpected classes are discarded), and result
processing (form action results flow back through the same path).
