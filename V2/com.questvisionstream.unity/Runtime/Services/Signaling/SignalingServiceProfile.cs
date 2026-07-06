// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using RealityCollective.ServiceFramework.Definitions;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="SignalingService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Signaling Service Profile", fileName = "SignalingServiceProfile")]
    public class SignalingServiceProfile : BaseProfile
    {
        [Header("Server")]
        [SerializeField]
        [Tooltip("Signaling WebSocket URL, e.g. ws://100.x.x.x:3000 (Tailscale IP) or wss://machine.tailnet.ts.net")]
        private string serverUrl = "ws://localhost:3000";

        [SerializeField]
        [Tooltip("Optional shared auth token (the server's QVS_AUTH_TOKEN). Sent as ?token=.")]
        private string authToken = "";

        [Header("Behaviour")]
        [SerializeField]
        [Tooltip("Connect as soon as the service starts.")]
        private bool autoConnect = true;

        [SerializeField]
        [Tooltip("Reconnect backoff in seconds; empty disables reconnect. Never applies to server close codes 4000/4001.")]
        private float[] reconnectBackoffSeconds = { 1f, 2f, 4f, 8f };

        [SerializeField]
        [Tooltip("Interval for the __keepalive status heartbeat that stops idle proxies (e.g. tailscale serve) dropping the socket. 0 disables.")]
        private float keepAliveIntervalSeconds = 15f;

        public string ServerUrl { get => serverUrl; set => serverUrl = value; }
        public string AuthToken { get => authToken; set => authToken = value; }
        public bool AutoConnect { get => autoConnect; set => autoConnect = value; }
        public float[] ReconnectBackoffSeconds { get => reconnectBackoffSeconds; set => reconnectBackoffSeconds = value; }
        public float KeepAliveIntervalSeconds { get => keepAliveIntervalSeconds; set => keepAliveIntervalSeconds = value; }
    }
}
