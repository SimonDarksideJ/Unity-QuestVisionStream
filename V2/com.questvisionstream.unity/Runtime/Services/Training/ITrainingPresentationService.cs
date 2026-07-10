// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// Receives UX requests from the training state flow and owns the training
    /// UI: displays the step form with its details and actions, places the world
    /// label + connector on the step's detected class (when provided), and keeps
    /// the current step readout in the hand menu up to date. Pressing an action
    /// feeds a <see cref="Training.TrainingStepResult"/> back to
    /// <see cref="ITrainingStateService.CompleteStep"/>.
    ///
    /// The interface lives in the package; the uGUI implementation lives in the
    /// client project (same split as the render modules).
    /// </summary>
    public interface ITrainingPresentationService : IService
    {
        /// <summary>True while a step form is being displayed.</summary>
        bool IsFormVisible { get; }

        /// <summary>Re-present the active step (re-anchors the form in front of the user).</summary>
        void ShowCurrentStep();

        /// <summary>
        /// Press an action on the visible form programmatically (0 = default).
        /// The controller's hardware button mapping routes through this too.
        /// </summary>
        void TriggerOption(int optionIndex);
    }
}
