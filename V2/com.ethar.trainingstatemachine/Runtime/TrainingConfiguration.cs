// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Linq;

namespace Ethar.Training
{
    /// <summary>
    /// Serializable snapshot of one scenario step — the plain-data mirror of the
    /// immutable <see cref="TrainingStep"/>, using only basic C# types so the
    /// configuration can be serialized and handed across runtimes.
    /// </summary>
    [Serializable]
    public struct TrainingStepData
    {
        public string WaitingClass;
        public string Title;
        public string Description;
        public string[] Options;
        public string DetectedClass;
        public string Label;
        public string ImageRef;
        public string Result;
        public string ModelRef;

        /// <summary>Build the immutable runtime step (null fields default to empty).</summary>
        public TrainingStep ToStep() => new TrainingStep(
            WaitingClass, Title, Description,
            Options ?? Array.Empty<string>(),
            DetectedClass, Label, ImageRef, Result, ModelRef);

        /// <summary>Snapshot a runtime step back into plain data (options copied).</summary>
        public static TrainingStepData FromStep(TrainingStep step)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            return new TrainingStepData
            {
                WaitingClass = step.WaitingClass,
                Title = step.Title,
                Description = step.Description,
                Options = step.Options.ToArray(),
                DetectedClass = step.DetectedClass,
                Label = step.Label,
                ImageRef = step.ImageRef,
                Result = step.Result,
                ModelRef = step.ModelRef
            };
        }
    }

    /// <summary>Serializable snapshot of a whole scenario queue.</summary>
    [Serializable]
    public struct TrainingScenarioData
    {
        public string Name;
        public TrainingStepData[] Steps;

        /// <summary>True when the data describes at least one step.</summary>
        public bool HasSteps => Steps != null && Steps.Length > 0;

        /// <summary>Build the immutable runtime scenario the state machine consumes.</summary>
        public TrainingScenario ToScenario()
        {
            var steps = Steps ?? Array.Empty<TrainingStepData>();
            var runtimeSteps = new TrainingStep[steps.Length];
            for (var i = 0; i < steps.Length; i++)
            {
                runtimeSteps[i] = steps[i].ToStep();
            }

            return new TrainingScenario(Name, runtimeSteps);
        }

        /// <summary>Snapshot a runtime scenario back into plain data.</summary>
        public static TrainingScenarioData FromScenario(TrainingScenario scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            return new TrainingScenarioData
            {
                Name = scenario.Name,
                Steps = scenario.Steps.Select(TrainingStepData.FromStep).ToArray()
            };
        }
    }

    /// <summary>
    /// The complete, serializable configuration input for
    /// <see cref="TrainingStateMachine.Initialize"/>. Hosts keep their own
    /// authoring format (e.g. a Unity ScriptableObject profile) and convert to
    /// this struct for initialization.
    /// </summary>
    [Serializable]
    public struct TrainingStateMachineConfig
    {
        /// <summary>The scenario queue to run.</summary>
        public TrainingScenarioData Scenario;

        /// <summary>Detections below this confidence (0..1) never advance the flow. Synthetic action responses always pass.</summary>
        public float MinimumDetectionConfidence;
    }
}
