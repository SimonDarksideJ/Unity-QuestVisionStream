# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Ethar Training State Machine — standalone Python port of the
``com.ethar.trainingstatemachine`` Unity package.

A training flow state machine: a queue of expected semantic classes driven by
detection events. Configured from plain serializable data, no framework
dependencies — the API mirrors the C# implementation 1:1.
"""

from .enums import TrainingClassSource, TrainingFlowStatus, TrainingProcessOutcome
from .scenario import TrainingStep, TrainingScenario
from .config import TrainingStepData, TrainingScenarioData, TrainingStateMachineConfig
from .results import (
    TrainingStepResult,
    TrainingAdvance,
    TrainingStepActivated,
    TrainingClassSighting,
    TrainingCompletion,
    TrainingProcessResult,
)
from .machine import TrainingStateMachine
from .parser import (
    try_parse_scenario,
    scenario_to_json,
    try_parse_config,
    config_to_json,
)
from .library import ethar_demo, ethar_demo_data, ethar_demo_config
from .validation import (
    ValidationSeverity,
    ValidationMessage,
    ValidationReport,
    validate_scenario,
)
from .mermaid import try_parse_markdown, to_markdown
from .csv_io import try_parse_csv

__all__ = [
    "TrainingClassSource",
    "TrainingFlowStatus",
    "TrainingProcessOutcome",
    "TrainingStep",
    "TrainingScenario",
    "TrainingStepData",
    "TrainingScenarioData",
    "TrainingStateMachineConfig",
    "TrainingStepResult",
    "TrainingAdvance",
    "TrainingStepActivated",
    "TrainingClassSighting",
    "TrainingCompletion",
    "TrainingProcessResult",
    "TrainingStateMachine",
    "try_parse_scenario",
    "scenario_to_json",
    "try_parse_config",
    "config_to_json",
    "ethar_demo",
    "ethar_demo_data",
    "ethar_demo_config",
    "ValidationSeverity",
    "ValidationMessage",
    "ValidationReport",
    "validate_scenario",
    "try_parse_markdown",
    "to_markdown",
    "try_parse_csv",
]

__version__ = "1.0.0"
