// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using QuestVisionStream.Core;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>A validated detections payload plus its arrival time on the pose clock.</summary>
    public sealed class DetectionArrival
    {
        public DetectionArrival(DetectionsPayload payload, RenderBatch batch, double arrivalTimeMs)
        {
            Payload = payload;
            Batch = batch;
            ArrivalTimeMs = arrivalTimeMs;
        }

        public DetectionsPayload Payload { get; }

        /// <summary>The payload pre-normalized to viewport space (profile invert flags applied).</summary>
        public RenderBatch Batch { get; }

        public double ArrivalTimeMs { get; }
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

        /// <summary>Total validated payloads this session.</summary>
        long ReceivedCount { get; }

        /// <summary>Messages dropped by validation this session.</summary>
        long InvalidCount { get; }
    }
}
