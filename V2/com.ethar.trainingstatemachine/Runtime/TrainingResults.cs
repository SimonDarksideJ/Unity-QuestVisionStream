// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

namespace Ethar.Training
{
    /// <summary>
    /// The custom response class a training form feeds back to the state machine
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

    /// <summary>A step became active — the payload of <see cref="TrainingStateMachine.StepActivated"/>.</summary>
    public sealed class TrainingStepActivated
    {
        public TrainingStepActivated(TrainingStep step, int stepIndex, int stepCount, string arrivedClass, TrainingClassSource source)
        {
            Step = step;
            StepIndex = stepIndex;
            StepCount = stepCount;
            ArrivedClass = arrivedClass ?? string.Empty;
            Source = source;
        }

        public TrainingStep Step { get; }

        /// <summary>0-based index into the scenario queue.</summary>
        public int StepIndex { get; }

        public int StepCount { get; }

        /// <summary>The class that activated the step; empty when activated by <see cref="TrainingStateMachine.Begin"/>.</summary>
        public string ArrivedClass { get; }

        public TrainingClassSource Source { get; }
    }

    /// <summary>The active step's detected class was seen — the payload of <see cref="TrainingStateMachine.CurrentClassSighted"/>.</summary>
    public sealed class TrainingClassSighting
    {
        public TrainingClassSighting(string className, float confidence, TrainingClassSource source)
        {
            ClassName = className ?? string.Empty;
            Confidence = confidence;
            Source = source;
        }

        public string ClassName { get; }

        public float Confidence { get; }

        public TrainingClassSource Source { get; }
    }

    /// <summary>The scenario finished — the payload of <see cref="TrainingStateMachine.ScenarioCompleted"/>.</summary>
    public sealed class TrainingCompletion
    {
        public TrainingCompletion(string arrivedClass, TrainingClassSource source)
        {
            ArrivedClass = arrivedClass ?? string.Empty;
            Source = source;
        }

        /// <summary>The class that completed the scenario; empty when the final step's action completed it.</summary>
        public string ArrivedClass { get; }

        public TrainingClassSource Source { get; }
    }

    /// <summary>
    /// The full report for one class arrival offered through
    /// <see cref="TrainingStateMachine.ProcessClass"/> — what happened, and the
    /// transition when one occurred. Hosts use this to enrich their own events
    /// (e.g. attach detection geometry) without re-deriving machine state.
    /// </summary>
    public sealed class TrainingProcessResult
    {
        public TrainingProcessResult(
            TrainingProcessOutcome outcome,
            string className,
            float confidence,
            TrainingClassSource source,
            bool sightedCurrentClass,
            TrainingAdvance advance)
        {
            Outcome = outcome;
            ClassName = className ?? string.Empty;
            Confidence = confidence;
            Source = source;
            SightedCurrentClass = sightedCurrentClass;
            Advance = advance;
        }

        public TrainingProcessOutcome Outcome { get; }

        public string ClassName { get; }

        public float Confidence { get; }

        public TrainingClassSource Source { get; }

        /// <summary>True when the class matched the active step's annotated detection class (world-label re-sighting).</summary>
        public bool SightedCurrentClass { get; }

        /// <summary>The transition, when <see cref="Outcome"/> is Advanced or Completed; null otherwise.</summary>
        public TrainingAdvance Advance { get; }
    }
}
