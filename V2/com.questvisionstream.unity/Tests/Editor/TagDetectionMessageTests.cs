// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using NUnit.Framework;
using QuestVisionStream.Protocol;
using QuestVisionStream.Services;
using UnityEngine;

namespace QuestVisionStream.Tests
{
    /// <summary>
    /// Protocol integration for the AprilTag → detection bridge: the synthetic
    /// tag payload must ride the SAME wire path as a server message, and its
    /// viewport-native coordinates must survive the flip-free local
    /// normalization exactly (the local publish path never applies the capture
    /// invert flags).
    /// </summary>
    public class TagDetectionMessageTests
    {
        [Test]
        public void TagMessage_FollowsTheDetectionsPath()
        {
            var json = TagDetectionMessage.ToDetectionsJson("station1", new Vector2(0.25f, 0.75f), new Vector2(0.1f, 0.1f));

            var kind = DetectionChannelParser.Parse(json, out var payload);

            Assert.That(kind, Is.EqualTo(DetectionChannelMessageKind.Detections));
            Assert.That(payload.Frame, Is.EqualTo(TagDetectionMessage.SyntheticFrame));
            Assert.That(payload.Frame, Is.Not.EqualTo(Training.TrainingResponseMessage.SyntheticFrame),
                "tag payloads must be distinguishable from action responses");
            Assert.That(payload.Detections.Count, Is.EqualTo(1));
            Assert.That(payload.Detections[0].Label, Is.EqualTo("station1"));
            Assert.That(payload.Detections[0].Conf, Is.EqualTo(1f));
        }

        [Test]
        public void TagMessage_ViewportCenterSurvivesFlipFreeNormalization()
        {
            // Mirrors DetectionService.PublishLocal: local payloads normalize with
            // both invert flags OFF, so the authored viewport centre round-trips.
            var json = TagDetectionMessage.ToDetectionsJson("station1", new Vector2(0.25f, 0.75f), new Vector2(0.1f, 0.1f));
            DetectionChannelParser.Parse(json, out var payload);

            var batch = Core.DetectionMath.ToRenderBatch(payload, invertY: false, invertX: false);

            Assert.That(batch.Detections[0].Center.x, Is.EqualTo(0.25f).Within(1e-3));
            Assert.That(batch.Detections[0].Center.y, Is.EqualTo(0.75f).Within(1e-3));
            Assert.That(batch.Detections[0].Rect.Width, Is.EqualTo(0.1f).Within(1e-3));
            Assert.That(batch.Detections[0].Rect.Height, Is.EqualTo(0.1f).Within(1e-3));
        }

        [Test]
        public void TagMessage_BoxIsClampedIntoTheFrame()
        {
            var json = TagDetectionMessage.ToDetectionsJson("edge", new Vector2(0.99f, 0.01f), new Vector2(0.2f, 0.2f));
            DetectionChannelParser.Parse(json, out var payload);

            var d = payload.Detections[0];
            Assert.That(d.X1, Is.GreaterThanOrEqualTo(0f));
            Assert.That(d.Y1, Is.GreaterThanOrEqualTo(0f));
            Assert.That(d.X2, Is.LessThanOrEqualTo(TagDetectionMessage.FrameSize));
            Assert.That(d.Y2, Is.LessThanOrEqualTo(TagDetectionMessage.FrameSize));
        }

        [Test]
        public void TagRegistry_EffectiveClassNameFallsBackToName()
        {
            var withClass = new TagDefinition { Id = 0, Name = "Alpha", ClassName = "tv" };
            var withoutClass = new TagDefinition { Id = 1, Name = "Bravo" };

            Assert.That(withClass.EffectiveClassName, Is.EqualTo("tv"));
            Assert.That(withoutClass.EffectiveClassName, Is.EqualTo("Bravo"));
        }
    }
}
