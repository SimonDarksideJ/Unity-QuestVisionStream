// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using NUnit.Framework;
using QuestVisionStream.Protocol;

namespace QuestVisionStream.Tests
{
    public class DetectionChannelParserTests
    {
        // The canonical wire fixture — matches webrtc_server.py / the TS client tests.
        private const string ValidPayload =
            "{\"type\":\"detections\",\"frame\":123,\"pts\":369000,\"width\":640,\"height\":480," +
            "\"detections\":[{\"label\":\"cup\",\"conf\":0.82,\"bbox\":[10,20,110,220]}]}";

        [Test]
        public void Parse_ValidPayload_ReadsAllFields()
        {
            var kind = DetectionChannelParser.Parse(ValidPayload, out var payload);

            Assert.That(kind, Is.EqualTo(DetectionChannelMessageKind.Detections));
            Assert.That(payload.Frame, Is.EqualTo(123));
            Assert.That(payload.Pts, Is.EqualTo(369000));
            Assert.That(payload.Width, Is.EqualTo(640));
            Assert.That(payload.Height, Is.EqualTo(480));
            Assert.That(payload.Detections.Count, Is.EqualTo(1));
            Assert.That(payload.Detections[0].Label, Is.EqualTo("cup"));
            Assert.That(payload.Detections[0].Conf, Is.EqualTo(0.82f).Within(1e-5));
            Assert.That(payload.Detections[0].X2, Is.EqualTo(110f));
        }

        [Test]
        public void Parse_Ready_ReturnsReady()
        {
            Assert.That(
                DetectionChannelParser.Parse("{\"type\":\"ready\"}", out _),
                Is.EqualTo(DetectionChannelMessageKind.Ready));
        }

        [Test]
        public void Parse_NullPts_IsValid()
        {
            const string json = "{\"type\":\"detections\",\"frame\":1,\"pts\":null,\"width\":640,\"height\":480,\"detections\":[]}";

            var kind = DetectionChannelParser.Parse(json, out var payload);

            Assert.That(kind, Is.EqualTo(DetectionChannelMessageKind.Detections));
            Assert.That(payload.Pts, Is.Null);
        }

        [Test]
        public void Parse_MissingPts_IsValid()
        {
            const string json = "{\"type\":\"detections\",\"frame\":1,\"width\":640,\"height\":480,\"detections\":[]}";

            Assert.That(
                DetectionChannelParser.Parse(json, out var payload),
                Is.EqualTo(DetectionChannelMessageKind.Detections));
            Assert.That(payload.Pts, Is.Null);
        }

        [Test]
        public void Parse_UnknownAdditiveFields_AreIgnored()
        {
            const string json = "{\"type\":\"detections\",\"frame\":1,\"width\":640,\"height\":480,\"detections\":[],\"future\":\"field\"}";

            Assert.That(
                DetectionChannelParser.Parse(json, out _),
                Is.EqualTo(DetectionChannelMessageKind.Detections));
        }

        [TestCase("{\"type\":\"detections\",\"frame\":\"x\",\"width\":640,\"height\":480,\"detections\":[]}")]
        [TestCase("{\"type\":\"detections\",\"frame\":1,\"width\":640,\"height\":480,\"detections\":[{\"label\":\"cup\",\"conf\":0.5,\"bbox\":[1,2,3]}]}")]
        [TestCase("{\"type\":\"detections\",\"frame\":1,\"width\":640,\"height\":480,\"detections\":[{\"label\":\"cup\",\"conf\":\"high\",\"bbox\":[1,2,3,4]}]}")]
        [TestCase("{\"type\":\"detections\",\"frame\":1,\"width\":640,\"height\":480,\"detections\":[{\"conf\":0.5,\"bbox\":[1,2,3,4]}]}")]
        [TestCase("{\"type\":\"detections\",\"frame\":1,\"width\":640,\"height\":480}")]
        [TestCase("not json")]
        [TestCase("")]
        [TestCase("{\"type\":\"something-else\"}")]
        public void Parse_InvalidPayloads_ReturnUnknown(string json)
        {
            Assert.That(
                DetectionChannelParser.Parse(json, out _),
                Is.EqualTo(DetectionChannelMessageKind.Unknown));
        }
    }

