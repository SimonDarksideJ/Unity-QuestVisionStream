# Ethar Training State Machine — Python (`com.ethar.trainingstatemachine.python`)

The training builder toolchain for the `com.ethar.trainingstatemachine` Unity
package — the scenario data model, validation, and every conversion format
(JSON wire format, mermaid markdown dialect, spreadsheet CSV), with no
dependencies beyond the Python 3.8+ standard library. The dialects and the
JSON wire format — including the optional per-step `modelRef` model catalog
key — mirror the C# implementation 1:1, so a scenario authored here is
bit-for-bit what the headset runs.

> This package is a standalone **export for desk-side authoring and
> server-side Python use** — it is not referenced by the Unity project in any
> way. 📖 The unified wire-format and capability reference for both
> implementations is
> [`V2/Documentation/Training-Configuration-Reference.md`](../Documentation/Training-Configuration-Reference.md);
> changes must land in both packages and that document together (parity policy).

## Layout

```text
training_state_machine/   the package (enums, scenario model, config data,
                          JSON parser, validation, mermaid + CSV import/export)
builder.py                training builder CLI (any of md/json/csv to either other)
examples/                 the demo configurations in every format the builder
                          speaks (ethar_demo, EtharTrainingScenario-Extended)
tests/                    unittest suite (wire format / mermaid dialect / CSV / builder)
```

## Training builder

**This is THE conversion tool for the project** (one implementation by
design). Author scenarios as mermaid diagrams, spreadsheets, or JSON, and
convert any format to either of the other two — every conversion prints a
validation report; the scenario JSON is what the Unity asset imports/exports
(dialect and usage: `V2/Documentation/Training-Builder.md`):

```bash
python builder.py md2json  flow.md       -o scenario.json
python builder.py md2csv   flow.md       -o steps.csv
python builder.py json2md  scenario.json -o flow.md
python builder.py json2csv scenario.json -o steps.csv
python builder.py csv2md   steps.csv     -o flow.md --name "Line 4 Training"
python builder.py csv2json steps.csv     -o scenario.json --config --confidence 0.5
```

Exit codes: 0 = OK (warnings allowed), 2 = validation errors (nothing
written). The CSV dialect has no scenario-name cell, so supply `--name` when
importing from CSV; everything else round-trips losslessly.

## Library usage

```python
from training_state_machine import (
    to_csv, to_markdown, try_parse_markdown, validate_scenario)

ok, scenario, confidence, report = try_parse_markdown(open("flow.md").read())
print(report)                     # the same validation the CLI prints
csv_text = to_csv(scenario)       # spreadsheet dialect
markdown = to_markdown(scenario)  # canonical mermaid document
```

Configuration is plain serializable data (`TrainingScenario` /
`TrainingStateMachineConfig`), loaded from JSON with `try_parse_scenario` /
`try_parse_config` and saved with `scenario_to_json` / `config_to_json` — the
same wire format the Unity packages use.

## Tests

```bash
python -m unittest discover -v
```

The suite pins cross-language compatibility of the authoring formats: the
JSON wire format (parse, defaults, round trips, `modelRef`), the mermaid
dialect (parse, round trips, branch/merge rejection), CSV import/export
(dialect, round trips, quoting), and the chain validation report.
