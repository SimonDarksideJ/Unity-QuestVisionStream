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
        private float labelCharacterSize = 0.05f;

        public float PlacementDistanceMeters { get => placementDistanceMeters; set => placementDistanceMeters = value; }
        public Color BoxColor { get => boxColor; set => boxColor = value; }
        public float LineWidth { get => lineWidth; set => lineWidth = value; }
        public float LabelOffsetMeters { get => labelOffsetMeters; set => labelOffsetMeters = value; }
        public float LabelCharacterSize { get => labelCharacterSize; set => labelCharacterSize = value; }
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
        }

        private readonly EphemeralBoxRenderModuleProfile profile;
        private readonly List<BoxVisual> pool = new List<BoxVisual>();
        private readonly Vector3[] cornerBuffer = new Vector3[5];
        private GameObject container;
        private Material lineMaterial;
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

            for (var i = 0; i < visibleCount && i < pool.Count; i++)
            {
                var label = pool[i].Label.transform;
                label.rotation = Quaternion.LookRotation(label.position - viewer.transform.position);
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
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
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

                pool.Add(new BoxVisual { Root = root, Line = line, Label = label });
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
        }
    }
}
