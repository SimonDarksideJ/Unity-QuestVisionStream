// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Text;
using QuestVisionStream.Core;
using QuestVisionStream.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// The warm-up screen: streaming does NOT start on launch. A head-locked panel
    /// shows live readiness (discovered server, signaling, camera, connection
    /// progress) so the user can confirm the connection before anything is sent,
    /// then:
    ///
    ///   A (right controller) — start streaming
    ///   B (right controller) — toggle the debug panel (all status fields, live)
    ///
    /// Controller buttons rather than laser-pointer UI keeps the client free of
    /// any interaction-toolkit dependency.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class StartupFlowController : MonoBehaviour
    {
        private IStatusService status;
        private IWebRTCService webrtc;
        private ISignalingService signaling;
        private ICameraStreamService camera;

        private TextMesh panelText;
        private GameObject panelRoot;
        private InputAction startAction;
        private InputAction debugAction;
        private bool started;
        private bool debugVisible;
        private float nextRefreshRealtime;

        public void Initialize(
            IStatusService statusService,
            IWebRTCService webrtcService,
            ISignalingService signalingService,
            ICameraStreamService cameraService)
        {
            status = statusService;
            webrtc = webrtcService;
            signaling = signalingService;
            camera = cameraService;

            panelRoot = new GameObject("WarmupPanel");
            panelRoot.transform.SetParent(transform, false);
            panelRoot.transform.localPosition = new Vector3(0, 0, 1.2f);
            var text = new GameObject("Text");
            text.transform.SetParent(panelRoot.transform, false);
            panelText = text.AddComponent<TextMesh>();
            panelText.characterSize = 0.018f;
            panelText.fontSize = 48;
            panelText.anchor = TextAnchor.MiddleCenter;
            panelText.alignment = TextAlignment.Center;
            panelText.color = Color.white;

            startAction = new InputAction("QVS Start", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            debugAction = new InputAction("QVS Debug", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            startAction.performed += _ => OnStartPressed();
            debugAction.performed += _ => debugVisible = !debugVisible;
            startAction.Enable();
            debugAction.Enable();
        }

        private void OnDestroy()
        {
            startAction?.Dispose();
            debugAction?.Dispose();
        }

        private void Update()
        {
            if (status == null || Time.realtimeSinceStartup < nextRefreshRealtime)
            {
                return;
            }

            nextRefreshRealtime = Time.realtimeSinceStartup + 0.25f;
            panelText.text = debugVisible ? BuildDebugText() : started ? BuildStreamingText() : BuildWarmupText();
            panelRoot.SetActive(debugVisible || !started || HasProblem());
        }

        private void OnStartPressed()
        {
            if (started)
            {
                return;
            }

            started = true;
            webrtc.BeginStreaming();
        }

        private bool HasProblem() => status.Model.Headline() != null;

        private string BuildWarmupText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("QUEST VISION STREAM");
            builder.AppendLine();
            builder.AppendLine($"server:     {signaling.CurrentServerDisplay ?? "resolving…"}");
            builder.AppendLine($"signaling:  {Describe(StatusModel.Fields.Signaling)}");
            builder.AppendLine($"camera:     {Describe(StatusModel.Fields.Camera)}");
            builder.AppendLine();

            var ready = signaling.IsConnected && camera.State == CameraStreamState.Active;
            builder.AppendLine(ready ? "READY" : "waiting for connection + camera…");
            builder.AppendLine();
            builder.AppendLine("( A )  start streaming");
            builder.Append("( B )  debug status");
            return builder.ToString();
        }

        private string BuildStreamingText()
        {
            // Shown only while a problem exists once streaming has started.
            var builder = new StringBuilder();
            builder.AppendLine(status.Model.Headline() ?? string.Empty);
            builder.Append("( B )  debug status");
            return builder.ToString();
        }

        private string BuildDebugText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("QVS DEBUG");
            builder.AppendLine();
            builder.AppendLine($"server:     {signaling.CurrentServerDisplay ?? "unresolved"}");

            foreach (var field in new[]
                     {
                         StatusModel.Fields.Signaling,
                         StatusModel.Fields.Camera,
                         StatusModel.Fields.Connection,
                         StatusModel.Fields.Quality,
                         StatusModel.Fields.Detections,
                         StatusModel.Fields.Server,
                         StatusModel.Fields.Device
                     })
            {
                builder.AppendLine($"{field}: {Describe(field)}");
            }

            builder.AppendLine($"webrtc:     {webrtc.State} · {webrtc.LastDiagnostic}");
            builder.AppendLine();
            builder.Append(started ? "( B )  hide debug" : "( A )  start streaming   ( B )  hide debug");
            return builder.ToString();
        }

        private string Describe(string field)
        {
            var entry = status.Model.Get(field);
            return entry.HasValue ? entry.Value.Value : "—";
        }
    }
}
