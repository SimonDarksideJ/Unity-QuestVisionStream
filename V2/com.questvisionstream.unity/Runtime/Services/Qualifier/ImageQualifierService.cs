// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using QuestVisionStream.Core;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="ImageQualifierService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Image Qualifier Service Profile", fileName = "ImageQualifierServiceProfile")]
    public class ImageQualifierServiceProfile : BaseServiceProfile<IImageQualifierModule>
    {
        [SerializeField]
        [Tooltip("How often to sample the camera, in ms.")]
        private float sampleIntervalMs = 100f;

        [SerializeField]
        [Tooltip("Downscaled sample size — enough for global metrics, cheap to read back.")]
        private Vector2Int sampleSize = new Vector2Int(64, 48);

        public float SampleIntervalMs { get => sampleIntervalMs; set => sampleIntervalMs = value; }
        public Vector2Int SampleSize { get => sampleSize; set => sampleSize = value; }
    }

    /// <summary>
    /// <see cref="IImageQualifierService"/>: ticks its sampler at the configured
    /// interval while the camera is active, evaluates every registered
    /// <see cref="IImageQualifierModule"/> and publishes combined reports.
    /// </summary>
    [System.Runtime.InteropServices.Guid("b1375687-4dbf-4348-bb05-b7fb5d841d0f")]
    public class ImageQualifierService : BaseServiceWithConstructor, IImageQualifierService
    {
        private readonly ImageQualifierServiceProfile profile;
        private readonly ICameraStreamService camera;
        private readonly TextureSampler sampler = new TextureSampler();
        private float nextSampleRealtime;

        public ImageQualifierService(
            string name,
            uint priority,
            ImageQualifierServiceProfile profile,
            ICameraStreamService camera)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        public event Action<QualityReport> QualityChanged;

        public QualityReport LastReport { get; private set; }

        // Default to "stream" before the first sample so startup is never blocked.
        public bool ShouldStream => LastReport?.Ok ?? true;

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            if (camera.State != CameraStreamState.Active)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            if (now < nextSampleRealtime)
            {
                return;
            }

            nextSampleRealtime = now + profile.SampleIntervalMs / 1000f;
            sampler.TrySample(camera.SourceTexture, profile.SampleSize.x, profile.SampleSize.y, EvaluateSample);
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            sampler.Dispose();
            base.Destroy();
        }

        private void EvaluateSample(Color32[] pixels, int width, int height)
        {
            var metrics = new List<QualityMetric>();
            foreach (var module in ServiceModules)
            {
                if (module is IImageQualifierModule qualifier)
                {
                    metrics.Add(qualifier.Evaluate(pixels, width, height));
                }
            }

            if (metrics.Count == 0)
            {
                return;
            }

            var previousOk = ShouldStream;
            LastReport = new QualityReport(metrics, Time.realtimeSinceStartupAsDouble * 1000.0);

            if (LastReport.Ok != previousOk)
            {
                Debug.Log($"[QVS:Qualifier] Quality {(LastReport.Ok ? "ok" : "below threshold")} (score {LastReport.Score:0.00})");
            }

            QualityChanged?.Invoke(LastReport);
        }
    }
}
