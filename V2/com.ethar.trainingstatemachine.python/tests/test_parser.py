# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Scenario/config JSON wire format — mirrors the C# TrainingScenarioParserTests.
The format is shared with the Unity packages, so these tests pin compatibility."""

import unittest

from training_state_machine import (
    TrainingStateMachine,
    config_to_json,
    ethar_demo,
    ethar_demo_config,
    scenario_to_json,
    try_parse_config,
    try_parse_scenario,
)

# Mirrors the built-in demo configuration (Training_Scenario.xlsx).
SCENARIO_JSON = """{
    "name": "Ethar Training Demo",
    "steps": [
        { "waitingClass": "", "title": "Welcome", "description": "Ready to begin your Ethar training?", "options": ["Begin"], "imageRef": "camera", "result": "begintraining" },
        { "waitingClass": "begintraining", "title": "Look for a Monitor", "description": "Search your space and locate the monitor", "options": ["Search"], "result": "tv" },
        { "waitingClass": "tv", "title": "Found TV", "options": ["Next"], "detectedClass": "tv", "label": "This is a tv", "result": "foundtv" },
        { "waitingClass": "foundtv", "result": "person" },
        { "waitingClass": "person", "title": "Found Person", "options": ["Finish"], "detectedClass": "person", "label": "This is a person", "result": "finishtraining" },
        { "waitingClass": "finishtraining", "title": "Complete", "options": ["End"], "result": "" }
    ]
}"""


class TrainingScenarioParserTests(unittest.TestCase):
    def test_parse_valid_scenario_reads_queue_in_order(self):
        ok, scenario = try_parse_scenario(SCENARIO_JSON)

        self.assertTrue(ok)
        self.assertEqual(scenario.name, "Ethar Training Demo")
        self.assertEqual(len(scenario.steps), 6)

        self.assertEqual(scenario.steps[0].waiting_class, "")
        self.assertEqual(scenario.steps[0].result, "begintraining")
        self.assertEqual(scenario.steps[0].options, ("Begin",))

        self.assertEqual(scenario.steps[2].detected_class, "tv")
        self.assertEqual(scenario.steps[2].label, "This is a tv")
        self.assertTrue(scenario.steps[2].has_world_label)

    def test_parse_missing_fields_default_to_empty(self):
        _, scenario = try_parse_scenario(SCENARIO_JSON)
        pass_through = scenario.steps[3]

        self.assertEqual(pass_through.waiting_class, "foundtv")
        self.assertEqual(pass_through.title, "")
        self.assertEqual(pass_through.options, ())
        self.assertFalse(pass_through.has_presentation)
        self.assertEqual(pass_through.result, "person")

    def test_parse_queue_chains_each_result_is_the_next_waiting_class(self):
        _, scenario = try_parse_scenario(SCENARIO_JSON)
        for i in range(len(scenario.steps) - 1):
            self.assertEqual(scenario.steps[i + 1].waiting_class, scenario.steps[i].result,
                             f"step {i} result should activate step {i + 1}")

        self.assertEqual(scenario.steps[-1].result, "", "final step completes the scenario")

    def test_parse_malformed_input_is_rejected(self):
        self.assertFalse(try_parse_scenario(None)[0])
        self.assertFalse(try_parse_scenario("")[0])
        self.assertFalse(try_parse_scenario("not json")[0])
        self.assertFalse(try_parse_scenario('{"name":"x"}')[0], "no steps")
        self.assertFalse(try_parse_scenario('{"steps":[]}')[0], "empty steps")
        self.assertFalse(try_parse_scenario('{"steps":[42]}')[0], "step not an object")

    def test_to_json_round_trips_the_queue(self):
        _, original = try_parse_scenario(SCENARIO_JSON)
        text = scenario_to_json(original)

        ok, reparsed = try_parse_scenario(text)
        self.assertTrue(ok)
        self.assertEqual(reparsed, original, "frozen dataclasses compare by value")

    def test_parse_model_ref_round_trips_and_defaults_empty(self):
        json_text = """{"steps":[
            { "waitingClass": "", "title": "Station 1", "options": ["Go"], "detectedClass": "station1", "label": "Station 1", "modelRef": "pumpAssembly", "result": "station1" },
            { "waitingClass": "station1", "result": "" }
        ]}"""

        ok, scenario = try_parse_scenario(json_text)
        self.assertTrue(ok)
        self.assertEqual(scenario.steps[0].model_ref, "pumpAssembly")
        self.assertTrue(scenario.steps[0].has_model)
        self.assertEqual(scenario.steps[1].model_ref, "", "missing modelRef defaults to empty")
        self.assertFalse(scenario.steps[1].has_model)

        ok, reparsed = try_parse_scenario(scenario_to_json(scenario))
        self.assertTrue(ok)
        self.assertEqual(reparsed, scenario, "modelRef survives the round trip")

    def test_config_to_json_round_trips_the_config(self):
        config = ethar_demo_config(0.65)

        text = config_to_json(config)

        ok, reparsed = try_parse_config(text)
        self.assertTrue(ok)
        self.assertEqual(reparsed.minimum_detection_confidence, 0.65)
        self.assertEqual(reparsed.scenario.name, "Ethar Training Demo")
        self.assertEqual(len(reparsed.scenario.steps), 6)
        self.assertEqual(reparsed.scenario.steps[2].detected_class, "tv")

        # And a machine initialized from the round-tripped config runs the flow.
        machine = TrainingStateMachine(reparsed)
        self.assertTrue(machine.begin())
        self.assertEqual(machine.expected_class, "begintraining")

    def test_parse_config_malformed_input_is_rejected(self):
        self.assertFalse(try_parse_config(None)[0])
        self.assertFalse(try_parse_config("not json")[0])
        self.assertFalse(try_parse_config("{}")[0], "no scenario")
        self.assertFalse(try_parse_config('{"scenario":{"steps":[]}}')[0], "empty steps")

    def test_library_ethar_demo_matches_the_excel_flow(self):
        demo = ethar_demo()

        self.assertEqual(len(demo.steps), 6)
        self.assertEqual(demo.steps[0].title, "Welcome")
        self.assertEqual(demo.steps[1].result, "tv")
        self.assertFalse(demo.steps[3].has_presentation)
        self.assertEqual(demo.steps[4].detected_class, "person")
        self.assertEqual(demo.steps[5].result, "")


if __name__ == "__main__":
    unittest.main()
