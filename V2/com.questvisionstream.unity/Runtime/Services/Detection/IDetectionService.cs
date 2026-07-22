// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using QuestVisionStream.Core;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>Which pipeline produced a detections payload.</summary>
    public enum DetectionOrigin
    {
        /// <summary>The inference server, over the WebRTC data channel.</summary>
        Server = 0,

        /// <summary>An on-device AprilTag sighting bridged into the detection pipeline.</summary>
        AprilTag
    }

    /// <summary>A validated detections payload plus its arrival time on the pose clock.</summary>
    public sealed class DetectionArrival
    {
        public DetectionArrival(DetectionsPayload payload, RenderBatch batch, double arrivalTimeMs, DetectionOrigin origin = DetectionOrigin.Server)
        {
            Payload = payload;
            Batch = batch;
            ArrivalTimeMs = arrivalTimeMs;
            Origin = origin;
        }

        public DetectionsPayload Payload { get; }

        /// <summary>The payload pre-normalized to viewport space (profile invert flags applied).</summary>
        public RenderBatch Batch { get; }

        public double ArrivalTimeMs { get; }

        /// <summary>Where the payload came from. Consumers that only care about classes can ignore this.</summary>
        public DetectionOrigin Origin { get; }
    }

    /// <summary>
    /// Parses and validates the detections data channel: emits the server ready
    /// handshake and per-frame detection batches. Invalid payloads are counted and
    /// dropped — they can never produce NaN geometry downstream.
    /// </summary>
    public interface IDetectionService : IService
    {
        /// <summary>The server sent its <c>{"type":"ready"}</c> handshake.</summary>
        event Action ServerReady;

        /// <summary>A validated payload arrived (normalized batch + arrival timestamp included).</summary>
        event Action<DetectionArrival> DetectionsReceived;

        /// <summary>Total validated SERVER payloads this session.</summary>
        long ReceivedCount { get; }

        /// <summary>Total validated locally published payloads this session (e.g. AprilTag bridge).</summary>
        long LocalCount { get; }

        /// <summary>Messages dropped by validation this session.</summary>
        long InvalidCount { get; }

        /// <summary>
        /// Publish a locally synthesized, wire-shaped detections payload into the
        /// SAME validated pipeline server payloads use, so on-device sources
        /// (AprilTags today) reach every consumer — renderer, HUD, training —
        /// through one seam. The JSON is run through the standard channel parser;
        /// invalid payloads are counted and dropped. Coordinates must already be
        /// viewport-native (origin bottom-left) — the capture invert flags are NOT
        /// applied to local payloads.
        /// </summary>
        void PublishLocal(string json, DetectionOrigin origin);
    }
}
