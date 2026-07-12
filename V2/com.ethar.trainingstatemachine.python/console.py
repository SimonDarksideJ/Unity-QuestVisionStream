# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Interactive console harness for the Ethar Training State Machine.

Runs the same configuration as the Unity client (the built-in Ethar demo
scenario, or a config/scenario JSON file) and lets you drive the engine by
typing class names - exactly what the semantic detection pipeline would send.

Usage:
    python console.py [path/to/config.json]

Commands:
    begin              start (or restart) the scenario queue
    press [n]          press the active step's action button n (default 0)
    reset              back to idle, keeping the scenario
    status             show the machine state
    help               show this help
    quit / exit        leave
    <class> [conf]     anything else is offered to the engine as a detected
                       class, e.g. "tv" or "tv 0.42" (default confidence 1.0)
"""

import sys

from training_state_machine import (
    TrainingClassSource,
    TrainingProcessOutcome,
    TrainingStateMachine,
    TrainingStepResult,
    ethar_demo_config,
    try_parse_config,
    try_parse_scenario,
)
from training_state_machine.config import TrainingScenarioData, TrainingStateMachineConfig


def load_config(path):
    """Accept either a full machine config or a bare scenario JSON file."""
    with open(path, encoding="utf-8") as handle:
        text = handle.read()

    ok, config = try_parse_config(text)
    if ok:
        return config

    ok, scenario = try_parse_scenario(text)
    if ok:
        return TrainingStateMachineConfig(
            scenario=TrainingScenarioData.from_scenario(scenario),
            minimum_detection_confidence=0.5,
        )

    raise ValueError(f"'{path}' is neither a machine config nor a scenario JSON file")


def describe_step(machine):
    step = machine.current_step
    if step is None:
        return
    if not step.has_presentation:
        print(f"  (pass-through step - waiting for '{machine.expected_class}')")
        return

    print(f"  +- STEP {machine.current_step_index + 1} OF {len(machine.scenario.steps)} -- {step.title}")
    if step.description:
        print(f"  |  {step.description}")
    if step.has_world_label:
        print(f"  |  [world label on '{step.detected_class}': \"{step.label}\"]")
    for i, option in enumerate(step.options):
        print(f"  |  action {i}: [{option}]  (press {i})")
    if machine.expected_class:
        print(f"  +- expecting '{machine.expected_class}'")
    else:
        print("  +- final step - press its action to complete")


def print_status(machine):
    print(f"  scenario: {machine.scenario.name if machine.scenario else '(none)'}")
    print(f"  status: {machine.status.name}, step index: {machine.current_step_index}, "
          f"expecting: '{machine.expected_class}', discarded: {machine.discarded_count}")


def main(argv):
    if len(argv) > 1:
        try:
            config = load_config(argv[1])
        except (OSError, ValueError) as error:
            print(f"error: {error}")
            return 1
        print(f"Loaded configuration from {argv[1]}")
    else:
        config = ethar_demo_config()
        print("Loaded the built-in Ethar demo configuration")

    machine = TrainingStateMachine(config)

    machine.scenario_loaded.subscribe(
        lambda scenario: print(f"* scenario '{scenario.name}' loaded ({len(scenario.steps)} steps)"))
    machine.step_activated.subscribe(
        lambda activated: (
            print(f"* step {activated.step_index + 1}/{activated.step_count} activated"
                  + (f" by '{activated.arrived_class}' ({activated.source.name})" if activated.arrived_class else "")),
            describe_step(machine),
        ))
    machine.current_class_sighted.subscribe(
        lambda sighting: print(f"* sighted current class '{sighting.class_name}' (conf {sighting.confidence:.2f}) - world label refresh"))
    machine.scenario_completed.subscribe(
        lambda completion: print("* scenario COMPLETE"
                                 + (f" on '{completion.arrived_class}'" if completion.arrived_class else " (final action)")))

    print(f"* scenario '{machine.scenario.name}' ready ({len(machine.scenario.steps)} steps), "
          f"confidence gate {machine.minimum_detection_confidence:.2f}")
    print("Type 'begin' to start, 'help' for commands.")

    while True:
        try:
            line = input("> ").strip()
        except EOFError:
            break

        if not line:
            continue

        parts = line.split()
        command = parts[0].lower()

        if command in ("quit", "exit"):
            break

        if command == "help":
            print(__doc__)
            continue

        if command == "begin":
            if machine.status.name != "IDLE":
                machine.reset()
            machine.begin()
            continue

        if command == "reset":
            machine.reset()
            print("* reset to idle")
            continue

        if command == "status":
            print_status(machine)
            continue

        if command == "press":
            step = machine.current_step
            if step is None:
                print("  no active step - 'begin' first")
                continue
            index = 0
            if len(parts) > 1:
                try:
                    index = int(parts[1])
                except ValueError:
                    print(f"  '{parts[1]}' is not an option index")
                    continue
            option = step.options[index] if 0 <= index < len(step.options) else ""
            result = machine.complete_step(TrainingStepResult(
                machine.current_step_index, step.waiting_class, step.result, option))
            if result.outcome not in (TrainingProcessOutcome.ADVANCED, TrainingProcessOutcome.COMPLETED):
                print(f"  press -> {result.outcome.name}")
            continue

        # Anything else is a class name, optionally followed by a confidence.
        class_name = parts[0]
        confidence = 1.0
        if len(parts) > 1:
            try:
                confidence = float(parts[1])
            except ValueError:
                print(f"  '{parts[1]}' is not a confidence value")
                continue

        result = machine.process_class(class_name, confidence, TrainingClassSource.DETECTION)
        if result.outcome == TrainingProcessOutcome.IGNORED:
            print(f"  '{class_name}' ignored (expecting '{machine.expected_class or '-'}', "
                  f"discarded {machine.discarded_count})")
        elif result.outcome == TrainingProcessOutcome.BELOW_CONFIDENCE:
            print(f"  '{class_name}' below the {machine.minimum_detection_confidence:.2f} confidence gate")
        elif result.outcome == TrainingProcessOutcome.NOT_RUNNING:
            print("  machine is not running - 'begin' first")

    print("bye")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
