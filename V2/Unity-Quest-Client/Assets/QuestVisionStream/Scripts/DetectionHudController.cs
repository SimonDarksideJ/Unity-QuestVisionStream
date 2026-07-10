// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Text;
using QuestVisionStream.Core;
using QuestVisionStream.Services;
using UnityEngine;
using UnityEngine.UI;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// A head-locked, in-headset detection feed for live testing: a translucent
    /// panel pinned to the left third of the view that lists every detection in the
    /// most recent server payload — class, confidence and an approximate on-screen
    /// location (both a coarse "top-left" descriptor and the raw normalized centre).
    ///
    /// This is deliberately a READ-OUT of what the client actually received, not of
    /// what it drew: if this panel shows detections but no boxes appear in the world,
    /// the fault is downstream of arrival (pose snapshot, render module, or the
    /// unprojection), not in the network/detection path. Created and parented to the
    /// camera by <see cref="QuestVisionStreamBootstrap"/>.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class DetectionHudController : MonoBehaviour
    {
        // Panel styling — a semi-transparent dark card with white text.
        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color DimTextColor = new Color(1f, 1f, 1f, 0.6f);

        private IDetectionService detections;
        private IDetectionRendererService renderer;
        private IPoseTrackingService pose;
        private IAnchoredTagRenderModule anchored;

        // Left-third placement. The panel is head-locked at HeadDistanceMeters, its
        // centre pushed left so the card sits in the left band of the field of view.
        // Pushed back to a comfortable focal distance (1.2 m read as "too close and
        // out of view") and scaled up so it stays readable further away.
        private const float HeadDistanceMeters = 2.8f;
        private const float PanelCentreXMeters = -1.0f;
        private const float PanelWidthMeters = 0.85f;
        private const float PanelHeightMeters = 1.45f;
        private const float WorldScale = 0.0016f; // 1 canvas unit = 1.6 mm
        private const int MaxLinesShown = 12;

        private Text headerText;
        private Text bodyText;

        private string pendingHeader;
        private string pendingBody;
        private bool dirty;

        public void Initialize(IDetectionService detectionService, IDetectionRendererService rendererService = null, IPoseTrackingService poseService = null, IAnchoredTagRenderModule anchoredModule = null)
        {
            detections = detectionService;
            renderer = rendererService;
            pose = poseService;
            anchored = anchoredModule;
            BuildPanel();

            pendingHeader = "DETECTIONS";
            pendingBody = "waiting for the first payload…";
            dirty = true;

            if (detections != null)
            {
                detections.DetectionsReceived += OnDetections;
            }
        }

        private void OnDestroy()
        {
            if (detections != null)
            {
                detections.DetectionsReceived -= OnDetections;
            }
        }

        private void Update()
        {
            // The detection event fires on the Unity main thread, but apply the text
            // in Update so a burst of payloads only costs one Text rebuild per frame.
            if (!dirty)
            {
                return;
            }

            dirty = false;
            headerText.text = pendingHeader;
            bodyText.text = pendingBody;
        }

        private void OnDetections(DetectionArrival arrival)
        {
            var batch = arrival.Batch;
            var list = batch.Detections;

            var mode = renderer != null ? renderer.ActiveModuleName : "?";
            var pitch = pose != null ? $"  ·  pitch {pose.CameraPitchCompensationDegrees:0.0}° (hold Y + L-stick)" : string.Empty;
            var tags = anchored != null && anchored.IsActiveRenderer
                ? $"  ·  tags: {(anchored.PersistentTags ? "persistent" : "tracking")} (B to toggle)"
                : string.Empty;
            pendingHeader = $"DETECTIONS  f#{batch.Frame}  {batch.FrameWidth}x{batch.FrameHeight}\n{list.Count} seen · mode: {mode}  (X to switch){tags}{pitch}";

            if (list.Count == 0)
            {
                pendingBody = "no detections in frame";
                dirty = true;
                return;
            }

            var builder = new StringBuilder(256);
            var shown = Mathf.Min(list.Count, MaxLinesShown);
            for (var i = 0; i < shown; i++)
            {
                var detection = list[i];
                builder.Append("• ").Append(detection.Label)
                    .Append("  ").Append(Mathf.RoundToInt(detection.Conf * 100f)).Append('%')
                    .Append("  ").Append(DescribeLocation(detection.Center))
                    .Append("  (").Append(detection.Center.x.ToString("0.00"))
                    .Append(',').Append(detection.Center.y.ToString("0.00")).Append(')');

                if (i < shown - 1)
                {
                    builder.Append('\n');
                }
            }

            if (list.Count > shown)
            {
                builder.Append("\n… +").Append(list.Count - shown).Append(" more");
            }

            pendingBody = builder.ToString();
            dirty = true;
        }

        /// <summary>
        /// Coarse on-screen quadrant for a normalized viewport centre (0..1, origin
        /// bottom-left, so higher Y reads as "top").
        /// </summary>
        private static string DescribeLocation(Vector2 center)
        {
            string vertical = center.y >= 0.6f ? "top" : center.y <= 0.4f ? "bottom" : "mid";
            string horizontal = center.x <= 0.4f ? "left" : center.x >= 0.6f ? "right" : "centre";

            if (vertical == "mid" && horizontal == "centre")
            {
                return "centre";
            }

            return $"{vertical}-{horizontal}";
        }

        // ---- world-space uGUI construction (mirrors the intro card's approach) ----

        private void BuildPanel()
        {
            var root = new GameObject("QVS_DetectionHud_Canvas");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(PanelCentreXMeters, 0f, HeadDistanceMeters);
            root.transform.localScale = Vector3.one * WorldScale;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)root.transform;
            canvasRect.sizeDelta = new Vector2(PanelWidthMeters / WorldScale, PanelHeightMeters / WorldScale);

            var card = CreatePanel(canvasRect, "Card", PanelColor);
            Stretch(card);

            headerText = CreateText(card, "Header", 26, FontStyle.Bold, TextColor, TextAnchor.UpperLeft);
            Place(headerText, new Vector2(0f, 1f), new Vector2(18, -14), new Vector2(PanelWidthMeters / WorldScale - 36, 90));

            bodyText = CreateText(card, "Body", 22, FontStyle.Normal, TextColor, TextAnchor.UpperLeft);
            Place(bodyText, new Vector2(0f, 1f), new Vector2(18, -110), new Vector2(PanelWidthMeters / WorldScale - 36, PanelHeightMeters / WorldScale - 130));
        }

        private static RectTransform CreatePanel(RectTransform parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return rect;
        }

        private static Text CreateText(RectTransform parent, string name, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(Text text, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)text.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
