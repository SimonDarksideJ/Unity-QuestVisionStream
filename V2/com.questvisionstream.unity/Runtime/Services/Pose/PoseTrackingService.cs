// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using QuestVisionStream.Core;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="PoseTrackingService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Pose Tracking Service Profile", fileName = "PoseTrackingServiceProfile")]
    public class PoseTrackingServiceProfile : BaseProfile
    {
        [SerializeField]
        [Tooltip("Baseline capture→arrival latency assumption in ms; the pts-based estimator adds measured queuing delay on top.")]
        private float baseLatencyMs = 200f;

        [SerializeField]
        [Tooltip("How much pose history to keep, in ms. Must exceed the worst expected round trip.")]
        private float historyWindowMs = 2000f;

        [SerializeField]
        [Tooltip("Simple-mode alignment aid: pitches the detection rays down to offset the passthrough camera being mounted above/pitched relative to the eye (positive = boxes move DOWN). ~5° suits arm's-length desk objects. This is a fixed-depth approximation — accurate near one distance, drifting closer/farther. The depth/anchored mode is the true fix.")]
        private float cameraPitchCompensationDegrees = 5f;

        public float BaseLatencyMs { get => baseLatencyMs; set => baseLatencyMs = value; }
        public float HistoryWindowMs { get => historyWindowMs; set => historyWindowMs = value; }
        public float CameraPitchCompensationDegrees { get => cameraPitchCompensationDegrees; set => cameraPitchCompensationDegrees = value; }
    }

    /// <summary>
    /// <see cref="IPoseTrackingService"/>: samples <c>Camera.main</c> (the XR
    /// centre-eye on Quest) every frame into a <see cref="PoseHistory"/> and runs
    /// the <see cref="LatencyEstimator"/> over payload arrivals.
    /// </summary>
    [System.Runtime.InteropServices.Guid("a8279cd9-3dbe-4206-ab19-8f3a6a4be3e3")]
    public class PoseTrackingService : BaseServiceWithConstructor, IPoseTrackingService
    {
        private readonly PoseHistory history;
        private readonly LatencyEstimator latency;
        private readonly ICameraStreamService cameraStream;
        private Camera trackedCamera;
        private bool intrinsicsLogged;

        /// <inheritdoc />
        public float CameraPitchCompensationDegrees { get; set; }

        public PoseTrackingService(string name, uint priority, PoseTrackingServiceProfile profile, ICameraStreamService cameraStream)
            : base(name, priority)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            // May be null in bare/test setups — the code falls back to the display
            // camera's projection when no capture intrinsics are available.
            this.cameraStream = cameraStream;
            CameraPitchCompensationDegrees = profile.CameraPitchCompensationDegrees;
            history = new PoseHistory(profile.HistoryWindowMs);
            latency = new LatencyEstimator(profile.BaseLatencyMs);
        }

        public double EstimatedLatencyMs => latency.LatencyMs();

        public double NowMs => Time.realtimeSinceStartupAsDouble * 1000.0;

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            if (trackedCamera == null)
            {
                trackedCamera = Camera.main;
                if (trackedCamera == null)
                {
                    return;
                }
            }

            // cameraToWorldMatrix carries the view-space (-Z forward) convention the
            // projection is expressed in; pair it with the passthrough camera's
            // intrinsics projection so detections unproject through the CAPTURE FOV.
            var pose = trackedCamera.cameraToWorldMatrix;

            // Simple-mode aid: pitch the recorded view frame down to offset the
            // passthrough camera's mount (negative angle about the view's right axis
            // tilts forward down, so a positive compensation pulls boxes down).
            if (CameraPitchCompensationDegrees != 0f)
            {
                pose *= Matrix4x4.Rotate(Quaternion.AngleAxis(-CameraPitchCompensationDegrees, Vector3.right));
            }

            history.Record(NowMs, pose, ResolveProjectionInverse());
        }

        /// <summary>
        /// Inverse of the passthrough camera's intrinsics projection (cached once
        /// available), falling back to the display camera projection until then.
        /// </summary>
        private Matrix4x4 ResolveProjectionInverse()
        {
            if (cameraStream != null && cameraStream.TryGetCameraProjection(out var projection))
            {
                if (!intrinsicsLogged)
                {
                    intrinsicsLogged = true;
                    Debug.Log("[QVS:Pose] Unprojecting detections through the passthrough camera intrinsics (FOV-correct placement).");
                }

                return projection.inverse;
            }

            return trackedCamera.projectionMatrix.inverse;
        }

        /// <inheritdoc />
        public void ObserveArrival(double arrivalTimeMs, long? pts) => latency.Observe(arrivalTimeMs, pts);

        /// <inheritdoc />
        public CameraPoseSnapshot? SnapshotForArrival(double arrivalTimeMs)
            => history.Lookup(arrivalTimeMs - latency.LatencyMs());

        /// <inheritdoc />
        public void ClearHistory()
        {
            history.Clear();
            latency.Reset();
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            history.Clear();
            base.Destroy();
        }
    }
}
