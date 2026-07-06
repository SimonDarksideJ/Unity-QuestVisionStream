// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using QuestVisionStream.Core;
using QuestVisionStream.Protocol;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// <see cref="IStatusService"/>: wires the streaming services into the
    /// <see cref="StatusModel"/> and mirrors every field change to the server's
    /// status uplink (deduped by the model; full resend on reconnect so the server
    /// always has the complete picture).
    /// </summary>
    [System.Runtime.InteropServices.Guid("9e7918e8-3bf3-456c-a604-ee6f1fd7e9de")]
    public class StatusService : BaseServiceWithConstructor, IStatusService
    {
        private readonly ISignalingService signaling;
        private readonly ICameraStreamService camera;
        private readonly IWebRTCService webrtc;
        private readonly IDetectionService detections;

        private IImageQualifierService qualifier;
        private double lastDetectionRateReport;
        private long lastReceivedCount;

        public StatusService(
            string name,
            uint priority,
            ISignalingService signaling,
            ICameraStreamService camera,
            IWebRTCService webrtc,
            IDetectionService detections)
            : base(name, priority)
        {
            this.signaling = signaling ?? throw new ArgumentNullException(nameof(signaling));
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
            this.webrtc = webrtc ?? throw new ArgumentNullException(nameof(webrtc));
            this.detections = detections ?? throw new ArgumentNullException(nameof(detections));
        }

        public StatusModel Model { get; } = new StatusModel();

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            ServiceManager.Instance?.TryGetService(out qualifier);

            Model.Changed += OnModelChanged;

            signaling.Connected += OnSignalingConnected;
            signaling.Disconnected += OnSignalingDisconnected;
            camera.StateChanged += OnCameraStateChanged;
            webrtc.StateChanged += OnWebRTCStateChanged;
            webrtc.DiagnosticChanged += OnWebRTCDiagnostic;
            detections.ServerReady += OnServerReady;
            if (qualifier != null)
            {
                qualifier.QualityChanged += OnQualityChanged;
            }

            Report(StatusModel.Fields.Device, $"{SystemInfo.deviceModel} · {Application.productName} v{Application.version}");
            Report(StatusModel.Fields.Signaling, "connecting", StatusSeverity.Progress);
        }

        /// <inheritdoc />
        public void Report(string field, string value, StatusSeverity severity = StatusSeverity.Ok)
            => Model.Set(field, value, severity);

        /// <inheritdoc />
        public override void Update()
        {
            base.Update();

            // Detections field: rate summary once a second while payloads flow.
            var now = Time.realtimeSinceStartupAsDouble;
            if (now - lastDetectionRateReport >= 1.0)
            {
                var received = detections.ReceivedCount;
                if (received != lastReceivedCount)
                {
                    var delta = received - lastReceivedCount;
                    lastReceivedCount = received;
                    Report(StatusModel.Fields.Detections, $"{delta}/s · {received} total");
                }

                lastDetectionRateReport = now;
            }
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            Model.Changed -= OnModelChanged;
            signaling.Connected -= OnSignalingConnected;
            signaling.Disconnected -= OnSignalingDisconnected;
            camera.StateChanged -= OnCameraStateChanged;
            webrtc.StateChanged -= OnWebRTCStateChanged;
            webrtc.DiagnosticChanged -= OnWebRTCDiagnostic;
            detections.ServerReady -= OnServerReady;
            if (qualifier != null)
            {
                qualifier.QualityChanged -= OnQualityChanged;
            }

            base.Destroy();
        }

        private void OnModelChanged(string field, StatusEntry entry)
        {
            // Mirror every change to the server (skip internal fields).
            if (!field.StartsWith("__"))
            {
                signaling.SendStatus(field, entry.Value);
            }
        }

        private void OnSignalingConnected()
        {
            // Include WHERE we connected — the first question when anything fails.
            var server = signaling.CurrentServerDisplay;
            Report(StatusModel.Fields.Signaling, string.IsNullOrEmpty(server) ? "connected" : $"connected · {server}");

            // Full resend so a restarted server still has the complete device picture.
            foreach (var pair in new List<KeyValuePair<string, StatusEntry>>(Model.Entries))
            {
                if (!pair.Key.StartsWith("__"))
                {
                    signaling.SendStatus(pair.Key, pair.Value.Value);
                }
            }
        }

        private void OnSignalingDisconnected(int code)
        {
            var description = ServerCloseCodes.Describe(code);
            var severity = ServerCloseCodes.SuppressesReconnect(code) ? StatusSeverity.Error : StatusSeverity.Warning;
            Report(StatusModel.Fields.Signaling, $"disconnected: {description}", severity);
        }

        private void OnCameraStateChanged(CameraStreamState state)
        {
            switch (state)
            {
                case CameraStreamState.Active:
                    var resolution = camera.StreamResolution;
                    Report(StatusModel.Fields.Camera, $"capturing → {resolution.x}x{resolution.y}");
                    break;
                case CameraStreamState.Waiting:
                    Report(StatusModel.Fields.Camera, "waiting for camera/permission", StatusSeverity.Progress);
                    break;
                case CameraStreamState.Error:
                    Report(StatusModel.Fields.Camera, camera.LastError ?? "camera failed", StatusSeverity.Error);
                    break;
                default:
                    Report(StatusModel.Fields.Camera, "idle", StatusSeverity.Progress);
                    break;
            }
        }

        private void OnWebRTCStateChanged(WebRTCConnectionState state)
        {
            switch (state)
            {
                case WebRTCConnectionState.Connected:
                    Report(StatusModel.Fields.Connection, "connected");
                    break;
                case WebRTCConnectionState.Failed:
                    // Say WHERE it died, not just that it failed — the last diagnostic
                    // is the furthest point the connection reached.
                    Report(StatusModel.Fields.Connection, $"failed after '{webrtc.LastDiagnostic}' — renegotiating", StatusSeverity.Error);
                    break;
                case WebRTCConnectionState.Disconnected:
                    Report(StatusModel.Fields.Connection, "interrupted", StatusSeverity.Warning);
                    break;
                default:
                    Report(StatusModel.Fields.Connection, state.ToString().ToLowerInvariant(), StatusSeverity.Progress);
                    break;
            }
        }

        private void OnWebRTCDiagnostic(string diagnostic)
        {
            // Progress detail between hard states — never demote a Connected/Error entry.
            if (webrtc.State != WebRTCConnectionState.Connected &&
                webrtc.State != WebRTCConnectionState.Failed)
            {
                Report(StatusModel.Fields.Connection, diagnostic, StatusSeverity.Progress);
            }
        }

        private void OnServerReady() => Report(StatusModel.Fields.Server, "ready");

        private void OnQualityChanged(QualityReport report)
        {
            if (report.Ok)
            {
                Report(StatusModel.Fields.Quality, $"ok ({report.Score:0.00})");
            }
            else
            {
                var detail = report.Metrics.Count > 0 ? report.Metrics[0].Detail : "below threshold";
                Report(StatusModel.Fields.Quality, detail, StatusSeverity.Warning);
            }
        }
    }
}
