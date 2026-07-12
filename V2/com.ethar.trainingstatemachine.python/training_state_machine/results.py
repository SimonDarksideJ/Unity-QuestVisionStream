# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

from dataclasses import dataclass
from typing import Optional

from .enums import TrainingClassSource, TrainingProcessOutcome
from .scenario import TrainingStep


@dataclass(frozen=True)
class TrainingStepResult:
    """The custom response class a training form feeds back to the state machine
    when the user presses an action — "the action is complete, here is the
    result". ``result_class`` travels the same path as a detection."""

    #: Index of the step the action was pressed on.
    step_index: int

    #: That step's waiting class (identity check against the current step).
    waiting_class: str

    #: The class to emit — the step's ``result``. Empty completes the scenario.
    result_class: str

    #: Which option was pressed (audit only — every option emits the same result).
    option_label: str


@dataclass(frozen=True)
class TrainingAdvance:
    """The outcome of a class arrival that changed state."""

    #: The newly activated step (None when ``completed``).
    step: Optional[TrainingStep]

    step_index: int

    #: The class that caused the transition.
    arrived_class: str

    source: TrainingClassSource

    #: True when the arrival finished the scenario instead of activating a step.
    completed: bool


@dataclass(frozen=True)
class TrainingStepActivated:
    """A step became active — the payload of ``TrainingStateMachine.step_activated``."""

    step: TrainingStep

    #: 0-based index into the scenario queue.
    step_index: int

    step_count: int

    #: The class that activated the step; empty when activated by ``begin()``.
    arrived_class: str

    source: TrainingClassSource


@dataclass(frozen=True)
class TrainingClassSighting:
    """The active step's detected class was seen — the payload of
    ``TrainingStateMachine.current_class_sighted``."""

    class_name: str
    confidence: float
    source: TrainingClassSource


@dataclass(frozen=True)
class TrainingCompletion:
    """The scenario finished — the payload of ``TrainingStateMachine.scenario_completed``."""

    #: The class that completed the scenario; empty when the final step's action completed it.
    arrived_class: str

    source: TrainingClassSource


@dataclass(frozen=True)
class TrainingProcessResult:
    """The full report for one class arrival offered through
    :meth:`TrainingStateMachine.process_class` — what happened, and the
    transition when one occurred. Hosts use this to enrich their own events
    (e.g. attach detection geometry) without re-deriving machine state."""

    outcome: TrainingProcessOutcome
    class_name: str
    confidence: float
    source: TrainingClassSource

    #: True when the class matched the active step's annotated detection class
    #: (world-label re-sighting).
    sighted_current_class: bool

    #: The transition, when ``outcome`` is ADVANCED or COMPLETED; None otherwise.
    advance: Optional[TrainingAdvance]
