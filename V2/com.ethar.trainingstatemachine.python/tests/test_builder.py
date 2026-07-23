# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Training builder — mermaid dialect, CSV import/export and validation report.
Mirrors the C# TrainingBuilderTests; the dialect is shared, so these tests pin
cross-language compatibility of the authoring format."""

import unittest

from training_state_machine import (
    ValidationSeverity,
    to_csv,
    to_markdown,
    try_parse_csv,
    try_parse_markdown,
    validate_scenario,
)

# The demo scenario, hand-authored in the mermaid dialect.
DEMO_MARKDOWN = """# Ethar Training Demo

```mermaid
flowchart TD
  %% minimumDetectionConfidence: 0.5
  welcome["Welcome|Ready to begin your Ethar training?|!camera"]
  lookfor["Look for a Monitor|Search your space and locate the monitor|!camera"]
  foundtv["Found TV|Great - that is the monitor|@tv: This is a tv|!camera"]
  bridge[" "]
  person["Found Person|Now find a person|@person: This is a person|!camera"]
  done["Complete|Training complete|!camera"]

  welcome -->|Begin @begintraining| lookfor
  lookfor -->|Search @tv| foundtv
  foundtv -->|Next @foundtv| bridge
  bridge -->|@person| person
  person -->|Finish @finishtraining| done
  done -->|End| END
```
"""


def demo_scenario():
    """The demo scenario, via the mermaid dialect — the diagram IS the data."""
    ok, scenario, _, report = try_parse_markdown(DEMO_MARKDOWN)
    assert ok, str(report)
    return scenario


class MermaidParseTests(unittest.TestCase):
    def test_parse_demo_markdown_reads_the_full_queue(self):
        ok, scenario, confidence, report = try_parse_markdown(DEMO_MARKDOWN)

        self.assertTrue(ok, str(report))
        self.assertFalse(report.has_errors)
        self.assertEqual(confidence, 0.5)
        self.assertEqual(scenario.name, "Ethar Training Demo")
        self.assertEqual(len(scenario.steps), 6)

        self.assertEqual(scenario.steps[0].waiting_class, "")
        self.assertEqual(scenario.steps[0].title, "Welcome")
        self.assertEqual(scenario.steps[0].options, ("Begin",))
        self.assertEqual(scenario.steps[0].result, "begintraining")
        self.assertEqual(scenario.steps[0].image_ref, "camera")

        self.assertEqual(scenario.steps[1].waiting_class, "begintraining")
        self.assertEqual(scenario.steps[1].result, "tv")

        self.assertEqual(scenario.steps[2].waiting_class, "tv")
        self.assertEqual(scenario.steps[2].detected_class, "tv")
        self.assertEqual(scenario.steps[2].label, "This is a tv")

        # Pass-through: no title/options, detection-advanced.
        self.assertEqual(scenario.steps[3].waiting_class, "foundtv")
        self.assertFalse(scenario.steps[3].has_presentation)
        self.assertEqual(scenario.steps[3].result, "person")

        # Final step: edge to END = empty result.
        self.assertEqual(scenario.steps[5].options, ("End",))
        self.assertEqual(scenario.steps[5].result, "")

    def test_action_edge_without_class_mints_the_target_node_id(self):
        ok, scenario, _, report = try_parse_markdown(
            "# T\n```mermaid\nflowchart TD\n  a[\"A\"]\n  b[\"B\"]\n  a -->|Go| b\n  b -->|Done| END\n```")

        self.assertTrue(ok, str(report))
        self.assertEqual(scenario.steps[0].result, "b")
        self.assertEqual(scenario.steps[1].waiting_class, "b")

    def test_model_ref_segment_round_trips(self):
        ok, scenario, _, report = try_parse_markdown(
            "# T\n```mermaid\nflowchart TD\n"
            "  a[\"Station 1|Fit the pump|@station1: Station 1|#pumpAssembly\"]\n"
            "  a -->|Done| END\n```")

        self.assertTrue(ok, str(report))
        self.assertEqual(scenario.steps[0].model_ref, "pumpAssembly")
        self.assertEqual(scenario.steps[0].detected_class, "station1")

    def test_branching_is_an_error(self):
        ok, _, _, report = try_parse_markdown(
            "# T\n```mermaid\nflowchart TD\n  a[\"A\"]\n  b[\"B\"]\n  c[\"C\"]\n"
            "  a -->|x| b\n  a -->|y| c\n```")

        self.assertFalse(ok)
        self.assertTrue(report.has_errors)
        self.assertTrue(any("branches" in m.message for m in report.messages))

    def test_conflicting_incoming_classes_is_an_error(self):
        ok, _, _, report = try_parse_markdown(
            "# T\n```mermaid\nflowchart TD\n  a[\"A\"]\n  b[\"B\"]\n  c[\"C\"]\n"
            "  a -->|@x| c\n  b -->|@y| c\n```")

        self.assertFalse(ok)
        self.assertTrue(any("different classes" in m.message for m in report.messages))

    def test_missing_mermaid_block_is_an_error(self):
        ok, _, _, report = try_parse_markdown("# Just a heading\n\nNo diagram here.")
        self.assertFalse(ok)
        self.assertTrue(report.has_errors)

    def test_explicit_waiting_override(self):
        ok, scenario, _, report = try_parse_markdown(
            "# T\n```mermaid\nflowchart TD\n  a[\"A\"]\n  b[\"B|?special\"]\n  a -->|@special| END\n```")

        self.assertTrue(ok, str(report))
        self.assertEqual(scenario.steps[1].waiting_class, "special")


class MermaidRoundTripTests(unittest.TestCase):
    def test_demo_scenario_round_trips_through_markdown(self):
        demo = demo_scenario()
        markdown = to_markdown(demo, 0.5)

        ok, reparsed, confidence, report = try_parse_markdown(markdown)

        self.assertTrue(ok, str(report))
        self.assertFalse(report.has_errors, str(report))
        self.assertEqual(confidence, 0.5)
        self.assertEqual(reparsed.name, demo.name)
        self.assertEqual(reparsed, demo, "the generated diagram IS the configuration")

    def test_empty_title_with_description_round_trips(self):
        from training_state_machine import TrainingScenario, TrainingStep
        scenario = TrainingScenario(name="T", steps=(
            TrainingStep(title="A", options=("Go",), result="x"),
            TrainingStep(waiting_class="x", description="description only", result=""),
        ))

        ok, reparsed, _, report = try_parse_markdown(to_markdown(scenario))

        self.assertTrue(ok, str(report))
        self.assertEqual(reparsed, scenario)

    def test_generated_markdown_contains_diagram_and_table(self):
        markdown = to_markdown(demo_scenario())
        self.assertIn("```mermaid", markdown)
        self.assertIn("flowchart TD", markdown)
        self.assertIn("| # | Step |", markdown)
        self.assertIn("END", markdown)


class CsvImportTests(unittest.TestCase):
    DEMO_CSV = (
        "waitingClass,title,description,options,detectedClass,label,imageRef,modelRef,result\n"
        ',Welcome,"Ready to begin your Ethar training?",Begin,,,camera,,begintraining\n'
        'begintraining,Look for a Monitor,"Search your space and locate the monitor",Search,,,camera,,tv\n'
        'tv,Found TV,"Great - that is the monitor",Next,tv,This is a tv,camera,,foundtv\n'
        "foundtv,,,,,,,,person\n"
        'person,Found Person,"Now find a person",Finish,person,This is a person,camera,,finishtraining\n'
        'finishtraining,Complete,"Training complete",End,,,camera,,\n'
    )

    def test_csv_parses_the_demo_flow(self):
        ok, scenario, report = try_parse_csv(self.DEMO_CSV, name="Ethar Training Demo")

        self.assertTrue(ok, str(report))
        self.assertFalse(report.has_errors)
        self.assertEqual(len(scenario.steps), 6)
        self.assertEqual(scenario.steps[2].detected_class, "tv")
        self.assertEqual(scenario.steps[2].options, ("Next",))
        self.assertEqual(scenario.steps[5].result, "")

    def test_csv_multiple_options_split_on_semicolon(self):
        csv_text = "title,options,result\nPick,Go;Skip,x\n"
        ok, scenario, report = try_parse_csv(csv_text)

        self.assertTrue(ok, str(report))
        self.assertEqual(scenario.steps[0].options, ("Go", "Skip"))
        self.assertTrue(any("waitingclass" in m.message.casefold() for m in report.messages),
                        "missing waitingClass column should be reported")

    def test_csv_unknown_column_is_reported_not_fatal(self):
        csv_text = "title,result,notes\nA,,left over from the spreadsheet\n"
        ok, _, report = try_parse_csv(csv_text)

        self.assertTrue(ok)
        self.assertTrue(any("notes" in m.message for m in report.messages))

    def test_csv_without_recognised_columns_is_an_error(self):
        ok, _, report = try_parse_csv("foo,bar\n1,2\n")
        self.assertFalse(ok)
        self.assertTrue(report.has_errors)

    def test_csv_to_markdown_round_trips(self):
        ok, scenario, _ = try_parse_csv(self.DEMO_CSV, name="Ethar Training Demo")
        self.assertTrue(ok)

        ok2, reparsed, _, report = try_parse_markdown(to_markdown(scenario))
        self.assertTrue(ok2, str(report))
        self.assertEqual(reparsed, scenario)


class CsvExportTests(unittest.TestCase):
    def test_demo_scenario_round_trips_through_csv(self):
        demo = demo_scenario()

        ok, reparsed, report = try_parse_csv(to_csv(demo), name=demo.name)

        self.assertTrue(ok, str(report))
        self.assertFalse(report.has_errors)
        self.assertEqual(reparsed, demo, "the exported CSV IS the configuration")

    def test_export_writes_the_canonical_header(self):
        first_line = to_csv(demo_scenario()).splitlines()[0]
        self.assertEqual(
            first_line,
            "waitingClass,title,description,options,detectedClass,label,imageRef,modelRef,result")

    def test_export_joins_options_on_semicolon(self):
        from training_state_machine import TrainingScenario, TrainingStep
        scenario = TrainingScenario(name="T", steps=(
            TrainingStep(title="Pick", options=("Go", "Skip"), result=""),
        ))

        csv_text = to_csv(scenario)
        self.assertIn("Go;Skip", csv_text)

        ok, reparsed, _ = try_parse_csv(csv_text, name="T")
        self.assertTrue(ok)
        self.assertEqual(reparsed.steps[0].options, ("Go", "Skip"))

    def test_export_quotes_cells_containing_commas(self):
        from training_state_machine import TrainingScenario, TrainingStep
        scenario = TrainingScenario(name="T", steps=(
            TrainingStep(title="A", description="Good - the TV is on. Next, find the person",
                         options=("Next",), result=""),
        ))

        ok, reparsed, report = try_parse_csv(to_csv(scenario), name="T")
        self.assertTrue(ok, str(report))
        self.assertEqual(reparsed, scenario, "commas inside cells survive the round trip")


class ValidationReportTests(unittest.TestCase):
    def test_dead_end_result_is_a_warning(self):
        ok, _, report = try_parse_csv("title,result\nA,ghost\nB,\n")
        self.assertTrue(ok)
        self.assertTrue(report.has_warnings)
        self.assertTrue(any("ghost" in m.message for m in report.messages))

    def test_valid_demo_has_no_warnings(self):
        report = validate_scenario(demo_scenario())
        self.assertFalse(report.has_errors)
        self.assertFalse(report.has_warnings)
        self.assertTrue(any(m.severity == ValidationSeverity.INFO for m in report.messages))


if __name__ == "__main__":
    unittest.main()
