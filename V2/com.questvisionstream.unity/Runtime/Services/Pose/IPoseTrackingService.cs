// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using QuestVisionStream.Core;
using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// Time-and-pose alignment: records the XR camera pose every frame and
    /// estimates the capture→arrival latency from server <c>pts</c> timestamps, so
    /// consumers can place a late-arriving detection through the pose from
    /// ~capture time instead of wherever the head points when the reply lands
    /// (the P0 "pose-freeze" behaviour; fixed a measured 0.75 m error in V2 web).
    /// </summary>
    public interface IPoseTrackingService : IService
    {
        /// <summary>Current latency estimate in milliseconds (base + measured queuing delay).</summary>
        double EstimatedLatencyMs { get; }

        /// <summary>Feed a payload arrival into the latency estimator.</summary>
        void ObserveArrival(double arrivalTimeMs, long? pts);

        /// <summary>The recorded pose nearest to (<paramref name="arrivalTimeMs"/> − latency), or null.</summary>
        CameraPoseSnapshot? SnapshotForArrival(double arrivalTimeMs);

        /// <summary>Current time on the clock used for snapshots/arrivals (ms).</summary>
        double NowMs { get; }

        /// <summary>Drop all history — required after a tracking-space recenter.</summary>
        void ClearHistory();

        /// <summary>
        /// Simple-mode vertical alignment aid, in degrees (positive pitches detection
        /// rays down / boxes down). Settable at runtime for live in-headset tuning.
        /// </summary>
        float CameraPitchCompensationDegrees { get; set; }
    }
}
