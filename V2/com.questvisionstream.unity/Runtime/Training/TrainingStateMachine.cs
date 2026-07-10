// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using Newtonsoft.Json.Linq;

namespace QuestVisionStream.Training
{
    /// <summary>Where a class arrival came from.</summary>
    public enum TrainingClassSource
    {
        /// <summary>A real detection from the server's detections data channel.</summary>
        Detection = 0,

        /// <summary>The synthetic "detected class from pressing an action" on a training form.</summary>
        ActionResponse
    }

    /// <summary>Lifecycle of a loaded scenario.</summary>
    public enum TrainingFlowStatus
    {
        /// <summary>No scenario loaded, or loaded but not begun.</summary>
        Idle = 0,

        /// <summary>A step is active and the machine is waiting for its expected class.</summary>
        Running,

        /// <summary>The final step's action fired — the procedure is done.</summary>
        Completed
    }

    /// <summary>
    /// The custom response class a training form feeds back to the state service
    /// when the user presses an action — "the action is complete, here is the
    /// result". <see cref="ResultClass"/> travels the same path as a detection.
    /// </summary>
    public sealed class TrainingStepResult
    {
        public TrainingStepResult(int stepIndex, string waitingClass, string resultClass, string optionLabel)
        {
            StepIndex = stepIndex;
            WaitingClass = waitingClass ?? string.Empty;
            ResultClass = resultClass ?? string.Empty;
            OptionLabel = optionLabel ?? string.Empty;
        }

        /// <summary>Index of the step the action was pressed on.</summary>
        public int StepIndex { get; }

        /// <summary>That step's waiting class (identity check against the current step).</summary>
        public string WaitingClass { get; }

        /// <summary>The class to emit — the step's <see cref="TrainingStep.Result"/>. Empty completes the scenario.</summary>
        public string ResultClass { get; }

        /// <summary>Which option was pressed (audit only — every option emits the same result).</summary>
        public string OptionLabel { get; }
    }

    /// <summary>
    /// Builds the synthetic detections-channel message for a training response, so
    /// action results follow the SAME path as server detections: the JSON below is
    /// run through <see cref="Protocol.DetectionChannelParser"/> and the normal
    /// batch handler, exactly like a data-channel payload.
    /// </summary>
    public static class TrainingResponseMessage
    {
        /// <summary>Frame marker distinguishing synthetic response payloads from server frames.</summary>
        public const int SyntheticFrame = -1;

        /// <summary>
        /// A wire-shaped detections payload carrying the result class as a single
        /// full-confidence, centre-of-frame detection.
        /// </summary>
        public static string ToDetectionsJson(string resultClass)
        {
            var root = new JObject
            {
                ["type"] = "detections",
                ["frame"] = SyntheticFrame,
                ["width"] = 2,
                ["height"] = 2,
                ["detections"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = resultClass ?? string.Empty,
                        ["conf"] = 1.0,
                        ["bbox"] = new JArray { 1, 1, 1, 1 }
                    }
                }
            };

