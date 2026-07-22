# Training Material

Editable training scenario definitions — **the mermaid diagram IS the
configuration** ([dialect reference](../Documentation/Training-Builder.md)).
Each `.md` here is a scenario you can edit visually (VS Code renders the
diagram live) and convert back into the configuration the headset runs.

| Document | Source | Runs as |
|---|---|---|
| [EtharTrainingScenario.md](EtharTrainingScenario.md) | Generated from the Unity app's shipped `Assets/QuestVisionStream/Resources/EtharTrainingScenario.asset` | The default demo scenario |

## Edit → convert → deploy

```bash
# 1. Edit the .md (keep the dialect: node text = title|description|@class: label|#modelRef|!imageRef;
#    edge label = buttons + @resultClass; END = final step).

# 2. Convert to the wire-format config (validation report prints; exit 2 = errors, nothing written):
cd ../com.ethar.trainingstatemachine.python
python builder.py md2json ../TrainingMaterial/EtharTrainingScenario.md -o ../TrainingMaterial/EtharTrainingScenario.json

# 3. Load it into the app — either:
#    - Unity: select the TrainingScenarioAsset → Import JSON… → pick the .json
#      (or create a new asset via Create → QuestVisionStream → Training Scenario), or
#    - runtime: push the JSON through ITrainingStateService.LoadScenarioJson.
```

## Getting a diagram back from Unity

Asset inspector → **Export JSON…**, then:

```bash
python builder.py json2md exported.json -o ../TrainingMaterial/MyScenario.md
```

Tips: scenario steps advance on **class names** — detector classes (`tv`,
`person`), AprilTag registry class names, or synthetic action tokens; give a
step `#modelRef` to spawn its catalog model aligned to the step's tag. The
`%% minimumDetectionConfidence:` comment inside the diagram carries the
detection gate (the app default is 0.5).
