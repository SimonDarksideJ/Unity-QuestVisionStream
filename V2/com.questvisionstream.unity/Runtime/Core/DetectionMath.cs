// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using QuestVisionStream.Protocol;
using UnityEngine;

namespace QuestVisionStream.Core
{
    /// <summary>
    /// A rectangle in normalized viewport space (0..1, origin bottom-left).
    /// </summary>
    public readonly struct NormalizedRect
    {
        public NormalizedRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public Vector2 BottomLeft => new Vector2(X, Y);
        public Vector2 BottomRight => new Vector2(X + Width, Y);
        public Vector2 TopLeft => new Vector2(X, Y + Height);
        public Vector2 TopRight => new Vector2(X + Width, Y + Height);
    }

    /// <summary>A detection converted to resolution-independent viewport space.</summary>
    public readonly struct RenderableDetection
    {
        public RenderableDetection(string label, float conf, Vector2 center, NormalizedRect rect, Detection source)
        {
            Label = label;
            Conf = conf;
            Center = center;
            Rect = rect;
            Source = source;
        }

        public string Label { get; }
        public float Conf { get; }

        /// <summary>Bbox center in normalized viewport space (0..1, origin bottom-left).</summary>
        public Vector2 Center { get; }

        public NormalizedRect Rect { get; }

        /// <summary>The raw stream-pixel detection this was derived from.</summary>
        public Detection Source { get; }
    }

    /// <summary>One payload's worth of renderable detections.</summary>
    public sealed class RenderBatch
    {
        public int Frame { get; internal set; }
        public long? Pts { get; internal set; }
        public int FrameWidth { get; internal set; }
        public int FrameHeight { get; internal set; }
        public IReadOnlyList<RenderableDetection> Detections { get; internal set; }
    }

    /// <summary>
    /// Stream-pixel to viewport-space conversion, ported from the V1
    /// <c>DetectionSpawnerManager.ComputeWorldPosition</c> first stage via the V2
    /// TypeScript <c>DetectionMath</c>:
    /// <code>nx = cx / frameW ; ny = cy / frameH ; if invertY: ny = 1 - ny</code>
    /// Normalizing against the PAYLOAD's width/height (never a constant) keeps the
    /// math correct as the server ramps its frame size (adaptive resolution).
    /// </summary>
    public static class DetectionMath
    {
        /// <summary>
        /// Convert a stream-pixel detection to a normalized viewport center + rect.
        /// </summary>
        /// <param name="detection">The raw detection.</param>
        /// <param name="frameWidth">Width of the frame the bbox was computed against.</param>
        /// <param name="frameHeight">Height of the frame the bbox was computed against.</param>
        /// <param name="invertY">
        /// Flip the Y axis. The server sends v-flipped frames by default
        /// (<c>QVS_FLIP_VERTICAL=true</c>), so this defaults on — keep it configurable
        /// to match the capture pipeline.
        /// </param>
        /// <param name="invertX">Flip the X axis, for mirrored streams. Default off.</param>
        public static void Normalize(
            in Detection detection,
            int frameWidth,
            int frameHeight,
            out Vector2 center,
            out NormalizedRect rect,
            bool invertY = true,
            bool invertX = false)
        {
            float w = Mathf.Max(1, frameWidth);
            float h = Mathf.Max(1, frameHeight);

            var cx = (detection.X1 + detection.X2) * 0.5f;
            var cy = (detection.Y1 + detection.Y2) * 0.5f;
            var nx = cx / w;
            var ny = cy / h;
            if (invertY) { ny = 1f - ny; }
            if (invertX) { nx = 1f - nx; }
            center = new Vector2(nx, ny);

            var rx = Mathf.Min(detection.X1, detection.X2) / w;
            var rw = Mathf.Abs(detection.X2 - detection.X1) / w;
            var ry = Mathf.Min(detection.Y1, detection.Y2) / h;
            var rh = Mathf.Abs(detection.Y2 - detection.Y1) / h;
            if (invertY) { ry = 1f - ry - rh; }
            if (invertX) { rx = 1f - rx - rw; }
            rect = new NormalizedRect(rx, ry, rw, rh);
        }

        /// <summary>Build a full <see cref="RenderBatch"/> from a server payload.</summary>
        public static RenderBatch ToRenderBatch(DetectionsPayload payload, bool invertY = true, bool invertX = false)
        {
            var detections = new List<RenderableDetection>(payload.Detections.Count);
            foreach (var detection in payload.Detections)
            {
                Normalize(detection, payload.Width, payload.Height, out var center, out var rect, invertY, invertX);
                detections.Add(new RenderableDetection(detection.Label, detection.Conf, center, rect, detection));
            }

            return new RenderBatch
            {
                Frame = payload.Frame,
                Pts = payload.Pts,
                FrameWidth = payload.Width,
                FrameHeight = payload.Height,
                Detections = detections
            };
        }
    }
}
