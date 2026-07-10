// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// Raw per-frame environment depth from the headset (Meta's Depth API, surfaced
    /// through the Unity OpenXR provider as <see cref="AROcclusionManager"/> /
    /// <c>XR_META_environment_depth</c>). Gives the true metric distance to whatever
    /// the passthrough camera sees at a detection's centre pixel — <b>no room scan
    /// required</b> — which is the accurate fix for the anchored renderer's
    /// "object placed in my face" symptom (a coarse scene-mesh raycast hitting the
    /// nearest surface instead of the object).
    ///
    /// Sampling only ever reads the <i>interior</i> of a detection (its centre),
    /// where depth is locally smooth, so a few pixels of reprojection error barely
    /// move the result — that is what makes the single-build approach robust without
    /// on-device iteration. Every sample is logged (first dozen) so a log pull
    /// confirms the mapping, and callers fall back to the scene mesh then a fixed
    /// distance whenever a sample is unavailable or implausible.
    ///
    /// The CPU depth image is <see cref="XRCpuImage.Format.DepthUint16"/> — linear
    /// depth in millimetres. The world→pixel projection follows AR Foundation's own
    /// occlusion reprojection (<c>ARShaderOcclusion</c>): a view matrix built as
    /// <c>TRS(pose, rot, (1,1,-1)).inverse</c> and an asymmetric-frustum projection
    /// from the depth frame's <see cref="XRFov"/>.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class EnvironmentDepthProvider : MonoBehaviour
    {
        /// <summary>The live provider, or null when depth is unavailable/unbuilt.</summary>
        public static EnvironmentDepthProvider Instance { get; private set; }

        // Plausible metric window — outside this a sample is treated as invalid so the
        // caller drops to the scene-mesh / fixed-distance fallback.
        private const float MinValidMeters = 0.15f;
        private const float MaxValidMeters = 12f;

        // Point along the capture ray used to pick the depth pixel. Any positive
        // distance projects to nearly the same pixel (the depth and passthrough
        // cameras sit centimetres apart on the headset); 2 m keeps parallax error
        // negligible for arm's-length-and-beyond objects.
        private const float ProbeDistanceMeters = 2f;

        private AROcclusionManager occlusion;
        private Matrix4x4 depthViewProjection;
        private bool hasViewProjection;

        // Open sample state (between TryBeginSample / EndSample).
        private XRCpuImage sampleImage;
        private bool sampleOpen;
        private XRCpuImage.Plane samplePlane;
        private int sampleWidth;
        private int sampleHeight;
        private XRCpuImage.Format sampleFormat;
        private readonly byte[] floatScratch = new byte[4];

        private int sampleLogBudget = 12; // log the first N samples, then go quiet
        private bool firstFrameLogged;

        /// <summary>Depth is initialized, the subsystem is running, and a frame's projection is cached.</summary>
        public bool IsAvailable =>
            occlusion != null && occlusion.subsystem != null && occlusion.subsystem.running && hasViewProjection;

        public void Initialize(Camera camera)
        {
            Instance = this;

            if (camera == null)
            {
                Debug.LogWarning("[QVS:Depth] No camera supplied — environment depth disabled (anchored mode will use scene-mesh / fixed fallback).");
                return;
            }

            occlusion = camera.GetComponent<AROcclusionManager>();
            if (occlusion == null)
            {
                occlusion = camera.gameObject.AddComponent<AROcclusionManager>();
            }

            occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Medium;
            occlusion.environmentDepthTemporalSmoothingRequested = true;
            occlusion.frameReceived += OnFrameReceived;

            Debug.Log("[QVS:Depth] AROcclusionManager attached — requesting environment depth (Medium + temporal smoothing). No room scan needed for depth.");
        }

        private void OnDestroy()
        {
            if (occlusion != null)
            {
                occlusion.frameReceived -= OnFrameReceived;
            }

            EndSample();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnFrameReceived(AROcclusionFrameEventArgs args)
        {
            if (!args.TryGetPoses(out var poses) ||
                !args.TryGetFovs(out var fovs) ||
                !args.TryGetNearFarPlanes(out var planes) ||
                poses.Count == 0 || fovs.Count == 0)
            {
                // ARCore/ARKit/Simulation return nothing here; Meta supplies all three.
                return;
            }

            // Eye 0 (left). The CPU depth image is assumed to share this view.
            depthViewProjection = BuildViewProjection(poses[0], fovs[0], planes);

            if (!firstFrameLogged)
            {
                firstFrameLogged = true;
                hasViewProjection = true;
                Debug.Log($"[QVS:Depth] First depth frame — views={poses.Count}, nearZ={planes.nearZ:0.###}m farZ={planes.farZ:0.#}m. Depth is live.");
            }
        }

        // ARShaderOcclusion convention (its GetViewProjectionMatrix): view looks down
        // -Z via the (1,1,-1) scale, projection is an asymmetric frustum from the fov.
        // Only x/y matter here (we read a pixel, not a depth-buffer z), so the z row is
        // a placeholder; w = -z_view drives the perspective divide.
        private static Matrix4x4 BuildViewProjection(Pose pose, XRFov fov, XRNearFarPlanes planes)
        {
            var view = Matrix4x4.TRS(pose.position, pose.rotation, new Vector3(1f, 1f, -1f)).inverse;

            float near = planes.nearZ > 0f ? planes.nearZ : 0.1f;
            float l = Mathf.Tan(-Mathf.Abs(fov.angleLeft)) * near;
            float r = Mathf.Tan(Mathf.Abs(fov.angleRight)) * near;
            float b = Mathf.Tan(-Mathf.Abs(fov.angleDown)) * near;
            float t = Mathf.Tan(Mathf.Abs(fov.angleUp)) * near;

            var proj = new Matrix4x4();
            proj.SetRow(0, new Vector4(2f * near / (r - l), 0f, (r + l) / (r - l), 0f));
            proj.SetRow(1, new Vector4(0f, 2f * near / (t - b), (t + b) / (t - b), 0f));
            proj.SetRow(2, new Vector4(0f, 0f, -1f, -2f * near)); // z unused for pixel lookup
            proj.SetRow(3, new Vector4(0f, 0f, -1f, 0f));          // w = -z_view

            return proj * view;
        }

        /// <summary>
        /// Acquire the latest depth image for a burst of samples (one acquire per
        /// arrival, not per detection). MUST be paired with <see cref="EndSample"/>.
        /// </summary>
        public bool TryBeginSample()
        {
            EndSample();

            if (!IsAvailable || !occlusion.TryAcquireEnvironmentDepthCpuImage(out sampleImage))
            {
                return false;
            }

            sampleWidth = sampleImage.width;
            sampleHeight = sampleImage.height;
            sampleFormat = sampleImage.format;

            try
            {
                samplePlane = sampleImage.GetPlane(0);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QVS:Depth] GetPlane failed: {e.Message}");
                sampleImage.Dispose();
                return false;
            }

            sampleOpen = true;
            return true;
        }

        /// <summary>Release the current sample image. Safe to call when none is open.</summary>
        public void EndSample()
        {
            if (!sampleOpen)
            {
                return;
            }

            sampleOpen = false;
            sampleImage.Dispose();
        }

        /// <summary>
        /// Metric depth (m) of the environment along <paramref name="captureRay"/>,
        /// read at the ray's projection into the current depth frame. Returns false
        /// when no sample is open, the ray falls outside the depth image, or the value
        /// is missing/implausible — the caller then falls back.
        /// </summary>
        public bool TryGetDepthMeters(Ray captureRay, out float meters)
        {
            meters = 0f;
            if (!sampleOpen)
            {
                return false;
            }

            var world = captureRay.origin + captureRay.direction * ProbeDistanceMeters;
            var clip = depthViewProjection * new Vector4(world.x, world.y, world.z, 1f);
            if (clip.w <= 0f)
            {
                return false; // behind the depth camera
            }

            float u = (clip.x / clip.w) * 0.5f + 0.5f;
            float v = (clip.y / clip.w) * 0.5f + 0.5f;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                return false; // outside the depth field of view
            }

            int px = Mathf.Clamp((int)(u * (sampleWidth - 1)), 0, sampleWidth - 1);
            int py = Mathf.Clamp((int)((1f - v) * (sampleHeight - 1)), 0, sampleHeight - 1); // image row 0 = top

            bool read = TryReadMeters(px, py, out meters);
            bool valid = read && meters >= MinValidMeters && meters <= MaxValidMeters;

            if (sampleLogBudget > 0)
            {
                sampleLogBudget--;
                Debug.Log($"[QVS:Depth] sample uv=({u:0.00},{v:0.00}) px=({px},{py})/{sampleWidth}x{sampleHeight} fmt={sampleFormat} -> {meters:0.00}m {(valid ? "ok" : "reject")}");
            }

            return valid;
        }

        private bool TryReadMeters(int px, int py, out float meters)
        {
            meters = 0f;

            var data = samplePlane.data;
            int idx = py * samplePlane.rowStride + px * samplePlane.pixelStride;

            switch (sampleFormat)
            {
                case XRCpuImage.Format.DepthUint16:
                    if (idx + 1 >= data.Length)
                    {
                        return false;
                    }

                    ushort mm = (ushort)(data[idx] | (data[idx + 1] << 8)); // little-endian
                    if (mm == 0)
                    {
                        return false; // 0 = no depth at this pixel
                    }

                    meters = mm / 1000f;
                    return true;

                case XRCpuImage.Format.DepthFloat32:
                    if (idx + 3 >= data.Length)
                    {
                        return false;
                    }

                    floatScratch[0] = data[idx];
                    floatScratch[1] = data[idx + 1];
                    floatScratch[2] = data[idx + 2];
                    floatScratch[3] = data[idx + 3];
                    meters = BitConverter.ToSingle(floatScratch, 0);
                    return meters > 0f;

                default:
                    return false;
            }
        }
    }
}
