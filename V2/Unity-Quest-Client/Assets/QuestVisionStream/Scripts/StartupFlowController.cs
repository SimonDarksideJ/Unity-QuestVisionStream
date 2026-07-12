// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using Ethar.UXTraining.Interaction;
using QuestVisionStream.Protocol;
using QuestVisionStream.Services;
using UnityEngine;
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
    /// Activation is the controller laser (com.ethar.uxtraining's
    /// <see cref="XRUiPointer"/>): point at the button and pull the trigger
    /// (acts ONLY while the button is enabled — same contract as a disabled DOM
    /// button). Status/diagnostics live in the detection HUD log window — this
    /// card renders no free-floating debug text.
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
        private Button buttonControl;
        private Text noteText;

        private bool buttonEnabled;
        private bool starting;
        private bool started;
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

            // Tier 3 — the session negotiates during this warm-up screen, so Enter
            // only lights up once the peer connection is fully warmed: by the time
            // the user can press it, frames flow instantly.
            if (conn == Conn.Connected && webrtc.State != WebRTCConnectionState.Connected)
            {
                SetButton("Negotiating…", false);
                SetNote("Warming up the vision stream…", false);
                return;
            }

            // Tier 4 — connection-driven, verbatim from the web intro.
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
                // Warm-up negotiation progress — flips Negotiating… ⇄ Enter.
                Render();
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

            // The session was negotiated during warm-up, so Connected is normally
            // already true here — enter immediately instead of waiting for a state
            // change that will never fire.
            if (webrtc.State == WebRTCConnectionState.Connected)
            {
                starting = false;
                started = true;
                Render();
            }
        }

        private void SetButton(string label, bool enabled)
        {
            buttonEnabled = enabled;
            buttonText.text = label;
            buttonImage.color = enabled ? ButtonEnabledColor : ButtonDisabledColor;
            buttonText.color = enabled ? ButtonEnabledText : ButtonDisabledText;
            buttonControl.interactable = enabled;
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
            canvas.worldCamera = Camera.main;
            // Point-and-click on the Enter button with the controller laser.
            XRUiPointer.EnsureSetup();
            XRUiPointer.RegisterCanvas(introRoot);
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
            buttonControl = buttonObject.gameObject.AddComponent<Button>();
            buttonControl.targetGraphic = buttonImage;
            buttonControl.onClick.AddListener(OnEnterPressed);

            // SetButton drives the colours by hand, so neutralise the ColorTint
            // transition (keep only a slight press dim) — interactable then gates
            // the hover glow and clicks without repainting the button.
            var tint = buttonControl.colors;
            tint.normalColor = tint.highlightedColor = tint.selectedColor = tint.disabledColor = Color.white;
            tint.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            tint.colorMultiplier = 1f;
            buttonControl.colors = tint;

            Ethar.UXTraining.Components.ButtonHoverGlow.Attach(buttonObject.gameObject, ButtonEnabledColor, radius: 10);

            var label = CreateText((RectTransform)buttonObject.transform, "Label", "Checking camera…", 24, FontStyle.Bold, ButtonDisabledText);
            Stretch(label, Vector2.zero, Vector2.zero);
            buttonText = label.GetComponent<Text>();

            var note = CreateText(card, "Note", string.Empty, 17, FontStyle.Normal, NoteColor);
            Place(note, new Vector2(0.5f, 1f), new Vector2(0, -335), new Vector2(560, 60));
            noteText = note.GetComponent<Text>();
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
