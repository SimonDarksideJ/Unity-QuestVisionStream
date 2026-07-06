// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// Hidden receiver for the Android plugin's single JSON event pipe
    /// (<c>UnitySendMessage(gameObject, "OnQvsEvent", json)</c>). Created by
    /// <see cref="AndroidWebRTCTransportModule"/>; not for manual use.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class QvsPluginEventReceiver : MonoBehaviour
    {
        /// <summary>Raised on the Unity main thread with the raw event JSON.</summary>
        public event Action<string> EventReceived;

        // Invoked by the plugin via UnitySendMessage — name must match the Kotlin UNITY_METHOD.
        public void OnQvsEvent(string json) => EventReceived?.Invoke(json);
    }
}
