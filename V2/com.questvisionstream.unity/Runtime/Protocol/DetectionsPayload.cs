// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace QuestVisionStream.Protocol
{
    /// <summary>
    /// A single detection as sent by the QuestVisionStreamServer.
    /// <see cref="Bbox"/> is <c>[x1, y1, x2, y2]</c> in STREAM pixels of the
    /// payload's <see cref="DetectionsPayload.Width"/> x <see cref="DetectionsPayload.Height"/> frame.
    /// </summary>
    public readonly struct Detection
    {
        public Detection(string label, float conf, float x1, float y1, float x2, float y2)
        {
            Label = label;
            Conf = conf;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public string Label { get; }

        /// <summary>Confidence 0..1.</summary>
        public float Conf { get; }

        public float X1 { get; }
        public float Y1 { get; }
        public float X2 { get; }
        public float Y2 { get; }
    }

    /// <summary>
    /// The per-frame payload pushed by the server over the <c>detections</c> data channel.
    /// Wire-identical to the V1 Unity client and the V2 WebXR client, so one server
    /// serves every client unchanged. Unknown fields are ignored (additive wire rule).
    /// </summary>
    public sealed class DetectionsPayload
    {
        /// <summary>Server counter of frames RECEIVED (gaps = frames the server skipped).</summary>
        public int Frame { get; internal set; }

        /// <summary>
        /// Media timestamp of the processed frame in RTP clock units (90 kHz), or
        /// <c>null</c> when the source frame carried none. Feeds <see cref="Core.LatencyEstimator"/>.
        /// </summary>
        public long? Pts { get; internal set; }

        /// <summary>Width of the frame the boxes were computed against (the server ramps this up).</summary>
        public int Width { get; internal set; }

        /// <summary>Height of the frame the boxes were computed against.</summary>
        public int Height { get; internal set; }

        public IReadOnlyList<Detection> Detections { get; internal set; }
    }

    /// <summary>The kind of message received on the <c>detections</c> data channel.</summary>
    public enum DetectionChannelMessageKind
    {
        /// <summary>Not a recognised message — ignore (additive wire rule).</summary>
        Unknown = 0,

        /// <summary>The <c>{"type":"ready"}</c> handshake sent when the channel opens.</summary>
        Ready,

        /// <summary>A <see cref="DetectionsPayload"/>.</summary>
        Detections
    }

    /// <summary>
    /// Parser + validator for the detections data channel. Validation is strict on
    /// numerics (a payload that parses can never produce NaN downstream in
    /// <see cref="Core.DetectionMath"/>) while tolerating unknown additive fields.
    /// </summary>
    public static class DetectionChannelParser
    {
        /// <summary>
        /// Parse a raw data channel text message. Returns the message kind;
        /// <paramref name="payload"/> is non-null only for <see cref="DetectionChannelMessageKind.Detections"/>.
        /// </summary>
        public static DetectionChannelMessageKind Parse(string json, out DetectionsPayload payload)
        {
            payload = null;

            if (string.IsNullOrEmpty(json))
            {
                return DetectionChannelMessageKind.Unknown;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                return DetectionChannelMessageKind.Unknown;
            }

            var type = root.Value<string>("type");
            switch (type)
            {
                case "ready":
                    return DetectionChannelMessageKind.Ready;
                case "detections":
                    return TryReadDetections(root, out payload)
                        ? DetectionChannelMessageKind.Detections
                        : DetectionChannelMessageKind.Unknown;
                default:
                    return DetectionChannelMessageKind.Unknown;
            }
        }

        private static bool TryReadDetections(JObject root, out DetectionsPayload payload)
        {
            payload = null;

            if (!TryGetFiniteNumber(root["frame"], out var frame) ||
                !TryGetFiniteNumber(root["width"], out var width) ||
                !TryGetFiniteNumber(root["height"], out var height))
            {
                return false;
            }

            if (!(root["detections"] is JArray detectionsArray))
            {
                return false;
            }

            var detections = new List<Detection>(detectionsArray.Count);
            foreach (var item in detectionsArray)
            {
                if (!(item is JObject det))
                {
                    return false;
                }

                var label = det.Value<string>("label");
                if (label == null ||
                    !TryGetFiniteNumber(det["conf"], out var conf) ||
                    !(det["bbox"] is JArray bbox) ||
                    bbox.Count != 4 ||
                    !TryGetFiniteNumber(bbox[0], out var x1) ||
                    !TryGetFiniteNumber(bbox[1], out var y1) ||
                    !TryGetFiniteNumber(bbox[2], out var x2) ||
                    !TryGetFiniteNumber(bbox[3], out var y2))
                {
                    return false;
                }

                detections.Add(new Detection(label, (float)conf, (float)x1, (float)y1, (float)x2, (float)y2));
            }

            // pts is additive and nullable — absence or null is valid, a non-numeric value is not a reason to drop the payload.
            long? pts = null;
            var ptsToken = root["pts"];
            if (ptsToken != null && ptsToken.Type != JTokenType.Null && TryGetFiniteNumber(ptsToken, out var ptsValue))
            {
                pts = (long)ptsValue;
            }

            payload = new DetectionsPayload
            {
                Frame = (int)frame,
                Pts = pts,
                Width = (int)width,
                Height = (int)height,
                Detections = detections
            };
            return true;
        }

        private static bool TryGetFiniteNumber(JToken token, out double value)
        {
            value = 0;

            if (token == null)
            {
                return false;
            }

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                return false;
            }

            value = token.Value<double>();
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
