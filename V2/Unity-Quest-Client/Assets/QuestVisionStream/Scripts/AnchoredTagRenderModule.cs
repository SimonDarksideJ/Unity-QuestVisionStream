// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using QuestVisionStream.Core;
using QuestVisionStream.Services;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Modules;
using UnityEngine;

namespace QuestVisionStream.Client
{
    /// <summary>Configuration for <see cref="AnchoredTagRenderModule"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Anchored Tag Render Module Profile", fileName = "AnchoredTagRenderModuleProfile")]
    public class AnchoredTagRenderModuleProfile : BaseProfile
    {
        [SerializeField]
        [Tooltip("Dedup policy for persistent tags (the V1 behaviour was one per class).")]
        private DedupPolicy dedupPolicy = DedupPolicy.PerClass;

        [SerializeField]
        [Tooltip("Minimum separation between same-class tags for SpatialPerClass.")]
        private float minDistanceMeters = 0.3f;

        [SerializeField]
        [Tooltip("Maximum physics raycast distance when looking for real geometry (scene mesh / colliders).")]
        private float maxRaycastDistance = 8f;

        [SerializeField]
        [Tooltip("Placement distance along the capture ray when nothing raycastable is hit.")]
        private float fallbackDistanceMeters = 2f;

        [SerializeField]
        private Color markerColor = new Color(0.2f, 0.78f, 1f);

        public DedupPolicy DedupPolicy { get => dedupPolicy; set => dedupPolicy = value; }
        public float MinDistanceMeters { get => minDistanceMeters; set => minDistanceMeters = value; }
        public float MaxRaycastDistance { get => maxRaycastDistance; set => maxRaycastDistance = value; }
        public float FallbackDistanceMeters { get => fallbackDistanceMeters; set => fallbackDistanceMeters = value; }
        public Color MarkerColor { get => markerColor; set => markerColor = value; }
    }

    /// <summary>Registration interface for <see cref="AnchoredTagRenderModule"/> (one per concrete module — SF registry keys by interface).</summary>
    public interface IAnchoredTagRenderModule : IDetectionRenderModule
    {
    }

    /// <summary>
    /// The V1 <c>DetectionSpawnerManager</c> behaviour, modernized: persistent
    /// world-anchored tags. Each detection's centre is cast along the CAPTURE-time
    /// pose ray (pose-freeze — V1 used the arrival pose and drifted under head
    /// motion) against physics geometry (the scene mesh when an ARMeshManager +
    /// colliders are present, or any scene colliders), falling back to a fixed
    /// distance on miss. Dedup per policy; everything is destroyed on Clear/recenter.
    /// </summary>
    [System.Runtime.InteropServices.Guid("011c38af-5d0b-47f0-b565-f9c507720543")]
    public class AnchoredTagRenderModule : BaseServiceModule, IAnchoredTagRenderModule
    {
        private sealed class Marker
        {
            public GameObject Root;
            public TextMesh Label;
        }

        private readonly AnchoredTagRenderModuleProfile profile;
        private readonly List<Marker> markers = new List<Marker>();
        private DetectionDeduper deduper;
        private GameObject container;
        private Material markerMaterial;

        public AnchoredTagRenderModule(
            string name,
            uint priority,
            AnchoredTagRenderModuleProfile profile,
            IDetectionRendererService parentService)
            : base(name, priority, profile, parentService)
        {
            this.profile = profile != null ? profile : ScriptableObject.CreateInstance<AnchoredTagRenderModuleProfile>();
            deduper = new DetectionDeduper(this.profile.DedupPolicy, this.profile.MinDistanceMeters);
        }

        public bool IsActiveRenderer { get; private set; }

        /// <inheritdoc />
        public void SetActiveRenderer(bool active)
        {
            if (IsActiveRenderer == active)
            {
                return;
            }

            IsActiveRenderer = active;
            if (!active)
            {
                Clear();
                DestroyContainer();
            }
        }

        /// <inheritdoc />
        public void RenderDetections(DetectionArrival arrival, CameraPoseSnapshot? capturePose)
        {
            if (!IsActiveRenderer || !capturePose.HasValue)
            {
                return;
            }

            EnsureContainer();
            var snapshot = capturePose.Value;

            foreach (var detection in arrival.Batch.Detections)
            {
                var ray = snapshot.ViewportPointToRay(detection.Center);

                Vector3 position;
                if (Physics.Raycast(ray, out var hit, profile.MaxRaycastDistance))
                {
                    position = hit.point;
                }
                else
                {
                    position = ray.origin + ray.direction * profile.FallbackDistanceMeters;
                }

                if (!deduper.ShouldPlace(detection.Label, position))
                {
                    continue;
                }

                markers.Add(CreateMarker(detection.Label, detection.Conf, position));
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            foreach (var marker in markers)
            {
                Object.Destroy(marker.Root);
            }

            markers.Clear();
            deduper.Reset();
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            if (!IsActiveRenderer || markers.Count == 0)
            {
                return;
            }

            var viewer = Camera.main;
            if (viewer == null)
            {
                return;
            }

            foreach (var marker in markers)
            {
                var label = marker.Label.transform;
                label.rotation = Quaternion.LookRotation(label.position - viewer.transform.position);
            }
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            Clear();
            DestroyContainer();
            base.Destroy();
        }

        private Marker CreateMarker(string label, float conf, Vector3 position)
        {
            var root = new GameObject($"AnchoredTag_{label}");
            root.transform.SetParent(container.transform, false);
            root.transform.position = position;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localScale = Vector3.one * 0.05f;
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = markerMaterial;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", profile.MarkerColor);
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0, 0.08f, 0);
            var text = labelObject.AddComponent<TextMesh>();
            text.text = $"{label} {Mathf.RoundToInt(conf * 100)}%";
            text.characterSize = 0.04f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = profile.MarkerColor;
            text.fontSize = 48;

            return new Marker { Root = root, Label = text };
        }

        private void EnsureContainer()
        {
            if (container == null)
            {
                container = new GameObject("QVS_AnchoredTags");
            }

            if (markerMaterial == null)
            {
                markerMaterial = new Material(Shader.Find("Sprites/Default"));
            }
        }

        private void DestroyContainer()
        {
            if (container != null)
            {
                Object.Destroy(container);
                container = null;
            }

            if (markerMaterial != null)
            {
                Object.Destroy(markerMaterial);
                markerMaterial = null;
            }
        }
    }
}
