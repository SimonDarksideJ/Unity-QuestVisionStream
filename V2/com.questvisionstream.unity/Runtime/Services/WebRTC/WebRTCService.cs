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
        private bool hadSignalingDrop;
        private bool firstFramePushed;
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
        public event Action StreamingBegan;
        public event Action<string> DiagnosticChanged;
        public event Action<string> DetectionMessageReceived;

        public WebRTCConnectionState State => state;

        public string LastDiagnostic { get; private set; } = "idle";

        public bool StreamingRequested { get; private set; }

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

            StreamingRequested = profile.AutoStartSession;

            // The qualifier is an optional collaborator — resolve it lazily so a
            // profile without one still works.
            RealityCollective.ServiceFramework.Services.ServiceManager.Instance
                ?.TryGetService(out qualifier);

            signaling.AnswerReceived += OnAnswerReceived;
            signaling.CandidateReceived += OnCandidateReceived;
            signaling.Connected += OnSignalingConnected;
            signaling.Disconnected += OnSignalingDisconnected;

            pump = new YuvFramePump(profile.UseGpuYuvConversion, profile.FlipStreamVertically);

            var transport = Transport;
            if (transport == null)
            {
                Debug.LogWarning("[QVS:WebRTC] No available transport module registered — streaming disabled on this platform.");
                return;
            }

            transport.LocalOfferCreated += OnLocalOffer;
            transport.LocalCandidateGathered += OnLocalCandidate;
            transport.ConnectionStateChanged += OnTransportStateChanged;
            transport.IceStateChanged += OnIceStateChanged;
            transport.DataChannelMessageReceived += OnDataChannelMessage;
            transport.ConfigureIce(BuildIceServers());
        }

        /// <inheritdoc />
        public void BeginStreaming()
        {
            if (StreamingRequested)
            {
                return;
            }

            StreamingRequested = true;
            ReportDiagnostic("frames enabled — streaming");
            StreamingBegan?.Invoke();
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

            // Session start gate: camera delivering frames + signaling open. The
            // session negotiates EAGERLY (during the warm-up screen) so the peer
            // connection, data channel and ICE are all warmed and Connected before
            // the user enters — consent gates the FRAMES below, not the plumbing.
            if (!sessionRequested &&
                camera.State == CameraStreamState.Active &&
                signaling.IsConnected)
            {
                BeginSession(transport);
                return;
            }

            // Frame pump — held until the user confirms (StreamingRequested): no
            // camera pixels leave the device before Enter, even though the
            // session is already negotiated.
            if (sessionRequested &&
                StreamingRequested &&
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

                // Consent audit: the log proves exactly when pixels first left the
                // device this session — it must always be after "frames enabled".
                if (!firstFramePushed)
                {
                    firstFramePushed = true;
                    ReportDiagnostic("first camera frame pushed");
                }
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

            // The server accepts exactly ONE offer per signaling socket (a fresh
            // socket reads as a same-client reconnect and cleanly replaces the old
            // session server-side), so renegotiation must cycle the socket — a
            // second offer on the same socket is silently ignored and the session
            // would hang at "negotiating" forever. Only cycle a live socket: when
            // signaling is already down (drop, or a deliberate 4000/4001 eviction)
            // its own reconnect policy decides if/when it comes back.
            if (signaling.IsConnected)
            {
                signaling.Disconnect();
                signaling.Connect();
            }

            // Update() re-enters BeginSession once camera + signaling are ready again.
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            signaling.AnswerReceived -= OnAnswerReceived;
            signaling.CandidateReceived -= OnCandidateReceived;
            signaling.Connected -= OnSignalingConnected;
            signaling.Disconnected -= OnSignalingDisconnected;

            Transport?.CloseSession();
            pump?.Dispose();
            pump = null;

            base.Destroy();
        }

        private void BeginSession(IWebRTCTransportModule transport)
        {
            sessionRequested = true;
            firstFramePushed = false;
            frameCounter = 0;

            var resolution = camera.StreamResolution;
            ReportDiagnostic($"starting session {resolution.x}x{resolution.y} @{profile.TargetFps}fps");
            pump.Configure(resolution.x, resolution.y);
            transport.StartSession(resolution.x, resolution.y, profile.TargetFps);
        }

        private void OnLocalOffer(string sdp)
        {
            ReportDiagnostic("offer sent");
            signaling.SendOffer(sdp);
        }

        private void OnLocalCandidate(IceCandidateMessage candidate)
            => signaling.SendCandidate(candidate.Candidate, candidate.SdpMid, candidate.SdpMLineIndex);

        private void OnAnswerReceived(string sdp)
        {
            ReportDiagnostic("answer received — negotiating");
            Transport?.SetRemoteAnswer(sdp);
        }

        private void OnIceStateChanged(string iceState) => ReportDiagnostic($"ICE: {iceState}");

        private void ReportDiagnostic(string diagnostic)
        {
            LastDiagnostic = diagnostic;
            Debug.Log($"[QVS:WebRTC] {diagnostic}");
            DiagnosticChanged?.Invoke(diagnostic);
        }

        private void OnCandidateReceived(IceCandidateMessage candidate) => Transport?.AddRemoteCandidate(candidate);

        private void OnSignalingDisconnected(int code) => hadSignalingDrop = true;

        private void OnSignalingConnected()
        {
            // Only a signaling connection that came back AFTER A REAL DROP while a
            // session was in flight warrants renegotiation (the server no longer
            // knows us). The first connect must never trigger this: with eager
            // session start, Update() can poll IsConnected and send the offer
            // before this event dispatches — treating that as "restored" used to
            // kill the brand-new session mid-negotiation.
            var dropped = hadSignalingDrop;
            hadSignalingDrop = false;

            if (dropped && sessionRequested && state != WebRTCConnectionState.Connected)
            {
                ScheduleReconnect("signaling restored after drop");
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
