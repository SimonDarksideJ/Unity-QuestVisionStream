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

        public float BaseLatencyMs { get => baseLatencyMs; set => baseLatencyMs = value; }
        public float HistoryWindowMs { get => historyWindowMs; set => historyWindowMs = value; }
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
        private Camera trackedCamera;

        public PoseTrackingService(string name, uint priority, PoseTrackingServiceProfile profile)
            : base(name, priority)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

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

            history.Record(NowMs, trackedCamera);
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
