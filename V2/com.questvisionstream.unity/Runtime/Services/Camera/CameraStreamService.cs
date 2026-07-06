// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="CameraStreamService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Camera Stream Service Profile", fileName = "CameraStreamServiceProfile")]
    public class CameraStreamServiceProfile : BaseServiceProfile<ICameraCaptureModule>
    {
        [SerializeField]
        [Tooltip("Maximum streamed frame size; the source aspect is preserved inside this box. 640x480 matches the proven V1 pipeline.")]
        private Vector2Int maxStreamResolution = new Vector2Int(640, 480);

        public Vector2Int MaxStreamResolution { get => maxStreamResolution; set => maxStreamResolution = value; }
    }

    /// <summary>
    /// <see cref="ICameraStreamService"/>: watches its capture modules, promotes the
    /// first non-idle one to <see cref="ActiveModule"/>, diffs its state into
    /// <see cref="StateChanged"/> events, and derives the capped even-aligned
    /// stream resolution (I420 requires even dimensions).
    /// </summary>
    [System.Runtime.InteropServices.Guid("f45d88c0-ec5b-4627-a0d0-6fc3ac6e1685")]
    public class CameraStreamService : BaseServiceWithConstructor, ICameraStreamService
    {
        private readonly CameraStreamServiceProfile profile;
        private CameraStreamState lastReportedState = CameraStreamState.Idle;

        public CameraStreamService(string name, uint priority, CameraStreamServiceProfile profile)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
        }

        public event Action<CameraStreamState> StateChanged;

        public ICameraCaptureModule ActiveModule
        {
            get
            {
                ICameraCaptureModule fallback = null;
                foreach (var module in ServiceModules)
                {
                    if (module is ICameraCaptureModule capture)
                    {
                        if (capture.State != CameraStreamState.Idle)
                        {
                            return capture;
                        }

                        fallback = fallback ?? capture;
                    }
                }

                return fallback;
            }
        }

        public CameraStreamState State => ActiveModule?.State ?? CameraStreamState.Idle;

        public Texture SourceTexture => ActiveModule?.SourceTexture;

        public string LastError => ActiveModule?.LastError;

        public Vector2Int StreamResolution
        {
            get
            {
                var module = ActiveModule;
                if (module == null || module.State != CameraStreamState.Active)
                {
                    return EvenAligned(profile.MaxStreamResolution);
                }

                return ComputeStreamResolution(module.SourceResolution, profile.MaxStreamResolution);
            }
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            var state = State;
            if (state != lastReportedState)
            {
                lastReportedState = state;
                if (state == CameraStreamState.Error)
                {
                    Debug.LogError($"[QVS:Camera] Capture error: {LastError}");
                }
                else
                {
                    Debug.Log($"[QVS:Camera] State: {state}");
                }

                StateChanged?.Invoke(state);
            }
        }

        /// <summary>
        /// Fit <paramref name="source"/> inside <paramref name="max"/> preserving
        /// aspect, never upscaling, aligned down to even dimensions for I420.
        /// </summary>
        public static Vector2Int ComputeStreamResolution(Vector2Int source, Vector2Int max)
        {
            if (source.x <= 0 || source.y <= 0)
            {
                return EvenAligned(max);
            }

            var scale = Mathf.Min(1f, Mathf.Min((float)max.x / source.x, (float)max.y / source.y));
            return EvenAligned(new Vector2Int(
                Mathf.Max(2, Mathf.RoundToInt(source.x * scale)),
                Mathf.Max(2, Mathf.RoundToInt(source.y * scale))));
        }

        private static Vector2Int EvenAligned(Vector2Int size)
            => new Vector2Int(size.x & ~1, size.y & ~1);
    }
}
