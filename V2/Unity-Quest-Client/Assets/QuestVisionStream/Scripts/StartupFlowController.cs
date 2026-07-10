// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Text;
using QuestVisionStream.Core;
using QuestVisionStream.Protocol;
using QuestVisionStream.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// The warm-up screen — a 1:1 re-engineering of the IWSDK web client's intro
    /// card (<c>quest-client/index.html</c> + <c>wireIntro</c>) in Unity UI:
    ///
    ///   - Card with title, subtitle, ONE button and a note line.
    ///   - The button is DISABLED until the signaling socket is connected (the
    ///     socket connects on launch; the camera does not stream until Enter).
    ///   - Button states: "Checking camera…" → "Connecting…" → "Enter" (enabled)
    ///     → "Starting…". (The web's "Checking XR…" tier maps to the passthrough
    ///     camera check — the equivalent runtime gate on Quest.)
    ///   - Note hints ported verbatim: connecting, close-code-4000 "already
    ///     connected", and the VPN/server failure hint.
    ///   - Card hides once the streaming session starts.
    ///
    /// Activation is the right-controller A button (acts ONLY while the button is
    /// enabled — same contract as a disabled DOM button); B toggles the live
    /// debug panel. No interaction-toolkit dependency.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class StartupFlowController : MonoBehaviour
    {
        // Palette lifted from the web intro card's CSS.
        private static readonly Color CardColor = new Color(0f, 0f, 0f, 0.92f);
        private static readonly Color TitleColor = Color.white;
        private static readonly Color SubColor = new Color(0.91f, 0.93f, 0.96f, 0.7f);
        private static readonly Color ButtonEnabledColor = new Color(0.14f, 0.76f, 0.66f);   // #23c3a8
        private static readonly Color ButtonEnabledText = new Color(0.02f, 0.13f, 0.17f);    // #06222b
        private static readonly Color ButtonDisabledColor = new Color(0.16f, 0.18f, 0.22f);  // #2a2e38
        private static readonly Color ButtonDisabledText = new Color(0.68f, 0.71f, 0.77f);   // #aeb6c4
        private static readonly Color NoteColor = new Color(0.91f, 0.93f, 0.96f, 0.6f);
        private static readonly Color NoteErrorColor = new Color(1f, 0.42f, 0.42f);          // #ff6b6b

        private IStatusService status;
        private IWebRTCService webrtc;
        private ISignalingService signaling;
        private ICameraStreamService camera;

        private GameObject introRoot;
        private Text buttonText;
        private Image buttonImage;
        private Text noteText;
        private TextMesh debugText;
        private GameObject debugRoot;

        private InputAction enterAction;
        private InputAction debugAction;

        private bool buttonEnabled;
        private bool starting;
        private bool started;
        private bool debugVisible;
        private int lastCloseCode;
        private float nextRefreshRealtime;

        private enum Conn { Connecting, Connected, Failed }

        private Conn conn = Conn.Connecting;

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

            conn = signaling.IsConnected ? Conn.Connected : Conn.Connecting;
            signaling.Connected += OnSignalingConnected;
            signaling.Disconnected += OnSignalingDisconnected;
            webrtc.StateChanged += OnWebRTCStateChanged;

            BuildIntroCard();
            BuildDebugPanel();

            enterAction = new InputAction("QVS Enter", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            // B (right secondary) is now the anchored-tag behaviour toggle (bootstrap),
            // so the warm-up debug panel moves to the right thumbstick click.
            debugAction = new InputAction("QVS Debug", InputActionType.Button);
            debugAction.AddBinding("<XRController>{RightHand}/thumbstickClicked");
            debugAction.AddBinding("<XRController>{RightHand}/primary2DAxisClick");
            enterAction.performed += _ => OnEnterPressed();
            debugAction.performed += _ => debugVisible = !debugVisible;
            enterAction.Enable();
            debugAction.Enable();

            Render();
        }

        private void OnDestroy()
        {
            if (signaling != null)
            {
                signaling.Connected -= OnSignalingConnected;
                signaling.Disconnected -= OnSignalingDisconnected;
            }

            if (webrtc != null)
            {
                webrtc.StateChanged -= OnWebRTCStateChanged;
            }

            enterAction?.Dispose();
            debugAction?.Dispose();
        }

        private void Update()
        {
            if (status == null || Time.realtimeSinceStartup < nextRefreshRealtime)
            {
                return;
            }

            nextRefreshRealtime = Time.realtimeSinceStartup + 0.25f;

            // Camera readiness feeds the "Checking camera…" tier (the web's
            // isSessionSupported query resolves once; camera state can change, so poll).
            Render();

            debugRoot.SetActive(debugVisible);
            if (debugVisible)
            {
                debugText.text = BuildDebugText();
            }
        }

        // ---- the wireIntro state machine, ported ----

        private void Render()
        {
            if (started)
            {
                introRoot.SetActive(false);
                return;
            }

            introRoot.SetActive(true);

            if (starting)
            {
                SetButton("Starting…", false);
                SetNote(string.Empty, false);
                return;
            }

            // Tier 1/2 — the platform gate (web: XR support; Unity: passthrough camera).
            if (camera.State == CameraStreamState.Error)
            {
                SetButton("Camera unavailable", false);
                SetNote($"Passthrough camera isn't available — {camera.LastError ?? "check headset permissions."}", true);
                return;
            }

            if (camera.State != CameraStreamState.Active)
            {
                SetButton("Checking camera…", false);
                SetNote("Waiting for the passthrough camera…", false);
                return;
            }

            // Tier 3+ — connection-driven, verbatim from the web intro.
            SetButton(conn == Conn.Connecting ? "Connecting…" : "Enter", conn == Conn.Connected);

            if (conn == Conn.Connected)
            {
                SetNote(string.Empty, false);
            }
            else if (conn == Conn.Connecting)
            {
                SetNote("Connecting to server…", false);
            }
            else if (lastCloseCode == ServerCloseCodes.SupersededByNewerClient)
            {
                SetNote("Another device is already connected — close it, then restart to take over.", true);
            }
            else
            {
                SetNote("Unable to connect to server, have you connected the VPN and started the server?", true);
            }
        }

        private void OnSignalingConnected()
        {
            conn = Conn.Connected;
            Render();
        }

        private void OnSignalingDisconnected(int code)
        {
            conn = Conn.Failed;
            lastCloseCode = code;
            Render();
        }

        private void OnWebRTCStateChanged(WebRTCConnectionState state)
        {
            if (!starting)
            {
                return;
            }

            if (state == WebRTCConnectionState.Connected)
            {
                // The web card hides on sessionstart; ours hides once streaming is live.
                starting = false;
                started = true;
                Render();
            }
            else if (state == WebRTCConnectionState.Failed)
            {
                starting = false;
                SetNote($"Could not start streaming: {webrtc.LastDiagnostic}", true);
                Render();
            }
        }

        private void OnEnterPressed()
        {
            // Same contract as a disabled DOM button: the press does nothing
            // unless the button is currently enabled.
            if (!buttonEnabled || starting || started)
            {
                return;
            }

            starting = true;
            Render();
            webrtc.BeginStreaming();
        }

        private void SetButton(string label, bool enabled)
        {
            buttonEnabled = enabled;
            buttonText.text = enabled ? $"{label}  ( A )" : label;
            buttonImage.color = enabled ? ButtonEnabledColor : ButtonDisabledColor;
            buttonText.color = enabled ? ButtonEnabledText : ButtonDisabledText;
        }

        private void SetNote(string text, bool error)
        {
            noteText.text = text;
            noteText.color = error ? NoteErrorColor : NoteColor;
        }

        // ---- Unity UI construction (the intro card, re-engineered) ----

        private void BuildIntroCard()
        {
            introRoot = new GameObject("QVS_Intro");
            introRoot.transform.SetParent(transform, false);
            introRoot.transform.localPosition = new Vector3(0, 0, 1.4f);

            var canvas = introRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)introRoot.transform;
            canvasRect.sizeDelta = new Vector2(620, 400);
            introRoot.transform.localScale = Vector3.one * 0.0012f;

            var card = CreatePanel(canvasRect, "Card", CardColor);
            Stretch(card, Vector2.zero, Vector2.zero);

            var title = CreateText(card, "Title", "Welcome to the Ethar\nTraining Demonstration", 42, FontStyle.Bold, TitleColor);
            Place(title, new Vector2(0.5f, 1f), new Vector2(0, -95), new Vector2(560, 150));

            var sub = CreateText(card, "Sub", "Put on your headset and step into the experience.", 20, FontStyle.Normal, SubColor);
            Place(sub, new Vector2(0.5f, 1f), new Vector2(0, -190), new Vector2(560, 40));

            var buttonObject = CreatePanel(card, "EnterButton", ButtonDisabledColor);
            Place((RectTransform)buttonObject.transform, new Vector2(0.5f, 1f), new Vector2(0, -265), new Vector2(300, 70));
            buttonImage = buttonObject.GetComponent<Image>();
            var button = buttonObject.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(OnEnterPressed);

            var label = CreateText((RectTransform)buttonObject.transform, "Label", "Checking camera…", 24, FontStyle.Bold, ButtonDisabledText);
            Stretch(label, Vector2.zero, Vector2.zero);
            buttonText = label.GetComponent<Text>();

            var note = CreateText(card, "Note", string.Empty, 17, FontStyle.Normal, NoteColor);
            Place(note, new Vector2(0.5f, 1f), new Vector2(0, -335), new Vector2(560, 60));
            noteText = note.GetComponent<Text>();

            var hint = CreateText(card, "Hint", "( stick-click )  debug status", 14, FontStyle.Normal, SubColor);
            Place(hint, new Vector2(0.5f, 0f), new Vector2(0, 18), new Vector2(560, 24));
        }

        private void BuildDebugPanel()
        {
            debugRoot = new GameObject("QVS_Debug");
            debugRoot.transform.SetParent(transform, false);
            debugRoot.transform.localPosition = new Vector3(-0.35f, 0.05f, 1.2f);
            debugText = debugRoot.AddComponent<TextMesh>();
            debugText.characterSize = 0.014f;
            debugText.fontSize = 48;
            debugText.anchor = TextAnchor.MiddleLeft;
            debugText.alignment = TextAlignment.Left;
            debugText.color = Color.white;
            debugRoot.SetActive(false);
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
                var entry = status.Model.Get(field);
                builder.AppendLine($"{field}: {(entry.HasValue ? entry.Value.Value : "—")}");
            }

            builder.Append($"webrtc:     {webrtc.State} · {webrtc.LastDiagnostic}");
            return builder.ToString();
        }

        // ---- tiny uGUI helpers ----

        private static RectTransform CreatePanel(RectTransform parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return rect;
        }

        private static RectTransform CreateText(RectTransform parent, string name, string value, int size, FontStyle style, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
