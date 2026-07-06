// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using Newtonsoft.Json.Linq;

namespace QuestVisionStream.Protocol
{
    /// <summary>
    /// WebSocket close codes defined by the QuestVisionStreamServer
    /// (<c>webrtc_server.py</c>). Codes 4000/4001 mean the server deliberately
    /// closed this session in favour of another connection — reconnecting would
    /// just fight it, so the signaling service stays down for those.
    /// </summary>
    public static class ServerCloseCodes
    {
        /// <summary>Superseded by a newer client (connection cap, default 1).</summary>
        public const int SupersededByNewerClient = 4000;

        /// <summary>Replaced by this client's own reconnect (matching <c>cid</c>).</summary>
        public const int ReplacedByOwnReconnect = 4001;

        /// <summary>Missing or invalid <c>?token=</c> auth token.</summary>
        public const int Unauthorized = 4401;

        /// <summary>The <c>Origin</c> header is not in the server's allowlist.</summary>
        public const int OriginForbidden = 4403;

        /// <summary>Should the client suppress its automatic reconnect for this close code?</summary>
        public static bool SuppressesReconnect(int code)
            => code == SupersededByNewerClient || code == ReplacedByOwnReconnect;

        /// <summary>Human readable description of a close code, for status surfaces.</summary>
        public static string Describe(int code)
        {
            switch (code)
            {
                case SupersededByNewerClient: return "another client took the connection";
                case ReplacedByOwnReconnect: return "replaced by own reconnect";
                case Unauthorized: return "auth token rejected";
                case OriginForbidden: return "origin not allowed";
                case 1000: return "closed normally";
                case 1001: return "endpoint going away";
                case 1006: return "connection dropped (no close frame)";
                case 1011: return "server error";
                default: return $"code {code}";
            }
        }
    }

    /// <summary>
    /// Helpers for the aiortc ICE-candidate quirk: aiortc sends/expects the SDP
    /// candidate line WITHOUT the <c>candidate:</c> prefix. Both helpers are
    /// idempotent so it never matters what the local WebRTC stack produced.
    /// </summary>
    public static class CandidateSdp
    {
        private const string Prefix = "candidate:";

        /// <summary>Strip the <c>candidate:</c> prefix before sending to the server.</summary>
        public static string StripPrefix(string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }

            return candidate.StartsWith(Prefix) ? candidate.Substring(Prefix.Length) : candidate;
        }

        /// <summary>Re-add the <c>candidate:</c> prefix for WebRTC stacks that require it.</summary>
        public static string EnsurePrefix(string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }

            return candidate.StartsWith(Prefix) ? candidate : Prefix + candidate;
        }
    }

    /// <summary>A remote ICE candidate received from (or sent to) the signaling server.</summary>
    public readonly struct IceCandidateMessage
    {
        public IceCandidateMessage(string candidate, string sdpMid, int sdpMLineIndex)
        {
            Candidate = candidate;
            SdpMid = sdpMid;
            SdpMLineIndex = sdpMLineIndex;
        }

        /// <summary>The candidate line, in on-the-wire (prefix-less) form.</summary>
        public string Candidate { get; }

        public string SdpMid { get; }
        public int SdpMLineIndex { get; }
    }

    /// <summary>The kind of message received from the signaling server.</summary>
    public enum SignalingMessageKind
    {
        Unknown = 0,
        Answer,
        Candidate
    }

    /// <summary>
    /// Builders and parser for the signaling WebSocket JSON protocol:
    /// <c>offer</c>/<c>candidate</c>/<c>status</c> out, <c>answer</c>/<c>candidate</c> in.
    /// </summary>
    public static class SignalingMessages
    {
        public static string Offer(string sdp)
            => new JObject { ["type"] = "offer", ["sdp"] = sdp }.ToString(Newtonsoft.Json.Formatting.None);

        /// <summary>Build a candidate message; the prefix strip is applied here so callers can pass either form.</summary>
        public static string Candidate(string candidate, string sdpMid, int sdpMLineIndex)
            => new JObject
            {
                ["type"] = "candidate",
                ["candidate"] = CandidateSdp.StripPrefix(candidate),
                ["sdpMid"] = sdpMid,
                ["sdpMLineIndex"] = sdpMLineIndex
            }.ToString(Newtonsoft.Json.Formatting.None);

        /// <summary>
        /// Free-form device telemetry the server logs for remote diagnosis. Fields
        /// prefixed <c>__</c> (e.g. <c>__keepalive</c>) generate traffic without log spam.
        /// </summary>
        public static string Status(string field, string value)
            => new JObject { ["type"] = "status", ["field"] = field, ["value"] = value }
                .ToString(Newtonsoft.Json.Formatting.None);

        /// <summary>
        /// Parse an inbound signaling message. Returns the kind; out parameters are
        /// populated per kind. Malformed or unknown messages return
        /// <see cref="SignalingMessageKind.Unknown"/> and must be skipped, never fatal.
        /// </summary>
        public static SignalingMessageKind Parse(string json, out string sdp, out IceCandidateMessage candidate)
        {
            sdp = null;
            candidate = default;

            if (string.IsNullOrEmpty(json))
            {
                return SignalingMessageKind.Unknown;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                return SignalingMessageKind.Unknown;
            }

            switch (root.Value<string>("type"))
            {
                case "answer":
                    sdp = root.Value<string>("sdp");
                    return sdp != null ? SignalingMessageKind.Answer : SignalingMessageKind.Unknown;

                case "candidate":
                    // V1 servers may (dead-code) trickle candidates; V2 embeds them in the
                    // answer SDP. Accept them either way. sdpMid is required by aiortc.
                    var line = root.Value<string>("candidate");
                    var sdpMid = root.Value<string>("sdpMid");
                    if (line == null || sdpMid == null)
                    {
                        return SignalingMessageKind.Unknown;
                    }

                    var sdpMLineIndex = root.Value<int?>("sdpMLineIndex") ?? 0;
                    candidate = new IceCandidateMessage(line, sdpMid, sdpMLineIndex);
                    return SignalingMessageKind.Candidate;

                default:
                    return SignalingMessageKind.Unknown;
            }
        }
    }
}
