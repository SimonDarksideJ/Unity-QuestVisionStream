// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using QuestVisionStream.Core;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Modules;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="EphemeralBoxRenderModule"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Ephemeral Box Render Module Profile", fileName = "EphemeralBoxRenderModuleProfile")]
    public class EphemeralBoxRenderModuleProfile : BaseProfile
    {
        [SerializeField]
        [Tooltip("Distance along the capture-pose ray to place box corners. Fixed-depth is the acknowledged v1 limitation; depth anchoring lives in the anchored module.")]
        private float placementDistanceMeters = 2f;

        [SerializeField]
        private Color boxColor = new Color(1f, 0.23f, 0.19f); // #ff3b30 — matches the WebXR client

        [SerializeField]
        private float lineWidth = 0.006f;

        [SerializeField]
        [Tooltip("Label height above the box's top edge, in meters.")]
        private float labelOffsetMeters = 0.035f;

        [SerializeField]
        [Tooltip("TextMesh character size. World line height is ~ characterSize * fontSize / 10, so 0.012 * 48 / 10 ≈ 6 cm at the label — readable without dominating the view.")]
        private float labelCharacterSize = 0.012f;

        [SerializeField]
        [Tooltip("Draw a small filled square at each box centre. A rendering-vs-placement probe: if the marker lands on the object but the outline does not, the outline (LineRenderer) is the problem; if the marker itself is off the object, it is a placement/camera-intrinsics problem.")]
        private bool showCenterMarker = true;

        [SerializeField]
        [Tooltip("Edge length of the centre-marker square, in meters. Set to 0.03 m (3 cm) so the probe is clearly visible at the 2 m placement distance — better too big and seen than too small to spot. (The original request was 0.2 cm, which is ~1-2 px at 2 m; drop it back down once placement is confirmed.)")]
        private float centerMarkerSizeMeters = 0.03f;

        [SerializeField]
        [Tooltip("Centre-marker colour — deliberately distinct from the box colour so the two primitives are told apart at a glance.")]
        private Color centerMarkerColor = new Color(0.15f, 1f, 0.4f); // bright green vs the red box

        public float PlacementDistanceMeters { get => placementDistanceMeters; set => placementDistanceMeters = value; }
        public Color BoxColor { get => boxColor; set => boxColor = value; }
        public float LineWidth { get => lineWidth; set => lineWidth = value; }
        public float LabelOffsetMeters { get => labelOffsetMeters; set => labelOffsetMeters = value; }
        public float LabelCharacterSize { get => labelCharacterSize; set => labelCharacterSize = value; }
        public bool ShowCenterMarker { get => showCenterMarker; set => showCenterMarker = value; }
        public float CenterMarkerSizeMeters { get => centerMarkerSizeMeters; set => centerMarkerSizeMeters = value; }
        public Color CenterMarkerColor { get => centerMarkerColor; set => centerMarkerColor = value; }
    }

    /// <summary>
    /// The V2 WebXR rendering behaviour, in Unity: each payload clears the previous
    /// boxes and redraws "what's seen right now" — hollow outline boxes whose four
    /// corners are unprojected through the CAPTURE-time pose at a fixed distance,
    /// with billboarded "label 82%" text above. Visuals are pooled (no per-frame
    /// allocation) and fully destroyed on deactivation.
    /// </summary>
    [System.Runtime.InteropServices.Guid("bdaa4894-87fb-4343-9416-56e6e452ac42")]
    public class EphemeralBoxRenderModule : BaseServiceModule, IEphemeralBoxRenderModule
    {
        private sealed class BoxVisual
        {
            public GameObject Root;
            public LineRenderer Line;
            public TextMesh Label;
            public Transform Marker;
        }

        private readonly EphemeralBoxRenderModuleProfile profile;
        private readonly List<BoxVisual> pool = new List<BoxVisual>();
        private readonly Vector3[] cornerBuffer = new Vector3[5];
        private GameObject container;
        private Material lineMaterial;
        private Material markerMaterial;
        private int visibleCount;

        public EphemeralBoxRenderModule(
            string name,
            uint priority,
            EphemeralBoxRenderModuleProfile profile,
            IDetectionRendererService parentService)
            : base(name, priority, profile, parentService)
        {
            this.profile = profile != null ? profile : ScriptableObject.CreateInstance<EphemeralBoxRenderModuleProfile>();
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
                DestroyVisuals();
            }
        }

        private bool firstDrawLogged;

        /// <inheritdoc />
        public void RenderDetections(DetectionArrival arrival, CameraPoseSnapshot? capturePose)
        {
            if (!IsActiveRenderer)
            {
                Debug.LogWarning($"[WSDetection] frame={arrival.Batch.Frame} NOT drawn — '{Name}' received a batch while inactive");
                return;
            }

            // Without a pose snapshot there is nothing correct to draw against — skip
            // rather than paint boxes into the wrong space.
            if (!capturePose.HasValue)
            {
                HideFrom(0);
                return;
            }

            if (!firstDrawLogged && arrival.Batch.Detections.Count > 0)
            {
                firstDrawLogged = true;
                Debug.Log($"[WSDetection] first boxes drawn (frame={arrival.Batch.Frame}, count={arrival.Batch.Detections.Count}, distance={profile.PlacementDistanceMeters}m)");
            }

            EnsureContainer();

            var snapshot = capturePose.Value;
            var distance = profile.PlacementDistanceMeters;
            var index = 0;

            foreach (var detection in arrival.Batch.Detections)
            {
                var visual = GetOrCreateVisual(index);
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

                var topCentre = (topLeft + topRight) * 0.5f;
                visual.Label.transform.position = topCentre + Vector3.up * profile.LabelOffsetMeters;
                visual.Label.text = $"{detection.Label} {Mathf.RoundToInt(detection.Conf * 100)}%";

                // Centre-marker probe: unproject the bbox centre through the SAME
                // capture pose and distance as the corners, so it lands exactly at
                // the box's geometric centre. If the marker is right but the outline
                // is missing, it is a LineRenderer problem; if the marker itself sits
                // off the object, it is placement/intrinsics.
                if (profile.ShowCenterMarker)
                {
                    visual.Marker.gameObject.SetActive(true);
                    visual.Marker.position = snapshot.UnprojectAtDistance(detection.Center, distance);
                }
                else
                {
                    visual.Marker.gameObject.SetActive(false);
                }

                visual.Root.SetActive(true);
                index++;
            }

            HideFrom(index);
            visibleCount = index;
        }

        /// <inheritdoc />
        public void Clear() => HideFrom(0);

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            if (!IsActiveRenderer || visibleCount == 0)
            {
                return;
            }

            // Billboard the labels toward the viewer.
            var viewer = Camera.main;
            if (viewer == null)
            {
                return;
            }

            var viewerPosition = viewer.transform.position;
            for (var i = 0; i < visibleCount && i < pool.Count; i++)
            {
                var visual = pool[i];
                visual.Label.transform.rotation = Quaternion.LookRotation(visual.Label.transform.position - viewerPosition);

                if (visual.Marker != null && visual.Marker.gameObject.activeSelf)
                {
                    visual.Marker.rotation = Quaternion.LookRotation(visual.Marker.position - viewerPosition);
                }
            }
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            DestroyVisuals();
            base.Destroy();
        }

        private void EnsureContainer()
        {
            if (container != null)
            {
                return;
            }

            container = new GameObject("QVS_EphemeralBoxes");

            // URP + single-pass-instanced XR does not render Sprites/Default on a
            // LineRenderer/MeshRenderer, so the colour lives on the material (URP
            // Unlit ignores the LineRenderer's vertex colours). One shared material
            // per colour — all outlines are BoxColor, all markers CenterMarkerColor.
            lineMaterial = UnlitMaterialFactory.Create();
            UnlitMaterialFactory.SetColor(lineMaterial, profile.BoxColor);

            markerMaterial = UnlitMaterialFactory.Create();
            UnlitMaterialFactory.SetColor(markerMaterial, profile.CenterMarkerColor);
        }

        private BoxVisual GetOrCreateVisual(int index)
        {
            while (pool.Count <= index)
            {
                var root = new GameObject($"DetectionBox_{pool.Count}");
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

                var labelObject = new GameObject("Label");
                labelObject.transform.SetParent(root.transform, false);
                var label = labelObject.AddComponent<TextMesh>();
                label.characterSize = profile.LabelCharacterSize;
                label.anchor = TextAnchor.LowerCenter;
                label.alignment = TextAlignment.Center;
                label.color = profile.BoxColor;
                label.fontSize = 48;

                var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.Destroy(marker.GetComponent<Collider>());
                marker.name = "CenterMarker";
                marker.transform.SetParent(root.transform, false);
                marker.transform.localScale = Vector3.one * profile.CenterMarkerSizeMeters;
                var markerRenderer = marker.GetComponent<MeshRenderer>();
                markerRenderer.sharedMaterial = markerMaterial; // colour carried by the shared material
                markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                markerRenderer.receiveShadows = false;
                marker.SetActive(false);

                pool.Add(new BoxVisual { Root = root, Line = line, Label = label, Marker = marker.transform });
            }

            return pool[index];
        }

        private void HideFrom(int index)
        {
            for (var i = index; i < pool.Count; i++)
            {
                pool[i].Root.SetActive(false);
            }

            visibleCount = Mathf.Min(visibleCount, index);
        }

        private void DestroyVisuals()
        {
            pool.Clear();
            visibleCount = 0;

            if (container != null)
            {
                UnityEngine.Object.Destroy(container);
                container = null;
            }

            if (lineMaterial != null)
            {
                UnityEngine.Object.Destroy(lineMaterial);
                lineMaterial = null;
            }

            if (markerMaterial != null)
            {
                UnityEngine.Object.Destroy(markerMaterial);
                markerMaterial = null;
            }
        }
    }
}
