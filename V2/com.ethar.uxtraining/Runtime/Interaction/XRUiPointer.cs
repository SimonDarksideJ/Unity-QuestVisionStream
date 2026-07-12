// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ethar.UXTraining.Interaction
{
    /// <summary>
    /// Controller UI pointing for world-space canvases, built purely on the
    /// Unity Input System's generic <c>&lt;XRController&gt;</c> layouts — no
    /// vendor SDK and no interaction-toolkit dependency, so the same pointer
    /// runs on Meta Quest, Magic Leap 2 or any OpenXR headset. The pointer is
    /// the configured hand's aim pose with the trigger as select, plus a laser
    /// so the user can see what they are aiming at. The Editor mouse keeps
    /// working through the same module.
    ///
    /// Implementation: one EventSystem carrying an
    /// <see cref="InputSystemUIInputModule"/> configured in code (no asset) with
    /// the Input System's tracked-device support. Canvases opt in via
    /// <see cref="RegisterCanvas"/>, which adds the
    /// <see cref="TrackedDeviceRaycaster"/> the module raycasts through.
    ///
    /// The beam is clamped to the module's own raycast result, so it stops on
    /// whatever the click would land on instead of passing through, and a small
    /// circular reticle is laid flat against the hit surface — the "you are
    /// pointing at this" cue that pairs with the buttons' hover glow.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class XRUiPointer : MonoBehaviour
    {
        public enum PointerHand { Left, Right }

        private const float RayLength = 2.5f;

        /// <summary>
        /// Which controller aims and clicks. Set BEFORE <see cref="EnsureSetup"/>;
        /// UX that follows the off hand (e.g. the hand menu) reads <see cref="OffHand"/>.
        /// </summary>
        public static PointerHand Hand = PointerHand.Right;

        /// <summary>The non-pointing hand — where palm-anchored UX should live.</summary>
        public static PointerHand OffHand => Hand == PointerHand.Right ? PointerHand.Left : PointerHand.Right;

        /// <summary>Laser/reticle accent. Set before <see cref="EnsureSetup"/> to re-tint (defaults to the Dark·Cyan accent).</summary>
        public static Color RayColor = new Color(0.30f, 0.76f, 0.88f, 0.75f);

        /// <summary>The Input System usage tag for a hand, e.g. "RightHand" — for callers binding their own hand-relative actions.</summary>
        public static string UsageTag(PointerHand hand) => hand == PointerHand.Right ? "RightHand" : "LeftHand";

        private static XRUiPointer instance;

        private InputSystemUIInputModule module;
        private InputActionAsset actionAsset;
        private InputActionMap actionMap;
        private InputAction aimPosition;
        private InputAction aimRotation;
        private LineRenderer ray;
        private Material rayMaterial;
        private Transform reticle;

        private static Sprite circleSprite;

        /// <summary>Build the EventSystem + input module + laser once. Safe to call from every UI owner.</summary>
        public static void EnsureSetup()
        {
            if (instance != null)
            {
                return;
            }

            var host = new GameObject("UXTraining_XRUiPointer");
            instance = host.AddComponent<XRUiPointer>();
        }

        /// <summary>
        /// Make a world-space canvas clickable: a <see cref="TrackedDeviceRaycaster"/>
        /// for the controller ray and a <see cref="GraphicRaycaster"/> for the Editor mouse.
        /// </summary>
        public static void RegisterCanvas(GameObject canvasObject)
        {
            if (canvasObject.GetComponent<GraphicRaycaster>() == null)
            {
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            if (canvasObject.GetComponent<TrackedDeviceRaycaster>() == null)
            {
                canvasObject.AddComponent<TrackedDeviceRaycaster>();
            }
        }

        private void Awake()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            module = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            ConfigureModuleActions();
            BuildRayVisual();
            BuildReticle();
        }

        private void OnDestroy()
        {
            actionMap?.Disable();
            if (actionAsset != null)
            {
                Destroy(actionAsset);
            }

            if (rayMaterial != null)
            {
                Destroy(rayMaterial);
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        private void ConfigureModuleActions()
        {
            var hand = UsageTag(Hand);

            // The map must live inside an InputActionAsset:
            // InputActionReference.Create throws for actions in a free-standing
            // map, which would leave the module with no actions at all.
            actionAsset = ScriptableObject.CreateInstance<InputActionAsset>();
            actionAsset.name = "UXTraining UI Actions";
            actionMap = actionAsset.AddActionMap("UXTraining UI");

            // Editor / flat-screen pointer.
            var point = actionMap.AddAction("point", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            point.AddBinding("<Mouse>/position");

            var click = actionMap.AddAction("click", InputActionType.PassThrough, expectedControlLayout: "Button");
            click.AddBinding("<Mouse>/leftButton");
            // Trigger = select on the pointing controller (control name differs across layouts — bind both).
            click.AddBinding($"<XRController>{{{hand}}}/triggerPressed");
            click.AddBinding($"<XRController>{{{hand}}}/triggerButton");

            var scroll = actionMap.AddAction("scroll", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            scroll.AddBinding("<Mouse>/scroll");

            // The OpenXR AIM pose — the OS-tuned pointing ray (the grip/device pose
            // points up through the controller ring and feels wrong for aiming).
            aimPosition = actionMap.AddAction("aimPosition", InputActionType.PassThrough, expectedControlLayout: "Vector3");
            aimPosition.AddBinding($"<XRController>{{{hand}}}/pointerPosition");

            aimRotation = actionMap.AddAction("aimRotation", InputActionType.PassThrough, expectedControlLayout: "Quaternion");
            aimRotation.AddBinding($"<XRController>{{{hand}}}/pointerRotation");

            module.point = InputActionReference.Create(point);
            module.leftClick = InputActionReference.Create(click);
            module.scrollWheel = InputActionReference.Create(scroll);
            module.trackedDevicePosition = InputActionReference.Create(aimPosition);
            module.trackedDeviceOrientation = InputActionReference.Create(aimRotation);

            actionMap.Enable();
        }

        private void BuildRayVisual()
        {
            var rayObject = new GameObject("Laser");
            rayObject.transform.SetParent(transform, false);

            rayMaterial = new Material(Shader.Find("Sprites/Default"));
            ray = rayObject.AddComponent<LineRenderer>();
            ray.useWorldSpace = true;
            ray.positionCount = 2;
            ray.material = rayMaterial;
            ray.startColor = RayColor;
            ray.endColor = new Color(RayColor.r, RayColor.g, RayColor.b, 0f); // fade out along the beam
            ray.startWidth = 0.004f;
            ray.endWidth = 0.001f;
            ray.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ray.receiveShadows = false;
            ray.enabled = false;
        }

        private void BuildReticle()
        {
            reticle = new GameObject("Reticle").transform;
            reticle.SetParent(transform, false);

            // Halo + dot, laid flat against the hit surface (the halo carries the
            // accent, the dot marks the exact hit point).
            CreateCircleRenderer("Halo", new Color(RayColor.r, RayColor.g, RayColor.b, 0.4f), 0.022f, order: 0);
            CreateCircleRenderer("Dot", new Color(0.95f, 0.98f, 1f, 0.95f), 0.008f, order: 1);
            reticle.gameObject.SetActive(false);
        }

        private void CreateCircleRenderer(string name, Color color, float diameterMeters, int order)
        {
            var circle = new GameObject(name);
            circle.transform.SetParent(reticle, false);
            var renderer = circle.AddComponent<SpriteRenderer>();
            renderer.sprite = GetCircleSprite();
            renderer.color = color;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            circle.transform.localScale = Vector3.one * (diameterMeters / renderer.sprite.bounds.size.x);
        }

        private static Sprite GetCircleSprite()
        {
            if (circleSprite != null)
            {
                return circleSprite;
            }

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "UXTraining_ReticleCircle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            var centre = (size - 1) * 0.5f;
            var radius = size * 0.5f - 1.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius - distance + 0.5f) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            circleSprite.name = "UXTraining_ReticleCircle";
            circleSprite.hideFlags = HideFlags.HideAndDontSave;
            return circleSprite;
        }

        private void LateUpdate()
        {
            var camera = Camera.main;
            var trackingOrigin = camera != null ? camera.transform.parent : null;

            // Poses arrive in tracking space; the module transforms them through
            // xrTrackingOrigin (the XR Origin's camera offset) — keep the laser in
            // the exact same space so the beam and the clicks always agree.
            if (module != null && module.xrTrackingOrigin != trackingOrigin)
            {
                module.xrTrackingOrigin = trackingOrigin;
            }

            var position = aimPosition.ReadValue<Vector3>();
            if (position == Vector3.zero)
            {
                ray.enabled = false; // controller not tracked
                reticle.gameObject.SetActive(false);
                return;
            }

            var rotation = aimRotation.ReadValue<Quaternion>();
            if (trackingOrigin != null)
            {
                position = trackingOrigin.TransformPoint(position);
                rotation = trackingOrigin.rotation * rotation;
            }

            // Clamp the beam to the module's own raycast: it stops on whatever a
            // click would land on, and the reticle sits flat on that surface.
            var endpoint = position + rotation * Vector3.forward * RayLength;
            var hit = CurrentUiHit();
            if (hit.isValid)
            {
                endpoint = hit.worldPosition;
                var surface = hit.gameObject.transform;
                reticle.rotation = surface.rotation;
                // Canvases face away from the viewer (+Z outward), so nudge the
                // reticle back along the surface normal to sit clear of the UI.
                reticle.position = hit.worldPosition - surface.forward * 0.004f;
                reticle.gameObject.SetActive(true);
            }
            else
            {
                reticle.gameObject.SetActive(false);
            }

            ray.enabled = true;
            ray.SetPosition(0, position);
            ray.SetPosition(1, endpoint);
        }

        /// <summary>The UI hit the module computed this frame for the aiming controller.</summary>
        private RaycastResult CurrentUiHit()
        {
            var device = aimPosition.activeControl?.device;
            if (device == null || module == null)
            {
                return default;
            }

            return module.GetLastRaycastResult(device.deviceId);
        }
    }
}
