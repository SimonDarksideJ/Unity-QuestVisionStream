// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// Controller-only UI pointing for every world-space canvas in the client
    /// (the warm-up Enter card, the training step form, the hand menu). Hands
    /// come later — for now the pointer is the RIGHT controller's aim pose with
    /// the trigger as select, plus a laser so the user can see what they're
    /// aiming at. The Editor mouse keeps working through the same module.
    ///
    /// Implementation: one EventSystem carrying an
    /// <see cref="InputSystemUIInputModule"/> configured in code (no asset) with
    /// the Input System's tracked-device support — no interaction-toolkit
    /// dependency, matching the project's hardware-button approach. Canvases
    /// opt in via <see cref="RegisterCanvas"/>, which adds the
    /// <see cref="TrackedDeviceRaycaster"/> the module raycasts through.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ControllerUiPointer : MonoBehaviour
    {
        private const float RayLength = 2.5f;
        private static readonly Color RayColor = new Color(0.30f, 0.76f, 0.88f, 0.75f); // matches the Dark·Cyan accent

        private static ControllerUiPointer instance;

        private InputSystemUIInputModule module;
        private InputActionMap actionMap;
        private InputAction aimPosition;
        private InputAction aimRotation;
        private LineRenderer ray;
        private Material rayMaterial;

        /// <summary>Build the EventSystem + input module + laser once. Safe to call from every UI owner.</summary>
        public static void EnsureSetup()
        {
            if (instance != null)
            {
                return;
            }

            var host = new GameObject("QVS_ControllerUiPointer");
            instance = host.AddComponent<ControllerUiPointer>();
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
        }

        private void OnDestroy()
        {
            actionMap?.Disable();
            actionMap?.Dispose();

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
            actionMap = new InputActionMap("QVS UI");

            // Editor / flat-screen pointer.
            var point = actionMap.AddAction("point", InputActionType.PassThrough, expectedControlType: "Vector2");
            point.AddBinding("<Mouse>/position");

            var click = actionMap.AddAction("click", InputActionType.PassThrough, expectedControlType: "Button");
            click.AddBinding("<Mouse>/leftButton");
            // Trigger = select on the pointing controller (control name differs across layouts — bind both).
            click.AddBinding("<XRController>{RightHand}/triggerPressed");
            click.AddBinding("<XRController>{RightHand}/triggerButton");

            var scroll = actionMap.AddAction("scroll", InputActionType.PassThrough, expectedControlType: "Vector2");
            scroll.AddBinding("<Mouse>/scroll");

            // The OpenXR AIM pose — the OS-tuned pointing ray (the grip/device pose
            // points up through the controller ring and feels wrong for aiming).
            aimPosition = actionMap.AddAction("aimPosition", InputActionType.PassThrough, expectedControlType: "Vector3");
            aimPosition.AddBinding("<XRController>{RightHand}/pointerPosition");

            aimRotation = actionMap.AddAction("aimRotation", InputActionType.PassThrough, expectedControlType: "Quaternion");
            aimRotation.AddBinding("<XRController>{RightHand}/pointerRotation");

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
                return;
            }

            var rotation = aimRotation.ReadValue<Quaternion>();
            if (trackingOrigin != null)
            {
                position = trackingOrigin.TransformPoint(position);
                rotation = trackingOrigin.rotation * rotation;
            }

            ray.enabled = true;
            ray.SetPosition(0, position);
            ray.SetPosition(1, position + rotation * Vector3.forward * RayLength);
        }
    }
}
