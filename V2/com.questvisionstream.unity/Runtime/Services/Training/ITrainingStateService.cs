// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using Ethar.Training;
using QuestVisionStream.Core;
using QuestVisionStream.Training;
using RealityCollective.ServiceFramework.Interfaces;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// A class arrival that matched the training flow, with enough geometry to
    /// place a world label at the detected box centre. Synthetic action responses
    /// carry <see cref="TrainingResponseMessage.SyntheticFrame"/> and a negative
    /// arrival time — they have no meaningful world position.
    /// </summary>
    public sealed class TrainingDetectionMatch
    {
        public TrainingDetectionMatch(string label, float conf, Vector2 center, NormalizedRect rect, double arrivalTimeMs, TrainingClassSource source)
        {
            Label = label;
            Conf = conf;
            Center = center;
            Rect = rect;
            ArrivalTimeMs = arrivalTimeMs;
            Source = source;
        }

        public string Label { get; }

        public float Conf { get; }

        /// <summary>Bbox centre in normalized viewport space (0..1, origin bottom-left).</summary>
        public Vector2 Center { get; }

        public NormalizedRect Rect { get; }

        /// <summary>Arrival time on the pose clock — feed to the pose service for capture-pose unprojection. Negative for synthetic responses.</summary>
        public double ArrivalTimeMs { get; }

        public TrainingClassSource Source { get; }

        /// <summary>True when this match came from a real server detection (has a placeable world point).</summary>
        public bool HasWorldPoint => Source == TrainingClassSource.Detection && ArrivalTimeMs >= 0;
    }

    /// <summary>A step became active — the payload handed to the presentation layer.</summary>
    public sealed class TrainingStepActivation
    {
        public TrainingStepActivation(TrainingStep step, int stepIndex, int stepCount, TrainingDetectionMatch match)
        {
            Step = step;
            StepIndex = stepIndex;
            StepCount = stepCount;
            Match = match;
        }

        public TrainingStep Step { get; }

        /// <summary>0-based index into the scenario queue.</summary>
        public int StepIndex { get; }

        public int StepCount { get; }

        /// <summary>The arrival that activated the step; null when activated by <see cref="ITrainingStateService.Begin"/>.</summary>
        public TrainingDetectionMatch Match { get; }
    }

    /// <summary>
    /// The authoritative training flow: manages the scenario's queue of expected
    /// classes and responses. Listens to every detections payload, discards
    /// classes that don't match the statically cached next expected state, and
    /// advances the queue when the expected class arrives — from the detector or
    /// from a training form action (which is fed through the same detections
    /// message path).
    /// </summary>
    public interface ITrainingStateService : IService
    {
        /// <summary>A scenario was (re)loaded.</summary>
        event Action<TrainingScenario> ScenarioLoaded;

        /// <summary>A step became active — present its UX.</summary>
        event Action<TrainingStepActivation> StepActivated;

        /// <summary>
        /// The active step's <see cref="TrainingStep.DetectedClass"/> was seen
        /// again — refresh the world label/connector at the new box centre.
        /// </summary>
        event Action<TrainingDetectionMatch> CurrentClassSighted;

        /// <summary>The final step's action fired — the procedure is complete.</summary>
        event Action ScenarioCompleted;

        TrainingScenario Scenario { get; }

        TrainingFlowStatus Status { get; }

        TrainingStep CurrentStep { get; }

        /// <summary>0-based active step index, -1 when idle/completed.</summary>
        int CurrentStepIndex { get; }

        /// <summary>The statically cached next expected class (empty when idle/completed/final step).</summary>
        string ExpectedClass { get; }

        /// <summary>Detections discarded because they didn't match the expected state.</summary>
        long DiscardedCount { get; }

        /// <summary>Replace the scenario (resets the flow to idle).</summary>
        void LoadScenario(TrainingScenario scenario);

        /// <summary>Parse and load scenario JSON. False (and no state change) on malformed input.</summary>
        bool LoadScenarioJson(string json);

        /// <summary>Start the queue at the entry step.</summary>
        void Begin();

        /// <summary>
        /// A training form action completed — feed the result back. The result
        /// class re-enters the flow through the same path as a detection message.
        /// </summary>
        void CompleteStep(TrainingStepResult result);

        /// <summary>Back to idle, keeping the loaded scenario.</summary>
        void ResetScenario();
    }
}
