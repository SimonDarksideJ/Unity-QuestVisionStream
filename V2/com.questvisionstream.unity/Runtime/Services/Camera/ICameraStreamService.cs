// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using RealityCollective.ServiceFramework.Interfaces;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Lifecycle state of the camera capture pipeline.</summary>
    public enum CameraStreamState
    {
        /// <summary>No capture module registered / nothing started.</summary>
        Idle = 0,

        /// <summary>Waiting for permission or for the device to start delivering frames.</summary>
        Waiting,

        /// <summary>Frames are flowing — <see cref="ICameraCaptureModule.SourceTexture"/> is live.</summary>
        Active,

        /// <summary>Terminal failure (permission denied, no device). Surfaced, never silent.</summary>
        Error
    }

    /// <summary>
    /// A camera source implementation. The package is capture-agnostic; the Quest
    /// app registers a module wrapping Meta's Passthrough Camera API
    /// (WebCamTexture), tests can register a static-texture module, other XR
    /// devices can bring their own.
    /// </summary>
    public interface ICameraCaptureModule : IServiceModule
    {
        CameraStreamState State { get; }

        /// <summary>The live source texture while <see cref="State"/> is Active, else null.</summary>
        Texture SourceTexture { get; }

        /// <summary>Native resolution of the source, valid while Active.</summary>
        Vector2Int SourceResolution { get; }

        /// <summary>Terminal error detail while <see cref="State"/> is Error.</summary>
        string LastError { get; }

        /// <summary>
        /// Try to build a projection matrix from the camera's real intrinsics (focal
        /// length / principal point). Detections are unprojected through THIS — the
        /// capture camera's field of view — so boxes align with the passthrough image
        /// instead of being over-scaled by the much wider display-eye FOV. Returns
        /// false when intrinsics are not yet available.
        /// </summary>
        bool TryGetProjectionMatrix(out Matrix4x4 projection);
    }

    /// <summary>
    /// Owns camera acquisition through swappable <see cref="ICameraCaptureModule"/>s
    /// and derives the capped stream resolution the encoder pipeline uses.
    /// </summary>
    public interface ICameraStreamService : IService
    {
        /// <summary>Raised on the main thread whenever the effective state changes.</summary>
        event Action<CameraStreamState> StateChanged;

        CameraStreamState State { get; }

        /// <summary>The module currently providing frames (the first registered module that isn't Idle).</summary>
        ICameraCaptureModule ActiveModule { get; }

        /// <summary>Live source texture, or null when not Active.</summary>
        Texture SourceTexture { get; }

        /// <summary>
        /// The resolution frames are streamed at: the source aspect fitted inside the
        /// profile's maximum (default 640x480), even-aligned for I420.
        /// </summary>
        Vector2Int StreamResolution { get; }

        /// <summary>Terminal error detail when <see cref="State"/> is Error.</summary>
        string LastError { get; }

        /// <summary>
        /// The active capture module's intrinsics-based projection (see
        /// <see cref="ICameraCaptureModule.TryGetProjectionMatrix"/>). False when unavailable.
        /// </summary>
        bool TryGetCameraProjection(out Matrix4x4 projection);
    }
}
