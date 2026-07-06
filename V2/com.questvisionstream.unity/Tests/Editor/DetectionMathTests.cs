// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using NUnit.Framework;
using QuestVisionStream.Core;
using QuestVisionStream.Protocol;
using UnityEngine;

namespace QuestVisionStream.Tests
{
    public class DetectionMathTests
    {
        private const float Epsilon = 1e-5f;

        [Test]
        public void Normalize_CentersAndInvertsYByDefault()
        {
            // 640x480 frame, bbox occupying the top-left quadrant in image space.
            var detection = new Detection("cup", 0.9f, 0, 0, 320, 240);

            DetectionMath.Normalize(detection, 640, 480, out var center, out var rect);

            Assert.That(center.x, Is.EqualTo(0.25f).Within(Epsilon));
            // Image-space cy = 120/480 = 0.25, inverted => 0.75 (viewport origin bottom-left).
            Assert.That(center.y, Is.EqualTo(0.75f).Within(Epsilon));
            Assert.That(rect.X, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(rect.Y, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(rect.Width, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(rect.Height, Is.EqualTo(0.5f).Within(Epsilon));
        }

        [Test]
        public void Normalize_WithoutInvertY_KeepsImageSpace()
        {
            var detection = new Detection("cup", 0.9f, 0, 0, 320, 240);

            DetectionMath.Normalize(detection, 640, 480, out var center, out var rect, invertY: false);

            Assert.That(center.y, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(rect.Y, Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void Normalize_InvertX_MirrorsHorizontally()
        {
            var detection = new Detection("cup", 0.9f, 0, 0, 320, 240);

            DetectionMath.Normalize(detection, 640, 480, out var center, out var rect, invertX: true);

            Assert.That(center.x, Is.EqualTo(0.75f).Within(Epsilon));
            Assert.That(rect.X, Is.EqualTo(0.5f).Within(Epsilon));
        }

        [Test]
        public void Normalize_SwappedBboxCorners_YieldsPositiveRect()
        {
            var detection = new Detection("cup", 0.9f, 320, 240, 0, 0);

            DetectionMath.Normalize(detection, 640, 480, out _, out var rect);

            Assert.That(rect.Width, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(rect.Height, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(rect.X, Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void Normalize_ZeroFrameDims_DoesNotDivideByZero()
        {
            var detection = new Detection("cup", 0.9f, 0, 0, 10, 10);

            DetectionMath.Normalize(detection, 0, 0, out var center, out _);

            Assert.That(float.IsNaN(center.x), Is.False);
            Assert.That(float.IsNaN(center.y), Is.False);
        }

        [Test]
        public void ToRenderBatch_UsesPayloadDims_NotAConstant()
        {
            // Same pixel bbox normalizes differently once the server ramps resolution.
            var payload = MakePayload(1280, 960, new Detection("cup", 0.9f, 0, 0, 320, 240));

            var batch = DetectionMath.ToRenderBatch(payload);

            Assert.That(batch.Detections[0].Center.x, Is.EqualTo(0.125f).Within(Epsilon));
            Assert.That(batch.FrameWidth, Is.EqualTo(1280));
        }

        private static DetectionsPayload MakePayload(int width, int height, params Detection[] detections)
        {
            var json = new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = "detections",
                ["frame"] = 1,
                ["width"] = width,
                ["height"] = height,
                ["detections"] = new Newtonsoft.Json.Linq.JArray()
            };
            foreach (var d in detections)
            {
                ((Newtonsoft.Json.Linq.JArray)json["detections"]).Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["label"] = d.Label,
                    ["conf"] = d.Conf,
                    ["bbox"] = new Newtonsoft.Json.Linq.JArray(d.X1, d.Y1, d.X2, d.Y2)
                });
            }

            Assert.That(
                DetectionChannelParser.Parse(json.ToString(), out var payload),
                Is.EqualTo(DetectionChannelMessageKind.Detections));
            return payload;
        }
    }

    public class DetectionDeduperTests
    {
        [Test]
        public void PerClass_AllowsFirstOnly()
        {
            var deduper = new DetectionDeduper(DedupPolicy.PerClass);

            Assert.That(deduper.ShouldPlace("cup"), Is.True);
            Assert.That(deduper.ShouldPlace("cup"), Is.False);
            Assert.That(deduper.ShouldPlace("chair"), Is.True);
        }

        [Test]
        public void SpatialPerClass_BlocksWithinMinDistance()
        {
            var deduper = new DetectionDeduper(DedupPolicy.SpatialPerClass, 0.3f);

            Assert.That(deduper.ShouldPlace("cup", Vector3.zero), Is.True);
            Assert.That(deduper.ShouldPlace("cup", new Vector3(0.1f, 0, 0)), Is.False);
            Assert.That(deduper.ShouldPlace("cup", new Vector3(1f, 0, 0)), Is.True);
            // Different class at the same spot is fine.
            Assert.That(deduper.ShouldPlace("chair", Vector3.zero), Is.True);
        }

        [Test]
        public void None_AlwaysPlaces()
        {
            var deduper = new DetectionDeduper(DedupPolicy.None);

            Assert.That(deduper.ShouldPlace("cup"), Is.True);
            Assert.That(deduper.ShouldPlace("cup"), Is.True);
        }

        [Test]
        public void Reset_ForgetsPlacements()
        {
            var deduper = new DetectionDeduper(DedupPolicy.PerClass);
            deduper.ShouldPlace("cup");

            deduper.Reset();

            Assert.That(deduper.ShouldPlace("cup"), Is.True);
        }
    }
}
