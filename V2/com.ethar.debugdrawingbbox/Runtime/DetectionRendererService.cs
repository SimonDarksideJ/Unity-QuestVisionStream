// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using QuestVisionStream.Services;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;
using UnityEngine.XR;

namespace Ethar.DebugDrawingBBox
{
    /// <summary>Configuration for <see cref="DetectionRendererService"/>.</summary>
    [CreateAssetMenu(menuName = "Ethar/Debug Drawing BBox/Detection Renderer Service Profile", fileName = "DetectionRendererServiceProfile")]
    public class DetectionRendererServiceProfile : RealityCollective.ServiceFramework.Definitions.BaseServiceProfile<IDetectionRenderModule>
    {
        [SerializeField]
        [Tooltip("Service module name to activate at start; empty activates the first registered module.")]
        private string initialActiveModule = "";

        public string InitialActiveModule { get => initialActiveModule; set => initialActiveModule = value; }
    }

    /// <summary>
    /// <see cref="IDetectionRendererService"/>: exactly one render module active at
    /// a time, batches delivered with their capture-time pose snapshot, recenter
    /// cleanup wired through <see cref="XRInputSubsystem.trackingOriginUpdated"/>.
    /// </summary>
    [System.Runtime.InteropServices.Guid("b30d2501-f419-4aa0-ab89-528e6de28f61")]
    public class DetectionRendererService : BaseServiceWithConstructor, IDetectionRendererService
    {
        private readonly DetectionRendererServiceProfile profile;
        private readonly IDetectionService detections;
        private readonly IPoseTrackingService poseTracking;
        private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
        private IDetectionRenderModule activeModule;
        private bool initialActivationDone;

        public DetectionRendererService(
            string name,
            uint priority,
            DetectionRendererServiceProfile profile,
            IDetectionService detections,
            IPoseTrackingService poseTracking)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.detections = detections ?? throw new ArgumentNullException(nameof(detections));
            this.poseTracking = poseTracking ?? throw new ArgumentNullException(nameof(poseTracking));
        }

        public string ActiveModuleName => activeModule?.Name;

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            detections.DetectionsReceived += OnDetections;

            // Recenter cleanup: everything placed (and every recorded pose) is
            // expressed in the old space — all of it is garbage after a recenter.
            SubsystemManager.GetSubsystems(inputSubsystems);
            foreach (var subsystem in inputSubsystems)
            {
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
            }
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            // Activate the initial module once modules have registered.
            if (!initialActivationDone)
            {
                initialActivationDone = TryInitialActivation();
            }
        }

        /// <inheritdoc />
        public bool SetActiveModule(string moduleName)
        {
            IDetectionRenderModule target = null;
            foreach (var module in ServiceModules)
            {
                if (module is IDetectionRenderModule renderModule &&
                    string.Equals(renderModule.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    target = renderModule;
                    break;
                }
            }

            if (target == null)
            {
                Debug.LogWarning($"[QVS:Renderer] No render module named '{moduleName}'");
                return false;
            }

            if (target == activeModule)
            {
                return true;
            }

            // The runtime-switch contract: the outgoing module cleans up after itself.
            if (activeModule != null)
            {
                activeModule.Clear();
                activeModule.SetActiveRenderer(false);
            }

            activeModule = target;
            activeModule.SetActiveRenderer(true);
            Debug.Log($"[QVS:Renderer] Active render module: {activeModule.Name}");
            return true;
        }

        /// <inheritdoc />
        public void ClearAll() => activeModule?.Clear();

        /// <inheritdoc />
        public override void Destroy()
        {
            detections.DetectionsReceived -= OnDetections;
            foreach (var subsystem in inputSubsystems)
            {
                subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            }

            activeModule?.Clear();
            base.Destroy();
        }

        private bool TryInitialActivation()
        {
            IDetectionRenderModule first = null;
            foreach (var module in ServiceModules)
            {
                if (module is IDetectionRenderModule renderModule)
                {
                    first = first ?? renderModule;
                    if (!string.IsNullOrEmpty(profile.InitialActiveModule) &&
                        string.Equals(renderModule.Name, profile.InitialActiveModule, StringComparison.OrdinalIgnoreCase))
                    {
                        return SetActiveModule(renderModule.Name);
                    }
                }
            }

            return first != null && SetActiveModule(first.Name);
        }

        private void OnDetections(DetectionArrival arrival)
        {
            if (activeModule == null)
            {
                Debug.LogWarning($"[WSDetection] frame={arrival.Batch.Frame} NOT drawn — no active render module");
                return;
            }

            var capturePose = poseTracking.SnapshotForArrival(arrival.ArrivalTimeMs);
            if (!capturePose.HasValue)
            {
                Debug.LogWarning($"[WSDetection] frame={arrival.Batch.Frame} NOT drawn — no capture pose recorded yet (is Camera.main present and the pose service ticking?)");
            }
            else
            {
                Debug.Log($"[WSDetection] draw frame={arrival.Batch.Frame} count={arrival.Batch.Detections.Count} module='{activeModule.Name}' latency={poseTracking.EstimatedLatencyMs:0}ms");
            }

            activeModule.RenderDetections(arrival, capturePose);
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            Debug.Log("[QVS:Renderer] Tracking origin recentered — clearing visuals and pose history");
            activeModule?.Clear();
            poseTracking.ClearHistory();
        }
    }
}
