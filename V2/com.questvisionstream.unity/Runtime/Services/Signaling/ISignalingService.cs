// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>Details of the last WebSocket close, for status surfaces and diagnostics.</summary>
    public readonly struct SignalingCloseInfo
    {
        public SignalingCloseInfo(int code, string reason)
        {
            Code = code;
            Reason = reason;
        }

        public int Code { get; }
        public string Reason { get; }

        public override string ToString() => $"{Code} ({ServerCloseCodes.Describe(Code)})" +
            (string.IsNullOrEmpty(Reason) ? string.Empty : $" \"{Reason}\"");
    }

    /// <summary>
    /// WebSocket signaling transport for the QuestVisionStreamServer: sends
    /// <c>offer</c>/<c>candidate</c>/<c>status</c>, receives <c>answer</c>/<c>candidate</c>.
    /// Owns the connection lifecycle: bounded-backoff reconnect, the server's
    /// deliberate-eviction close codes (4000/4001 stay down), the per-launch
    /// <c>cid</c> identity, the optional <c>?token=</c> auth and the
    /// <c>__keepalive</c> heartbeat that stops idle proxies dropping the socket.
    /// </summary>
    public interface ISignalingService : IService
    {
        /// <summary>Raised when the socket reaches OPEN (initial connect and every reconnect).</summary>
        event Action Connected;

        /// <summary>Raised when the socket closes, with the close code.</summary>
        event Action<int> Disconnected;

        /// <summary>An <c>answer</c> arrived from the server.</summary>
        event Action<string> AnswerReceived;

        /// <summary>
        /// A trickled <c>candidate</c> arrived. The V2 server embeds all candidates in
        /// the answer SDP so this rarely fires, but V1 servers may trickle — handle both.
        /// </summary>
        event Action<IceCandidateMessage> CandidateReceived;

        bool IsConnected { get; }

        /// <summary>The last close, if any — tells WHO closed the socket and why.</summary>
        SignalingCloseInfo? LastCloseInfo { get; }

        /// <summary>Begin connecting (no-op when already open/connecting). Safe to call repeatedly.</summary>
        void Connect();

        /// <summary>Close deliberately and suppress reconnect until <see cref="Connect"/> is called again.</summary>
        void Disconnect();

        void SendOffer(string sdp);

        /// <summary>Send a local ICE candidate (the aiortc prefix strip is applied on the way out).</summary>
        void SendCandidate(string candidate, string sdpMid, int sdpMLineIndex);

        /// <summary>
        /// Send free-form device telemetry for server-side visibility. Fields prefixed
        /// <c>__</c> are traffic-only (never logged by the server).
        /// </summary>
        void SendStatus(string field, string value);
    }
}
