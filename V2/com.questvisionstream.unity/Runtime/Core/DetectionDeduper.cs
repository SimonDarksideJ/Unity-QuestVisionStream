// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using UnityEngine;

namespace QuestVisionStream.Core
{
    /// <summary>Deduplication policy for persistent detection placements.</summary>
    public enum DedupPolicy
    {
        /// <summary>Place every detection.</summary>
        None = 0,

        /// <summary>One placement per class label (the V1 default).</summary>
        PerClass,

        /// <summary>
        /// Allow multiple per class, but skip when an existing same-class placement is
        /// within <see cref="DetectionDeduper.MinDistanceMeters"/> (V1 `allowMultiplePerClass`).
        /// </summary>
        SpatialPerClass
    }

    /// <summary>
    /// Tracks what has already been placed so repeat detections don't spawn
    /// duplicates. Ported from the V1 <c>DetectionSpawnerManager</c> dedup via the
    /// V2 TypeScript <c>DetectionDeduper</c>. Call <see cref="Reset"/> whenever the
    /// tracking space recenters — recorded world positions are garbage after that.
    /// </summary>
    public sealed class DetectionDeduper
    {
        private readonly HashSet<string> placedClasses = new HashSet<string>();
        private readonly Dictionary<string, List<Vector3>> placedByClass = new Dictionary<string, List<Vector3>>();

        public DetectionDeduper(DedupPolicy policy = DedupPolicy.PerClass, float minDistanceMeters = 0.3f)
        {
            Policy = policy;
            MinDistanceMeters = minDistanceMeters;
        }

        public DedupPolicy Policy { get; }

        /// <summary>Minimum separation between same-class placements for <see cref="DedupPolicy.SpatialPerClass"/>.</summary>
        public float MinDistanceMeters { get; }

        /// <summary>
        /// Should a detection of <paramref name="label"/> at <paramref name="worldPos"/>
        /// be placed? Records the placement when returning true. <paramref name="worldPos"/>
        /// is only meaningful for <see cref="DedupPolicy.SpatialPerClass"/>.
        /// </summary>
        public bool ShouldPlace(string label, Vector3? worldPos = null)
        {
            switch (Policy)
            {
                case DedupPolicy.None:
                    return true;

                case DedupPolicy.PerClass:
                    return placedClasses.Add(label);

                case DedupPolicy.SpatialPerClass:
                    if (!worldPos.HasValue)
                    {
                        return true;
                    }

                    if (!placedByClass.TryGetValue(label, out var existing))
                    {
                        existing = new List<Vector3>();
                        placedByClass[label] = existing;
                    }

                    foreach (var placed in existing)
                    {
                        if (Vector3.Distance(placed, worldPos.Value) < MinDistanceMeters)
                        {
                            return false;
                        }
                    }

                    existing.Add(worldPos.Value);
                    return true;

                default:
                    return true;
            }
        }

        public void Reset()
        {
            placedClasses.Clear();
            placedByClass.Clear();
        }
    }
}
