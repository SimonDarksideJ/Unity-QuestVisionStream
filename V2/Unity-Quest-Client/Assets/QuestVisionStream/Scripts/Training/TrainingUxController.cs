// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections;
using QuestVisionStream.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using XRTraining.Components;
using XRTraining.Theme;
using XRTraining.UI;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// The training UX, built from the XRTraining kit:
    ///
    ///   - <b>Display menu</b> — a world-space step form (eyebrow, progress ticks,
    ///     title, description, image placeholder, action buttons) re-anchored in
    ///     front of the user on every step. Rebuilt per step, focus-card style.
    ///   - <b>Hand menu</b> — the vertical 1d strip with a live current-step
    ///     readout, lazily following the left controller and shown only in the
    ///     palm-up pose (fades in when the palm rolls toward the face, out when
    ///     it rolls away; always visible in the Editor's untracked fallback).
    ///   - <b>Location indicator</b> — a pulsing marker at the detected box centre
    ///     with a leader-line connector up to a billboarded label pill.
    ///
    /// Interaction: uGUI Buttons work with a pointer (Editor); in-headset the
    /// right-controller A button presses the form's default (first) action —
    /// same hardware-button contract as the warm-up screen, and only while a
    /// form is showing, so it never fights the warm-up's Enter.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class TrainingUxController : MonoBehaviour
    {
        private const float FormPixelsToMetres = 0.0011f;
        private const float LabelPixelsToMetres = 0.001f;
        private const float MenuPixelsToMetres = 0.0005f; // hand menu at 50% — full size read too big on the wrist
        private const float LabelLiftMeters = 0.16f;
        private const float MenuFollowSeconds = 0.25f;
        private const float MenuFadeSeconds = 0.15f;

        // Palm-up gate with hysteresis: show when the palm rolls toward the face,
        // keep showing until it rolls clearly away — no flicker at the boundary.
        private const float MenuShowAboveDot = 0.55f;
        private const float MenuHideBelowDot = 0.35f;

        private ThemePalette theme;
        private Action<int> onOptionPressed;
        private UIFactory factory;

        // Display menu (step form).
        private GameObject formRoot;
        private Canvas formCanvas;
        private Canvas menuCanvas;
        private RectTransform formCanvasRect;
        private float formDistanceMeters = 1.25f;

        // Hand menu.
        private GameObject menuRoot;
        private HandMenu.Handle menuHandle;
        private CanvasGroup menuGroup;
        private InputAction leftPositionAction;
        private InputAction leftRotationAction;
        private bool menuPlaced;
        private bool menuShown;

        // Location indicator (marker + connector + label pill).
        private GameObject hintRoot;
        private Transform hintMarker;
        private RectTransform hintLabelCanvas;
        private Text hintLabelText;
        private LineRenderer hintLine;
        private Material hintMaterial;
        private Material hintLineMaterial;
        private Coroutine hintPulse;

        private InputAction selectAction;
        private int optionCount;

        public bool IsFormVisible => formRoot != null && formRoot.activeSelf;

        public void Initialize(ThemePalette palette, float formDistance, Action<int> optionPressed, HandMenu.Actions menuActions)
        {
            theme = palette;
            formDistanceMeters = formDistance;
            onOptionPressed = optionPressed;
            factory = new UIFactory(theme);

            ControllerUiPointer.EnsureSetup();
            BuildHandMenu(menuActions);

            // A presses the form's default action — active only while a form shows.
            selectAction = new InputAction("QVS Training Select", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            selectAction.performed += _ => OnSelectPressed();
            selectAction.Enable();

            leftPositionAction = new InputAction("QVS Training Menu Pos", InputActionType.Value, "<XRController>{LeftHand}/devicePosition");
            leftRotationAction = new InputAction("QVS Training Menu Rot", InputActionType.Value, "<XRController>{LeftHand}/deviceRotation");
            leftPositionAction.Enable();
            leftRotationAction.Enable();
        }

        private void OnDestroy()
        {
            selectAction?.Dispose();
            leftPositionAction?.Dispose();
            leftRotationAction?.Dispose();

            if (hintMaterial != null)
            {
                Destroy(hintMaterial);
            }

            if (hintLineMaterial != null)
            {
                Destroy(hintLineMaterial);
            }
        }

        // ------------------------------------------------------------ step form

        /// <summary>Show (or hide, for pass-through steps) the form for a step.</summary>
        public void ShowStep(TrainingStepActivation activation)
        {
            if (formRoot == null)
            {
                BuildFormCanvas();
            }

            var step = activation.Step;
            if (!step.HasPresentation)
            {
                formRoot.SetActive(false);
                optionCount = 0;
                return;
            }

            RebuildFormContent(activation);
            formRoot.SetActive(true);
            AnchorInFront(formRoot.transform, formDistanceMeters, heightOffset: -0.04f);
        }

        /// <summary>Re-anchor the current form in front of the user (hand menu TASKS).</summary>
        public void RepresentForm()
        {
            if (IsFormVisible)
            {
                AnchorInFront(formRoot.transform, formDistanceMeters, heightOffset: -0.04f);
            }
        }

        public void HideForm()
        {
            optionCount = 0;
            if (formRoot != null)
            {
                formRoot.SetActive(false);
            }
        }

        private void BuildFormCanvas()
        {
            formRoot = new GameObject("QVS_TrainingForm");
            formRoot.transform.SetParent(transform, false);

            formCanvas = formRoot.AddComponent<Canvas>();
            formCanvas.renderMode = RenderMode.WorldSpace;
            ControllerUiPointer.RegisterCanvas(formRoot);
            formCanvasRect = (RectTransform)formRoot.transform;
            formCanvasRect.sizeDelta = new Vector2(520, 700);
            formRoot.transform.localScale = Vector3.one * FormPixelsToMetres;
        }

        private void RebuildFormContent(TrainingStepActivation activation)
        {
            for (var i = formCanvasRect.childCount - 1; i >= 0; i--)
            {
                var child = formCanvasRect.GetChild(i);
                child.gameObject.SetActive(false); // Destroy defers to end of frame
                Destroy(child.gameObject);
            }

            var step = activation.Step;
            var f = factory;
            var T = theme;

            var panel = f.Plate(formCanvasRect, 18, name: "StepPanel");
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            // No parent layout group — width must be explicit; the fitter drives height.
            panel.sizeDelta = new Vector2(520, 0);
            f.VLayout(panel, new RectOffset(26, 26, 24, 24), 12);
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Eyebrow row: step counter + scenario position.
            var top = f.Rect("Top", panel);
            f.HLayout(top, 8, TextAnchor.MiddleCenter, controlWidth: true, controlHeight: true, expandWidth: false);
            f.Sized(top);
            f.Label(top, $"STEP {activation.StepIndex + 1} OF {activation.StepCount}", 12, T.accent, FontStyle.Bold, TextAnchor.MiddleLeft, wrap: false);
            var spacer = f.Rect("Spacer", top);
            spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            f.Label(top, "ETHAR TRAINING", 11, T.textLo, FontStyle.Bold, TextAnchor.MiddleRight, wrap: false);

            // Progress ticks (focus-card style).
            var ticks = f.Rect("Ticks", panel);
            f.HLayout(ticks, 4, TextAnchor.MiddleCenter, controlWidth: true, controlHeight: true, expandWidth: true);
            f.Sized(ticks, h: 5);
            for (var i = 0; i < activation.StepCount; i++)
            {
                var color = i < activation.StepIndex ? T.accent
                    : i == activation.StepIndex ? T.AccentA(0.5f) : T.Overlay(0.12f);
                var tick = f.Img(ticks, color, 3, false, "Tick");
                var le = tick.gameObject.AddComponent<LayoutElement>();
                le.flexibleWidth = 1;
                le.preferredHeight = 5;
            }

            f.Label(panel, step.Title, 28, T.textHi, FontStyle.Bold);
            if (step.Description.Length > 0)
            {
                f.Label(panel, step.Description, 15, T.textMid);
            }

            // Image placeholder — the shared client-side "camera on a grey backdrop".
            if (step.ImageRef.Length > 0)
            {
                var image = f.Plate(panel, 12, new Color(0.35f, 0.37f, 0.40f, 0.95f), T.Overlay(0.18f), "ImagePlaceholder");
                f.Sized(image, h: 130);
                var glyph = f.Label(image, "📷", 44, new Color(0.9f, 0.92f, 0.94f), FontStyle.Normal, TextAnchor.MiddleCenter, wrap: false);
                UIFactory.Stretch(glyph.rectTransform);
                var caption = f.Label(image, step.ImageRef, 11, new Color(0.82f, 0.84f, 0.87f), FontStyle.Normal, TextAnchor.LowerRight, wrap: false);
                UIFactory.Stretch(caption.rectTransform, 8f);
            }

            // Actions — default one; the first is the ( A ) hardware-button target.
            optionCount = step.Options.Count;
            if (optionCount > 0)
            {
                var row = f.Rect("Actions", panel);
                f.HLayout(row, 10, TextAnchor.MiddleCenter, controlWidth: true, controlHeight: true, expandWidth: false);
                f.Sized(row);
                for (var i = 0; i < step.Options.Count; i++)
                {
                    var captured = i;
                    var label = i == 0 ? $"{step.Options[i]}  ( A )" : step.Options[i];
                    var button = i == 0
                        ? f.PrimaryButton(row, label, () => onOptionPressed?.Invoke(captured), height: 52, fontSize: 16)
                        : f.SecondaryButton(row, label, () => onOptionPressed?.Invoke(captured), height: 52, fontSize: 16);
                    button.GetComponent<LayoutElement>().flexibleWidth = 1;
                }
            }

            f.Label(panel, "Pinch or press ( A ) to select · Turn palm up for menu", 11, T.textLo);
        }

        // ------------------------------------------------------------ hand menu

        private void BuildHandMenu(HandMenu.Actions actions)
        {
            menuRoot = new GameObject("QVS_TrainingHandMenu");
            menuRoot.transform.SetParent(transform, false);

            menuCanvas = menuRoot.AddComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.WorldSpace;
            ControllerUiPointer.RegisterCanvas(menuRoot);
            var rect = (RectTransform)menuRoot.transform;
            rect.sizeDelta = new Vector2(96, 480);
            menuRoot.transform.localScale = Vector3.one * MenuPixelsToMetres;

            menuHandle = HandMenu.Build(factory, rect, actions);
            var strip = menuHandle.Root;
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
            strip.pivot = new Vector2(0.5f, 0.5f);
            strip.anchoredPosition = Vector2.zero;

            // Hidden until the palm-up gesture; the group also gates raycasts so
            // an invisible menu can never swallow a press.
            menuGroup = menuRoot.AddComponent<CanvasGroup>();
            menuGroup.alpha = 0f;
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
        }

        public void SetHandMenuStep(string eyebrow, string title) => menuHandle?.SetStep(eyebrow, title);

        // ---------------------------------------------------- location indicator

        /// <summary>Place (or move) the marker + connector + label pill at a detected box centre.</summary>
        public void ShowWorldLabel(Vector3 worldPoint, string labelText)
        {
            if (hintRoot == null)
            {
                BuildWorldLabel();
            }

            hintRoot.SetActive(true);
            hintLabelText.text = labelText;
            UpdateWorldLabel(worldPoint);
        }

        /// <summary>Refresh the indicator position on a re-sighting of the step's class.</summary>
        public void UpdateWorldLabel(Vector3 worldPoint)
        {
            if (hintRoot == null || !hintRoot.activeSelf)
            {
                return;
            }

            hintMarker.position = worldPoint;
            hintLabelCanvas.position = worldPoint + Vector3.up * LabelLiftMeters;
            hintLine.SetPosition(0, worldPoint);
            hintLine.SetPosition(1, hintLabelCanvas.position + Vector3.down * 0.028f);
        }

        public void HideWorldLabel()
        {
            if (hintRoot != null)
            {
                hintRoot.SetActive(false);
            }
        }

        /// <summary>Draw attention to the current indicator (hand menu HINT).</summary>
        public void PulseWorldLabel()
        {
            if (hintRoot == null || !hintRoot.activeSelf)
            {
                return;
            }

            if (hintPulse != null)
            {
                StopCoroutine(hintPulse);
            }

            hintPulse = StartCoroutine(PulseMarker());
        }

        private void BuildWorldLabel()
        {
            hintRoot = new GameObject("QVS_TrainingHint");
            hintRoot.transform.SetParent(transform, false);

            // Two materials: the marker is tinted via material colour, the line via
            // vertex colours (a shared tinted material would double-tint the line).
            hintMaterial = new Material(Shader.Find("Sprites/Default"));
            hintLineMaterial = new Material(Shader.Find("Sprites/Default"));

            // Marker dot at the detected box centre.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(hintRoot.transform, false);
            marker.transform.localScale = Vector3.one * 0.028f;
            var markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sharedMaterial = hintMaterial;
            hintMaterial.color = theme.accent;
            hintMarker = marker.transform;

            // Leader-line connector up to the pill.
            var line = new GameObject("Connector");
            line.transform.SetParent(hintRoot.transform, false);
            hintLine = line.AddComponent<LineRenderer>();
            hintLine.useWorldSpace = true;
            hintLine.positionCount = 2;
            hintLine.material = hintLineMaterial;
            hintLine.startColor = theme.AccentA(0.85f);
            hintLine.endColor = theme.AccentA(0.85f);
            hintLine.startWidth = 0.004f;
            hintLine.endWidth = 0.004f;
            hintLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hintLine.receiveShadows = false;

            // Label pill — billboarded world canvas.
            var canvasObject = new GameObject("LabelPill");
            canvasObject.transform.SetParent(hintRoot.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            hintLabelCanvas = (RectTransform)canvasObject.transform;
            hintLabelCanvas.sizeDelta = new Vector2(280, 56);
            canvasObject.transform.localScale = Vector3.one * LabelPixelsToMetres;

            var pill = factory.Plate(hintLabelCanvas, 16, theme.Panel(), theme.HintBorder(), "Pill");
            UIFactory.Stretch(pill);
            hintLabelText = factory.Label(pill, string.Empty, 18, theme.textHi, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            UIFactory.Stretch(hintLabelText.rectTransform, 8f);
        }

        private IEnumerator PulseMarker()
        {
            const float duration = 1.6f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var tri = 1f - Mathf.Abs(2f * ((elapsed / 0.8f) % 1f) - 1f);
                hintMarker.localScale = Vector3.one * Mathf.Lerp(0.028f, 0.048f, tri);
                yield return null;
            }

            hintMarker.localScale = Vector3.one * 0.028f;
            hintPulse = null;
        }

        // ---------------------------------------------------------------- frame

        private void Update()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            // World-space canvases need an event camera for pointer raycasts (Editor).
            if (formCanvas != null && formCanvas.worldCamera == null) formCanvas.worldCamera = camera;
            if (menuCanvas != null && menuCanvas.worldCamera == null) menuCanvas.worldCamera = camera;

            FollowHand(camera);

            if (hintRoot != null && hintRoot.activeSelf)
            {
                // Billboard the pill toward the viewer.
                hintLabelCanvas.rotation = Quaternion.LookRotation(hintLabelCanvas.position - camera.transform.position);
            }
        }

        private void FollowHand(Camera camera)
        {
            if (menuRoot == null)
            {
                return;
            }

            Vector3 target;
            bool wantShown;
            var handPosition = leftPositionAction.ReadValue<Vector3>();
            if (handPosition != Vector3.zero)
            {
                // Tracked-space controller pose → world via the camera's parent (XR Origin offset).
                var trackingSpace = camera.transform.parent;
                var handRotation = leftRotationAction.ReadValue<Quaternion>();
                if (trackingSpace != null)
                {
                    handPosition = trackingSpace.TransformPoint(handPosition);
                    handRotation = trackingSpace.rotation * handRotation;
                }

                // Palm-up gate: turning the palm toward the face rolls the controller's
                // thumb face (its up axis) toward the head. Show only in that pose.
                var toHead = (camera.transform.position - handPosition).normalized;
                var facing = Vector3.Dot(handRotation * Vector3.up, toHead);
                wantShown = facing > (menuShown ? MenuHideBelowDot : MenuShowAboveDot);

                // Ulnar side of the palm: a little inward and up from the grip.
                target = handPosition + handRotation * new Vector3(0.09f, 0.03f, 0.02f);
            }
            else
            {
                // No controller tracked (Editor) — rest at the lower left of view,
                // always shown so the menu stays testable with a mouse.
                target = camera.transform.TransformPoint(new Vector3(-0.26f, -0.18f, 0.7f));
                wantShown = true;
            }

            menuShown = wantShown;
            var alphaTarget = menuShown ? 1f : 0f;
            menuGroup.alpha = Mathf.MoveTowards(menuGroup.alpha, alphaTarget, Time.deltaTime / MenuFadeSeconds);
            menuGroup.interactable = menuShown;
            menuGroup.blocksRaycasts = menuShown;

            if (!menuPlaced)
            {
                menuRoot.transform.position = target;
                menuPlaced = true;
            }
            else
            {
                // ~0.25 s lazy follow from the design.
                var t = 1f - Mathf.Exp(-Time.deltaTime / MenuFollowSeconds);
                menuRoot.transform.position = Vector3.Lerp(menuRoot.transform.position, target, t);
            }

            menuRoot.transform.rotation = Quaternion.LookRotation(menuRoot.transform.position - camera.transform.position);
        }

        private void AnchorInFront(Transform panel, float distance, float heightOffset)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var forward = camera.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.001f ? Vector3.forward : forward.normalized;

            var position = camera.transform.position + forward * distance + Vector3.up * heightOffset;
            panel.position = position;
            panel.rotation = Quaternion.LookRotation(position - camera.transform.position);
        }

        private void OnSelectPressed()
        {
            if (IsFormVisible && optionCount > 0)
            {
                onOptionPressed?.Invoke(0);
            }
        }

    }
}
