// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using QuestVisionStream.Core;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="DetectionService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Detection Service Profile", fileName = "DetectionServiceProfile")]
    public class DetectionServiceProfile : BaseProfile
    {
        [SerializeField]
        [Tooltip("Flip Y when normalizing bboxes. The server v-flips frames by default (QVS_FLIP_VERTICAL=true), so this stays on unless the server config changes.")]
        private bool invertY = true;

        [SerializeField]
        [Tooltip("Flip X when normalizing bboxes — for mirrored streams.")]
        private bool invertX = false;

        public bool InvertY { get => invertY; set => invertY = value; }
        public bool InvertX { get => invertX; set => invertX = value; }
    }

    /// <summary>
    /// <see cref="IDetectionService"/>: subscribes to the WebRTC data channel,
    /// validates payloads, feeds pts into the pose/latency service and publishes
    /// normalized render batches.
    /// </summary>
    [System.Runtime.InteropServices.Guid("bd36bae9-60d2-48ee-8130-4b7627ba773a")]
    public class DetectionService : BaseServiceWithConstructor, IDetectionService
    {
        private readonly DetectionServiceProfile profile;
        private readonly IWebRTCService webrtc;
        private readonly IPoseTrackingService poseTracking;

        public DetectionService(
            string name,
            uint priority,
            DetectionServiceProfile profile,
            IWebRTCService webrtc,
            IPoseTrackingService poseTracking)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.webrtc = webrtc ?? throw new ArgumentNullException(nameof(webrtc));
            this.poseTracking = poseTracking ?? throw new ArgumentNullException(nameof(poseTracking));
        }

        public event Action ServerReady;
        public event Action<DetectionArrival> DetectionsReceived;

        public long ReceivedCount { get; private set; }
        public long InvalidCount { get; private set; }

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();
            webrtc.DetectionMessageReceived += OnDataChannelMessage;
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            webrtc.DetectionMessageReceived -= OnDataChannelMessage;
            base.Destroy();
        }

        private void OnDataChannelMessage(string json)
        {
            switch (DetectionChannelParser.Parse(json, out var payload))
            {
                case DetectionChannelMessageKind.Ready:
                    Debug.Log("[QVS:Detection] Server ready");
                    ServerReady?.Invoke();
                    break;

                case DetectionChannelMessageKind.Detections:
                    ReceivedCount++;
                    var arrivalMs = poseTracking.NowMs;
                    poseTracking.ObserveArrival(arrivalMs, payload.Pts);
                    var batch = DetectionMath.ToRenderBatch(payload, profile.InvertY, profile.InvertX);
                    DetectionsReceived?.Invoke(new DetectionArrival(payload, batch, arrivalMs));
                    break;

                default:
                    InvalidCount++;
                    if (InvalidCount == 1 || InvalidCount % 100 == 0)
                    {
                        Debug.LogWarning($"[QVS:Detection] Dropped {InvalidCount} unrecognised/invalid channel message(s)");
                    }
                    break;
            }
        }
    }
}
