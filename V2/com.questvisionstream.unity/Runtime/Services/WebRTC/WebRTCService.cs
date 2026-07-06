// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// <see cref="IWebRTCService"/>: session orchestration ported from the hardened
    /// V2 TypeScript <c>WebRTCService</c> plus the V1 frame pipeline. Waits for
    /// camera Active + signaling connected, starts the transport session, bridges
    /// SDP/ICE, pumps frames every Nth update, and renegotiates on terminal
    /// failure or restored signaling — every failure mode has a recovery path.
    /// </summary>
    [System.Runtime.InteropServices.Guid("97851a3f-7943-4e80-9869-8548cde814eb")]
    public class WebRTCService : BaseServiceWithConstructor, IWebRTCService
    {
        private readonly WebRTCServiceProfile profile;
        private readonly ISignalingService signaling;
        private readonly ICameraStreamService camera;

        private IImageQualifierService qualifier;
        private YuvFramePump pump;
        private WebRTCConnectionState state = WebRTCConnectionState.New;
        private bool sessionRequested;
        private bool everAttempted;
        private float reconnectAtRealtime = -1f;
        private int frameCounter;

        public WebRTCService(
            string name,
            uint priority,
            WebRTCServiceProfile profile,
            ISignalingService signaling,
            ICameraStreamService camera)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.signaling = signaling ?? throw new ArgumentNullException(nameof(signaling));
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        public event Action<WebRTCConnectionState> StateChanged;
        public event Action<string> DetectionMessageReceived;

        public WebRTCConnectionState State => state;

        private IWebRTCTransportModule Transport
        {
            get
            {
                foreach (var module in ServiceModules)
                {
                    if (module is IWebRTCTransportModule transport && transport.IsAvailable)
                    {
                        return transport;
                    }
                }

                return null;
            }
        }

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            // The qualifier is an optional collaborator — resolve it lazily so a
            // profile without one still works.
            RealityCollective.ServiceFramework.Services.ServiceManager.Instance
                ?.TryGetService(out qualifier);

            signaling.AnswerReceived += OnAnswerReceived;
            signaling.CandidateReceived += OnCandidateReceived;
            signaling.Connected += OnSignalingConnected;

            pump = new YuvFramePump(profile.UseGpuYuvConversion);

            var transport = Transport;
            if (transport == null)
            {
                Debug.LogWarning("[QVS:WebRTC] No available transport module registered — streaming disabled on this platform.");
                return;
            }

            transport.LocalOfferCreated += OnLocalOffer;
            transport.LocalCandidateGathered += OnLocalCandidate;
            transport.ConnectionStateChanged += OnTransportStateChanged;
            transport.DataChannelMessageReceived += OnDataChannelMessage;
            transport.ConfigureIce(BuildIceServers());
        }

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            var transport = Transport;
            if (transport == null)
            {
                return;
            }

            // Scheduled renegotiation (single timer).
            if (reconnectAtRealtime >= 0 && Time.realtimeSinceStartup >= reconnectAtRealtime)
            {
                reconnectAtRealtime = -1f;
                RestartSession();
                return;
            }

            // Session start gate: camera delivering frames + signaling open.
            if (!sessionRequested &&
                camera.State == CameraStreamState.Active &&
                signaling.IsConnected)
            {
                BeginSession(transport);
                return;
            }

            // Frame pump.
            if (sessionRequested &&
                transport.IsSessionActive &&
                camera.State == CameraStreamState.Active)
            {
                frameCounter++;
                if (frameCounter % Mathf.Max(1, profile.SendEveryNthFrame) != 0)
                {
                    return;
                }

                if (profile.GateStreamingOnQuality && qualifier != null && !qualifier.ShouldStream)
                {
                    return;
                }

                pump.PumpFrame(camera.SourceTexture, transport);
            }
        }

        /// <inheritdoc />
        public void RestartSession()
        {
            var transport = Transport;
            if (transport == null)
            {
                return;
            }

            Debug.Log("[QVS:WebRTC] Restarting session");
            transport.CloseSession();
            sessionRequested = false;
            // Update() re-enters BeginSession once camera + signaling are ready again.
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            signaling.AnswerReceived -= OnAnswerReceived;
            signaling.CandidateReceived -= OnCandidateReceived;
            signaling.Connected -= OnSignalingConnected;

            Transport?.CloseSession();
            pump?.Dispose();
            pump = null;

            base.Destroy();
        }

        private void BeginSession(IWebRTCTransportModule transport)
        {
            sessionRequested = true;
            everAttempted = true;
            frameCounter = 0;

            var resolution = camera.StreamResolution;
            Debug.Log($"[QVS:WebRTC] Starting session {resolution.x}x{resolution.y} @{profile.TargetFps}fps");
            pump.Configure(resolution.x, resolution.y);
            transport.StartSession(resolution.x, resolution.y, profile.TargetFps);
        }

        private void OnLocalOffer(string sdp)
        {
            Debug.Log("[QVS:WebRTC] Offer created; sending");
            signaling.SendOffer(sdp);
        }

        private void OnLocalCandidate(IceCandidateMessage candidate)
            => signaling.SendCandidate(candidate.Candidate, candidate.SdpMid, candidate.SdpMLineIndex);

        private void OnAnswerReceived(string sdp) => Transport?.SetRemoteAnswer(sdp);

        private void OnCandidateReceived(IceCandidateMessage candidate) => Transport?.AddRemoteCandidate(candidate);

        private void OnSignalingConnected()
        {
            // Signaling came back after a drop: if our session never completed (or died
            // with it), the server no longer knows us — renegotiate.
            if (everAttempted && state != WebRTCConnectionState.Connected)
            {
                ScheduleReconnect("signaling restored");
            }
        }

        private void OnTransportStateChanged(WebRTCConnectionState newState)
        {
            if (newState == state)
            {
                return;
            }

            state = newState;
            Debug.Log($"[QVS:WebRTC] State: {newState}");
            StateChanged?.Invoke(newState);

            // 'Failed' is terminal (unlike 'Disconnected', which ICE can self-heal);
            // recover by renegotiating a fresh session.
            if (newState == WebRTCConnectionState.Failed)
            {
                ScheduleReconnect("peer connection failed");
            }
        }

        private void OnDataChannelMessage(string message) => DetectionMessageReceived?.Invoke(message);

        private void ScheduleReconnect(string reason)
        {
            if (!profile.AutoReconnect || reconnectAtRealtime >= 0)
            {
                return;
            }

            Debug.Log($"[QVS:WebRTC] Renegotiating in {profile.ReconnectDelaySeconds:0.#}s ({reason})");
            reconnectAtRealtime = Time.realtimeSinceStartup + profile.ReconnectDelaySeconds;
        }

        private IReadOnlyList<IceServerConfig> BuildIceServers()
        {
            var servers = new List<IceServerConfig>();
            if (profile.StunUrls != null)
            {
                foreach (var url in profile.StunUrls)
                {
                    if (!string.IsNullOrEmpty(url))
                    {
                        servers.Add(new IceServerConfig { Url = url });
                    }
                }
            }

            if (profile.EnableTurn && !string.IsNullOrEmpty(profile.TurnUrl))
            {
                servers.Add(new IceServerConfig
                {
                    Url = profile.TurnUrl,
                    Username = profile.TurnUsername,
                    Credential = profile.TurnCredential
                });
            }

            return servers;
        }
    }
}
