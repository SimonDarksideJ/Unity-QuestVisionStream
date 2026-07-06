// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using UnityEngine;

namespace QuestVisionStream.Core
{
    /// <summary>A camera pose captured at a known time. Matrices are copied — snapshots never move.</summary>
    public readonly struct CameraPoseSnapshot
    {
        public CameraPoseSnapshot(double timeMs, Matrix4x4 cameraToWorld, Matrix4x4 projectionInverse)
        {
            TimeMs = timeMs;
            CameraToWorld = cameraToWorld;
            ProjectionInverse = projectionInverse;
        }

        public double TimeMs { get; }

        /// <summary>Camera local-to-world matrix at capture time.</summary>
        public Matrix4x4 CameraToWorld { get; }

        /// <summary>Inverse projection matrix at capture time.</summary>
        public Matrix4x4 ProjectionInverse { get; }

        /// <summary>World-space camera position at capture time.</summary>
        public Vector3 Origin => CameraToWorld.GetColumn(3);

        /// <summary>
        /// Build a world-space ray through a normalized viewport point (0..1, origin
        /// bottom-left) using the CAPTURE-time pose — the pose-freeze primitive that
        /// keeps detections aligned with the pixels they describe after the
        /// capture-&gt;inference-&gt;return round trip.
        /// </summary>
        public Ray ViewportPointToRay(Vector2 viewportPoint)
        {
            // NDC point on the near-ish plane, unprojected through the recorded matrices.
            var ndc = new Vector3(viewportPoint.x * 2f - 1f, viewportPoint.y * 2f - 1f, 0.5f);
            var viewPoint = ProjectionInverse.MultiplyPoint(ndc);
            var worldPoint = CameraToWorld.MultiplyPoint(viewPoint);
            var origin = Origin;
            return new Ray(origin, (worldPoint - origin).normalized);
        }

        /// <summary>Unproject a viewport point and walk <paramref name="distanceMeters"/> along the ray.</summary>
        public Vector3 UnprojectAtDistance(Vector2 viewportPoint, float distanceMeters)
        {
            var ray = ViewportPointToRay(viewportPoint);
            return ray.origin + ray.direction * distanceMeters;
        }
    }

    /// <summary>
    /// Short history of camera poses, recorded every frame, so a detection that
    /// arrives one round-trip late can be placed through the pose from ~capture
    /// time (arrival − estimated latency) instead of wherever the head points when
    /// the reply lands. Clear it on recenter — old-space poses are garbage.
    /// </summary>
    public sealed class PoseHistory
    {
        private readonly List<CameraPoseSnapshot> snapshots = new List<CameraPoseSnapshot>();
        private readonly double maxAgeMs;

        public PoseHistory(double maxAgeMs = 2000)
        {
            this.maxAgeMs = maxAgeMs;
        }

        public int Count => snapshots.Count;

        public void Record(double timeMs, Camera camera)
            => Record(timeMs, camera.transform.localToWorldMatrix, camera.projectionMatrix.inverse);

        public void Record(double timeMs, Matrix4x4 cameraToWorld, Matrix4x4 projectionInverse)
        {
            snapshots.Add(new CameraPoseSnapshot(timeMs, cameraToWorld, projectionInverse));
            var cutoff = timeMs - maxAgeMs;
            while (snapshots.Count > 1 && snapshots[0].TimeMs < cutoff)
            {
                snapshots.RemoveAt(0);
            }
        }

        /// <summary>The snapshot nearest in time, or null when none recorded.</summary>
        public CameraPoseSnapshot? Lookup(double timeMs)
        {
            if (snapshots.Count == 0)
            {
                return null;
            }

            CameraPoseSnapshot best = snapshots[0];
            var bestDelta = double.PositiveInfinity;
            foreach (var snapshot in snapshots)
            {
                var delta = System.Math.Abs(snapshot.TimeMs - timeMs);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = snapshot;
                }
            }

            return best;
        }

        public void Clear() => snapshots.Clear();
    }
}
