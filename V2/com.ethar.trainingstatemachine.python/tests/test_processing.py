# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""process_class / complete_step — the checks performed on every class arrival
before a new action is sent to the client, validated against the built-in demo
configuration. Covers the three processing contracts: valid processing (expected
class advances), ignore processing (unexpected class discarded), and result
processing (form action results flow back through the same path). Mirrors the
C# TrainingProcessingTests."""

import unittest

from training_state_machine import (
    TrainingClassSource,
    TrainingFlowStatus,
    TrainingProcessOutcome,
    TrainingStateMachine,
    TrainingStepResult,
    ethar_demo_config,
)


class TrainingProcessingTests(unittest.TestCase):
    def setUp(self):
        self.machine = TrainingStateMachine(ethar_demo_config())
        self.machine.begin()

    def press_current_action(self):
        step = self.machine.current_step
        option = step.options[0] if step.options else ""
        return self.machine.complete_step(TrainingStepResult(
            self.machine.current_step_index, step.waiting_class, step.result, option))

    def walk_to_final_step(self):
        self.machine.process_class("begintraining", 1.0, TrainingClassSource.ACTION_RESPONSE)
        self.machine.process_class("tv", 0.9, TrainingClassSource.DETECTION)
        self.machine.process_class("foundtv", 1.0, TrainingClassSource.ACTION_RESPONSE)
        self.machine.process_class("person", 0.9, TrainingClassSource.DETECTION)
        self.machine.process_class("finishtraining", 1.0, TrainingClassSource.ACTION_RESPONSE)
        self.assertEqual(self.machine.current_step.title, "Complete")

    # ---------------------------------------------------------- valid processing

    def test_process_class_expected_class_advances_the_queue(self):
        result = self.machine.process_class("begintraining", 1.0, TrainingClassSource.ACTION_RESPONSE)

        self.assertEqual(result.outcome, TrainingProcessOutcome.ADVANCED)
        self.assertIsNotNone(result.advance)
        self.assertEqual(result.advance.step_index, 1)
        self.assertEqual(self.machine.current_step.title, "Look for a Monitor")

    def test_process_class_expected_detection_at_the_gate_advances(self):
        self.machine.process_class("begintraining", 1.0, TrainingClassSource.ACTION_RESPONSE)

        result = self.machine.process_class("tv", 0.5, TrainingClassSource.DETECTION)

        self.assertEqual(result.outcome, TrainingProcessOutcome.ADVANCED,
                         "confidence equal to the gate passes")
        self.assertEqual(self.machine.current_step.title, "Found TV")

    def test_process_class_fires_step_activated_with_arrival_context(self):
        activations = []
        self.machine.step_activated.subscribe(activations.append)

        self.machine.process_class("begintraining", 1.0, TrainingClassSource.ACTION_RESPONSE)

        self.assertEqual(len(activations), 1)
        self.assertEqual(activations[0].arrived_class, "begintraining")
        self.assertEqual(activations[0].source, TrainingClassSource.ACTION_RESPONSE)
        self.assertEqual(activations[0].step_index, 1)

    def test_process_class_sights_the_current_detected_class_without_advancing(self):
        self.machine.process_class("begintraining", 1.0, TrainingClassSource.ACTION_RESPONSE)
        self.machine.process_class("tv", 0.9, TrainingClassSource.DETECTION)

        sightings = []
        self.machine.current_class_sighted.subscribe(sightings.append)

        # "Found TV" is active (detected class tv, expecting foundtv): a tv
        # re-sighting refreshes the world label but never moves the queue.
        result = self.machine.process_class("tv", 0.8, TrainingClassSource.DETECTION)

        self.assertEqual(result.outcome, TrainingProcessOutcome.IGNORED)
        self.assertTrue(result.sighted_current_class)
        self.assertEqual(len(sightings), 1)
        self.assertEqual(sightings[0].class_name, "tv")
        self.assertEqual(sightings[0].confidence, 0.8)
        self.assertEqual(self.machine.current_step.title, "Found TV")

    # --------------------------------------------------------- ignore processing

    def test_process_class_unexpected_class_is_ignored_and_counted(self):
        # Welcome is active, expecting 'begintraining' — the detector's
        # person/tv firehose must be discarded.
        person = self.machine.process_class("person", 0.95, TrainingClassSource.DETECTION)
        tv = self.machine.process_class("tv", 0.95, TrainingClassSource.DETECTION)

        self.assertEqual(person.outcome, TrainingProcessOutcome.IGNORED)
        self.assertEqual(tv.outcome, TrainingProcessOutcome.IGNORED)
        self.assertIsNone(person.advance)
        self.assertEqual(self.machine.current_step_index, 0,
                         "ignored classes never move the queue")
        self.assertEqual(self.machine.discarded_count, 2)

    def test_process_class_expected_detection_below_the_gate_is_discarded(self):
        self.machine.process_class("begintraining", 1.0, TrainingClassSource.ACTION_RESPONSE)

        result = self.machine.process_class("tv", 0.49, TrainingClassSource.DETECTION)

        self.assertEqual(result.outcome, TrainingProcessOutcome.BELOW_CONFIDENCE)
        self.assertEqual(self.machine.current_step.title, "Look for a Monitor",
                         "low-confidence detections never advance")

    def test_process_class_action_response_ignores_the_confidence_gate(self):
        result = self.machine.process_class("begintraining", 0.0, TrainingClassSource.ACTION_RESPONSE)

        self.assertEqual(result.outcome, TrainingProcessOutcome.ADVANCED,
                         "synthetic action responses always pass the gate")

    def test_process_class_when_idle_or_completed_processes_nothing(self):
        self.machine.reset()

        result = self.machine.process_class("begintraining", 1.0, TrainingClassSource.ACTION_RESPONSE)

        self.assertEqual(result.outcome, TrainingProcessOutcome.NOT_RUNNING)
        self.assertEqual(self.machine.discarded_count, 0,
                         "a machine that isn't running counts nothing")

    def test_process_class_ignored_classes_never_fire_events(self):
        fired = []
        self.machine.step_activated.subscribe(fired.append)
        self.machine.scenario_completed.subscribe(fired.append)

        self.machine.process_class("person", 0.95, TrainingClassSource.DETECTION)

        self.assertEqual(fired, [])

    # --------------------------------------------------------- result processing

    def test_complete_step_feeds_the_result_class_through_the_same_path(self):
        result = self.press_current_action()

        self.assertEqual(result.outcome, TrainingProcessOutcome.ADVANCED)
        self.assertEqual(result.source, TrainingClassSource.ACTION_RESPONSE)
        self.assertEqual(self.machine.current_step.title, "Look for a Monitor")

    def test_complete_step_stale_step_result_is_rejected(self):
        welcome = self.machine.current_step
        self.machine.process_class("begintraining", 1.0, TrainingClassSource.ACTION_RESPONSE)

        # A press from the Welcome form arriving after the queue moved on.
        result = self.machine.complete_step(TrainingStepResult(
            0, welcome.waiting_class, welcome.result, "Begin"))

        self.assertEqual(result.outcome, TrainingProcessOutcome.STALE_STEP)
        self.assertEqual(self.machine.current_step_index, 1,
                         "stale results never move the queue")

    def test_complete_step_on_the_final_step_completes_the_scenario(self):
        self.walk_to_final_step()

        completions = []
        self.machine.scenario_completed.subscribe(completions.append)

        result = self.press_current_action()

        self.assertEqual(result.outcome, TrainingProcessOutcome.COMPLETED)
        self.assertEqual(self.machine.status, TrainingFlowStatus.COMPLETED)
        self.assertEqual(len(completions), 1)
        self.assertEqual(completions[0].arrived_class, "",
                         "the final action completes without a class")

    def test_complete_step_when_not_running_is_refused(self):
        self.machine.reset()

        result = self.machine.complete_step(TrainingStepResult(0, "", "begintraining", "Begin"))

        self.assertEqual(result.outcome, TrainingProcessOutcome.NOT_RUNNING)

    def test_complete_step_none_is_refused(self):
        result = self.machine.complete_step(None)

        self.assertEqual(result.outcome, TrainingProcessOutcome.NOT_RUNNING)

    def test_full_demo_flow_through_process_class_and_complete_step(self):
        completions = []
        self.machine.scenario_completed.subscribe(completions.append)

        self.press_current_action()                                              # Welcome → begintraining
        self.machine.process_class("tv", 0.9, TrainingClassSource.DETECTION)     # → Found TV
        self.press_current_action()                                              # → foundtv → pass-through → expecting person
        self.machine.process_class("person", 0.9, TrainingClassSource.DETECTION) # → Found Person
        self.press_current_action()                                              # → finishtraining → Complete
        self.press_current_action()                                              # final action → done

        self.assertEqual(len(completions), 1)
        self.assertEqual(self.machine.status, TrainingFlowStatus.COMPLETED)


if __name__ == "__main__":
    unittest.main()
