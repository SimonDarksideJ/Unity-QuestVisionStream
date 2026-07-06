// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="TagRoutingService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Tag Routing Service Profile", fileName = "TagRoutingServiceProfile")]
    public class TagRoutingServiceProfile : RealityCollective.ServiceFramework.Definitions.BaseProfile
    {
        [SerializeField]
        [Tooltip("The known-tag registry. When null, the built-in Alpha…Juliet default is used.")]
        private TagRegistryAsset registry;

        [SerializeField]
        [Tooltip("Only surface tags present in the registry (suppresses decoder false-positives).")]
        private bool knownTagsOnly = true;

        [SerializeField]
        [Tooltip("A tracked tag unseen for this long (ms) emits Exit and is forgotten.")]
        private float tagTtlMs = 8000f;

        public TagRegistryAsset Registry { get => registry; set => registry = value; }
        public bool KnownTagsOnly { get => knownTagsOnly; set => knownTagsOnly = value; }
        public float TagTtlMs { get => tagTtlMs; set => tagTtlMs = value; }
    }

    /// <summary>
    /// <see cref="ITagRoutingService"/> — ported from the V2 IWSDK
    /// <c>AprilTagRoutingService</c>: diff per tick → enter/update, TTL sweep →
    /// exit, registry filter, rules engine (rule exceptions are contained, and
    /// rules may add rules — sequences compose).
    /// </summary>
    [System.Runtime.InteropServices.Guid("27c8b70a-ae9b-44db-9321-ba5347ad540f")]
    public class TagRoutingService : BaseServiceWithConstructor, ITagRoutingService
    {
        private readonly TagRoutingServiceProfile profile;
        private readonly ITagDetectionService detection;
        private readonly Dictionary<int, TagObservation> tracked = new Dictionary<int, TagObservation>();
        private readonly Dictionary<string, TagRule> rules = new Dictionary<string, TagRule>(StringComparer.OrdinalIgnoreCase);
        private readonly List<int> expiredBuffer = new List<int>();
        private TagRegistryAsset registry;
        private double lastTickMs;

        public TagRoutingService(
            string name,
            uint priority,
            TagRoutingServiceProfile profile,
            ITagDetectionService detection)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.detection = detection ?? throw new ArgumentNullException(nameof(detection));
        }

        public event Action<TagObservation> TagEntered;
        public event Action<TagObservation> TagUpdated;
        public event Action<TagObservation> TagExited;

        public IReadOnlyCollection<TagObservation> TrackedTags => tracked.Values;

        private TagRegistryAsset Registry
        {
            get
            {
                if (registry == null)
                {
                    registry = profile.Registry != null ? profile.Registry : TagRegistryAsset.CreateDefault();
                }

                return registry;
            }
        }

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();
            detection.TagsDetected += OnTagsDetected;
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            detection.TagsDetected -= OnTagsDetected;
            tracked.Clear();
            rules.Clear();
            base.Destroy();
        }

        /// <inheritdoc />
        public void AddRule(TagRule rule)
        {
            if (rule == null || string.IsNullOrEmpty(rule.Id) || rule.Run == null)
            {
                Debug.LogWarning("[QVS:TagRouting] Ignoring invalid rule (needs Id and Run)");
                return;
            }

            rules[rule.Id] = rule;
        }

        /// <inheritdoc />
        public void RemoveRule(string ruleId) => rules.Remove(ruleId);

        /// <inheritdoc />
        public void ForgetAll() => tracked.Clear();

        private void OnTagsDetected(IReadOnlyList<DetectedTag> sightings, double timeMs)
        {
            lastTickMs = timeMs;
            var seen = new HashSet<int>();

            foreach (var sighting in sightings)
            {
                // Drop decoder false-positives (unregistered ids) unless configured
                // to surface everything.
                if (profile.KnownTagsOnly && !Registry.IsKnown(sighting.Id))
                {
                    continue;
                }

                seen.Add(sighting.Id);

                if (tracked.TryGetValue(sighting.Id, out var existing))
                {
                    existing.WorldPose = sighting.WorldPose;
                    existing.LastSeenMs = timeMs;
                    Dispatch(TagLifecycleEvent.Update, existing);
                }
                else
                {
                    var info = Registry.GetInfo(sighting.Id);
                    var observation = new TagObservation
                    {
                        Id = sighting.Id,
                        TagName = info.Name,
                        Color = info.Color,
                        WorldPose = sighting.WorldPose,
                        FirstSeenMs = timeMs,
                        LastSeenMs = timeMs
                    };
                    tracked[sighting.Id] = observation;
                    Debug.Log($"[QVS:TagRouting] Tag entered: {info.Name} (#{sighting.Id})");
                    Dispatch(TagLifecycleEvent.Enter, observation);
                }
            }

            // TTL sweep — expire tags unseen past the TTL.
            expiredBuffer.Clear();
            foreach (var pair in tracked)
            {
                if (!seen.Contains(pair.Key) && timeMs - pair.Value.LastSeenMs > profile.TagTtlMs)
                {
                    expiredBuffer.Add(pair.Key);
                }
            }

            foreach (var id in expiredBuffer)
            {
                var observation = tracked[id];
                tracked.Remove(id);
                Debug.Log($"[QVS:TagRouting] Tag exited: {observation.TagName} (#{id})");
                Dispatch(TagLifecycleEvent.Exit, observation);
            }
        }

        private void Dispatch(TagLifecycleEvent phase, TagObservation observation)
        {
            switch (phase)
            {
                case TagLifecycleEvent.Enter: TagEntered?.Invoke(observation); break;
                case TagLifecycleEvent.Update: TagUpdated?.Invoke(observation); break;
                case TagLifecycleEvent.Exit: TagExited?.Invoke(observation); break;
            }

            // Rules run after raw subscribers; a throwing rule never breaks tracking.
            // Snapshot the list so a rule adding rules doesn't invalidate iteration.
            foreach (var rule in new List<TagRule>(rules.Values))
            {
                if (rule.On != phase)
                {
                    continue;
                }

                if (rule.TagId.HasValue && rule.TagId.Value != observation.Id)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(rule.TagName) &&
                    !string.Equals(rule.TagName, observation.TagName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    rule.Run(observation);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[QVS:TagRouting] Rule '{rule.Id}' threw: {e.Message}");
                }
            }
        }
    }
}
