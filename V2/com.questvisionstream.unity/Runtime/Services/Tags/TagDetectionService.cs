// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using QuestVisionStream.Core;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="TagDetectionService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Tag Detection Service Profile", fileName = "TagDetectionServiceProfile")]
    public class TagDetectionServiceProfile : RealityCollective.ServiceFramework.Definitions.BaseServiceProfile<ITagDetectorModule>
    {
        [SerializeField]
        [Tooltip("Decode throttle in ms (the WebXR client used 120).")]
        private float detectIntervalMs = 120f;

        [SerializeField]
        [Tooltip("Frame size handed to the detector. 640x480 balances range vs decode cost.")]
        private Vector2Int sampleSize = new Vector2Int(640, 480);

        [SerializeField]
        [Tooltip("Vertical FoV (degrees) of the CAPTURE camera used for pose estimation. 0 = use Camera.main. Set from passthrough camera intrinsics for accurate distances.")]
        private float verticalFovOverrideDegrees = 0f;

        public float DetectIntervalMs { get => detectIntervalMs; set => detectIntervalMs = value; }
        public Vector2Int SampleSize { get => sampleSize; set => sampleSize = value; }
        public float VerticalFovOverrideDegrees { get => verticalFovOverrideDegrees; set => verticalFovOverrideDegrees = value; }
    }

    /// <summary>
    /// <see cref="ITagDetectionService"/>: every <c>DetectIntervalMs</c> samples the
    /// camera texture, snapshots the head pose at REQUEST time (the readback lands
    /// a frame or two later — the pose must match the pixels), runs the active
    /// detector module and publishes world-posed sightings.
    /// </summary>
    [System.Runtime.InteropServices.Guid("650070bb-c559-48a3-af5b-359063d7a6d7")]
    public class TagDetectionService : BaseServiceWithConstructor, ITagDetectionService
    {
        private readonly TagDetectionServiceProfile profile;
        private readonly ICameraStreamService camera;
        private readonly TextureSampler sampler = new TextureSampler();
        private float nextDetectRealtime;
        private bool warnedUnavailable;

        public TagDetectionService(
            string name,
            uint priority,
            TagDetectionServiceProfile profile,
            ICameraStreamService camera)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        public event Action<IReadOnlyList<DetectedTag>, double> TagsDetected;

        public bool DetectorAvailable => ActiveDetector != null;

        private ITagDetectorModule ActiveDetector
        {
            get
            {
                foreach (var module in ServiceModules)
                {
                    if (module is ITagDetectorModule detector && detector.IsAvailable)
                    {
                        return detector;
                    }
                }

                return null;
            }
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            if (camera.State != CameraStreamState.Active)
            {
                return;
            }

            var detector = ActiveDetector;
            if (detector == null)
            {
                if (!warnedUnavailable)
                {
                    warnedUnavailable = true;
                    Debug.LogWarning("[QVS:Tags] No tag detector module available — tag detection disabled.");
                }
                return;
            }

            var now = Time.realtimeSinceStartup;
            if (now < nextDetectRealtime)
            {
                return;
            }

            nextDetectRealtime = now + profile.DetectIntervalMs / 1000f;

            // Pose + FoV captured NOW; the readback callback fires a frame or two
            // later and must not use the then-current head pose.
            var viewer = Camera.main;
            if (viewer == null)
            {
                return;
            }

            var cameraPose = new Pose(viewer.transform.position, viewer.transform.rotation);
            var fovDegrees = profile.VerticalFovOverrideDegrees > 0
                ? profile.VerticalFovOverrideDegrees
                : viewer.fieldOfView;
            var fovRadians = fovDegrees * Mathf.Deg2Rad;
            var sampleTimeMs = Time.realtimeSinceStartupAsDouble * 1000.0;

            sampler.TrySample(camera.SourceTexture, profile.SampleSize.x, profile.SampleSize.y, (pixels, width, height) =>
            {
                var cameraSpaceTags = detector.Detect(pixels, width, height, fovRadians);
                if (cameraSpaceTags == null)
                {
                    return;
                }

                var worldTags = new List<DetectedTag>(cameraSpaceTags.Count);
                foreach (var (id, cameraSpacePose) in cameraSpaceTags)
                {
                    var worldPose = new Pose(
                        cameraPose.position + cameraPose.rotation * cameraSpacePose.position,
                        cameraPose.rotation * cameraSpacePose.rotation);
                    worldTags.Add(new DetectedTag(id, worldPose));
                }

                TagsDetected?.Invoke(worldTags, sampleTimeMs);
            });
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            sampler.Dispose();
            base.Destroy();
        }
    }
}
