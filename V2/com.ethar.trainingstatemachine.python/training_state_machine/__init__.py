# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Ethar Training State Machine — builder toolchain for the
``com.ethar.trainingstatemachine`` Unity package.

The scenario data model, validation, and the conversion formats (JSON wire
format, mermaid markdown dialect, spreadsheet CSV) behind ``builder.py``.
Plain serializable data, no framework dependencies — the dialects mirror the
C# implementation 1:1.
"""

from .enums import TrainingClassSource, TrainingFlowStatus, TrainingProcessOutcome
from .scenario import TrainingStep, TrainingScenario
from .config import TrainingStepData, TrainingScenarioData, TrainingStateMachineConfig
from .parser import (
    try_parse_scenario,
    scenario_to_json,
    try_parse_config,
    config_to_json,
)
from .validation import (
    ValidationSeverity,
    ValidationMessage,
    ValidationReport,
    validate_scenario,
)
from .mermaid import try_parse_markdown, to_markdown
from .csv_io import try_parse_csv, to_csv

__all__ = [
    "TrainingClassSource",
    "TrainingFlowStatus",
    "TrainingProcessOutcome",
    "TrainingStep",
    "TrainingScenario",
    "TrainingStepData",
    "TrainingScenarioData",
    "TrainingStateMachineConfig",
    "try_parse_scenario",
    "scenario_to_json",
    "try_parse_config",
    "config_to_json",
    "ValidationSeverity",
    "ValidationMessage",
    "ValidationReport",
    "validate_scenario",
    "try_parse_markdown",
    "to_markdown",
    "try_parse_csv",
    "to_csv",
]

__version__ = "1.0.0"
