// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// Places training-step models on physical markers. When an activated step
    /// carries a <c>modelRef</c>, this service resolves it through the host's
    /// model catalog and instantiates the prefab aligned to the AprilTag whose
    /// registry class name matches the step's <c>detectedClass</c> (falling back
    /// to its <c>waitingClass</c>), keeping it aligned via
    /// <see cref="TagPoseFollower"/>.
    ///
    /// The delineation is strict: the tag services detect, the training engine
    /// decides (this service only reacts to <see cref="ITrainingStateService"/>
    /// step activations), and this placement layer instantiates.
    /// </summary>
    public interface ITrainingModelPlacementService : IService
    {
        /// <summary>Models currently instantiated (spawned and not yet cleared).</summary>
        int ActiveModelCount { get; }

        /// <summary>Despawn every model this service has instantiated.</summary>
        void ClearModels();
    }
}
