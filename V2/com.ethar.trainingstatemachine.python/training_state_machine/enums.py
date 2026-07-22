# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

from enum import Enum


class TrainingClassSource(Enum):
    """Where a class arrival came from."""

    #: A real detection from the semantic detection pipeline.
    DETECTION = 0

    #: The synthetic "detected class from pressing an action" on a training form.
    ACTION_RESPONSE = 1

    #: An on-device fiducial sighting (e.g. AprilTag) bridged into the class
    #: pipeline. Deterministic — always full confidence, never gated by
    #: ``minimum_detection_confidence``.
    APRIL_TAG = 2


class TrainingFlowStatus(Enum):
    """Lifecycle of a loaded scenario."""

    #: No scenario loaded, or loaded but not begun.
    IDLE = 0

    #: A step is active and the machine is waiting for its expected class.
    RUNNING = 1

    #: The final step's action fired — the procedure is done.
    COMPLETED = 2


class TrainingProcessOutcome(Enum):
    """The outcome of offering a single class arrival to the state machine."""

    #: The machine is idle or completed — everything is discarded.
    NOT_RUNNING = 0

    #: A detection below the configured minimum confidence — discarded.
    BELOW_CONFIDENCE = 1

    #: The class did not match the expected state — discarded.
    IGNORED = 2

    #: A step result for a step that is no longer active — discarded.
    STALE_STEP = 3

    #: The expected class arrived and activated the next step.
    ADVANCED = 4

    #: The arrival (or final action) completed the scenario.
    COMPLETED = 5
