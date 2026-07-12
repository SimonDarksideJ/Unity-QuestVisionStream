// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;

namespace Ethar.Training
{
    /// <summary>
    /// The training flow state machine: a queue of expected classes fed by a
    /// semantic detection pipeline. Exactly one step is active at a time; the
    /// ONLY class that can move the queue is the active step's
    /// <see cref="TrainingStep.Result"/>, cached in <see cref="ExpectedClass"/>
    /// so the per-detection hot path is a single string comparison — no scenario
    /// table lookups. Everything else is discarded.
    ///
    /// Initialize from a serializable <see cref="TrainingStateMachineConfig"/>
    /// struct. No engine dependencies — basic C# types only, designed for 1:1
    /// ports to other runtimes.
    /// </summary>
    public sealed class TrainingStateMachine
    {
        private const StringComparison ClassComparison = StringComparison.OrdinalIgnoreCase;

        public TrainingStateMachine()
        {
        }

        public TrainingStateMachine(TrainingStateMachineConfig config) => Initialize(config);

        /// <summary>A scenario was (re)loaded.</summary>
        public event Action<TrainingScenario> ScenarioLoaded;

        /// <summary>A step became active — via <see cref="Begin"/> or an expected class arrival.</summary>
        public event Action<TrainingStepActivated> StepActivated;

        /// <summary>The active step's <see cref="TrainingStep.DetectedClass"/> was seen again.</summary>
        public event Action<TrainingClassSighting> CurrentClassSighted;

        /// <summary>The scenario finished — terminal arrival or final step's action.</summary>
        public event Action<TrainingCompletion> ScenarioCompleted;

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

        /// <summary>Detections below this confidence (0..1) are discarded by <see cref="ProcessClass"/>. Action responses always pass.</summary>
        public float MinimumDetectionConfidence { get; set; }

        /// <summary>Arrivals discarded because they didn't match the expected state.</summary>
        public long DiscardedCount { get; private set; }

        /// <summary>
        /// Configure from the serializable struct: applies the confidence gate and
        /// loads the scenario (resetting to <see cref="TrainingFlowStatus.Idle"/>).
        /// </summary>
        public void Initialize(TrainingStateMachineConfig config)
        {
            MinimumDetectionConfidence = Clamp01(config.MinimumDetectionConfidence);
            Load(config.Scenario.ToScenario());
        }

        /// <summary>Load a scenario and reset to <see cref="TrainingFlowStatus.Idle"/>.</summary>
        public void Load(TrainingScenario scenario)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Reset();
            ScenarioLoaded?.Invoke(scenario);
        }

        /// <summary>Back to idle; the loaded scenario and configuration are kept.</summary>
        public void Reset()
        {
            Status = TrainingFlowStatus.Idle;
            CurrentStepIndex = -1;
            ExpectedClass = string.Empty;
            CurrentDetectedClass = string.Empty;
            DiscardedCount = 0;
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
            StepActivated?.Invoke(new TrainingStepActivated(
                CurrentStep, 0, Scenario.Steps.Count, string.Empty, TrainingClassSource.ActionResponse));
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
        /// The single class-arrival entry point: applies the confidence gate
        /// (detections only), reports re-sightings of the active step's detected
        /// class, and offers the class to the queue. This is the check performed
        /// before any new action is sent to the client — non-matching classes are
        /// discarded and counted.
        /// </summary>
        public TrainingProcessResult ProcessClass(string className, float confidence, TrainingClassSource source)
        {
            if (!IsRunning)
            {
                return new TrainingProcessResult(TrainingProcessOutcome.NotRunning, className, confidence, source, false, null);
            }

            var minimum = source == TrainingClassSource.Detection ? MinimumDetectionConfidence : 0f;
            if (confidence < minimum)
            {
                return new TrainingProcessResult(TrainingProcessOutcome.BelowConfidence, className, confidence, source, false, null);
            }

            // Keep the world label tracking the active step's annotated class.
            var sighted = MatchesCurrentDetectedClass(className);
            if (sighted)
            {
                CurrentClassSighted?.Invoke(new TrainingClassSighting(className, confidence, source));
            }

            if (!MatchesExpected(className))
            {
                DiscardedCount++;
                return new TrainingProcessResult(TrainingProcessOutcome.Ignored, className, confidence, source, sighted, null);
            }

            var advance = Offer(className, source);
            if (advance == null)
            {
                DiscardedCount++;
                return new TrainingProcessResult(TrainingProcessOutcome.Ignored, className, confidence, source, sighted, null);
            }

            return new TrainingProcessResult(
                advance.Completed ? TrainingProcessOutcome.Completed : TrainingProcessOutcome.Advanced,
                className, confidence, source, sighted, advance);
        }

        /// <summary>
        /// A training form action completed — feed the result back. Stale results
        /// (for a step no longer active) are rejected. An empty result class
        /// completes the scenario (final step); otherwise the result class flows
        /// through <see cref="ProcessClass"/> exactly like a detection.
        /// </summary>
        public TrainingProcessResult CompleteStep(TrainingStepResult result)
        {
            if (result == null || !IsRunning)
            {
                return new TrainingProcessResult(TrainingProcessOutcome.NotRunning, result?.ResultClass, 1f, TrainingClassSource.ActionResponse, false, null);
            }

            if (result.StepIndex != CurrentStepIndex)
            {
                return new TrainingProcessResult(TrainingProcessOutcome.StaleStep, result.ResultClass, 1f, TrainingClassSource.ActionResponse, false, null);
            }

            if (result.ResultClass.Length == 0)
            {
                // Final step — there is no class to flow, the action itself completes.
                var advance = CompleteScenario(TrainingClassSource.ActionResponse);
                return advance == null
                    ? new TrainingProcessResult(TrainingProcessOutcome.Ignored, string.Empty, 1f, TrainingClassSource.ActionResponse, false, null)
                    : new TrainingProcessResult(TrainingProcessOutcome.Completed, string.Empty, 1f, TrainingClassSource.ActionResponse, false, advance);
            }

            return ProcessClass(result.ResultClass, 1f, TrainingClassSource.ActionResponse);
        }

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
                    StepActivated?.Invoke(new TrainingStepActivated(
                        Scenario.Steps[i], i, Scenario.Steps.Count, className, source));
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
            ScenarioCompleted?.Invoke(new TrainingCompletion(arrivedClass, source));
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

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
