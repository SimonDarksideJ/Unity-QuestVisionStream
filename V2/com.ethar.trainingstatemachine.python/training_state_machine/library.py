# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Built-in scenarios, so the flow runs with no authored configuration. The demo
mirrors ``Training_Scenario.xlsx`` row for row: welcome → begin → find tv →
confirm → find person → confirm → finish. Identical to the C# package's
``TrainingScenarioLibrary``."""

from .config import TrainingScenarioData, TrainingStateMachineConfig
from .scenario import TrainingScenario, TrainingStep


def ethar_demo() -> TrainingScenario:
    return TrainingScenario("Ethar Training Demo", (
        TrainingStep(
            waiting_class="",
            title="Welcome",
            description="Ready to begin your Ethar training?",
            options=("Begin",),
            detected_class="",
            label="",
            image_ref="camera",
            result="begintraining"),
        TrainingStep(
            waiting_class="begintraining",
            title="Look for a Monitor",
            description="Search your space and locate the monitor",
            options=("Search",),
            detected_class="",
            label="",
            image_ref="camera",
            result="tv"),
        TrainingStep(
            waiting_class="tv",
            title="Found TV",
            description="You found the TV, can you now locate a person",
            options=("Next",),
            detected_class="tv",
            label="This is a tv",
            image_ref="camera",
            result="foundtv"),
        TrainingStep(
            waiting_class="foundtv",
            title="",
            description="",
            options=(),
            detected_class="",
            label="",
            image_ref="",
            result="person"),
        TrainingStep(
            waiting_class="person",
            title="Found Person",
            description="You found a person",
            options=("Finish",),
            detected_class="person",
            label="This is a person",
            image_ref="camera",
            result="finishtraining"),
        TrainingStep(
            waiting_class="finishtraining",
            title="Complete",
            description="You have found everything and the course is complete",
            options=("End",),
            detected_class="",
            label="",
            image_ref="camera",
            result=""),
    ))


def ethar_demo_data() -> TrainingScenarioData:
    """The demo scenario as serializable configuration data."""
    return TrainingScenarioData.from_scenario(ethar_demo())


def ethar_demo_config(minimum_detection_confidence: float = 0.5) -> TrainingStateMachineConfig:
    """A complete demo machine configuration with the default confidence gate."""
    return TrainingStateMachineConfig(
        scenario=ethar_demo_data(),
        minimum_detection_confidence=minimum_detection_confidence,
    )
