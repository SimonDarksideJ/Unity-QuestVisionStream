// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Modules;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="BrightnessQualifierModule"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Brightness Qualifier Module Profile", fileName = "BrightnessQualifierModuleProfile")]
    public class BrightnessQualifierModuleProfile : BaseProfile
    {
        [SerializeField]
        [Range(0, 255)]
        [Tooltip("Below this mean luma the frame counts as too dark for reliable detections.")]
        private int minLuma = 40;

        [SerializeField]
        [Range(0, 255)]
        [Tooltip("Above this mean luma the frame counts as blown out.")]
        private int maxLuma = 230;

        [SerializeField]
        [Range(1, 30)]
        [Tooltip("Smoothing window (number of samples averaged).")]
        private int smoothingSamples = 10;

        public int MinLuma { get => minLuma; set => minLuma = value; }
        public int MaxLuma { get => maxLuma; set => maxLuma = value; }
        public int SmoothingSamples { get => smoothingSamples; set => smoothingSamples = value; }
    }

    /// <summary>
    /// Rec.709 luminance qualifier, ported from Meta's
    /// <c>BrightnessEstimationManager</c> via the V2 TypeScript module: mean
    /// <c>0.2126R + 0.7152G + 0.0722B</c> over the sample, smoothed with a ring buffer.
    /// </summary>
    [System.Runtime.InteropServices.Guid("980ba51a-0a86-4c2e-bb9a-b291ffb6446a")]
    public class BrightnessQualifierModule : BaseServiceModule, IImageQualifierModule
    {
        private readonly BrightnessQualifierModuleProfile profile;
        private readonly float[] window;
        private int windowCount;
        private int windowIndex;

        public BrightnessQualifierModule(
            string name,
            uint priority,
            BrightnessQualifierModuleProfile profile,
            IImageQualifierService parentService)
            : base(name, priority, profile, parentService)
        {
            this.profile = profile != null ? profile : ScriptableObject.CreateInstance<BrightnessQualifierModuleProfile>();
            window = new float[Mathf.Max(1, this.profile.SmoothingSamples)];
        }

        /// <inheritdoc />
        public QualityMetric Evaluate(Color32[] pixels, int width, int height)
        {
            double sum = 0;
            foreach (var pixel in pixels)
            {
                sum += 0.2126 * pixel.r + 0.7152 * pixel.g + 0.0722 * pixel.b;
            }

            var luma = (float)(sum / pixels.Length);

            window[windowIndex] = luma;
            windowIndex = (windowIndex + 1) % window.Length;
            windowCount = Mathf.Min(windowCount + 1, window.Length);

            float smoothed = 0;
            for (var i = 0; i < windowCount; i++)
            {
                smoothed += window[i];
            }

            smoothed /= windowCount;

            var ok = smoothed >= profile.MinLuma && smoothed <= profile.MaxLuma;
            // Score: distance into the acceptable band, 1.0 at the centre.
            var mid = (profile.MinLuma + profile.MaxLuma) * 0.5f;
            var halfBand = Mathf.Max(1f, (profile.MaxLuma - profile.MinLuma) * 0.5f);
            var score = Mathf.Clamp01(1f - Mathf.Abs(smoothed - mid) / halfBand);

            var detail = ok ? $"luma {smoothed:0}" : smoothed < profile.MinLuma ? $"too dark ({smoothed:0})" : $"blown out ({smoothed:0})";
            return new QualityMetric("brightness", score, ok, detail);
        }
    }
}
