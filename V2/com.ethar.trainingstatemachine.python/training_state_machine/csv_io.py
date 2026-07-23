# Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
# Licensed under the MIT License. See LICENSE in the repository root for license information.

"""CSV import/export for the training builder — accelerates spreadsheet-based
authoring (the demo scenario's heritage is a Training_Scenario.xlsx). One row
per step, one column per step field; ``options`` are ``;``-separated inside
their cell. A 1:1 behavioural twin of the C# ``TrainingCsvBuilder``."""

import csv
import io
from typing import Dict, List, Optional, Tuple

from .scenario import TrainingScenario, TrainingStep
from .validation import ValidationReport, validate_scenario

#: Canonical column key → step field. Header matching is case-insensitive and
#: ignores spaces/underscores, so "Waiting Class", "waiting_class" and
#: "waitingClass" all resolve to the same column.
COLUMNS = ("waitingclass", "title", "description", "options",
           "detectedclass", "label", "imageref", "modelref", "result")

#: Header row written by :func:`to_csv` — the camelCase spellings of COLUMNS,
#: in the same order, matching the wire-format field names.
HEADER = ("waitingClass", "title", "description", "options",
          "detectedClass", "label", "imageRef", "modelRef", "result")

OPTIONS_SEPARATOR = ";"


def to_csv(scenario: TrainingScenario) -> str:
    """Serialize a scenario to spreadsheet CSV — one row per step, the exact
    dialect :func:`try_parse_csv` reads back (scenario name excepted: CSV has
    no name cell, so exports round-trip via ``--name``)."""
    buffer = io.StringIO()
    writer = csv.writer(buffer, lineterminator="\n")
    writer.writerow(HEADER)
    for step in scenario.steps:
        writer.writerow((
            step.waiting_class,
            step.title,
            step.description,
            OPTIONS_SEPARATOR.join(step.options),
            step.detected_class,
            step.label,
            step.image_ref,
            step.model_ref,
            step.result,
        ))
    return buffer.getvalue()


def try_parse_csv(text: Optional[str], name: str = "") -> Tuple[bool, Optional[TrainingScenario], ValidationReport]:
    """Parse step rows from CSV. Returns ``(ok, scenario, report)`` — ``ok`` is
    False (scenario ``None``) when the report contains errors."""
    report = ValidationReport()

    if not text or not text.strip():
        report.error("Empty CSV document.")
        return False, None, report

    rows = list(csv.reader(io.StringIO(text)))
    rows = [row for row in rows if any(cell.strip() for cell in row)]
    if len(rows) < 2:
        report.error("CSV needs a header row plus at least one step row.")
        return False, None, report

    header = [_normalize(cell) for cell in rows[0]]
    mapping: Dict[str, int] = {}
    for index, key in enumerate(header):
        if key in COLUMNS:
            if key in mapping:
                report.warning(f"Duplicate column '{rows[0][index].strip()}' — the first occurrence wins.")
            else:
                mapping[key] = index
        elif key:
            report.info(f"Ignoring unknown column '{rows[0][index].strip()}'.")

    if not mapping:
        report.error(
            "No recognised columns in the header row. Expected any of: "
            "waitingClass, title, description, options, detectedClass, label, "
            "imageRef, modelRef, result.")
        return False, None, report

    for key in ("waitingclass", "result"):
        if key not in mapping:
            report.warning(f"Column '{key}' is missing — every step will default it to empty.")

    steps: List[TrainingStep] = []
    for row in rows[1:]:
        def cell(key: str) -> str:
            index = mapping.get(key, -1)
            return row[index].strip() if 0 <= index < len(row) else ""

        options = tuple(o.strip() for o in cell("options").split(OPTIONS_SEPARATOR) if o.strip())
        steps.append(TrainingStep(
            waiting_class=cell("waitingclass"),
            title=cell("title"),
            description=cell("description"),
            options=options,
            detected_class=cell("detectedclass"),
            label=cell("label"),
            image_ref=cell("imageref"),
            result=cell("result"),
            model_ref=cell("modelref"),
        ))

    scenario = TrainingScenario(name=name, steps=tuple(steps))
    validate_scenario(scenario, report)
    return True, scenario, report


def _normalize(cell: str) -> str:
    return cell.strip().casefold().replace(" ", "").replace("_", "")
