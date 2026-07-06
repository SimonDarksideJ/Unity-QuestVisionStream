// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using Newtonsoft.Json.Linq;

namespace QuestVisionStream.Protocol
{
    /// <summary>
    /// Parser for the deployment's remote-config endpoint (the Cloudflare Pages
    /// Function <c>GET /api/config</c>, backed by the <c>QVS_CONFIG</c> KV
    /// namespace). The server host publishes its current signaling URL there
    /// (<c>tools/setup-tailscale-mac.sh</c> pushes <c>wss://&lt;machine&gt;.&lt;tailnet&gt;.ts.net/?token=…</c>
    /// into the <c>signaling_url</c> key), so clients discover the live address
    /// from one static, well-known URL instead of being rebuilt/reconfigured.
    ///
    /// Response shape: <c>{ "server": "wss://host:3000" }</c> (empty when unset).
    /// </summary>
    public static class RemoteConfig
    {
        /// <summary>
        /// Parse an <c>/api/config</c> response body. Returns true only when it
        /// yields a usable <c>ws://</c> or <c>wss://</c> signaling URL.
        /// </summary>
        public static bool TryParseServerUrl(string json, out string serverUrl)
        {
            serverUrl = null;

            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            string candidate;
            try
            {
                candidate = JObject.Parse(json).Value<string>("server");
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(candidate) || !IsWebSocketUrl(candidate))
            {
                return false;
            }

            serverUrl = candidate;
            return true;
        }

        /// <summary>Is this a well-formed ws:// or wss:// URL?</summary>
        public static bool IsWebSocketUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == "ws" || uri.Scheme == "wss");
    }
}
