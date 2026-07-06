// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;

namespace QuestVisionStream.Core
{
    /// <summary>
    /// Per-session latency estimation from the server's <c>pts</c> media timestamps.
    ///
    /// Absolute capture-to-arrival latency cannot be derived from pts alone (the RTP
    /// clock has an arbitrary offset from the client clock), but its variation can:
    /// <c>offset_i = arrival_i − pts_i(ms)</c> is constant when frames flow smoothly
    /// and grows when a frame queued behind slow inference or network jitter. So:
    /// <code>latency_i = baseLatencyMs + (offset_i − min(offset over recent window))</code>
    /// — the configured baseline plus the measured queuing delay. The windowed
    /// minimum re-anchors the baseline as conditions drift. Frames without pts fall
    /// back to the base. Only a clock-synchronized protocol could remove the base.
    /// </summary>
    public sealed class LatencyEstimator
    {
        private const double RtpClockHz = 90_000;

        /// <summary>(arrivalMs, offsetMs) pairs within the window, arrival-ordered.</summary>
        private readonly List<(double arrivalMs, double offsetMs)> offsets = new List<(double, double)>();
        private readonly double baseLatencyMs;
        private readonly double windowMs;
        private double? lastOffsetMs;

        public LatencyEstimator(double baseLatencyMs = 200, double windowMs = 5000)
        {
            this.baseLatencyMs = baseLatencyMs;
            this.windowMs = windowMs;
        }

        public void Observe(double arrivalMs, long? pts)
        {
            if (!pts.HasValue)
            {
                return;
            }

            var offsetMs = arrivalMs - (pts.Value / RtpClockHz) * 1000.0;
            lastOffsetMs = offsetMs;
            offsets.Add((arrivalMs, offsetMs));

            var cutoff = arrivalMs - windowMs;
            while (offsets.Count > 1 && offsets[0].arrivalMs < cutoff)
            {
                offsets.RemoveAt(0);
            }
        }

        public double LatencyMs()
        {
            if (!lastOffsetMs.HasValue || offsets.Count == 0)
            {
                return baseLatencyMs;
            }

            var minOffset = double.PositiveInfinity;
            foreach (var (_, offset) in offsets)
            {
                if (offset < minOffset)
                {
                    minOffset = offset;
                }
            }

            return baseLatencyMs + System.Math.Max(0, lastOffsetMs.Value - minOffset);
        }

        public void Reset()
        {
            offsets.Clear();
            lastOffsetMs = null;
        }
    }
}
