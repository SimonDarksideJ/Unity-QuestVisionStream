// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using RealityCollective.ServiceFramework.Interfaces;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>A tag found in one detector pass, posed in world space.</summary>
    public readonly struct DetectedTag
    {
        public DetectedTag(int id, Pose worldPose)
        {
            Id = id;
            WorldPose = worldPose;
        }

        public int Id { get; }
        public Pose WorldPose { get; }
    }

    /// <summary>
    /// A fiducial detector backend. The default is Keijiro's AprilTag package
    /// (tagStandard41h12, full 6-DoF pose estimation); the seam exists so a 36h11
    /// or OpenCV backend can be swapped in later without touching the pipeline.
    /// </summary>
    public interface ITagDetectorModule : IServiceModule
    {
        /// <summary>Is the backend usable (native lib present on this platform)?</summary>
        bool IsAvailable { get; }

        /// <summary>Human-readable dictionary name (e.g. "tagStandard41h12") — printed tags must match.</summary>
        string DictionaryName { get; }

        /// <summary>
        /// Detect tags in one RGBA frame. Returns CAMERA-space poses; the detection
        /// service converts to world space using the pose the frame was captured at.
        /// </summary>
        IReadOnlyList<(int id, Pose cameraSpacePose)> Detect(Color32[] pixels, int width, int height, float verticalFovRadians);
    }

    /// <summary>
    /// Samples the passthrough camera on an interval and runs the active detector
    /// module, publishing world-posed raw sightings. Detection is entirely
    /// on-device — nothing AprilTag-related touches the server.
    /// </summary>
    public interface ITagDetectionService : IService
    {
        /// <summary>Raw sightings from one detector pass (already world-posed), with the sample time.</summary>
        event Action<IReadOnlyList<DetectedTag>, double> TagsDetected;

        /// <summary>False when no detector module is available on this platform.</summary>
        bool DetectorAvailable { get; }
    }

    /// <summary>A live tracked tag, enriched from the registry, carried on routing events.</summary>
    public sealed class TagObservation
    {
        public int Id { get; internal set; }
        public string TagName { get; internal set; }

        /// <summary>
        /// The detection-pipeline class token this tag stands for (registry
        /// <see cref="TagDefinition.EffectiveClassName"/>). This is the identity
        /// the ClassName architecture unifies on: the bridge publishes it as the
        /// detection <c>label</c>, and training steps match it as
        /// <c>waitingClass</c>/<c>detectedClass</c>.
        /// </summary>
        public string ClassName { get; internal set; }

        public Color Color { get; internal set; }
        public Pose WorldPose { get; internal set; }
        public double FirstSeenMs { get; internal set; }
        public double LastSeenMs { get; internal set; }
    }

    /// <summary>The lifecycle phases a <see cref="TagRule"/> can trigger on.</summary>
    public enum TagLifecycleEvent
    {
        /// <summary>The tag became visible (was not tracked on the previous tick).</summary>
        Enter = 0,

        /// <summary>A tracked tag was seen again (fresh pose).</summary>
        Update,

        /// <summary>The tag went unseen past its TTL and is now gone.</summary>
        Exit
    }

    /// <summary>A "see X → do Y" rule evaluated by the routing service.</summary>
    public sealed class TagRule
    {
        /// <summary>Unique rule id (used to remove it).</summary>
        public string Id { get; set; }

        public TagLifecycleEvent On { get; set; }

        /// <summary>Optional filter: match a specific tag id.</summary>
        public int? TagId { get; set; }

        /// <summary>Optional filter: match a registry tag name (case-insensitive).</summary>
        public string TagName { get; set; }

        public Action<TagObservation> Run { get; set; }
    }

    /// <summary>
    /// The semantic layer over raw sightings: diffs each detection tick against
    /// tracked state to emit enter/update/exit (TTL-based), filters decoder
    /// false-positives via the registry, and evaluates registered rules — the
    /// "see X → do Y" primitive ported from the V2 IWSDK client.
    /// </summary>
    public interface ITagRoutingService : IService
    {
        event Action<TagObservation> TagEntered;
        event Action<TagObservation> TagUpdated;
        event Action<TagObservation> TagExited;

        void AddRule(TagRule rule);
        void RemoveRule(string ruleId);

        /// <summary>Currently tracked (visible) tags.</summary>
        IReadOnlyCollection<TagObservation> TrackedTags { get; }

        /// <summary>Drop all tracked state (recenter, scene change).</summary>
        void ForgetAll();
    }

    /// <summary>
    /// Default visual consumer of routing events: one coloured, labelled marker per
    /// visible tag at its estimated world pose, removed on exit and recenter.
    /// </summary>
    public interface ITagPlacementService : IService
    {
        /// <summary>Remove all placed markers.</summary>
        void Clear();
    }

    /// <summary>
    /// Bridges tag routing events into the detection pipeline: each sighting is
    /// republished through <see cref="IDetectionService.PublishLocal"/> as a
    /// wire-shaped detections payload whose label is the tag's registry
    /// <see cref="TagObservation.ClassName"/>. This unifies AprilTags under the
    /// ClassName architecture — tags render, log and drive the training flow
    /// exactly like server detections, and can augment or replace the ML detector
    /// entirely (offline mode). The tag services stay untouched: tags detect,
    /// this bridge only translates.
    /// </summary>
    public interface ITagDetectionBridgeService : IService
    {
        /// <summary>Payloads published into the detection pipeline this session.</summary>
        long PublishedCount { get; }
    }
}