    public class SignalingMessagesTests
    {
        [Test]
        public void Candidate_StripsThePrefixOnTheWire()
        {
            var json = SignalingMessages.Candidate("candidate:842163049 1 udp 1677729535 1.2.3.4 3478 typ srflx", "0", 0);

            StringAssert.Contains("\"candidate\":\"842163049 1 udp", json);
            StringAssert.DoesNotContain("candidate:842163049", json);
        }

        [Test]
        public void Candidate_PrefixlessInput_PassesThrough()
        {
            var json = SignalingMessages.Candidate("842163049 1 udp 1677729535 1.2.3.4 3478 typ srflx", "0", 0);

            StringAssert.Contains("\"candidate\":\"842163049 1 udp", json);
        }

        [Test]
        public void CandidateSdp_HelpersAreIdempotent()
        {
            Assert.That(CandidateSdp.StripPrefix(CandidateSdp.StripPrefix("candidate:x")), Is.EqualTo("x"));
            Assert.That(CandidateSdp.EnsurePrefix(CandidateSdp.EnsurePrefix("x")), Is.EqualTo("candidate:x"));
        }

        [Test]
        public void Parse_Answer_ReturnsSdp()
        {
            var kind = SignalingMessages.Parse("{\"type\":\"answer\",\"sdp\":\"v=0...\"}", out var sdp, out _);

            Assert.That(kind, Is.EqualTo(SignalingMessageKind.Answer));
            Assert.That(sdp, Is.EqualTo("v=0..."));
        }

        [Test]
        public void Parse_Candidate_ReadsAllFields()
        {
            var kind = SignalingMessages.Parse(
                "{\"type\":\"candidate\",\"candidate\":\"842 1 udp ...\",\"sdpMid\":\"0\",\"sdpMLineIndex\":0}",
                out _, out var candidate);

            Assert.That(kind, Is.EqualTo(SignalingMessageKind.Candidate));
            Assert.That(candidate.Candidate, Is.EqualTo("842 1 udp ..."));
            Assert.That(candidate.SdpMid, Is.EqualTo("0"));
        }

        [Test]
        public void Parse_CandidateWithoutSdpMid_IsRejected()
        {
            // aiortc requires sdpMid; the server drops such messages, so must we.
            var kind = SignalingMessages.Parse(
                "{\"type\":\"candidate\",\"candidate\":\"842 1 udp ...\"}",
                out _, out _);

            Assert.That(kind, Is.EqualTo(SignalingMessageKind.Unknown));
        }

        [TestCase("not json")]
        [TestCase("{\"type\":\"offer\",\"sdp\":\"x\"}")]
        public void Parse_MalformedOrUnexpected_ReturnsUnknown(string json)
        {
            Assert.That(SignalingMessages.Parse(json, out _, out _), Is.EqualTo(SignalingMessageKind.Unknown));
        }

        [Test]
        public void Status_BuildsExpectedShape()
        {
            var json = SignalingMessages.Status("camera", "capturing 1280x960");

            StringAssert.Contains("\"type\":\"status\"", json);
            StringAssert.Contains("\"field\":\"camera\"", json);
        }
    }

    public class ServerCloseCodesTests
    {
        [Test]
        public void SuppressesReconnect_OnlyForDeliberateEviction()
        {
            Assert.That(ServerCloseCodes.SuppressesReconnect(4000), Is.True);
            Assert.That(ServerCloseCodes.SuppressesReconnect(4001), Is.True);
            Assert.That(ServerCloseCodes.SuppressesReconnect(4401), Is.False);
            Assert.That(ServerCloseCodes.SuppressesReconnect(1006), Is.False);
        }
    }
}
