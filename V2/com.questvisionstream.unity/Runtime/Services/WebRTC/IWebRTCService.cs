// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>Peer connection lifecycle state (mirrors the WebRTC PeerConnectionState).</summary>
    public enum WebRTCConnectionState
    {
        New = 0,
        Connecting,
        Connected,

        /// <summary>ICE hiccup — can self-heal; not treated as terminal.</summary>
        Disconnected,

        /// <summary>Terminal — the service recovers by renegotiating a fresh session.</summary>
        Failed,
        Closed
    }

    /// <summary>A STUN or TURN server entry.</summary>
    [Serializable]
    public struct IceServerConfig
    {
        public string Url;
        public string Username;
        public string Credential;
    }

    /// <summary>
    /// A WebRTC transport implementation. The default is the minimal Android
    /// plugin (native google-webrtc via <c>QuestVisionStreamPlugin.androidlib</c>);
    /// the seam exists so another stack can be swapped in without touching the
    /// orchestration in <see cref="WebRTCService"/>.
    ///
    /// Contract: <see cref="StartSession"/> creates the peer connection, adds the
    /// video track, creates the <c>detections</c> data channel (client is offerer
    /// and creates the channel — the server only listens) and produces an offer via
    /// <see cref="LocalOfferCreated"/>. All events fire on the Unity main thread.
    /// </summary>
    public interface IWebRTCTransportModule : IServiceModule
    {
        /// <summary>The local offer SDP is ready to send to the server.</summary>
        event Action<string> LocalOfferCreated;

        /// <summary>A local ICE candidate was gathered (trickle it to the server).</summary>
        event Action<IceCandidateMessage> LocalCandidateGathered;

        event Action<WebRTCConnectionState> ConnectionStateChanged;

        /// <summary>ICE connection state detail (CHECKING/CONNECTED/FAILED…) for diagnostics.</summary>
        event Action<string> IceStateChanged;

        /// <summary>A text message arrived on the detections data channel.</summary>
        event Action<string> DataChannelMessageReceived;

        /// <summary>Is this transport usable on the current platform/build?</summary>
        bool IsAvailable { get; }

        bool IsSessionActive { get; }

        void ConfigureIce(IReadOnlyList<IceServerConfig> iceServers);

        /// <summary>Create the peer connection + track + data channel and produce an offer.</summary>
        void StartSession(int width, int height, int targetFps);

        void SetRemoteAnswer(string sdp);

        void AddRemoteCandidate(IceCandidateMessage candidate);

        /// <summary>Push one planar I420 frame into the video track.</summary>
        void PushFrameYuv(byte[] y, byte[] u, byte[] v, int width, int height);

        /// <summary>Push one packed RGB24 frame (CPU fallback path; the plugin converts).</summary>
        void PushFrameRgb(byte[] rgb, int width, int height);

        /// <summary>Send a text message to the server over the detections channel.</summary>
        void SendDataChannelMessage(string message);

        /// <summary>Tear down the current session; the module must be restartable afterwards.</summary>
        void CloseSession();
    }

    /// <summary>
    /// Orchestrates the streaming session: waits for camera + signaling, starts the
    /// transport, bridges SDP/ICE between transport and signaling, pumps camera
    /// frames into the track, and renegotiates on terminal failures (the V2
    /// recovery story: nothing fails silently or permanently).
    /// </summary>
    public interface IWebRTCService : IService
    {
        event Action<WebRTCConnectionState> StateChanged;

        /// <summary>
        /// Connection-progress detail ("offer sent", "answer received",
        /// "ICE: CHECKING"…) — tells a status surface WHERE a connection is,
        /// or where it died, rather than just that it failed.
        /// </summary>
        event Action<string> DiagnosticChanged;

        /// <summary>Raw text from the detections data channel (parsed by the detection service).</summary>
        event Action<string> DetectionMessageReceived;

        WebRTCConnectionState State { get; }

        /// <summary>The most recent connection-progress detail.</summary>
        string LastDiagnostic { get; }

        /// <summary>
        /// Has streaming been requested? True from the start when the profile's
        /// AutoStartSession is on; otherwise false until <see cref="BeginStreaming"/>.
        /// </summary>
        bool StreamingRequested { get; }

        /// <summary>
        /// Request the streaming session (the warm-up screen's Start button). The
        /// session still waits for camera Active + signaling connected.
        /// </summary>
        void BeginStreaming();

        /// <summary>Tear down and renegotiate now (also used internally for recovery).</summary>
        void RestartSession();
    }
}
