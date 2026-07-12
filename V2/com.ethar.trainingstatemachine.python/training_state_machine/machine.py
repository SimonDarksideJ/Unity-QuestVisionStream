# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

from typing import Callable, List, Optional

from .config import TrainingStateMachineConfig
from .enums import TrainingClassSource, TrainingFlowStatus, TrainingProcessOutcome
from .results import (
    TrainingAdvance,
    TrainingClassSighting,
    TrainingCompletion,
    TrainingProcessResult,
    TrainingStepActivated,
    TrainingStepResult,
)
from .scenario import TrainingScenario, TrainingStep


class Event:
    """A minimal multicast event, mirroring C# ``event Action<T>``."""

    def __init__(self) -> None:
        self._handlers: List[Callable] = []

    def subscribe(self, handler: Callable) -> None:
        self._handlers.append(handler)

    def unsubscribe(self, handler: Callable) -> None:
        self._handlers.remove(handler)

    def fire(self, payload) -> None:
        for handler in list(self._handlers):
            handler(payload)


def _classes_equal(a: str, b: str) -> bool:
    # OrdinalIgnoreCase in the C# implementation.
    return a.casefold() == b.casefold()


class TrainingStateMachine:
    """The training flow state machine: a queue of expected classes fed by a
    semantic detection pipeline. Exactly one step is active at a time; the ONLY
    class that can move the queue is the active step's ``result``, cached in
    :attr:`expected_class` so the per-detection hot path is a single string
    comparison — no scenario table lookups. Everything else is discarded.

    A 1:1 port of the C# ``Ethar.Training.TrainingStateMachine``.
    """

    def __init__(self, config: Optional[TrainingStateMachineConfig] = None) -> None:
        #: A scenario was (re)loaded. Handlers receive a TrainingScenario.
        self.scenario_loaded = Event()

        #: A step became active — via begin() or an expected class arrival.
        #: Handlers receive a TrainingStepActivated.
        self.step_activated = Event()

        #: The active step's detected class was seen again.
        #: Handlers receive a TrainingClassSighting.
        self.current_class_sighted = Event()

        #: The scenario finished — terminal arrival or final step's action.
        #: Handlers receive a TrainingCompletion.
        self.scenario_completed = Event()

        self.scenario: Optional[TrainingScenario] = None
        self.status = TrainingFlowStatus.IDLE

        #: Active step index, or -1 when idle/completed.
        self.current_step_index = -1

        #: The next class the queue is waiting for — the statically cached copy of
        #: the active step's result. Empty when idle, completed, or on a final step.
        self.expected_class = ""

        #: Cached copy of the active step's detected class, for world-label re-sightings.
        self.current_detected_class = ""

        #: Detections below this confidence (0..1) are discarded by process_class().
        #: Action responses always pass.
        self.minimum_detection_confidence = 0.0

        #: Arrivals discarded because they didn't match the expected state.
        self.discarded_count = 0

        if config is not None:
            self.initialize(config)

    @property
    def current_step(self) -> Optional[TrainingStep]:
        if self.scenario is not None and 0 <= self.current_step_index < len(self.scenario.steps):
            return self.scenario.steps[self.current_step_index]
        return None

    @property
    def is_running(self) -> bool:
        return self.status == TrainingFlowStatus.RUNNING

    def initialize(self, config: TrainingStateMachineConfig) -> None:
        """Configure from the serializable data: applies the confidence gate and
        loads the scenario (resetting to IDLE)."""
        self.minimum_detection_confidence = min(max(config.minimum_detection_confidence, 0.0), 1.0)
        self.load(config.scenario.to_scenario())

    def load(self, scenario: TrainingScenario) -> None:
        """Load a scenario and reset to IDLE."""
        if scenario is None:
            raise ValueError("scenario must not be None")
        self.scenario = scenario
        self.reset()
        self.scenario_loaded.fire(scenario)

    def reset(self) -> None:
        """Back to idle; the loaded scenario and configuration are kept."""
        self.status = TrainingFlowStatus.IDLE
        self.current_step_index = -1
        self.expected_class = ""
        self.current_detected_class = ""
        self.discarded_count = 0

    def begin(self) -> bool:
        """Start the queue: activates the entry step (index 0)."""
        if self.scenario is None or len(self.scenario.steps) == 0:
            return False

        self.status = TrainingFlowStatus.RUNNING
        self._activate(0)
        self.step_activated.fire(TrainingStepActivated(
            step=self.current_step,
            step_index=0,
            step_count=len(self.scenario.steps),
            arrived_class="",
            source=TrainingClassSource.ACTION_RESPONSE,
        ))
        return True

    def matches_expected(self, class_name: str) -> bool:
        """The single hot-path check: is this the class the queue is waiting for?"""
        return (
            self.is_running
            and len(self.expected_class) > 0
            and _classes_equal(self.expected_class, class_name)
        )

    def matches_current_detected_class(self, class_name: str) -> bool:
        """Does this class match the active step's annotated detection class?"""
        return (
            self.is_running
            and len(self.current_detected_class) > 0
            and _classes_equal(self.current_detected_class, class_name)
        )

    def process_class(
        self,
        class_name: str,
        confidence: float,
        source: TrainingClassSource,
    ) -> TrainingProcessResult:
        """The single class-arrival entry point: applies the confidence gate
        (detections only), reports re-sightings of the active step's detected
        class, and offers the class to the queue. This is the check performed
        before any new action is sent to the client — non-matching classes are
        discarded and counted."""
        if not self.is_running:
            return TrainingProcessResult(
                TrainingProcessOutcome.NOT_RUNNING, class_name, confidence, source, False, None)

        minimum = self.minimum_detection_confidence if source == TrainingClassSource.DETECTION else 0.0
        if confidence < minimum:
            return TrainingProcessResult(
                TrainingProcessOutcome.BELOW_CONFIDENCE, class_name, confidence, source, False, None)

        # Keep the world label tracking the active step's annotated class.
        sighted = self.matches_current_detected_class(class_name)
        if sighted:
            self.current_class_sighted.fire(TrainingClassSighting(class_name, confidence, source))

        if not self.matches_expected(class_name):
            self.discarded_count += 1
            return TrainingProcessResult(
                TrainingProcessOutcome.IGNORED, class_name, confidence, source, sighted, None)

        advance = self.offer(class_name, source)
        if advance is None:
            self.discarded_count += 1
            return TrainingProcessResult(
                TrainingProcessOutcome.IGNORED, class_name, confidence, source, sighted, None)

        outcome = (
            TrainingProcessOutcome.COMPLETED if advance.completed
            else TrainingProcessOutcome.ADVANCED
        )
        return TrainingProcessResult(outcome, class_name, confidence, source, sighted, advance)

    def complete_step(self, result: Optional[TrainingStepResult]) -> TrainingProcessResult:
        """A training form action completed — feed the result back. Stale results
        (for a step no longer active) are rejected. An empty result class
        completes the scenario (final step); otherwise the result class flows
        through :meth:`process_class` exactly like a detection."""
        if result is None or not self.is_running:
            return TrainingProcessResult(
                TrainingProcessOutcome.NOT_RUNNING,
                result.result_class if result is not None else "",
                1.0, TrainingClassSource.ACTION_RESPONSE, False, None)

        if result.step_index != self.current_step_index:
            return TrainingProcessResult(
                TrainingProcessOutcome.STALE_STEP, result.result_class,
                1.0, TrainingClassSource.ACTION_RESPONSE, False, None)

        if len(result.result_class) == 0:
            # Final step — there is no class to flow, the action itself completes.
            advance = self.complete_scenario(TrainingClassSource.ACTION_RESPONSE)
            if advance is None:
                return TrainingProcessResult(
                    TrainingProcessOutcome.IGNORED, "",
                    1.0, TrainingClassSource.ACTION_RESPONSE, False, None)
            return TrainingProcessResult(
                TrainingProcessOutcome.COMPLETED, "",
                1.0, TrainingClassSource.ACTION_RESPONSE, False, advance)

        return self.process_class(result.result_class, 1.0, TrainingClassSource.ACTION_RESPONSE)

    def offer(self, class_name: str, source: TrainingClassSource) -> Optional[TrainingAdvance]:
        """Offer an arrived class to the queue. Non-matching classes are discarded
        (returns None). A match advances to the next step waiting on that class —
        or completes the scenario when no such step remains."""
        if not self.matches_expected(class_name):
            return None

        # Move next: read the queue for the step this class activates.
        for i in range(self.current_step_index + 1, len(self.scenario.steps)):
            if _classes_equal(self.scenario.steps[i].waiting_class, class_name):
                self._activate(i)
                self.step_activated.fire(TrainingStepActivated(
                    step=self.scenario.steps[i],
                    step_index=i,
                    step_count=len(self.scenario.steps),
                    arrived_class=class_name,
                    source=source,
                ))
                return TrainingAdvance(self.scenario.steps[i], i, class_name, source, completed=False)

        # Expected class with no queued step — treat as terminal.
        return self._complete_internal(class_name, source)

    def complete_scenario(self, source: TrainingClassSource) -> Optional[TrainingAdvance]:
        """Complete the active FINAL step (its result is empty, so no class flows).
        Non-final steps must advance via :meth:`offer` instead."""
        if not self.is_running or len(self.expected_class) > 0:
            return None

        return self._complete_internal("", source)

    def _complete_internal(self, arrived_class: str, source: TrainingClassSource) -> TrainingAdvance:
        self.status = TrainingFlowStatus.COMPLETED
        self.current_step_index = -1
        self.expected_class = ""
        self.current_detected_class = ""
        self.scenario_completed.fire(TrainingCompletion(arrived_class, source))
        return TrainingAdvance(None, -1, arrived_class, source, completed=True)

    def _activate(self, index: int) -> None:
        self.current_step_index = index
        step = self.scenario.steps[index]

        # Receive correct state, move next, read next expected state, store for
        # comparison — the static cache that keeps the detection hot path cheap.
        self.expected_class = step.result
        self.current_detected_class = step.detected_class
