// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;

namespace Ethar.UXTraining
{
    /// <summary>
    /// Presentation payload for one training step — the seam between this
    /// package and whatever detection/state pipeline drives it. Hosts map their
    /// own scenario model onto this and hand it to
    /// <see cref="TrainingUxController.ShowStep"/>; the package never sees the
    /// host's domain types.
    /// </summary>
    public sealed class TrainingStepView
    {
        public TrainingStepView(
            int stepIndex,
            int stepCount,
            string title,
            string description,
            IReadOnlyList<string> options,
            string imageRef = "")
        {
            StepIndex = stepIndex;
            StepCount = stepCount;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Options = options ?? Array.Empty<string>();
            ImageRef = imageRef ?? string.Empty;
        }

        /// <summary>0-based index of this step in the scenario.</summary>
        public int StepIndex { get; }

        /// <summary>Total steps in the scenario (drives the progress ticks).</summary>
        public int StepCount { get; }

        /// <summary>Form headline.</summary>
        public string Title { get; }

        /// <summary>Form body text (empty = omitted).</summary>
        public string Description { get; }

        /// <summary>Action button labels; index 0 renders as the primary button.</summary>
        public IReadOnlyList<string> Options { get; }

        /// <summary>Image reference shown in the form's placeholder (empty = no image block).</summary>
        public string ImageRef { get; }

        /// <summary>A step with no title and no options is a pass-through: no form is shown.</summary>
        public bool HasPresentation => Title.Length > 0 || Options.Count > 0;
    }
}