            return root.ToString(Newtonsoft.Json.Formatting.None);
        }
    }

    /// <summary>The outcome of a class arrival that changed state.</summary>
    public sealed class TrainingAdvance
    {
        public TrainingAdvance(TrainingStep step, int stepIndex, string arrivedClass, TrainingClassSource source, bool completed)
        {
            Step = step;
            StepIndex = stepIndex;
            ArrivedClass = arrivedClass;
            Source = source;
            Completed = completed;
        }

        /// <summary>The newly activated step (null when <see cref="Completed"/>).</summary>
        public TrainingStep Step { get; }

        public int StepIndex { get; }

        /// <summary>The class that caused the transition.</summary>
        public string ArrivedClass { get; }

        public TrainingClassSource Source { get; }

        /// <summary>True when the arrival finished the scenario instead of activating a step.</summary>
        public bool Completed { get; }
    }

    /// <summary>
    /// The pure training flow state machine: a queue of expected classes. Exactly
    /// one step is active at a time; the ONLY class that can move the queue is the
    /// active step's <see cref="TrainingStep.Result"/>, cached in
    /// <see cref="ExpectedClass"/> so the per-detection hot path is a single string
    /// comparison — no scenario table lookups. Everything else is discarded.
    /// No Unity scene dependencies — covered by EditMode tests.
    /// </summary>
    public sealed class TrainingStateMachine
    {
        private const StringComparison ClassComparison = StringComparison.OrdinalIgnoreCase;

        public TrainingScenario Scenario { get; private set; }

        public TrainingFlowStatus Status { get; private set; } = TrainingFlowStatus.Idle;

        /// <summary>Active step index, or -1 when idle/completed.</summary>
        public int CurrentStepIndex { get; private set; } = -1;

        public TrainingStep CurrentStep =>
            Scenario != null && CurrentStepIndex >= 0 && CurrentStepIndex < Scenario.Steps.Count
                ? Scenario.Steps[CurrentStepIndex]
                : null;

        /// <summary>
        /// The next class the queue is waiting for — the statically cached copy of
        /// the active step's result. Empty when idle, completed, or on a final step.
        /// </summary>
        public string ExpectedClass { get; private set; } = string.Empty;

        /// <summary>Cached copy of the active step's detected class, for world-label re-sightings.</summary>
        public string CurrentDetectedClass { get; private set; } = string.Empty;

        public bool IsRunning => Status == TrainingFlowStatus.Running;

        /// <summary>Load a scenario and reset to <see cref="TrainingFlowStatus.Idle"/>.</summary>
        public void Load(TrainingScenario scenario)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Reset();
        }

        /// <summary>Back to idle; the loaded scenario is kept.</summary>
        public void Reset()
        {
            Status = TrainingFlowStatus.Idle;
            CurrentStepIndex = -1;
            ExpectedClass = string.Empty;
            CurrentDetectedClass = string.Empty;
        }

        /// <summary>Start the queue: activates the entry step (index 0).</summary>
        public bool Begin()
        {
            if (Scenario == null || Scenario.Steps.Count == 0)
            {
                return false;
            }

            Status = TrainingFlowStatus.Running;
            Activate(0);
            return true;
        }

        /// <summary>The single hot-path check: is this the class the queue is waiting for?</summary>
        public bool MatchesExpected(string className) =>
            IsRunning &&
            ExpectedClass.Length > 0 &&
            string.Equals(ExpectedClass, className, ClassComparison);

        /// <summary>Does this class match the active step's annotated detection class?</summary>
        public bool MatchesCurrentDetectedClass(string className) =>
            IsRunning &&
            CurrentDetectedClass.Length > 0 &&
            string.Equals(CurrentDetectedClass, className, ClassComparison);

        /// <summary>
        /// Offer an arrived class to the queue. Non-matching classes are discarded
        /// (returns null). A match advances to the next step waiting on that class
        /// — or completes the scenario when no such step remains.
        /// </summary>
        public TrainingAdvance Offer(string className, TrainingClassSource source)
        {
            if (!MatchesExpected(className))
            {
                return null;
            }

            // Move next: read the queue for the step this class activates.
            for (var i = CurrentStepIndex + 1; i < Scenario.Steps.Count; i++)
            {
                if (string.Equals(Scenario.Steps[i].WaitingClass, className, ClassComparison))
                {
                    Activate(i);
                    return new TrainingAdvance(Scenario.Steps[i], i, className, source, completed: false);
                }
            }

            // Expected class with no queued step — treat as terminal.
            return CompleteInternal(className, source);
        }

        /// <summary>
        /// Complete the active FINAL step (its result is empty, so no class flows).
        /// Non-final steps must advance via <see cref="Offer"/> instead.
        /// </summary>
        public TrainingAdvance CompleteScenario(TrainingClassSource source)
        {
            if (!IsRunning || ExpectedClass.Length > 0)
            {
                return null;
            }

            return CompleteInternal(string.Empty, source);
        }

        private TrainingAdvance CompleteInternal(string arrivedClass, TrainingClassSource source)
        {
            Status = TrainingFlowStatus.Completed;
            CurrentStepIndex = -1;
            ExpectedClass = string.Empty;
            CurrentDetectedClass = string.Empty;
            return new TrainingAdvance(null, -1, arrivedClass, source, completed: true);
        }

        private void Activate(int index)
        {
            CurrentStepIndex = index;
            var step = Scenario.Steps[index];

            // Receive correct state, move next, read next expected state, store for
            // comparison — the static cache that keeps the detection hot path cheap.
            ExpectedClass = step.Result;
            CurrentDetectedClass = step.DetectedClass;
        }
    }
}
