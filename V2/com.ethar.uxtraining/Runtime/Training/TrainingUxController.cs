// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections;
using Ethar.UXTraining.Components;
using Ethar.UXTraining.Interaction;
using Ethar.UXTraining.Settings;
using Ethar.UXTraining.Theme;
using Ethar.UXTraining.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ethar.UXTraining
{
    /// <summary>
    /// The training UX, built from the Ethar UX Training kit:
    ///
    ///   - <b>Display menu</b> — a world-space step form (eyebrow, progress ticks,
    ///     title, description, optional step image, action buttons), placed by a
    ///     <see cref="WindowFollower"/>: anchored in front of the user per step
    ///     (Fixed) or label-aware smooth-following (HeadLocked), per the shared
    ///     <see cref="UxSettings"/>. Rebuilt per step, focus-card style.
    ///   - <b>Hand menu</b> — the vertical 1d strip with a live current-step
    ///     readout, lazily following the off-hand controller and shown only in the
    ///     palm-up pose (fades in when the palm rolls toward the face, out when
    ///     it rolls away; always visible in the Editor's untracked fallback).
    ///   - <b>Location indicator</b> — a pulsing hotspot marker at a host-supplied
    ///     world point with a leader-line connector up to a billboarded label pill.
    ///
    /// Interaction: uGUI Buttons pressed with the controller laser (trigger)
    /// in-headset, or the mouse in the Editor — both through
    /// <see cref="XRUiPointer"/>, driven purely by Unity Input System
    /// <c>&lt;XRController&gt;</c> bindings so the UX ports to any OpenXR headset.
    /// The hand menu follows <see cref="XRUiPointer.OffHand"/> automatically.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class TrainingUxController : MonoBehaviour
    {
        private const float FormPixelsToMetres = 0.0011f;
        private const float MenuPixelsToMetres = 0.0005f; // hand menu at 50% — full size read too big on the wrist
        private const float LabelLiftMeters = 0.16f;
        private const float MenuFollowSeconds = 0.25f;
        private const float MenuFadeSeconds = 0.15f;

        // Palm-up gate with hysteresis: show when the palm rolls toward the face,
        // keep showing until it rolls clearly away — no flicker at the boundary.
        private const float MenuShowAboveDot = 0.55f;
        private const float MenuHideBelowDot = 0.35f;

        private ThemePalette theme;
        private UxSettings settings;
        private Action<int> onOptionPressed;
        private UIFactory factory;
        private string brandText = "TRAINING";

        // Display menu (step form).
        private GameObject formRoot;
        private WindowFollower formFollower;
        private Canvas formCanvas;
        private Canvas menuCanvas;
        private RectTransform formCanvasRect;
        private float formDistanceMeters = 1.25f;

        // Hand menu.
        private GameObject menuRoot;
        private HandMenu.Handle menuHandle;
        private CanvasGroup menuGroup;
        private InputAction menuHandPositionAction;
        private InputAction menuHandRotationAction;
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

        public bool IsFormVisible => formRoot != null && formRoot.activeSelf;

        /// <param name="palette">Active theme (see <see cref="ThemeLibrary"/> for built-ins).</param>
        /// <param name="formDistance">How far in front of the user the step form anchors, in meters.</param>
        /// <param name="optionPressed">Invoked with the option index when an action button is pressed.</param>
        /// <param name="menuActions">Handlers for the hand menu tiles.</param>
        /// <param name="brand">Eyebrow text on the form's top-right (e.g. the product/course name).</param>
        /// <param name="uxSettings">Shared UX tuning (label sizing, window placement). Null uses <see cref="UxSettings.Defaults"/>.</param>
        public void Initialize(ThemePalette palette, float formDistance, Action<int> optionPressed,
            HandMenu.Actions menuActions, string brand = "TRAINING", UxSettings uxSettings = null)
        {
            theme = palette;
            settings = uxSettings != null ? uxSettings : UxSettings.Defaults;
            formDistanceMeters = formDistance;
            onOptionPressed = optionPressed;
            brandText = brand ?? string.Empty;
            factory = new UIFactory(theme);

            XRUiPointer.EnsureSetup();
            BuildHandMenu(menuActions);

            // The hand menu lives on the non-pointing hand — generic Input System
            // XR bindings, resolved per the pointer's configured handedness.
            var menuHand = XRUiPointer.UsageTag(XRUiPointer.OffHand);
            menuHandPositionAction = new InputAction("UXTraining Menu Pos", InputActionType.Value, $"<XRController>{{{menuHand}}}/devicePosition");
            menuHandRotationAction = new InputAction("UXTraining Menu Rot", InputActionType.Value, $"<XRController>{{{menuHand}}}/deviceRotation");
            menuHandPositionAction.Enable();
            menuHandRotationAction.Enable();
        }

        private void OnDestroy()
        {
            menuHandPositionAction?.Dispose();
            menuHandRotationAction?.Dispose();
            WindowFollower.UnregisterObstacle(hintLabelCanvas);

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
        public void ShowStep(TrainingStepView step)
        {
            if (formRoot == null)
            {
                BuildFormCanvas();
            }

            if (!step.HasPresentation)
            {
                formRoot.SetActive(false);
                return;
            }

            RebuildFormContent(step);
            formRoot.SetActive(true);
            formFollower.Reanchor();
        }

        /// <summary>Re-anchor the current form in front of the user (hand menu TASKS).</summary>
        public void RepresentForm()
        {
            if (IsFormVisible)
            {
                formFollower.Reanchor();
            }
        }

        public void HideForm()
        {
            if (formRoot != null)
            {
                formRoot.SetActive(false);
            }
        }

        private void BuildFormCanvas()
        {
            formRoot = new GameObject("UXTraining_Form");
            formRoot.transform.SetParent(transform, false);

            formCanvas = formRoot.AddComponent<Canvas>();
            formCanvas.renderMode = RenderMode.WorldSpace;
            XRUiPointer.RegisterCanvas(formRoot);
            formCanvasRect = (RectTransform)formRoot.transform;
            formCanvasRect.sizeDelta = new Vector2(520, 700);
            formRoot.transform.localScale = Vector3.one * FormPixelsToMetres;

            // Fixed or head-locked per the shared UX settings; head-locked keeps
            // clear of any active world label (sliding-door stop at the boundary).
            formFollower = WindowFollower.Attach(formRoot, settings, formDistanceMeters, heightOffsetMeters: -0.04f);
        }

        private void RebuildFormContent(TrainingStepView step)
        {
            for (var i = formCanvasRect.childCount - 1; i >= 0; i--)
            {
                var child = formCanvasRect.GetChild(i);
                child.gameObject.SetActive(false); // Destroy defers to end of frame
                Destroy(child.gameObject);
            }

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

            // Eyebrow row: step counter + brand.
            var top = f.Rect("Top", panel);
            f.HLayout(top, 8, TextAnchor.MiddleCenter, controlWidth: true, controlHeight: true, expandWidth: false);
            f.Sized(top);
            f.Label(top, $"STEP {step.StepIndex + 1} OF {step.StepCount}", 12, T.accent, FontStyle.Bold, TextAnchor.MiddleLeft, wrap: false);
            var spacer = f.Rect("Spacer", top);
            spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            f.Label(top, brandText, 11, T.textLo, FontStyle.Bold, TextAnchor.MiddleRight, wrap: false);

            // Progress ticks (focus-card style).
            var ticks = f.Rect("Ticks", panel);
            f.HLayout(ticks, 4, TextAnchor.MiddleCenter, controlWidth: true, controlHeight: true, expandWidth: true);
            f.Sized(ticks, h: 5);
            for (var i = 0; i < step.StepCount; i++)
            {
                var color = i < step.StepIndex ? T.accent
                    : i == step.StepIndex ? T.AccentA(0.5f) : T.Overlay(0.12f);
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

            // Step image — rendered only when the scenario provides one and the
            // host resolved it (no image, no block: the grey area never shows empty).
            if (step.Image != null)
            {
                var plate = f.Plate(panel, 12, new Color(0.35f, 0.37f, 0.40f, 0.95f), T.Overlay(0.18f), "StepImage");
                var aspect = step.Image.height > 0 ? (float)step.Image.width / step.Image.height : 1f;
                // Inner width is the panel minus its padding; size the plate so the
                // photo fills it (letterboxed by the fitter), within sane bounds.
                const float innerWidth = 520f - 26f * 2f - 8f * 2f;
                f.Sized(plate, h: Mathf.Clamp(innerWidth / aspect + 16f, 90f, 260f));

                // Padded holder keeps the plate's rounded border visible; the
                // fitter letterboxes the photo inside it at its native aspect.
                var holder = f.Rect("PhotoHolder", plate);
                UIFactory.Stretch(holder, 8f);
                var photo = f.Rect("Photo", holder);
                var raw = photo.gameObject.AddComponent<RawImage>();
                raw.texture = step.Image;
                raw.raycastTarget = false;
                var fitter = photo.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = aspect;
            }

            // Actions — default one; the first renders as the primary button.
            if (step.Options.Count > 0)
            {
                var row = f.Rect("Actions", panel);
                f.HLayout(row, 10, TextAnchor.MiddleCenter, controlWidth: true, controlHeight: true, expandWidth: false);
                f.Sized(row);
                for (var i = 0; i < step.Options.Count; i++)
                {
                    var captured = i;
                    var button = i == 0
                        ? f.PrimaryButton(row, step.Options[i], () => onOptionPressed?.Invoke(captured), height: 52, fontSize: 16)
                        : f.SecondaryButton(row, step.Options[i], () => onOptionPressed?.Invoke(captured), height: 52, fontSize: 16);
                    button.GetComponent<LayoutElement>().flexibleWidth = 1;
                }
            }

            f.Label(panel, "Point and pull the trigger to select · Turn palm up for menu", 11, T.textLo);
        }

        // ------------------------------------------------------------ hand menu

        private void BuildHandMenu(HandMenu.Actions actions)
        {
            menuRoot = new GameObject("UXTraining_HandMenu");
            menuRoot.transform.SetParent(transform, false);

            menuCanvas = menuRoot.AddComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.WorldSpace;
            XRUiPointer.RegisterCanvas(menuRoot);
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

        /// <summary>Place (or move) the hotspot marker + connector + label pill at a world point.</summary>
        public void ShowWorldLabel(Vector3 worldPoint, string labelText)
        {
            if (hintRoot == null)
            {
                BuildWorldLabel();
            }

            hintRoot.SetActive(true);
            hintLabelText.text = labelText;
            // Head-locked windows must keep clear of the label while it shows.
            WindowFollower.RegisterObstacle(hintLabelCanvas);
            UpdateWorldLabel(worldPoint);
        }

        /// <summary>Refresh the indicator position (e.g. on a re-sighting of the tracked object).</summary>
        public void UpdateWorldLabel(Vector3 worldPoint)
        {
            if (hintRoot == null || !hintRoot.activeSelf)
            {
                return;
            }

            hintMarker.position = worldPoint;
            hintLabelCanvas.position = worldPoint + Vector3.up * LabelLiftMeters;
            hintLine.SetPosition(0, worldPoint);
            // Attach the connector to the pill's bottom edge (half its scaled height).
            var pillHalfHeight = hintLabelCanvas.sizeDelta.y * 0.5f * settings.LabelScale;
            hintLine.SetPosition(1, hintLabelCanvas.position + Vector3.down * pillHalfHeight);
        }

        public void HideWorldLabel()
        {
            if (hintRoot != null)
            {
                hintRoot.SetActive(false);
                WindowFollower.UnregisterObstacle(hintLabelCanvas);
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
            hintRoot = new GameObject("UXTraining_Hint");
            hintRoot.transform.SetParent(transform, false);

            // Two materials: the marker is tinted via material colour, the line via
            // vertex colours (a shared tinted material would double-tint the line).
            hintMaterial = new Material(Shader.Find("Sprites/Default"));
            hintLineMaterial = new Material(Shader.Find("Sprites/Default"));

            // Marker dot at the indicated world point.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(hintRoot.transform, false);
            marker.transform.localScale = Vector3.one * settings.LabelDotDiameterMeters;
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
            hintLine.startWidth = settings.LabelLineWidthMeters;
            hintLine.endWidth = settings.LabelLineWidthMeters;
            hintLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hintLine.receiveShadows = false;

            // Label pill — billboarded world canvas.
            var canvasObject = new GameObject("LabelPill");
            canvasObject.transform.SetParent(hintRoot.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            hintLabelCanvas = (RectTransform)canvasObject.transform;
            hintLabelCanvas.sizeDelta = new Vector2(280, 56);
            canvasObject.transform.localScale = Vector3.one * settings.LabelScale;

            var pill = factory.Plate(hintLabelCanvas, 16, theme.Panel(), theme.HintBorder(), "Pill");
            UIFactory.Stretch(pill);
            hintLabelText = factory.Label(pill, string.Empty, 18, theme.textHi, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            UIFactory.Stretch(hintLabelText.rectTransform, 8f);
        }

        private IEnumerator PulseMarker()
        {
            const float duration = 1.6f;
            var dot = settings.LabelDotDiameterMeters;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var tri = 1f - Mathf.Abs(2f * ((elapsed / 0.8f) % 1f) - 1f);
                hintMarker.localScale = Vector3.one * Mathf.Lerp(dot, dot * 1.7f, tri);
                yield return null;
            }

            hintMarker.localScale = Vector3.one * dot;
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
            var handPosition = menuHandPositionAction.ReadValue<Vector3>();
            if (handPosition != Vector3.zero)
            {
                // Tracked-space controller pose → world via the camera's parent (XR Origin offset).
                var trackingSpace = camera.transform.parent;
                var handRotation = menuHandRotationAction.ReadValue<Quaternion>();
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
    }
}
