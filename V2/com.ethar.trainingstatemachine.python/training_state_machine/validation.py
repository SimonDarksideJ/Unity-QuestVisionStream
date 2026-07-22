# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Scenario validation: static checks mirroring how the state machine walks the
queue, so authoring mistakes surface in a report instead of on device. A 1:1
behavioural twin of the C# ``TrainingScenarioValidator``; the Unity inspector
runs the same checks live."""

from dataclasses import dataclass, field
from enum import Enum
from typing import List

from .scenario import TrainingScenario


class ValidationSeverity(Enum):
    INFO = 0
    WARNING = 1
    ERROR = 2


@dataclass(frozen=True)
class ValidationMessage:
    severity: ValidationSeverity
    message: str

    def __str__(self) -> str:
        return f"[{self.severity.name}] {self.message}"


@dataclass
class ValidationReport:
    """An ordered list of findings. Errors mean the input could not produce a
    usable scenario; warnings mean the scenario loads but the chain has holes;
    info lines aid verification."""

    messages: List[ValidationMessage] = field(default_factory=list)

    @property
    def has_errors(self) -> bool:
        return any(m.severity == ValidationSeverity.ERROR for m in self.messages)

    @property
    def has_warnings(self) -> bool:
        return any(m.severity == ValidationSeverity.WARNING for m in self.messages)

    def error(self, message: str) -> None:
        self.messages.append(ValidationMessage(ValidationSeverity.ERROR, message))

    def warning(self, message: str) -> None:
        self.messages.append(ValidationMessage(ValidationSeverity.WARNING, message))

    def info(self, message: str) -> None:
        self.messages.append(ValidationMessage(ValidationSeverity.INFO, message))

    def __str__(self) -> str:
        return "\n".join(str(m) for m in self.messages) if self.messages else "No findings."


def validate_scenario(scenario: TrainingScenario, report: ValidationReport = None) -> ValidationReport:
    """Chain-rule checks, mirroring the machine's forward-only ``offer`` walk:
    unreachable steps, early completion, dead-end results, plus the resolved
    expected-class queue as an info line."""
    report = report if report is not None else ValidationReport()
    steps = scenario.steps

    if not steps:
        report.warning("Scenario has no steps.")
        return report

    if steps[0].waiting_class:
        report.warning(
            f"Step 1 waits for '{steps[0].waiting_class}', but the entry step is "
            "activated by begin — its waiting class is ignored.")

    for i, step in enumerate(steps):
        step_name = step.title if step.title else f"step {i + 1}"

        if i > 0 and not step.waiting_class:
            report.warning(f"'{step_name}' has no waiting class — no arrival can ever activate it.")

        if not step.result:
            if i < len(steps) - 1:
                report.warning(
                    f"'{step_name}' has an empty result — pressing its action completes the "
                    f"scenario, so the {len(steps) - 1 - i} step(s) after it are unreachable.")
            continue

        resolved = any(
            steps[j].waiting_class.casefold() == step.result.casefold()
            for j in range(i + 1, len(steps)))
        if not resolved:
            message = (
                f"'{step_name}' expects '{step.result}', which matches no later step's "
                "waiting class — its arrival will end the scenario there.")
            if i < len(steps) - 1:
                report.warning(message)
            else:
                report.info(message)

    queue = " → ".join(step.result if step.result else "complete" for step in steps)
    report.info(f"Expected class queue: begin → {queue}.")
    return report
