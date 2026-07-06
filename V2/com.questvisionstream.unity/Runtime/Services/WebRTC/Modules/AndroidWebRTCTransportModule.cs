// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Interfaces;
using RealityCollective.ServiceFramework.Modules;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// <see cref="IWebRTCTransportModule"/> over the minimal
    /// <c>QuestVisionStreamPlugin.androidlib</c> (native google-webrtc
    /// 125.6422.06.1). The V2 plugin is media-only: this module drives it through
    /// JNI and receives its single JSON event pipe via <see cref="QvsPluginEventReceiver"/>.
    /// Unavailable off-device (Editor/desktop) — the service logs and stays idle.
    /// </summary>
    [System.Runtime.InteropServices.Guid("213947d4-85e2-4406-b55d-4b07a79a042c")]
    public class AndroidWebRTCTransportModule : BaseServiceModule, IWebRTCTransportModule
    {
        private const string PluginClass = "com.questvisionstream.QuestVisionStreamManager";
        private const string ReceiverName = "QVSPluginEventReceiver";

        private AndroidJavaObject plugin;
        private QvsPluginEventReceiver receiver;
        private bool sessionActive;

        public AndroidWebRTCTransportModule(string name, uint priority, BaseProfile profile, IWebRTCService parentService)
            : base(name, priority, profile, parentService)
        {
        }

        public event Action<string> LocalOfferCreated;
        public event Action<IceCandidateMessage> LocalCandidateGathered;
        public event Action<WebRTCConnectionState> ConnectionStateChanged;
        public event Action<string> DataChannelMessageReceived;

        public bool IsAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public bool IsSessionActive => sessionActive;

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            if (!IsAvailable)
            {
                return;
            }

            var receiverObject = new GameObject(ReceiverName) { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(receiverObject);
            receiver = receiverObject.AddComponent<QvsPluginEventReceiver>();
            receiver.EventReceived += OnPluginEvent;

#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                plugin = new AndroidJavaObject(PluginClass, activity, ReceiverName);
            }
#endif
        }

        /// <inheritdoc />
        public void ConfigureIce(IReadOnlyList<IceServerConfig> iceServers)
        {
            if (plugin == null)
            {
                return;
            }

            using (var list = new AndroidJavaObject("java.util.ArrayList"))
            {
                foreach (var server in iceServers)
                {
                    if (string.IsNullOrEmpty(server.Username))
                    {
                        list.Call<bool>("add", server.Url);
                    }
                }

                plugin.Call("setIceServers", list);
            }

            foreach (var server in iceServers)
            {
                if (!string.IsNullOrEmpty(server.Username))
                {
                    plugin.Call("addTurnServer", server.Url, server.Username, server.Credential ?? string.Empty);
                }
            }
        }

        /// <inheritdoc />
        public void StartSession(int width, int height, int targetFps)
        {
            if (plugin == null)
            {
                Debug.LogWarning("[QVS:AndroidWebRTC] StartSession ignored — plugin unavailable on this platform.");
                return;
            }

            sessionActive = true;
            plugin.Call("startSession", width, height, targetFps);
        }

        /// <inheritdoc />
        public void SetRemoteAnswer(string sdp) => plugin?.Call("setRemoteAnswer", sdp);

        /// <inheritdoc />
        public void AddRemoteCandidate(IceCandidateMessage candidate)
            => plugin?.Call("addRemoteCandidate", candidate.Candidate, candidate.SdpMid, candidate.SdpMLineIndex);

        /// <inheritdoc />
        public void PushFrameYuv(byte[] y, byte[] u, byte[] v, int width, int height)
            => plugin?.Call("updateFrameDataYUV", y, u, v, width, height);

        /// <inheritdoc />
        public void PushFrameRgb(byte[] rgb, int width, int height)
            => plugin?.Call("updateFrameData", rgb, width, height);

        /// <inheritdoc />
        public void SendDataChannelMessage(string message) => plugin?.Call("sendDataChannelMessage", message);

        /// <inheritdoc />
        public void CloseSession()
        {
            sessionActive = false;
            plugin?.Call("closeSession");
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            CloseSession();
            plugin?.Dispose();
            plugin = null;

            if (receiver != null)
            {
                receiver.EventReceived -= OnPluginEvent;
                UnityEngine.Object.Destroy(receiver.gameObject);
                receiver = null;
            }

            base.Destroy();
        }

        private void OnPluginEvent(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                Debug.LogWarning("[QVS:AndroidWebRTC] Ignoring malformed plugin event");
                return;
            }

            switch (root.Value<string>("event"))
            {
                case "localOffer":
                    LocalOfferCreated?.Invoke(root.Value<string>("sdp"));
                    break;

                case "localCandidate":
                    LocalCandidateGathered?.Invoke(new IceCandidateMessage(
                        root.Value<string>("candidate"),
                        root.Value<string>("sdpMid"),
                        root.Value<int?>("sdpMLineIndex") ?? 0));
                    break;

                case "pcState":
                    ConnectionStateChanged?.Invoke(MapState(root.Value<string>("state")));
                    break;

                case "dcMessage":
                    DataChannelMessageReceived?.Invoke(root.Value<string>("message"));
                    break;

                case "dcState":
                    Debug.Log($"[QVS:AndroidWebRTC] DataChannel '{root.Value<string>("label")}': {root.Value<string>("state")}");
                    break;

                case "error":
                    Debug.LogError($"[QVS:AndroidWebRTC] Plugin error: {root.Value<string>("message")}");
                    break;
            }
        }

        private static WebRTCConnectionState MapState(string pluginState)
        {
            switch (pluginState)
            {
                case "NEW": return WebRTCConnectionState.New;
                case "CONNECTING": return WebRTCConnectionState.Connecting;
                case "CONNECTED": return WebRTCConnectionState.Connected;
                case "DISCONNECTED": return WebRTCConnectionState.Disconnected;
                case "FAILED": return WebRTCConnectionState.Failed;
                case "CLOSED": return WebRTCConnectionState.Closed;
                default: return WebRTCConnectionState.New;
            }
        }
    }
}
