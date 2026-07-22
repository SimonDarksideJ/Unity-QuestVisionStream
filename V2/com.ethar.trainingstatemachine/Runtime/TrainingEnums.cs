// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

namespace Ethar.Training
{
    /// <summary>Where a class arrival came from.</summary>
    public enum TrainingClassSource
    {
        /// <summary>A real detection from the semantic detection pipeline.</summary>
        Detection = 0,

        /// <summary>The synthetic "detected class from pressing an action" on a training form.</summary>
        ActionResponse,

        /// <summary>
        /// An on-device fiducial sighting (e.g. AprilTag) bridged into the class
        /// pipeline. Deterministic — always full confidence, never gated by
        /// <see cref="TrainingStateMachine.MinimumDetectionConfidence"/>.
        /// </summary>
        AprilTag
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

    /// <summary>The outcome of offering a single class arrival to the state machine.</summary>
    public enum TrainingProcessOutcome
    {
        /// <summary>The machine is idle or completed — everything is discarded.</summary>
        NotRunning = 0,

        /// <summary>A detection below the configured minimum confidence — discarded.</summary>
        BelowConfidence,

        /// <summary>The class did not match the expected state — discarded.</summary>
        Ignored,

        /// <summary>A step result for a step that is no longer active — discarded.</summary>
        StaleStep,

        /// <summary>The expected class arrived and activated the next step.</summary>
        Advanced,

        /// <summary>The arrival (or final action) completed the scenario.</summary>
        Completed
    }
}
