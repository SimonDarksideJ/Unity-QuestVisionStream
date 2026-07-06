// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using RealityCollective.ServiceFramework.Interfaces;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>One module's verdict on the current frame.</summary>
    public readonly struct QualityMetric
    {
        public QualityMetric(string name, float score, bool ok, string detail)
        {
            Name = name;
            Score = score;
            Ok = ok;
            Detail = detail;
        }

        public string Name { get; }

        /// <summary>0..1, where 1 is ideal.</summary>
        public float Score { get; }

        public bool Ok { get; }
        public string Detail { get; }
    }

    /// <summary>The combined verdict across all modules for one sample.</summary>
    public sealed class QualityReport
    {
        public QualityReport(IReadOnlyList<QualityMetric> metrics, double timeMs)
        {
            Metrics = metrics;
            TimeMs = timeMs;
            Ok = true;
            Score = 1f;
            foreach (var metric in metrics)
            {
                Ok &= metric.Ok;
                Score = Mathf.Min(Score, metric.Score);
            }
        }

        public IReadOnlyList<QualityMetric> Metrics { get; }

        /// <summary>AND of all module verdicts.</summary>
        public bool Ok { get; }

        /// <summary>Min of all module scores.</summary>
        public float Score { get; }

        public double TimeMs { get; }
    }

    /// <summary>
    /// A pluggable frame-quality check (brightness today; motion blur, exposure
    /// histogram etc. later). Evaluated over a small downscaled RGBA sample.
    /// </summary>
    public interface IImageQualifierModule : IServiceModule
    {
        QualityMetric Evaluate(Color32[] pixels, int width, int height);
    }

    /// <summary>
    /// Edge quality gate ported from the V1 <c>BrightnessEstimation</c> intent via
    /// the V2 library: samples the camera at an interval, runs qualifier modules,
    /// reports quality. <see cref="ShouldStream"/> defaults TRUE before the first
    /// sample so startup is never blocked, and streaming is only actually gated
    /// when the WebRTC profile opts in (the raw passthrough feed reads darker than
    /// the tone-mapped view — gating on it deadlocked the WebXR client).
    /// </summary>
    public interface IImageQualifierService : IService
    {
        event Action<QualityReport> QualityChanged;

        /// <summary>Latest combined verdict; true before any sample exists.</summary>
        bool ShouldStream { get; }

        QualityReport LastReport { get; }
    }
}
