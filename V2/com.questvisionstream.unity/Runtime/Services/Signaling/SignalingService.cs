// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;
using Utilities.WebSockets;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// <see cref="ISignalingService"/> over <c>com.utilities.websockets</c> (all
    /// events arrive on the Unity main thread, so no locking is needed here).
    /// Ported from the hardened V2 TypeScript <c>SignalingService</c>: bounded
    /// backoff, one reconnect in flight at a time, stale-socket guards, no
    /// reconnect on the server's deliberate-eviction close codes (4000/4001).
    /// </summary>
    [System.Runtime.InteropServices.Guid("9ef447c2-21f5-4b5b-acbb-1510b4d6bc04")]
    public class SignalingService : BaseServiceWithConstructor, ISignalingService
    {
        private readonly SignalingServiceProfile profile;

        /// <summary>
        /// Per-app-launch connection identity. A reconnect carrying the same cid
        /// cleanly replaces our own half-open prior session (server close 4001)
        /// instead of fighting the connection cap.
        /// </summary>
        private readonly string connectionId = Guid.NewGuid().ToString("N");

        private WebSocket socket;
        private bool closedByUser;
        private int reconnectAttempt;
        private float reconnectAtRealtime = -1f;
        private float nextKeepAliveRealtime = -1f;
        private SignalingCloseInfo? lastCloseInfo;

        public SignalingService(string name, uint priority, SignalingServiceProfile profile)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
        }

        public event Action Connected;
        public event Action<int> Disconnected;
        public event Action<string> AnswerReceived;
        public event Action<IceCandidateMessage> CandidateReceived;

        public bool IsConnected => socket != null && socket.State == State.Open;

        public SignalingCloseInfo? LastCloseInfo => lastCloseInfo;

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            if (profile.AutoConnect)
            {
                Connect();
            }
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            var now = Time.realtimeSinceStartup;

            // Scheduled reconnect (single timer — never stacked).
            if (reconnectAtRealtime >= 0 && now >= reconnectAtRealtime)
            {
                reconnectAtRealtime = -1f;
                Connect();
            }

            // Application-level keepalive: __keepalive generates traffic without server log spam.
            if (IsConnected && profile.KeepAliveIntervalSeconds > 0 && now >= nextKeepAliveRealtime)
            {
                nextKeepAliveRealtime = now + profile.KeepAliveIntervalSeconds;
                SendStatus("__keepalive", string.Empty);
            }
        }

        /// <inheritdoc />
        public void Connect()
        {
            closedByUser = false;
            reconnectAtRealtime = -1f;

            if (socket != null &&
                (socket.State == State.Open || socket.State == State.Connecting))
            {
                // Never open a second socket while one is live or still connecting —
                // overlapping sockets are exactly how ghost sessions were created.
                return;
            }

            DisposeSocket();

            var url = BuildUrl();
            Debug.Log($"[QVS:Signaling] Connecting to {profile.ServerUrl}");
            var newSocket = new WebSocket(url);
            socket = newSocket;

            newSocket.OnOpen += () =>
            {
                if (socket != newSocket) { return; } // superseded while connecting
                Debug.Log("[QVS:Signaling] Connected");
                reconnectAttempt = 0;
                nextKeepAliveRealtime = Time.realtimeSinceStartup + profile.KeepAliveIntervalSeconds;
                Connected?.Invoke();
            };

            newSocket.OnMessage += dataFrame =>
            {
                if (socket != newSocket || dataFrame.Type != OpCode.Text) { return; }
                HandleMessage(dataFrame.Text);
            };

            newSocket.OnError += exception =>
            {
                if (socket != newSocket) { return; }
                Debug.LogWarning($"[QVS:Signaling] Socket error: {exception.Message}");
                // OnClose follows and drives the retry — nothing else to do here.
            };

            newSocket.OnClose += (code, reason) =>
            {
                // A late close from a superseded socket must not look like a live
                // disconnect (or double-schedule reconnects).
                if (socket != newSocket) { return; }
                socket = null;
                newSocket.Dispose();

                var closeCode = (int)code;
                lastCloseInfo = new SignalingCloseInfo(closeCode, reason);
                Debug.LogWarning($"[QVS:Signaling] Disconnected: {lastCloseInfo}");
                Disconnected?.Invoke(closeCode);

                // 4000 = superseded by a different client; 4001 = replaced by our own
                // reconnect. The server deliberately closed us in favour of another
                // connection — reconnecting would just fight it. Stay down.
                if (ServerCloseCodes.SuppressesReconnect(closeCode))
                {
                    Debug.LogWarning($"[QVS:Signaling] Closed by server ({ServerCloseCodes.Describe(closeCode)}) — not reconnecting.");
                    return;
                }

                if (!closedByUser)
                {
                    ScheduleReconnect();
                }
            };

            newSocket.Connect();
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            closedByUser = true;
            reconnectAtRealtime = -1f;

            if (socket != null)
            {
                var closing = socket;
                socket = null;
                closing.Close();
                closing.Dispose();
            }
        }

        /// <inheritdoc />
        public void SendOffer(string sdp) => Send(SignalingMessages.Offer(sdp));

        /// <inheritdoc />
        public void SendCandidate(string candidate, string sdpMid, int sdpMLineIndex)
            => Send(SignalingMessages.Candidate(candidate, sdpMid, sdpMLineIndex));

        /// <inheritdoc />
        public void SendStatus(string field, string value)
            => Send(SignalingMessages.Status(field, value), quiet: true);

        /// <inheritdoc />
        public override void Destroy()
        {
            Disconnect();
            base.Destroy();
        }

        private void Send(string message, bool quiet = false)
        {
            if (!IsConnected)
            {
                if (!quiet)
                {
                    Debug.LogWarning("[QVS:Signaling] Dropping message; socket not open");
                }
                return;
            }

            _ = socket.SendAsync(message);
        }

        private void HandleMessage(string json)
        {
            // Unknown/malformed messages are skipped, never fatal (additive wire rule).
            switch (SignalingMessages.Parse(json, out var sdp, out var candidate))
            {
                case SignalingMessageKind.Answer:
                    AnswerReceived?.Invoke(sdp);
                    break;
                case SignalingMessageKind.Candidate:
                    CandidateReceived?.Invoke(candidate);
                    break;
            }
        }

        private void ScheduleReconnect()
        {
            if (reconnectAtRealtime >= 0)
            {
                return; // one reconnect in flight at a time
            }

            var backoff = profile.ReconnectBackoffSeconds;
            if (backoff == null || backoff.Length == 0)
            {
                return;
            }

            var delay = backoff[Mathf.Min(reconnectAttempt, backoff.Length - 1)];
            reconnectAttempt++;
            Debug.Log($"[QVS:Signaling] Reconnecting in {delay:0.#}s (attempt {reconnectAttempt})");
            reconnectAtRealtime = Time.realtimeSinceStartup + delay;
        }

        private Uri BuildUrl()
        {
            var url = profile.ServerUrl.TrimEnd('/');
            var separator = url.Contains("?") ? "&" : "?";
            url = $"{url}{separator}cid={connectionId}";
            if (!string.IsNullOrEmpty(profile.AuthToken))
            {
                url += $"&token={Uri.EscapeDataString(profile.AuthToken)}";
            }

            return new Uri(url);
        }

        private void DisposeSocket()
        {
            if (socket == null)
            {
                return;
            }

            var old = socket;
            socket = null;
            try
            {
                old.Dispose();
            }
            catch
            {
                // Disposing a partially-opened socket must never take the service down.
            }
        }
    }
}
