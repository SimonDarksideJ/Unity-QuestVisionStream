// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Modules;
using QuestVisionStream.Services;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace QuestVisionStream.Client
{
    /// <summary>Registration interface for <see cref="MetaPassthroughCameraCaptureModule"/> — every module registers under its own interface (the SF registry forbids duplicate interface registrations).</summary>
    public interface IMetaPassthroughCameraCaptureModule : ICameraCaptureModule
    {
    }

    /// <summary>
    /// <see cref="ICameraCaptureModule"/> for Quest 3/3S passthrough via the
    /// Unity OpenXR Meta AR Foundation camera path (<see cref="ARCameraManager"/> +
    /// CPU images). Plain <c>WebCamTexture</c> does NOT work on Quest 3 — camera
    /// frames must come through Meta's passthrough camera access, which under the
    /// OpenXR plugin is surfaced by the "Meta Quest: Camera (Passthrough)" OpenXR
    /// feature (enabled in this project, with Camera Image Support on).
    ///
    /// Requires both <c>android.permission.CAMERA</c> and
    /// <c>horizonos.permission.HEADSET_CAMERA</c> — declared in the manifest and
    /// requested here at runtime.
    /// </summary>
    [System.Runtime.InteropServices.Guid("1e5820b0-8fc6-488e-b906-375dc18767a9")]
    public class MetaPassthroughCameraCaptureModule : BaseServiceModule, IMetaPassthroughCameraCaptureModule
    {
        private const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        private ARCameraManager cameraManager;
        private Texture2D frameTexture;
        private CameraStreamState state = CameraStreamState.Idle;
        private bool permissionsRequested;
        private bool configurationChosen;
        private float permissionDeniedAtRealtime = -1f;

        public MetaPassthroughCameraCaptureModule(
            string name,
            uint priority,
            BaseProfile profile,
            ICameraStreamService parentService)
            : base(name, priority, profile, parentService)
        {
        }

        public CameraStreamState State => state;

        public Texture SourceTexture => state == CameraStreamState.Active ? frameTexture : null;

        public Vector2Int SourceResolution => frameTexture != null
            ? new Vector2Int(frameTexture.width, frameTexture.height)
            : Vector2Int.zero;

        public string LastError { get; private set; }

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();
            state = CameraStreamState.Waiting;
            RequestPermissions();
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            if (state == CameraStreamState.Error || state == CameraStreamState.Idle)
            {
                return;
            }

            if (!HasPermissions())
            {
                // Permission dialogs have no callback we can rely on across OS
                // versions — poll, and give up into Error state only when the user
                // actively denied (flagged by the callbacks below).
                return;
            }

            if (cameraManager == null)
            {
                cameraManager = UnityEngine.Object.FindFirstObjectByType<ARCameraManager>();
                if (cameraManager == null)
                {
                    return; // rig not built yet
                }

                cameraManager.frameReceived += OnFrameReceived;
            }

            if (!configurationChosen)
            {
                configurationChosen = TryChooseSmallestConfiguration();
            }
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnFrameReceived;
                cameraManager = null;
            }

            if (frameTexture != null)
            {
                UnityEngine.Object.Destroy(frameTexture);
                frameTexture = null;
            }

            base.Destroy();
        }

        private void OnFrameReceived(ARCameraFrameEventArgs args)
        {
            if (cameraManager == null || !cameraManager.TryAcquireLatestCpuImage(out var image))
            {
                return;
            }

            using (image)
            {
                var conversionParams = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32);

                if (frameTexture == null ||
                    frameTexture.width != conversionParams.outputDimensions.x ||
                    frameTexture.height != conversionParams.outputDimensions.y)
                {
                    if (frameTexture != null)
                    {
                        UnityEngine.Object.Destroy(frameTexture);
                    }

                    frameTexture = new Texture2D(
                        conversionParams.outputDimensions.x,
                        conversionParams.outputDimensions.y,
                        TextureFormat.RGBA32,
                        false);
                }

                NativeArray<byte> buffer = frameTexture.GetRawTextureData<byte>();
                try
                {
                    image.Convert(conversionParams, buffer);
                }
                catch (Exception e)
                {
                    LastError = $"CPU image conversion failed: {e.Message}";
                    state = CameraStreamState.Error;
                    return;
                }

                frameTexture.Apply(false);
            }

            if (state != CameraStreamState.Active)
            {
                Debug.Log($"[QVS:MetaCamera] Passthrough camera delivering {frameTexture.width}x{frameTexture.height}");
                state = CameraStreamState.Active;
            }
        }

        /// <summary>
        /// The passthrough camera offers multiple capture resolutions; the smallest
        /// is plenty for a 640x480 stream and keeps the per-frame CPU conversion cheap.
        /// </summary>
        private bool TryChooseSmallestConfiguration()
        {
            var subsystem = cameraManager.subsystem;
            if (subsystem == null || !subsystem.running)
            {
                return false;
            }

            using (var configurations = cameraManager.GetConfigurations(Allocator.Temp))
            {
                if (!configurations.IsCreated || configurations.Length == 0)
                {
                    return false;
                }

                var best = configurations[0];
                foreach (var configuration in configurations)
                {
                    if (configuration.width * configuration.height < best.width * best.height)
                    {
                        best = configuration;
                    }
                }

                try
                {
                    cameraManager.currentConfiguration = best;
                    Debug.Log($"[QVS:MetaCamera] Capture configuration: {best.width}x{best.height} @{best.framerate ?? 0}fps");
                }
                catch (Exception e)
                {
                    // Not fatal — the default configuration still streams, just costs more CPU.
                    Debug.LogWarning($"[QVS:MetaCamera] Could not set camera configuration: {e.Message}");
                }
            }

            return true;
        }

        private void RequestPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (permissionsRequested)
            {
                return;
            }

            permissionsRequested = true;

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionDenied += permission =>
            {
                LastError = $"Permission denied: {permission}";
                state = CameraStreamState.Error;
            };

            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera, callbacks);
            }

            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                Permission.RequestUserPermission(HeadsetCameraPermission, callbacks);
            }
#else
            permissionsRequested = true;
#endif
        }

        private bool HasPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Camera) &&
                   Permission.HasUserAuthorizedPermission(HeadsetCameraPermission);
#else
            return true;
#endif
        }
    }
}
