# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""Training builder CLI — convert between the training configuration formats,
with a validation report on every run.

    python builder.py md2json  flow.md      [-o scenario.json] [--config] [--confidence 0.5]
    python builder.py json2md  scenario.json [-o flow.md]
    python builder.py csv2md   steps.csv    [-o flow.md]   [--name "My Scenario"]
    python builder.py csv2json steps.csv    [-o scenario.json] [--name "My Scenario"] [--config] [--confidence 0.5]

- ``md2json``  — markdown+mermaid document → scenario JSON (the wire format).
- ``json2md``  — scenario JSON (bare or full config) → markdown+mermaid document.
- ``csv2md``   — spreadsheet CSV → markdown+mermaid document (review the diagram,
                 then md2json it — or go straight to JSON with csv2json).
- ``csv2json`` — spreadsheet CSV → scenario JSON.

Exit codes: 0 = OK (warnings allowed), 2 = validation errors (nothing written).
The Unity inspector offers the same conversions on the TrainingScenarioAsset
(Import/Export Markdown, Import CSV) via the C# twin of this builder.
"""

import argparse
import sys

from training_state_machine import (
    TrainingStateMachineConfig,
    TrainingScenarioData,
    config_to_json,
    scenario_to_json,
    to_markdown,
    try_parse_config,
    try_parse_csv,
    try_parse_markdown,
    try_parse_scenario,
)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Training builder: mermaid/CSV/JSON conversions.")
    parser.add_argument("command", choices=("md2json", "json2md", "csv2md", "csv2json"))
    parser.add_argument("input", help="input file path")
    parser.add_argument("-o", "--output", default=None, help="output file path (default: stdout)")
    parser.add_argument("--name", default="", help="scenario name for CSV imports")
    parser.add_argument("--config", action="store_true",
                        help="emit a full machine config instead of a bare scenario (md2json/csv2json)")
    parser.add_argument("--confidence", type=float, default=None,
                        help="minimumDetectionConfidence for --config (overrides any %%%% comment)")
    args = parser.parse_args(argv)

    with open(args.input, encoding="utf-8") as handle:
        text = handle.read()

    if args.command == "md2json":
        ok, scenario, confidence, report = try_parse_markdown(text)
        _print_report(report)
        if not ok:
            return 2
        output = _to_json(scenario, args, confidence)

    elif args.command == "json2md":
        ok, config = try_parse_config(text)
        confidence = None
        if ok:
            scenario = config.scenario.to_scenario()
            confidence = config.minimum_detection_confidence
        else:
            ok, scenario = try_parse_scenario(text)
        if not ok:
            print("[ERROR] Input is neither a scenario nor a machine config JSON.", file=sys.stderr)
            return 2
        output = to_markdown(scenario, confidence)

    else:  # csv2md / csv2json
        ok, scenario, report = try_parse_csv(text, name=args.name)
        _print_report(report)
        if not ok:
            return 2
        output = (to_markdown(scenario, args.confidence)
                  if args.command == "csv2md"
                  else _to_json(scenario, args, None))

    if args.output:
        with open(args.output, "w", encoding="utf-8") as handle:
            handle.write(output if output.endswith("\n") else output + "\n")
        print(f"Wrote {args.output}", file=sys.stderr)
    else:
        print(output)
    return 0


def _to_json(scenario, args, parsed_confidence):
    confidence = args.confidence if args.confidence is not None else parsed_confidence
    if args.config or confidence is not None:
        config = TrainingStateMachineConfig(
            scenario=TrainingScenarioData.from_scenario(scenario),
            minimum_detection_confidence=confidence if confidence is not None else 0.0,
        )
        return config_to_json(config)
    return scenario_to_json(scenario)


def _print_report(report) -> None:
    for message in report.messages:
        print(str(message), file=sys.stderr)


if __name__ == "__main__":
    sys.exit(main())
