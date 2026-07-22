// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="TagDetectionBridgeService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Tag Detection Bridge Service Profile", fileName = "TagDetectionBridgeServiceProfile")]
    public class TagDetectionBridgeServiceProfile : RealityCollective.ServiceFramework.Definitions.BaseProfile
    {
        [SerializeField]
        [Tooltip("Also republish Update sightings (throttled below). Enter sightings always publish — they are what advances the training flow.")]
        private bool publishUpdates = true;

        [SerializeField]
        [Tooltip("Per-tag minimum interval (ms) between Update republishes, so a steadily visible tag doesn't flood the pipeline.")]
        private float updateIntervalMs = 500f;

        [SerializeField]
        [Range(0.01f, 0.5f)]
        [Tooltip("Edge of the synthesized viewport-space box around the tag centre (0..1 of the view) — detection consumers need a box; tags only have a point + pose.")]
        private float boxViewportSize = 0.08f;

        public bool PublishUpdates { get => publishUpdates; set => publishUpdates = value; }
        public float UpdateIntervalMs { get => updateIntervalMs; set => updateIntervalMs = value; }
        public float BoxViewportSize { get => boxViewportSize; set => boxViewportSize = value; }
    }

    /// <summary>
    /// <see cref="ITagDetectionBridgeService"/>: subscribes to the tag routing
    /// events and republishes each sighting into the detection pipeline as a
    /// ClassName detection (label = registry class name, conf 1.0, box projected
    /// around the tag's viewport position). The clean delineation holds — tag
    /// services detect, this bridge translates, the detection service exposes,
    /// the training engine decides, placement instantiates.
    /// </summary>
    [System.Runtime.InteropServices.Guid("5b4f0e6a-2c81-4d15-9f3e-7a90d3c4b1a2")]
    public class TagDetectionBridgeService : BaseServiceWithConstructor, ITagDetectionBridgeService
    {
        private readonly TagDetectionBridgeServiceProfile profile;
        private readonly ITagRoutingService routing;
        private readonly IDetectionService detections;
        private readonly Dictionary<int, double> lastPublishedMs = new Dictionary<int, double>();

        public TagDetectionBridgeService(
            string name,
            uint priority,
            TagDetectionBridgeServiceProfile profile,
            ITagRoutingService routing,
            IDetectionService detections)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.routing = routing ?? throw new ArgumentNullException(nameof(routing));
            this.detections = detections ?? throw new ArgumentNullException(nameof(detections));
        }

        public long PublishedCount { get; private set; }

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();
            routing.TagEntered += OnTagEntered;
            routing.TagUpdated += OnTagUpdated;
            routing.TagExited += OnTagExited;
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            routing.TagEntered -= OnTagEntered;
            routing.TagUpdated -= OnTagUpdated;
            routing.TagExited -= OnTagExited;
            lastPublishedMs.Clear();
            base.Destroy();
        }

        private void OnTagEntered(TagObservation observation) => Publish(observation, force: true);

        private void OnTagUpdated(TagObservation observation)
        {
            if (!profile.PublishUpdates)
            {
                return;
            }

            Publish(observation, force: false);
        }

        private void OnTagExited(TagObservation observation) => lastPublishedMs.Remove(observation.Id);

        private void Publish(TagObservation observation, bool force)
        {
            var nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
            if (!force &&
                lastPublishedMs.TryGetValue(observation.Id, out var last) &&
                nowMs - last < profile.UpdateIntervalMs)
            {
                return;
            }

            // Project the tag's world pose into the CURRENT view — the sighting is
            // fresh (this tick), so the live camera matches the sample closely
            // enough for a viewport box.
            var viewer = Camera.main;
            if (viewer == null)
            {
                return;
            }

            var viewport = viewer.WorldToViewportPoint(observation.WorldPose.position);
            if (viewport.z <= 0f)
            {
                return;
            }

            lastPublishedMs[observation.Id] = nowMs;

            var center = new Vector2(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y));
            var size = new Vector2(profile.BoxViewportSize, profile.BoxViewportSize);
            var json = TagDetectionMessage.ToDetectionsJson(observation.ClassName, center, size);
            detections.PublishLocal(json, DetectionOrigin.AprilTag);
            PublishedCount++;
        }
    }
}
