// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ethar.Training;
using QuestVisionStream.Training;
using UnityEditor;
using UnityEngine;

namespace QuestVisionStream.Editor
{
    /// <summary>
    /// Foldout label for a step in the scenario queue: shows the step's title and
    /// its "waiting → result" chain instead of Unity's default "Element N".
    /// </summary>
    [CustomPropertyDrawer(typeof(TrainingStepDefinition))]
    public sealed class TrainingStepDefinitionDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.PropertyField(position, property, new GUIContent(StepLabel(property)), true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUI.GetPropertyHeight(property, true);

        private static string StepLabel(SerializedProperty property)
        {
            var title = property.FindPropertyRelative("title").stringValue;
            var options = property.FindPropertyRelative("options");
            var waiting = property.FindPropertyRelative("waitingClass").stringValue;
            var result = property.FindPropertyRelative("result").stringValue;

            var name = title.Length > 0 ? title
                : options.arraySize > 0 ? "(untitled)"
                : "(pass-through)";
            var from = waiting.Length > 0 ? waiting : "begin";
            var to = result.Length > 0 ? result : "complete";
            return $"{name}   [{from} → {to}]";
        }
    }

    /// <summary>
    /// Inspector for <see cref="TrainingScenarioAsset"/>: the editable step queue
    /// plus live validation of the chain rule (every step's Result should be a
    /// later step's Waiting Class) and JSON import/export through
    /// <see cref="TrainingScenarioParser"/> for round-tripping with the wire format.
    /// </summary>
    [CustomEditor(typeof(TrainingScenarioAsset))]
    public sealed class TrainingScenarioAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("scenarioName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("steps"), includeChildren: true);
            serializedObject.ApplyModifiedProperties();

            var asset = (TrainingScenarioAsset)target;

            EditorGUILayout.Space();
            foreach (var issue in Validate(asset.Steps))
            {
                EditorGUILayout.HelpBox(issue.message, issue.warning ? MessageType.Warning : MessageType.Info);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import JSON…"))
                {
                    ImportJson(asset);
                }

                if (GUILayout.Button("Export JSON…"))
                {
                    ExportJson(asset);
                }
            }

            // The training builder: the mermaid diagram IS the configuration —
            // visual authoring/verification round-trips through the shared
            // dialect (see Documentation/Training-Builder.md). CSV accelerates
            // spreadsheet imports. Same conversions as the Python builder CLI.
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Markdown…"))
                {
                    ImportMarkdown(asset);
                }

                if (GUILayout.Button("Export Markdown…"))
                {
                    ExportMarkdown(asset);
                }

                if (GUILayout.Button("Import CSV…"))
                {
                    ImportCsv(asset);
                }
            }
        }

        /// <summary>
        /// Static checks mirroring how <see cref="TrainingStateMachine"/> walks the
        /// queue, so authoring mistakes surface here instead of on device.
        /// </summary>
        internal static IEnumerable<(string message, bool warning)> Validate(IReadOnlyList<TrainingStepDefinition> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                yield return ("Scenario has no steps — the service will fall back to the built-in demo.", true);
                yield break;
            }

            if (!string.IsNullOrEmpty(steps[0].waitingClass))
            {
                yield return ($"Step 1 waits for '{steps[0].waitingClass}', but the entry step is activated by Begin — its Waiting Class is ignored.", true);
            }

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var stepName = step.title.Length > 0 ? step.title : $"step {i + 1}";

                if (i > 0 && string.IsNullOrEmpty(step.waitingClass))
                {
                    yield return ($"'{stepName}' has no Waiting Class — no arrival can ever activate it.", true);
                }

                if (string.IsNullOrEmpty(step.result))
                {
                    if (i < steps.Count - 1)
                    {
                        yield return ($"'{stepName}' has an empty Result — pressing its action completes the scenario, so the {steps.Count - 1 - i} step(s) after it are unreachable.", true);
                    }

                    continue;
                }

                var resolved = false;
                for (var j = i + 1; j < steps.Count; j++)
                {
                    if (string.Equals(steps[j].waitingClass, step.result, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = true;
                        break;
                    }
                }

                if (!resolved)
                {
                    yield return ($"'{stepName}' expects '{step.result}', which matches no later step's Waiting Class — its arrival will end the scenario there.", i < steps.Count - 1);
                }
            }

            yield return ($"Expected class queue: begin → {string.Join(" → ", steps.Select(step => string.IsNullOrEmpty(step.result) ? "complete" : step.result))}.", false);
        }

        private static void ImportJson(TrainingScenarioAsset asset)
        {
            var path = EditorUtility.OpenFilePanel("Import training scenario JSON", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!TrainingScenarioParser.TryParse(File.ReadAllText(path), out var scenario))
            {
                EditorUtility.DisplayDialog("Import failed", $"'{Path.GetFileName(path)}' is not a valid scenario queue (see Documentation/Training-Flow.md).", "OK");
                return;
            }

            Undo.RecordObject(asset, "Import Training Scenario JSON");
            asset.FromScenario(scenario);
            EditorUtility.SetDirty(asset);
        }

        private static void ExportJson(TrainingScenarioAsset asset)
        {
            var path = EditorUtility.SaveFilePanel("Export training scenario JSON", Application.dataPath, asset.name, "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, TrainingScenarioParser.ToJson(asset.ToScenario()));
            AssetDatabase.Refresh();
        }

        private static void ImportMarkdown(TrainingScenarioAsset asset)
        {
            var path = EditorUtility.OpenFilePanel("Import training scenario markdown (mermaid)", Application.dataPath, "md");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var ok = TrainingMermaidBuilder.TryParseMarkdown(File.ReadAllText(path), out var scenario, out _, out var builderReport);
            if (!ok)
            {
                EditorUtility.DisplayDialog("Import failed", Summarize(builderReport), "OK");
                return;
            }

            Undo.RecordObject(asset, "Import Training Scenario Markdown");
            asset.FromScenario(scenario);
            EditorUtility.SetDirty(asset);
            EditorUtility.DisplayDialog("Markdown imported", Summarize(builderReport), "OK");
        }

        private static void ExportMarkdown(TrainingScenarioAsset asset)
        {
            var path = EditorUtility.SaveFilePanel("Export training scenario markdown (mermaid)", Application.dataPath, asset.name, "md");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, TrainingMermaidBuilder.ToMarkdown(asset.ToScenario()));
            AssetDatabase.Refresh();
        }

        private static void ImportCsv(TrainingScenarioAsset asset)
        {
            var path = EditorUtility.OpenFilePanel("Import training scenario CSV", Application.dataPath, "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var ok = TrainingCsvBuilder.TryParseCsv(
                File.ReadAllText(path), out var scenario, out var builderReport,
                name: Path.GetFileNameWithoutExtension(path));
            if (!ok)
            {
                EditorUtility.DisplayDialog("Import failed", Summarize(builderReport), "OK");
                return;
            }

            Undo.RecordObject(asset, "Import Training Scenario CSV");
            asset.FromScenario(scenario);
            EditorUtility.SetDirty(asset);
            EditorUtility.DisplayDialog("CSV imported", Summarize(builderReport), "OK");
        }

        /// <summary>Validation report → dialog body (bounded, so a long report can't overflow the dialog).</summary>
        private static string Summarize(TrainingValidationReport report)
        {
            const int maxLines = 14;
            var lines = report.Messages.Select(message => message.ToString()).ToList();
            if (lines.Count == 0)
            {
                return "No findings.";
            }

            if (lines.Count > maxLines)
            {
                var hidden = lines.Count - maxLines;
                lines = lines.Take(maxLines).ToList();
                lines.Add($"… and {hidden} more (see the inspector validation below).");
            }

            return string.Join("\n", lines);
        }
    }
}
