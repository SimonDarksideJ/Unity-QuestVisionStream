// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Ethar.Training;
using UnityEngine;

namespace QuestVisionStream.Training
{
    /// <summary>
    /// One editable step in a <see cref="TrainingScenarioAsset"/> — the mutable,
    /// Inspector-facing mirror of the immutable <see cref="TrainingStep"/>.
    /// </summary>
    [Serializable]
    public sealed class TrainingStepDefinition
    {
        [Tooltip("The class whose arrival activates this step — a real detection ('tv', 'person') or a previous step's action result ('begintraining'). Leave empty on the entry step (activated by Begin).")]
        public string waitingClass = string.Empty;

        [Tooltip("Form headline. A step with no title and no options is a pass-through: no form is shown.")]
        public string title = string.Empty;

        [TextArea(2, 4)]
        [Tooltip("Form body text.")]
        public string description = string.Empty;

        [Tooltip("Action button labels. Pressing ANY option sends Result down the class pipeline (usually one).")]
        public List<string> options = new List<string>();

        [Tooltip("Detection class to annotate in the world while this step is active (empty = none).")]
        public string detectedClass = string.Empty;

        [Tooltip("Text for the world label + connector placed at the detected box centre.")]
        public string label = string.Empty;

        [Tooltip("Client-side image reference for the form (a shared placeholder image for now).")]
        public string imageRef = string.Empty;

        [Tooltip("The next expected class — a later step's Waiting Class, arriving as a real detection or as this step's action result. Empty on the final step: pressing its action completes the scenario.")]
        public string result = string.Empty;

        [Tooltip("Model catalog key spawned aligned to this step's tag (the AprilTag whose registry Class Name matches Detected Class, or Waiting Class). Empty = no model. The host's Training Model Placement catalog resolves the key to a prefab.")]
        public string modelRef = string.Empty;

        /// <summary>Snapshot into the immutable runtime step (options copied).</summary>
        public TrainingStep ToStep() => new TrainingStep(
            waitingClass, title, description,
            options != null ? options.ToArray() : Array.Empty<string>(),
            detectedClass, label, imageRef, result, modelRef);

        internal static TrainingStepDefinition FromStep(TrainingStep step) => new TrainingStepDefinition
        {
            waitingClass = step.WaitingClass,
            title = step.Title,
            description = step.Description,
            options = step.Options.ToList(),
            detectedClass = step.DetectedClass,
            label = step.Label,
            imageRef = step.ImageRef,
            result = step.Result,
            modelRef = step.ModelRef
        };
    }

    /// <summary>
    /// A training scenario authored as a project asset — the designer-editable
    /// source for the <see cref="TrainingStateMachine"/>'s step queue. Create via
    /// <c>Assets → Create → QuestVisionStream → Training Scenario</c>, then assign
    /// it on the Training State Service profile (or the client bootstrap).
    /// JSON (see <see cref="TrainingScenarioParser"/>) remains the wire format;
    /// the custom inspector can import/export it.
    /// </summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Training Scenario", fileName = "TrainingScenario")]
    public sealed class TrainingScenarioAsset : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Display name of the scenario, shown on the hand menu readout.")]
        private string scenarioName = "New Training Scenario";

        [SerializeField]
        [Tooltip("The ordered step queue — one procedure at a time. Each step's Result should be a later step's Waiting Class (empty result on the final step).")]
        private List<TrainingStepDefinition> steps = new List<TrainingStepDefinition>();

        public string ScenarioName { get => scenarioName; set => scenarioName = value; }

        public List<TrainingStepDefinition> Steps => steps;

        /// <summary>Snapshot into the immutable runtime scenario the state machine consumes.</summary>
        public TrainingScenario ToScenario() => new TrainingScenario(
            scenarioName, steps.Select(step => step.ToStep()).ToArray());

        /// <summary>
        /// Snapshot into the serializable configuration struct — the plain-data
        /// form handed to the state machine for service initialization.
        /// </summary>
        public TrainingScenarioData ToScenarioData() => TrainingScenarioData.FromScenario(ToScenario());

        /// <summary>Replace this asset's content from a runtime scenario (JSON import).</summary>
        public void FromScenario(TrainingScenario scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            scenarioName = scenario.Name;
            steps = scenario.Steps.Select(TrainingStepDefinition.FromStep).ToList();
        }
    }
}
