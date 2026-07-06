// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using QuestVisionStream.Core;
using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// A way of visualizing detections. Two ship with QuestVisionStream:
    /// ephemeral pose-frozen outline boxes (the V2 WebXR behaviour) and persistent
    /// depth-anchored tags (the V1 Unity behaviour, app-side — it needs Meta's
    /// EnvironmentRaycastManager). Exactly one module renders at a time; the
    /// service switches between them at runtime, and a module MUST fully clean up
    /// its scene objects and GPU resources when deactivated.
    /// </summary>
    public interface IDetectionRenderModule : IServiceModule
    {
        /// <summary>Is this module the one currently rendering?</summary>
        bool IsActiveRenderer { get; }

        /// <summary>
        /// Toggle rendering. Deactivation must clear all visuals and release
        /// resources (the runtime-switch contract).
        /// </summary>
        void SetActiveRenderer(bool active);

        /// <summary>
        /// Render one arrived batch. <paramref name="capturePose"/> is the camera pose
        /// from ~capture time (pose-freeze) — null when no history exists yet.
        /// </summary>
        void RenderDetections(DetectionArrival arrival, CameraPoseSnapshot? capturePose);

        /// <summary>Remove all visuals and reset internal placement state (e.g. dedup).</summary>
        void Clear();
    }

    /// <summary>
    /// Routes detection batches to the single active render module, applying
    /// pose-freeze, and clears everything on tracking-space recenter (old-space
    /// placements are garbage after a recenter).
    /// </summary>
    public interface IDetectionRendererService : IService
    {
        /// <summary>Name of the module currently rendering, or null.</summary>
        string ActiveModuleName { get; }

        /// <summary>
        /// Switch the active module by service module name. The outgoing module is
        /// cleared and deactivated first. Returns false when no module matches.
        /// </summary>
        bool SetActiveModule(string moduleName);

        /// <summary>Clear the active module's visuals and placement state.</summary>
        void ClearAll();
    }
}
