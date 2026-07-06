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

        [SerializeField]
        [Tooltip("Log every payload received from the server in full detail, prefixed [WSDetection] for logcat filtering.")]
        private bool verboseDetectionLogging = true;

        public bool InvertY { get => invertY; set => invertY = value; }
        public bool InvertX { get => invertX; set => invertX = value; }
        public bool VerboseDetectionLogging { get => verboseDetectionLogging; set => verboseDetectionLogging = value; }
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
                    Debug.Log("[WSDetection] server ready handshake received");
                    ServerReady?.Invoke();
                    break;

                case DetectionChannelMessageKind.Detections:
                    ReceivedCount++;
                    var arrivalMs = poseTracking.NowMs;
                    poseTracking.ObserveArrival(arrivalMs, payload.Pts);
                    var batch = DetectionMath.ToRenderBatch(payload, profile.InvertY, profile.InvertX);

                    if (profile.VerboseDetectionLogging)
                    {
                        Debug.Log(DescribePayload(payload, batch));
                    }

                    DetectionsReceived?.Invoke(new DetectionArrival(payload, batch, arrivalMs));
                    break;

                default:
                    InvalidCount++;
                    // Log the raw message so a schema drift is visible, not silent.
                    var excerpt = json == null ? "<null>" : json.Length > 300 ? json.Substring(0, 300) + "…" : json;
                    Debug.LogWarning($"[WSDetection] UNPARSED channel message #{InvalidCount}: {excerpt}");
                    break;
            }
        }

        /// <summary>Full-detail payload dump, [WSDetection]-prefixed for logcat filtering.</summary>
        private static string DescribePayload(DetectionsPayload payload, RenderBatch batch)
        {
            var builder = new System.Text.StringBuilder(256);
            builder.Append("[WSDetection] frame=").Append(payload.Frame)
                .Append(" pts=").Append(payload.Pts.HasValue ? payload.Pts.Value.ToString() : "null")
                .Append(" size=").Append(payload.Width).Append('x').Append(payload.Height)
                .Append(" count=").Append(payload.Detections.Count);

            for (var i = 0; i < payload.Detections.Count; i++)
            {
                var d = payload.Detections[i];
                var n = batch.Detections[i];
                builder.Append(i == 0 ? " :: " : " | ")
                    .Append(d.Label)
                    .Append(' ').Append(d.Conf.ToString("0.00"))
                    .Append(" bbox=[").Append(d.X1.ToString("0")).Append(',').Append(d.Y1.ToString("0"))
                    .Append(',').Append(d.X2.ToString("0")).Append(',').Append(d.Y2.ToString("0"))
                    .Append("] center=(").Append(n.Center.x.ToString("0.000")).Append(',').Append(n.Center.y.ToString("0.000"))
                    .Append(") rect=(").Append(n.Rect.X.ToString("0.000")).Append(',').Append(n.Rect.Y.ToString("0.000"))
                    .Append(',').Append(n.Rect.Width.ToString("0.000")).Append(',').Append(n.Rect.Height.ToString("0.000"))
                    .Append(')');
            }

            return builder.ToString();
        }
    }
}
