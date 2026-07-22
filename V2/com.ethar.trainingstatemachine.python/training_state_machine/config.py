# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

from dataclasses import dataclass, field
from typing import List

from .scenario import TrainingScenario, TrainingStep


@dataclass
class TrainingStepData:
    """Serializable snapshot of one scenario step — the plain-data mirror of the
    immutable :class:`TrainingStep` (the C# ``TrainingStepData`` struct)."""

    waiting_class: str = ""
    title: str = ""
    description: str = ""
    options: List[str] = field(default_factory=list)
    detected_class: str = ""
    label: str = ""
    image_ref: str = ""
    result: str = ""
    model_ref: str = ""

    def to_step(self) -> TrainingStep:
        """Build the immutable runtime step (None fields default to empty)."""
        return TrainingStep(
            waiting_class=self.waiting_class or "",
            title=self.title or "",
            description=self.description or "",
            options=tuple(self.options or ()),
            detected_class=self.detected_class or "",
            label=self.label or "",
            image_ref=self.image_ref or "",
            result=self.result or "",
            model_ref=self.model_ref or "",
        )

    @staticmethod
    def from_step(step: TrainingStep) -> "TrainingStepData":
        """Snapshot a runtime step back into plain data (options copied)."""
        return TrainingStepData(
            waiting_class=step.waiting_class,
            title=step.title,
            description=step.description,
            options=list(step.options),
            detected_class=step.detected_class,
            label=step.label,
            image_ref=step.image_ref,
            result=step.result,
            model_ref=step.model_ref,
        )


@dataclass
class TrainingScenarioData:
    """Serializable snapshot of a whole scenario queue."""

    name: str = ""
    steps: List[TrainingStepData] = field(default_factory=list)

    @property
    def has_steps(self) -> bool:
        return bool(self.steps)

    def to_scenario(self) -> TrainingScenario:
        """Build the immutable runtime scenario the state machine consumes."""
        return TrainingScenario(
            name=self.name or "",
            steps=tuple(step.to_step() for step in (self.steps or ())),
        )

    @staticmethod
    def from_scenario(scenario: TrainingScenario) -> "TrainingScenarioData":
        """Snapshot a runtime scenario back into plain data."""
        return TrainingScenarioData(
            name=scenario.name,
            steps=[TrainingStepData.from_step(step) for step in scenario.steps],
        )


@dataclass
class TrainingStateMachineConfig:
    """The complete, serializable configuration input for
    :meth:`TrainingStateMachine.initialize`. Hosts keep their own authoring
    format and convert to this structure for initialization (the C#
    ``TrainingStateMachineConfig`` struct)."""

    #: The scenario queue to run.
    scenario: TrainingScenarioData = field(default_factory=TrainingScenarioData)

    #: Detections below this confidence (0..1) never advance the flow.
    #: Synthetic action responses always pass.
    minimum_detection_confidence: float = 0.0
