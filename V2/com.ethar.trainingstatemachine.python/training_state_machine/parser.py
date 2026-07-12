# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Parser + serializer for the scenario queue's JSON format, and for the
serializable :class:`TrainingStateMachineConfig` that wraps it. Tolerant of
missing fields (they default to empty — the wire rule used across the project),
strict about the overall shape: a scenario must carry at least one step, and
every step must be an object. The wire format is identical to the C# package's
``TrainingScenarioParser``."""

import json
from typing import Optional, Tuple

from .config import TrainingScenarioData, TrainingStateMachineConfig
from .scenario import TrainingScenario, TrainingStep


def try_parse_scenario(text: Optional[str]) -> Tuple[bool, Optional[TrainingScenario]]:
    """Parse scenario JSON. Returns ``(False, None)`` on malformed input."""
    if not text:
        return False, None

    try:
        root = json.loads(text)
    except (json.JSONDecodeError, TypeError):
        return False, None

    if not isinstance(root, dict):
        return False, None

    return _read_scenario(root)


def scenario_to_json(scenario: TrainingScenario, indented: bool = True) -> str:
    """Serialize a scenario back to its JSON queue format."""
    return json.dumps(_write_scenario(scenario), indent=2 if indented else None)


def try_parse_config(text: Optional[str]) -> Tuple[bool, Optional[TrainingStateMachineConfig]]:
    """Parse a full machine configuration: ``{ "minimumDetectionConfidence": 0.5,
    "scenario": { … } }``. Returns ``(False, None)`` on malformed input."""
    if not text:
        return False, None

    try:
        root = json.loads(text)
    except (json.JSONDecodeError, TypeError):
        return False, None

    if not isinstance(root, dict) or not isinstance(root.get("scenario"), dict):
        return False, None

    ok, scenario = _read_scenario(root["scenario"])
    if not ok:
        return False, None

    confidence = root.get("minimumDetectionConfidence", 0.0)
    if not isinstance(confidence, (int, float)) or isinstance(confidence, bool):
        confidence = 0.0

    return True, TrainingStateMachineConfig(
        scenario=TrainingScenarioData.from_scenario(scenario),
        minimum_detection_confidence=float(confidence),
    )


def config_to_json(config: TrainingStateMachineConfig, indented: bool = True) -> str:
    """Serialize a machine configuration to JSON."""
    root = {
        "minimumDetectionConfidence": config.minimum_detection_confidence,
        "scenario": _write_scenario(config.scenario.to_scenario()),
    }
    return json.dumps(root, indent=2 if indented else None)


def _string(value) -> str:
    return value if isinstance(value, str) else ""


def _read_scenario(root: dict) -> Tuple[bool, Optional[TrainingScenario]]:
    steps_array = root.get("steps")
    if not isinstance(steps_array, list) or len(steps_array) == 0:
        return False, None

    steps = []
    for item in steps_array:
        if not isinstance(item, dict):
            return False, None

        options = []
        raw_options = item.get("options")
        if isinstance(raw_options, list):
            options = [option for option in raw_options if isinstance(option, str)]

        steps.append(TrainingStep(
            waiting_class=_string(item.get("waitingClass")),
            title=_string(item.get("title")),
            description=_string(item.get("description")),
            options=tuple(options),
            detected_class=_string(item.get("detectedClass")),
            label=_string(item.get("label")),
            image_ref=_string(item.get("imageRef")),
            result=_string(item.get("result")),
        ))

    return True, TrainingScenario(name=_string(root.get("name")), steps=tuple(steps))


def _write_scenario(scenario: TrainingScenario) -> dict:
    return {
        "name": scenario.name,
        "steps": [
            {
                "waitingClass": step.waiting_class,
                "title": step.title,
                "description": step.description,
                "options": list(step.options),
                "detectedClass": step.detected_class,
                "label": step.label,
                "imageRef": step.image_ref,
                "result": step.result,
            }
            for step in scenario.steps
        ],
    }
