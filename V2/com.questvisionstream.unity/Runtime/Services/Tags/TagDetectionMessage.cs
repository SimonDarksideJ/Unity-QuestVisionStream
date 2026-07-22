// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using Newtonsoft.Json.Linq;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// Builds the synthetic detections-channel message for an on-device AprilTag
    /// sighting, so tag classes follow the SAME path as server detections — the
    /// counterpart of <see cref="Training.TrainingResponseMessage"/> for the tag
    /// pipeline. The JSON below is run through the standard channel parser via
    /// <see cref="IDetectionService.PublishLocal"/> and lands in the normal batch
    /// handler: a tag sighting IS a detected class.
    ///
    /// Coordinates are authored viewport-native (0..1, origin bottom-left) scaled
    /// into a synthetic <see cref="FrameSize"/>² frame; the local publish path
    /// applies no capture invert flags, so what is authored here is exactly what
    /// consumers see in the normalized <c>RenderBatch</c>.
    /// </summary>
    public static class TagDetectionMessage
    {
        /// <summary>Frame marker distinguishing tag payloads from server frames (-1 is the action-response marker).</summary>
        public const int SyntheticFrame = -2;

        /// <summary>Side of the synthetic square frame the viewport box is scaled into.</summary>
        public const int FrameSize = 1000;

        /// <summary>
        /// A wire-shaped detections payload carrying one tag sighting as a
        /// full-confidence detection: <paramref name="className"/> as the label,
        /// with a box of <paramref name="viewportSize"/> around
        /// <paramref name="viewportCenter"/> (both normalized viewport, origin
        /// bottom-left), clamped into the frame.
        /// </summary>
        public static string ToDetectionsJson(string className, Vector2 viewportCenter, Vector2 viewportSize)
        {
            var halfW = Mathf.Max(0f, viewportSize.x) * 0.5f;
            var halfH = Mathf.Max(0f, viewportSize.y) * 0.5f;
            var x1 = Mathf.Clamp01(viewportCenter.x - halfW) * FrameSize;
            var y1 = Mathf.Clamp01(viewportCenter.y - halfH) * FrameSize;
            var x2 = Mathf.Clamp01(viewportCenter.x + halfW) * FrameSize;
            var y2 = Mathf.Clamp01(viewportCenter.y + halfH) * FrameSize;

            var root = new JObject
            {
                ["type"] = "detections",
                ["frame"] = SyntheticFrame,
                ["width"] = FrameSize,
                ["height"] = FrameSize,
                ["detections"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = className ?? string.Empty,
                        ["conf"] = 1.0,
                        ["bbox"] = new JArray { x1, y1, x2, y2 }
                    }
                }
            };

            return root.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
