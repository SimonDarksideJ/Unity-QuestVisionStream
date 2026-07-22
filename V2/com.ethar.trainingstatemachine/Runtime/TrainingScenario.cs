// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;

namespace Ethar.Training
{
    /// <summary>
    /// One entry in a training scenario's queue of expected states.
    ///
    /// A step ACTIVATES when its <see cref="WaitingClass"/> arrives on the class
    /// pipeline — either a real detection from the detector ("tv", "person") or a
    /// synthetic class produced by pressing an action on the previous step's form
    /// ("begintraining", "foundtv"). While active, the step's <see cref="Result"/>
    /// is the single statically-cached class the state machine waits for next.
    /// </summary>
    public sealed class TrainingStep
    {
        public TrainingStep(
            string waitingClass,
            string title,
            string description,
            IReadOnlyList<string> options,
            string detectedClass,
            string label,
            string imageRef,
            string result,
            string modelRef = "")
        {
            WaitingClass = waitingClass ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Options = options ?? Array.Empty<string>();
            DetectedClass = detectedClass ?? string.Empty;
            Label = label ?? string.Empty;
            ImageRef = imageRef ?? string.Empty;
            Result = result ?? string.Empty;
            ModelRef = modelRef ?? string.Empty;
        }

        /// <summary>The class whose arrival activates this step. Empty on the entry step (activated by Begin).</summary>
        public string WaitingClass { get; }

        /// <summary>Form headline. A step with no title and no options is a pass-through: no UX is shown.</summary>
        public string Title { get; }

        /// <summary>Form body text.</summary>
        public string Description { get; }

        /// <summary>Action labels. Pressing any option sends <see cref="Result"/> down the class pipeline (default one).</summary>
        public IReadOnlyList<string> Options { get; }

        /// <summary>Detection class to annotate in the world while this step is active (empty = none).</summary>
        public string DetectedClass { get; }

        /// <summary>Text for the world label + connector placed at the detected box centre.</summary>
        public string Label { get; }

        /// <summary>Client-side image reference for the form (a shared placeholder image for now).</summary>
        public string ImageRef { get; }

        /// <summary>
        /// The next expected class. Arrives either as a real detection or as the
        /// synthetic "detected class from pressing an action". Empty on the final
        /// step — pressing its action completes the scenario.
        /// </summary>
        public string Result { get; }

        /// <summary>
        /// Host-resolved model reference to instantiate while this step is active,
        /// aligned to the physical marker (e.g. AprilTag) whose class name matches
        /// <see cref="DetectedClass"/> (falling back to <see cref="WaitingClass"/>).
        /// An opaque catalog key like <see cref="ImageRef"/> — the state machine
        /// never interprets it; the host's placement layer does. Empty = no model.
        /// </summary>
        public string ModelRef { get; }

        /// <summary>False for pass-through steps (e.g. "foundtv") that advance without showing a form.</summary>
        public bool HasPresentation => Title.Length > 0 || Options.Count > 0;

        /// <summary>True when this step places a world label on a detection.</summary>
        public bool HasWorldLabel => DetectedClass.Length > 0 && Label.Length > 0;

        /// <summary>True when this step asks the host to spawn a marker-aligned model.</summary>
        public bool HasModel => ModelRef.Length > 0;
    }

    /// <summary>An ordered queue of <see cref="TrainingStep"/>s — one procedure at a time.</summary>
    public sealed class TrainingScenario
    {
        public TrainingScenario(string name, IReadOnlyList<TrainingStep> steps)
        {
            Name = name ?? string.Empty;
            Steps = steps ?? Array.Empty<TrainingStep>();
        }

        public string Name { get; }

        public IReadOnlyList<TrainingStep> Steps { get; }
    }
}
