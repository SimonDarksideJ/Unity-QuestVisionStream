// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using RealityCollective.ServiceFramework.Definitions;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="WebRTCService"/> and its transport modules.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/WebRTC Service Profile", fileName = "WebRTCServiceProfile")]
    public class WebRTCServiceProfile : BaseServiceProfile<IWebRTCTransportModule>
    {
        [Header("ICE")]
        [SerializeField]
        [Tooltip("STUN servers. On LAN/Tailscale these are all that's needed (media flows directly).")]
        private string[] stunUrls = { "stun:stun.l.google.com:19302" };

        [SerializeField]
        [Tooltip("Optional TURN server for crossing NATs when not on LAN/Tailscale.")]
        private bool enableTurn = false;

        [SerializeField]
        private string turnUrl = "";

        [SerializeField]
        private string turnUsername = "";

        [SerializeField]
        private string turnCredential = "";

        [Header("Streaming")]
        [SerializeField]
        [Range(1, 30)]
        [Tooltip("Frame rate cap applied by the transport.")]
        private int targetFps = 30;

        [SerializeField]
        [Range(1, 4)]
        [Tooltip("Push every Nth rendered frame into the encoder (2 halves the JNI/readback load; the proven V1 default).")]
        private int sendEveryNthFrame = 2;

        [SerializeField]
        [Tooltip("Convert RGB→I420 on the GPU (compute shader + async readback). Disable to send RGB24 and convert in the plugin.")]
        private bool useGpuYuvConversion = true;

        [SerializeField]
        [Tooltip("Stop pushing frames while the image qualifier reports ShouldStream=false. OFF by default: the raw passthrough feed reads darker than the tone-mapped view, and gating on it deadlocked the WebXR client.")]
        private bool gateStreamingOnQuality = false;

        [Header("Recovery")]
        [SerializeField]
        [Tooltip("Renegotiate automatically when the peer connection fails or signaling is restored.")]
        private bool autoReconnect = true;

        [SerializeField]
        [Tooltip("Delay before an automatic renegotiation, in seconds.")]
        private float reconnectDelaySeconds = 2f;

        public string[] StunUrls { get => stunUrls; set => stunUrls = value; }
        public bool EnableTurn { get => enableTurn; set => enableTurn = value; }
        public string TurnUrl { get => turnUrl; set => turnUrl = value; }
        public string TurnUsername { get => turnUsername; set => turnUsername = value; }
        public string TurnCredential { get => turnCredential; set => turnCredential = value; }
        public int TargetFps { get => targetFps; set => targetFps = value; }
        public int SendEveryNthFrame { get => sendEveryNthFrame; set => sendEveryNthFrame = value; }
        public bool UseGpuYuvConversion { get => useGpuYuvConversion; set => useGpuYuvConversion = value; }
        public bool GateStreamingOnQuality { get => gateStreamingOnQuality; set => gateStreamingOnQuality = value; }
        public bool AutoReconnect { get => autoReconnect; set => autoReconnect = value; }
        public float ReconnectDelaySeconds { get => reconnectDelaySeconds; set => reconnectDelaySeconds = value; }
    }
}
