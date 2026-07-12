# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Core queue behaviour, validated against the built-in demo configuration
(the current test configuration) — mirrors the C# TrainingStateMachineTests."""

import unittest

from training_state_machine import (
    TrainingClassSource,
    TrainingFlowStatus,
    TrainingStateMachine,
    TrainingStateMachineConfig,
    TrainingScenarioData,
    TrainingStepData,
    ethar_demo,
    ethar_demo_config,
)


class TrainingStateMachineTests(unittest.TestCase):
    def setUp(self):
        self.machine = TrainingStateMachine(ethar_demo_config())

    def test_initialize_from_config_loads_scenario_and_confidence_gate(self):
        self.assertIsNotNone(self.machine.scenario)
        self.assertEqual(self.machine.scenario.name, "Ethar Training Demo")
        self.assertEqual(len(self.machine.scenario.steps), 6)
        self.assertEqual(self.machine.minimum_detection_confidence, 0.5)

    def test_initialize_clamps_confidence_and_defaults_empty_fields(self):
        config = TrainingStateMachineConfig(
            scenario=TrainingScenarioData(name=None, steps=[TrainingStepData(title="Only", options=None)]),
            minimum_detection_confidence=3.0,
        )

        self.machine.initialize(config)

        self.assertEqual(self.machine.minimum_detection_confidence, 1.0, "confidence clamped to 0..1")
        self.assertEqual(self.machine.scenario.name, "", "None strings default to empty")
        self.assertEqual(self.machine.scenario.steps[0].options, (), "None arrays default to empty")

    def test_load_is_idle_nothing_expected(self):
        self.assertEqual(self.machine.status, TrainingFlowStatus.IDLE)
        self.assertIsNone(self.machine.current_step)
        self.assertEqual(self.machine.expected_class, "")
        self.assertIsNone(
            self.machine.offer("tv", TrainingClassSource.DETECTION),
            "idle machine discards everything")

    def test_begin_activates_welcome_caches_expected_class(self):
        self.assertTrue(self.machine.begin())

        self.assertEqual(self.machine.status, TrainingFlowStatus.RUNNING)
        self.assertEqual(self.machine.current_step_index, 0)
        self.assertEqual(self.machine.current_step.title, "Welcome")
        self.assertEqual(self.machine.expected_class, "begintraining",
                         "next expected state statically cached")

    def test_begin_fires_step_activated_for_the_entry_step(self):
        activations = []
        self.machine.step_activated.subscribe(activations.append)

        self.machine.begin()

        self.assertEqual(len(activations), 1)
        self.assertEqual(activations[0].step_index, 0)
        self.assertEqual(activations[0].step_count, 6)
        self.assertEqual(activations[0].arrived_class, "",
                         "the entry step is activated by begin, not a class")

    def test_offer_non_expected_class_is_discarded(self):
        self.machine.begin()

        # The detector will happily report persons and tvs before Begin is pressed.
        self.assertIsNone(self.machine.offer("person", TrainingClassSource.DETECTION))
        self.assertIsNone(self.machine.offer("tv", TrainingClassSource.DETECTION))
        self.assertEqual(self.machine.current_step_index, 0, "discards never move the queue")

    def test_full_demo_flow_walks_the_queue_to_completion(self):
        self.machine.begin()

        # 1. Welcome → Begin (action response).
        advance = self.machine.offer("begintraining", TrainingClassSource.ACTION_RESPONSE)
        self.assertEqual(advance.step_index, 1)
        self.assertEqual(advance.step.title, "Look for a Monitor")
        self.assertEqual(self.machine.expected_class, "tv")

        # 2. Search → tv detected by the server.
        advance = self.machine.offer("tv", TrainingClassSource.DETECTION)
        self.assertEqual(advance.step.title, "Found TV")
        self.assertEqual(self.machine.current_detected_class, "tv",
                         "world label tracks the found class")
        self.assertEqual(self.machine.expected_class, "foundtv")

        # Repeat sightings of tv are discarded while waiting for the user confirm.
        self.assertIsNone(self.machine.offer("tv", TrainingClassSource.DETECTION))

        # 3. User confirms → foundtv activates the pass-through step.
        advance = self.machine.offer("foundtv", TrainingClassSource.ACTION_RESPONSE)
        self.assertFalse(advance.step.has_presentation, "no UX action, move next")
        self.assertEqual(self.machine.expected_class, "person",
                         "immediately waiting on the next detection")
        self.assertEqual(self.machine.current_detected_class, "",
                         "the tv indicator unlinks when its dialog progresses")

        # 4. person detected.
        advance = self.machine.offer("person", TrainingClassSource.DETECTION)
        self.assertEqual(advance.step.title, "Found Person")
        self.assertEqual(self.machine.expected_class, "finishtraining")

        # 5. User confirms → finish dialog.
        advance = self.machine.offer("finishtraining", TrainingClassSource.ACTION_RESPONSE)
        self.assertEqual(advance.step.title, "Complete")
        self.assertEqual(self.machine.expected_class, "", "final step has no result class")

        # 6. End — the action itself completes the scenario.
        completed = self.machine.complete_scenario(TrainingClassSource.ACTION_RESPONSE)
        self.assertTrue(completed.completed)
        self.assertEqual(self.machine.status, TrainingFlowStatus.COMPLETED)
        self.assertIsNone(self.machine.offer("tv", TrainingClassSource.DETECTION),
                          "completed machine discards everything")

    def test_offer_class_comparison_is_case_insensitive(self):
        self.machine.begin()
        self.machine.offer("BeginTraining", TrainingClassSource.ACTION_RESPONSE)

        advance = self.machine.offer("TV", TrainingClassSource.DETECTION)
        self.assertIsNotNone(advance)
        self.assertEqual(advance.step.title, "Found TV")

    def test_complete_scenario_on_a_non_final_step_is_refused(self):
        self.machine.begin()
        self.assertIsNone(
            self.machine.complete_scenario(TrainingClassSource.ACTION_RESPONSE),
            "a step with a result class must advance through offer")
        self.assertEqual(self.machine.status, TrainingFlowStatus.RUNNING)

    def test_reset_returns_to_idle_keeps_scenario_and_configuration(self):
        self.machine.begin()
        self.machine.offer("begintraining", TrainingClassSource.ACTION_RESPONSE)

        self.machine.reset()

        self.assertEqual(self.machine.status, TrainingFlowStatus.IDLE)
        self.assertIsNotNone(self.machine.scenario)
        self.assertEqual(self.machine.minimum_detection_confidence, 0.5,
                         "the confidence gate survives a reset")
        self.assertTrue(self.machine.begin(), "restart from the welcome step")
        self.assertEqual(self.machine.current_step_index, 0)

    def test_load_fires_scenario_loaded_and_resets(self):
        loaded = []
        self.machine.scenario_loaded.subscribe(loaded.append)
        self.machine.begin()

        self.machine.load(ethar_demo())

        self.assertEqual(len(loaded), 1)
        self.assertEqual(self.machine.status, TrainingFlowStatus.IDLE)

    def test_load_none_scenario_is_rejected(self):
        with self.assertRaises(ValueError):
            self.machine.load(None)


if __name__ == "__main__":
    unittest.main()
