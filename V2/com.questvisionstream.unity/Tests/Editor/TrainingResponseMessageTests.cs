// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using NUnit.Framework;
using QuestVisionStream.Protocol;
using QuestVisionStream.Training;

namespace QuestVisionStream.Tests
{
    /// <summary>
    /// Protocol integration for the training flow. The state machine itself now
    /// lives in <c>com.ethar.trainingstatemachine</c> and is covered by that
    /// package's tests; what remains here is the QuestVisionStream-specific
    /// bridge — the synthetic detections-channel payload an action response is
    /// converted into.
    /// </summary>
    public class TrainingResponseMessageTests
    {
        [Test]
        public void ResponseMessage_FollowsTheDetectionsPath()
        {
            // The synthetic action-response payload must parse through the SAME
            // channel parser as a server message and normalize to the frame centre.
            var json = TrainingResponseMessage.ToDetectionsJson("begintraining");

            var kind = DetectionChannelParser.Parse(json, out var payload);

            Assert.That(kind, Is.EqualTo(DetectionChannelMessageKind.Detections));
            Assert.That(payload.Frame, Is.EqualTo(TrainingResponseMessage.SyntheticFrame));
            Assert.That(payload.Detections.Count, Is.EqualTo(1));
            Assert.That(payload.Detections[0].Label, Is.EqualTo("begintraining"));
            Assert.That(payload.Detections[0].Conf, Is.EqualTo(1f));

            var batch = Core.DetectionMath.ToRenderBatch(payload);
            Assert.That(batch.Detections[0].Center.x, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(batch.Detections[0].Center.y, Is.EqualTo(0.5f).Within(1e-5));
        }

        [Test]
        public void ResponseMessage_EscapesAwkwardClassNames()
        {
            var json = TrainingResponseMessage.ToDetectionsJson("weird \"class\"\\name");

            Assert.That(DetectionChannelParser.Parse(json, out var payload), Is.EqualTo(DetectionChannelMessageKind.Detections));
            Assert.That(payload.Detections[0].Label, Is.EqualTo("weird \"class\"\\name"));
        }
    }
}
