// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using AprilTag;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Modules;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="KeijiroAprilTagDetectorModule"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Keijiro AprilTag Detector Module Profile", fileName = "KeijiroAprilTagDetectorModuleProfile")]
    public class KeijiroAprilTagDetectorModuleProfile : BaseProfile
    {
        [SerializeField]
        [Range(1, 4)]
        [Tooltip("Detector quad decimation — higher is faster, shorter range. 2 is Keijiro's default.")]
        private int decimation = 2;

        [SerializeField]
        [Tooltip("Physical printed tag width in meters — the detector needs it to estimate pose distance. Must match the printed tags.")]
        private float tagSizeMeters = 0.1f;

        public int Decimation { get => decimation; set => decimation = value; }
        public float TagSizeMeters { get => tagSizeMeters; set => tagSizeMeters = value; }
    }

    /// <summary>Registration interface for <see cref="KeijiroAprilTagDetectorModule"/> — every module registers under its own interface (the SF registry forbids duplicate interface registrations).</summary>
    public interface IKeijiroAprilTagDetectorModule : ITagDetectorModule
    {
    }

    /// <summary>
    /// <see cref="ITagDetectorModule"/> over <c>jp.keijiro.apriltag</c> (the
    /// official AprilTag C library with Unity job-system pose estimation).
    ///
    /// NOTE: this backend decodes the <b>tagStandard41h12</b> family only — print
    /// tags with <c>V2/tools/generate-apriltags.py</c> (41h12 mode), not the old
    /// 36h11 sheets from the WebXR client era. This assembly only compiles when
    /// <c>jp.keijiro.apriltag</c> is installed (see the asmdef versionDefines).
    /// </summary>
    [System.Runtime.InteropServices.Guid("45be85cb-7b19-45fc-bb8d-a74da03f0b9e")]
    public class KeijiroAprilTagDetectorModule : BaseServiceModule, IKeijiroAprilTagDetectorModule
    {
        private readonly KeijiroAprilTagDetectorModuleProfile profile;
        private readonly List<(int id, Pose cameraSpacePose)> resultBuffer = new List<(int, Pose)>();
        private TagDetector detector;
        private int detectorWidth;
        private int detectorHeight;
        private bool nativeUnavailable;

        public KeijiroAprilTagDetectorModule(
            string name,
            uint priority,
            KeijiroAprilTagDetectorModuleProfile profile,
            ITagDetectionService parentService)
            : base(name, priority, profile, parentService)
        {
            this.profile = profile != null ? profile : ScriptableObject.CreateInstance<KeijiroAprilTagDetectorModuleProfile>();
        }

        /// <inheritdoc />
        public bool IsAvailable => !nativeUnavailable;

        /// <inheritdoc />
        public string DictionaryName => "tagStandard41h12";

        /// <inheritdoc />
        public IReadOnlyList<(int id, Pose cameraSpacePose)> Detect(Color32[] pixels, int width, int height, float verticalFovRadians)
        {
            resultBuffer.Clear();

            if (nativeUnavailable || pixels == null || pixels.Length < width * height)
            {
                return resultBuffer;
            }

            try
            {
                if (detector == null || detectorWidth != width || detectorHeight != height)
                {
                    detector?.Dispose();
                    detector = new TagDetector(width, height, profile.Decimation);
                    detectorWidth = width;
                    detectorHeight = height;
                }

                detector.ProcessImage(pixels, verticalFovRadians, profile.TagSizeMeters);

                foreach (var tag in detector.DetectedTags)
                {
                    resultBuffer.Add((tag.ID, new Pose(tag.Position, tag.Rotation)));
                }
            }
            catch (DllNotFoundException)
            {
                nativeUnavailable = true;
                Debug.LogWarning("[QVS:Tags] AprilTag native library missing on this platform — Keijiro detector disabled.");
            }
            catch (EntryPointNotFoundException)
            {
                nativeUnavailable = true;
                Debug.LogWarning("[QVS:Tags] AprilTag native entry points missing — Keijiro detector disabled.");
            }

            return resultBuffer;
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            detector?.Dispose();
            detector = null;
            base.Destroy();
        }
    }
}
