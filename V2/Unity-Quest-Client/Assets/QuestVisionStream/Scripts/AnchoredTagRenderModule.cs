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
        [Tooltip("Start with persistent world-pinned tags (V1 behaviour). The B button toggles this at runtime; false = tags update in place to track the object.")]
        private bool persistentTags = true;

        [SerializeField]
        [Tooltip("Maximum physics raycast distance for the SCENE-MESH fallback (used only when environment depth is unavailable at a pixel).")]
        private float maxRaycastDistance = 8f;

        [SerializeField]
        [Tooltip("Last-resort placement distance along the capture ray when neither environment depth nor scene mesh is available.")]
        private float fallbackDistanceMeters = 2f;

        [SerializeField]
        [Tooltip("Box outline colour — deliberately distinct from the ephemeral red so the two modes read apart at a glance.")]
        private Color boxColor = new Color(0.2f, 0.78f, 1f); // cyan

        [SerializeField]
        private float lineWidth = 0.006f;

        [SerializeField]
        [Tooltip("Label height above the box's top edge, in meters.")]
        private float labelOffsetMeters = 0.035f;

        [SerializeField]
        [Tooltip("TextMesh character size (world line height ~ characterSize * fontSize / 10).")]
        private float labelCharacterSize = 0.012f;

        [SerializeField]
        [Tooltip("Edge length of the centre-marker square, in meters.")]
        private float centerMarkerSizeMeters = 0.03f;

        [SerializeField]
        [Tooltip("Centre-marker colour — distinct from the box colour.")]
        private Color centerMarkerColor = new Color(1f, 0.85f, 0.2f); // amber

        public bool PersistentTags { get => persistentTags; set => persistentTags = value; }
        public float MaxRaycastDistance { get => maxRaycastDistance; set => maxRaycastDistance = value; }
        public float FallbackDistanceMeters { get => fallbackDistanceMeters; set => fallbackDistanceMeters = value; }
        public Color BoxColor { get => boxColor; set => boxColor = value; }
        public float LineWidth { get => lineWidth; set => lineWidth = value; }
        public float LabelOffsetMeters { get => labelOffsetMeters; set => labelOffsetMeters = value; }
        public float LabelCharacterSize { get => labelCharacterSize; set => labelCharacterSize = value; }
        public float CenterMarkerSizeMeters { get => centerMarkerSizeMeters; set => centerMarkerSizeMeters = value; }
        public Color CenterMarkerColor { get => centerMarkerColor; set => centerMarkerColor = value; }
    }

    /// <summary>Registration interface for <see cref="AnchoredTagRenderModule"/> (one per concrete module — SF registry keys by interface).</summary>
    public interface IAnchoredTagRenderModule : IDetectionRenderModule
    {
        /// <summary>
        /// True (default): the first sighting of a class drops a permanent world pin
        /// that stays put. False: each new sighting moves the existing box/tag to the
        /// new position, so it tracks the object between detections. Bound to B.
        /// </summary>
        bool PersistentTags { get; set; }
    }

    /// <summary>
    /// Depth-anchored detections: one world-placed box + centre marker + label per
    /// class. Each detection's distance comes from real geometry — <b>environment
    /// depth first</b> (Meta Depth API, per-frame, no scan; via
    /// <see cref="EnvironmentDepthProvider"/>), then the scanned scene mesh
    /// (<c>Physics.Raycast</c>), then a fixed distance — resolved along the
    /// CAPTURE-time pose ray (pose-freeze), and the box corners are unprojected at
    /// that same distance so the outline sits on the object.
    ///
    /// Placement persistence is a runtime toggle (<see cref="PersistentTags"/>, B
    /// button): persistent world-pins (the V1 default) or update-in-place tracking.
    /// Everything is destroyed on Clear/recenter.
    /// </summary>
    [System.Runtime.InteropServices.Guid("011c38af-5d0b-47f0-b565-f9c507720543")]
    public class AnchoredTagRenderModule : BaseServiceModule, IAnchoredTagRenderModule
    {
        private sealed class AnchoredVisual
        {
            public GameObject Root;
            public LineRenderer Line;
            public Transform Marker;
            public TextMesh Label;
        }

        private readonly AnchoredTagRenderModuleProfile profile;
        private readonly Dictionary<string, AnchoredVisual> byClass = new Dictionary<string, AnchoredVisual>();
        private readonly Vector3[] cornerBuffer = new Vector3[5];
        private GameObject container;
        private Material lineMaterial;
        private Material markerMaterial;
        private bool firstPlacementLogged;

        public AnchoredTagRenderModule(
            string name,
            uint priority,
            AnchoredTagRenderModuleProfile profile,
            IDetectionRendererService parentService)
            : base(name, priority, profile, parentService)
        {
            this.profile = profile != null ? profile : ScriptableObject.CreateInstance<AnchoredTagRenderModuleProfile>();
            PersistentTags = this.profile.PersistentTags;
        }

        public bool IsActiveRenderer { get; private set; }

        /// <inheritdoc />
        public bool PersistentTags { get; set; }

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

            // One depth-image acquire per arrival, reused across every detection.
            var depth = EnvironmentDepthProvider.Instance;
            bool depthFrame = depth != null && depth.TryBeginSample();

            try
            {
                foreach (var detection in arrival.Batch.Detections)
                {
                    bool exists = byClass.TryGetValue(detection.Label, out var visual);

                    // Persistent mode: the first pin stays; ignore later sightings.
                    if (exists && PersistentTags)
                    {
                        continue;
                    }

                    var ray = snapshot.ViewportPointToRay(detection.Center);
                    float distance = ResolveDistance(ray, depth, depthFrame, out var source);

                    if (!exists)
                    {
                        visual = CreateVisual(detection.Label);
                        byClass[detection.Label] = visual;
                    }

                    UpdateVisual(visual, snapshot, detection, distance);

                    if (!firstPlacementLogged)
                    {
                        firstPlacementLogged = true;
                        Debug.Log($"[QVS:Anchored] first placement '{detection.Label}' via {source} at {distance:0.00}m (persistent={PersistentTags}).");
                    }
                }
            }
            finally
            {
                if (depthFrame)
                {
                    depth.EndSample();
                }
            }
        }

        /// <summary>Environment depth → scene-mesh raycast → fixed distance.</summary>
        private float ResolveDistance(Ray ray, EnvironmentDepthProvider depth, bool depthFrame, out string source)
        {
            if (depthFrame && depth.TryGetDepthMeters(ray, out var depthMeters))
            {
                source = "depth";
                return depthMeters;
            }

            if (Physics.Raycast(ray, out var hit, profile.MaxRaycastDistance))
            {
                source = "mesh";
                return hit.distance;
            }

            source = "fixed";
            return profile.FallbackDistanceMeters;
        }

        /// <inheritdoc />
        public void Clear()
        {
            foreach (var visual in byClass.Values)
            {
                Object.Destroy(visual.Root);
            }

            byClass.Clear();
            firstPlacementLogged = false; // re-log the depth source after each reset / mode switch
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            if (!IsActiveRenderer || byClass.Count == 0)
            {
                return;
            }

            var viewer = Camera.main;
            if (viewer == null)
            {
                return;
            }

            var viewerPosition = viewer.transform.position;
            foreach (var visual in byClass.Values)
            {
                visual.Label.transform.rotation = Quaternion.LookRotation(visual.Label.transform.position - viewerPosition);
                if (visual.Marker != null)
                {
                    visual.Marker.rotation = Quaternion.LookRotation(visual.Marker.position - viewerPosition);
                }
            }
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            Clear();
            DestroyContainer();
            base.Destroy();
        }

        private void UpdateVisual(AnchoredVisual visual, CameraPoseSnapshot snapshot, RenderableDetection detection, float distance)
        {
            var rect = detection.Rect;
            var bottomLeft = snapshot.UnprojectAtDistance(rect.BottomLeft, distance);
            var bottomRight = snapshot.UnprojectAtDistance(rect.BottomRight, distance);
            var topRight = snapshot.UnprojectAtDistance(rect.TopRight, distance);
            var topLeft = snapshot.UnprojectAtDistance(rect.TopLeft, distance);

            cornerBuffer[0] = bottomLeft;
            cornerBuffer[1] = bottomRight;
            cornerBuffer[2] = topRight;
            cornerBuffer[3] = topLeft;
            cornerBuffer[4] = bottomLeft;
            visual.Line.positionCount = 5;
            visual.Line.SetPositions(cornerBuffer);

            visual.Marker.position = snapshot.UnprojectAtDistance(detection.Center, distance);

            var topCentre = (topLeft + topRight) * 0.5f;
            visual.Label.transform.position = topCentre + Vector3.up * profile.LabelOffsetMeters;
            visual.Label.text = $"{detection.Label} {Mathf.RoundToInt(detection.Conf * 100f)}%";
        }

        private AnchoredVisual CreateVisual(string label)
        {
            var root = new GameObject($"AnchoredTag_{label}");
            root.transform.SetParent(container.transform, false);

            var line = root.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.material = lineMaterial;
            line.startColor = profile.BoxColor;
            line.endColor = profile.BoxColor;
            line.startWidth = profile.LineWidth;
            line.endWidth = profile.LineWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(marker.GetComponent<Collider>());
            marker.name = "CenterMarker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = Vector3.one * profile.CenterMarkerSizeMeters;
            var markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sharedMaterial = markerMaterial;
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            var text = labelObject.AddComponent<TextMesh>();
            text.characterSize = profile.LabelCharacterSize;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = profile.BoxColor;
            text.fontSize = 48;

            return new AnchoredVisual { Root = root, Line = line, Marker = marker.transform, Label = text };
        }

        private void EnsureContainer()
        {
            if (container == null)
            {
                container = new GameObject("QVS_AnchoredTags");
            }

            // URP + single-pass-instanced XR needs the colour on the material (Unlit
            // ignores LineRenderer vertex colours) — one shared material per colour.
            if (lineMaterial == null)
            {
                lineMaterial = UnlitMaterialFactory.Create();
                UnlitMaterialFactory.SetColor(lineMaterial, profile.BoxColor);
            }

            if (markerMaterial == null)
            {
                markerMaterial = UnlitMaterialFactory.Create();
                UnlitMaterialFactory.SetColor(markerMaterial, profile.CenterMarkerColor);
            }
        }

        private void DestroyContainer()
        {
            if (container != null)
            {
                Object.Destroy(container);
                container = null;
            }

            if (lineMaterial != null)
            {
                Object.Destroy(lineMaterial);
                lineMaterial = null;
            }

            if (markerMaterial != null)
            {
                Object.Destroy(markerMaterial);
                markerMaterial = null;
            }
        }
    }
}
