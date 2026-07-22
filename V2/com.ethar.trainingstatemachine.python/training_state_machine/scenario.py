# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

from dataclasses import dataclass, field
from typing import Tuple


@dataclass(frozen=True)
class TrainingStep:
    """One entry in a training scenario's queue of expected states.

    A step ACTIVATES when its ``waiting_class`` arrives on the class pipeline —
    either a real detection from the detector ("tv", "person") or a synthetic
    class produced by pressing an action on the previous step's form
    ("begintraining", "foundtv"). While active, the step's ``result`` is the
    single statically-cached class the state machine waits for next.
    """

    #: The class whose arrival activates this step. Empty on the entry step (activated by begin).
    waiting_class: str = ""

    #: Form headline. A step with no title and no options is a pass-through: no UX is shown.
    title: str = ""

    #: Form body text.
    description: str = ""

    #: Action labels. Pressing any option sends ``result`` down the class pipeline (default one).
    options: Tuple[str, ...] = ()

    #: Detection class to annotate in the world while this step is active (empty = none).
    detected_class: str = ""

    #: Text for the world label + connector placed at the detected box centre.
    label: str = ""

    #: Client-side image reference for the form (a shared placeholder image for now).
    image_ref: str = ""

    #: The next expected class. Arrives either as a real detection or as the
    #: synthetic "detected class from pressing an action". Empty on the final
    #: step — pressing its action completes the scenario.
    result: str = ""

    #: Host-resolved model reference to instantiate while this step is active,
    #: aligned to the physical marker (e.g. AprilTag) whose class name matches
    #: ``detected_class`` (falling back to ``waiting_class``). An opaque catalog
    #: key like ``image_ref`` — the state machine never interprets it; the
    #: host's placement layer does. Empty = no model.
    model_ref: str = ""

    @property
    def has_presentation(self) -> bool:
        """False for pass-through steps (e.g. "foundtv") that advance without showing a form."""
        return len(self.title) > 0 or len(self.options) > 0

    @property
    def has_world_label(self) -> bool:
        """True when this step places a world label on a detection."""
        return len(self.detected_class) > 0 and len(self.label) > 0

    @property
    def has_model(self) -> bool:
        """True when this step asks the host to spawn a marker-aligned model."""
        return len(self.model_ref) > 0


@dataclass(frozen=True)
class TrainingScenario:
    """An ordered queue of :class:`TrainingStep` — one procedure at a time."""

    name: str = ""
    steps: Tuple[TrainingStep, ...] = field(default_factory=tuple)
