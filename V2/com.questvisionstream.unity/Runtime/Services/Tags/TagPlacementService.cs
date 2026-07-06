// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;
using UnityEngine.XR;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="TagPlacementService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Tag Placement Service Profile", fileName = "TagPlacementServiceProfile")]
    public class TagPlacementServiceProfile : RealityCollective.ServiceFramework.Definitions.BaseProfile
    {
        [SerializeField]
        [Tooltip("Physical printed tag width in meters — must match the printed size for correct pose scale (~0.1 m is reliable at ~2 m).")]
        private float tagSizeMeters = 0.1f;

        [SerializeField]
        [Range(0f, 1f)]
        private float fillOpacity = 0.35f;

        [SerializeField]
        private float labelCharacterSize = 0.04f;

        public float TagSizeMeters { get => tagSizeMeters; set => tagSizeMeters = value; }
        public float FillOpacity { get => fillOpacity; set => fillOpacity = value; }
        public float LabelCharacterSize { get => labelCharacterSize; set => labelCharacterSize = value; }
    }

    /// <summary>
    /// <see cref="ITagPlacementService"/> — the Unity analogue of the IWSDK
    /// <c>AprilTagPlacementService</c>: per visible tag one colour-tinted
    /// translucent quad at the tag's estimated world pose, with an outline and a
    /// billboarded "Name · #id" label. Enter/update reposition, exit removes,
    /// recenter clears everything.
    /// </summary>
    [System.Runtime.InteropServices.Guid("dc011e36-0375-4537-8051-8773ae1e08e7")]
    public class TagPlacementService : BaseServiceWithConstructor, ITagPlacementService
    {
        private sealed class TagMarker
        {
            public GameObject Root;
            public TextMesh Label;
        }

        private readonly TagPlacementServiceProfile profile;
        private readonly ITagRoutingService routing;
        private readonly Dictionary<int, TagMarker> markers = new Dictionary<int, TagMarker>();
        private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
        private GameObject container;
        private Material fillMaterial;

        public TagPlacementService(
            string name,
            uint priority,
            TagPlacementServiceProfile profile,
            ITagRoutingService routing)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.routing = routing ?? throw new ArgumentNullException(nameof(routing));
        }

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            routing.TagEntered += OnTagPose;
            routing.TagUpdated += OnTagPose;
            routing.TagExited += OnTagExited;

            SubsystemManager.GetSubsystems(inputSubsystems);
            foreach (var subsystem in inputSubsystems)
            {
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
            }
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            var viewer = Camera.main;
            if (viewer == null)
            {
                return;
            }

            foreach (var marker in markers.Values)
            {
                var label = marker.Label.transform;
                label.rotation = Quaternion.LookRotation(label.position - viewer.transform.position);
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            foreach (var marker in markers.Values)
            {
                UnityEngine.Object.Destroy(marker.Root);
            }

            markers.Clear();
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            routing.TagEntered -= OnTagPose;
            routing.TagUpdated -= OnTagPose;
            routing.TagExited -= OnTagExited;
            foreach (var subsystem in inputSubsystems)
            {
                subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            }

            Clear();

            if (container != null)
            {
                UnityEngine.Object.Destroy(container);
                container = null;
            }

            if (fillMaterial != null)
            {
                UnityEngine.Object.Destroy(fillMaterial);
                fillMaterial = null;
            }

            base.Destroy();
        }

        private void OnTagPose(TagObservation observation)
        {
            if (!markers.TryGetValue(observation.Id, out var marker))
            {
                marker = CreateMarker(observation);
                markers[observation.Id] = marker;
            }

            marker.Root.transform.SetPositionAndRotation(observation.WorldPose.position, observation.WorldPose.rotation);
        }

        private void OnTagExited(TagObservation observation)
        {
            if (markers.TryGetValue(observation.Id, out var marker))
            {
                UnityEngine.Object.Destroy(marker.Root);
                markers.Remove(observation.Id);
            }
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            Clear();
            routing.ForgetAll();
        }

        private TagMarker CreateMarker(TagObservation observation)
        {
            EnsureContainer();

            var root = new GameObject($"Tag_{observation.TagName}_{observation.Id}");
            root.transform.SetParent(container.transform, false);

            // Colour-tinted translucent fill quad at the printed tag's physical size.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.localScale = Vector3.one * profile.TagSizeMeters;
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = fillMaterial;
            var block = new MaterialPropertyBlock();
            var color = observation.Color;
            color.a = profile.FillOpacity;
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Solid outline.
            var line = root.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 5;
            var half = profile.TagSizeMeters * 0.5f;
            line.SetPositions(new[]
            {
                new Vector3(-half, -half, 0),
                new Vector3(half, -half, 0),
                new Vector3(half, half, 0),
                new Vector3(-half, half, 0),
                new Vector3(-half, -half, 0)
            });
            line.startWidth = 0.004f;
            line.endWidth = 0.004f;
            line.material = fillMaterial;
            line.startColor = observation.Color;
            line.endColor = observation.Color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            // Billboarded name label above the quad.
            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0, profile.TagSizeMeters * 0.75f, 0);
            var label = labelObject.AddComponent<TextMesh>();
            label.text = $"{observation.TagName} · #{observation.Id}";
            label.characterSize = profile.LabelCharacterSize;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.color = observation.Color;
            label.fontSize = 48;

            return new TagMarker { Root = root, Label = label };
        }

        private void EnsureContainer()
        {
            if (container == null)
            {
                container = new GameObject("QVS_TagMarkers");
            }

            if (fillMaterial == null)
            {
                fillMaterial = new Material(Shader.Find("Sprites/Default"));
            }
        }
    }
}
