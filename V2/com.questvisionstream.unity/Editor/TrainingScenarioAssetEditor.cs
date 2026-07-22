// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.IO;
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
    /// plus live chain validation via the shared core
    /// <see cref="TrainingScenarioValidator"/> (the same checks the Python
    /// builder CLI prints), and JSON import/export through
    /// <see cref="TrainingScenarioParser"/>. JSON is the interchange format —
    /// mermaid/CSV authoring happens in the Python training builder
    /// (Documentation/Training-Builder.md) and round-trips through these
    /// import/export buttons.
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
            var report = TrainingScenarioValidator.Validate(asset.ToScenario());
            foreach (var message in report.Messages)
            {
                EditorGUILayout.HelpBox(message.Message, ToMessageType(message.Severity));
            }

            if (asset.Steps.Count == 0)
            {
                EditorGUILayout.HelpBox("With no steps, the service falls back to the built-in demo scenario at load.", MessageType.Info);
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

            EditorGUILayout.HelpBox(
                "Author scenarios visually as mermaid diagrams with the Python training builder " +
                "(V2/com.ethar.trainingstatemachine.python/builder.py): md2json → Import JSON here; " +
                "Export JSON → json2md to review the diagram. See Documentation/Training-Builder.md.",
                MessageType.None);
        }

        private static MessageType ToMessageType(TrainingValidationSeverity severity)
        {
            switch (severity)
            {
                case TrainingValidationSeverity.Error: return MessageType.Error;
                case TrainingValidationSeverity.Warning: return MessageType.Warning;
                default: return MessageType.Info;
            }
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
    }
}
