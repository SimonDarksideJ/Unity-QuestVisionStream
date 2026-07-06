// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using QuestVisionStream.Services;
using RealityCollective.ServiceFramework;
using RealityCollective.ServiceFramework.Services;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// Composition root for the QuestVisionStream client. Builds the XR rig
    /// (XR Origin + AR Foundation camera for Meta passthrough camera access) and
    /// registers the full streaming service graph with the RealityCollective
    /// Service Framework — code-first so every setting below is inspectable here;
    /// each service also carries a profile type, so the setup can be migrated to
    /// ServiceProvidersProfile assets in the editor whenever preferred.
    /// </summary>
    public sealed class QuestVisionStreamBootstrap : MonoBehaviour
    {
        /// <summary>Which detection render module starts active (switchable at runtime).</summary>
        public enum DetectionRenderMode
        {
            EphemeralBoxes,
            AnchoredTags
        }

        private const string EphemeralModuleName = "Ephemeral Boxes";
        private const string AnchoredModuleName = "Anchored Tags";

        [Header("Server")]
        [SerializeField]
        [Tooltip("Signaling WebSocket URL — e.g. ws://100.x.x.x:3000 (Tailscale IP of the Mac) or wss://machine.tailnet.ts.net")]
        private string serverUrl = "ws://localhost:3000";

        [SerializeField]
        [Tooltip("Optional QVS_AUTH_TOKEN shared secret.")]
        private string authToken = "";

        [Header("Streaming")]
        [SerializeField, Range(1, 30)]
        private int targetFps = 30;

        [SerializeField, Range(1, 4)]
        private int sendEveryNthFrame = 2;

        [SerializeField]
        [Tooltip("Server flips frames vertically by default (QVS_FLIP_VERTICAL=true) — leave on to match.")]
        private bool invertY = true;

        [Header("Detections")]
        [SerializeField]
        private DetectionRenderMode renderMode = DetectionRenderMode.EphemeralBoxes;

        [SerializeField]
        [Tooltip("Fixed placement distance for ephemeral boxes (and anchored fallback).")]
        private float placementDistanceMeters = 2f;

        [Header("Quality")]
        [SerializeField]
        private bool enableQualifier = true;

        [SerializeField]
        [Tooltip("Actually pause frame pushes on low quality. OFF by default — the raw passthrough feed reads darker than the tone-mapped view.")]
        private bool gateStreamingOnQuality = false;

        [Header("AprilTags")]
        [SerializeField]
        private bool enableAprilTags = true;

        [SerializeField]
        [Tooltip("Physical printed tag width in meters (tagStandard41h12 sheets from V2/tools/generate-apriltags.py).")]
        private float tagSizeMeters = 0.1f;

        private ServiceManager serviceManager;

        private void Awake()
        {
            EnsureCameraRig();

            // The GlobalServiceManager lives in the scene next to this component
            // (added here only as a safety net for bare scenes).
            var globalServiceManager = GetComponent<GlobalServiceManager>();
            if (globalServiceManager == null)
            {
                globalServiceManager = gameObject.AddComponent<GlobalServiceManager>();
            }

            globalServiceManager.InitializeServiceManager();
            serviceManager = globalServiceManager.Manager;

            // The manager only pumps Update/Start into services when it has an
            // active profile. When none is assigned in the scene, give it an empty
            // one and register the graph code-first below. (To go fully
            // asset-driven instead: assign a ServiceProvidersProfile on the
            // GlobalServiceManager and disable this component.)
            if (serviceManager.ActiveProfile == null)
            {
                serviceManager.ResetProfile(
                    ScriptableObject.CreateInstance<RealityCollective.ServiceFramework.Definitions.ServiceProvidersProfile>(),
                    gameObject);
            }

            RegisterServices();
        }

        private void Start()
        {
            // HUD after services are up (Start order: manager starts services first
            // by component order; the HUD only polls, so exact order is not critical).
            var camera = Camera.main;
            if (camera != null &&
                serviceManager.TryGetService<IStatusService>(out var status))
            {
                var hud = new GameObject("QVS_StatusHud");
                hud.transform.SetParent(camera.transform, false);
                hud.AddComponent<StatusHudController>().Initialize(status);
            }

            // The demo rule from the WebXR client: prove the "see X → do Y" seam.
            if (serviceManager.TryGetService<ITagRoutingService>(out var routing))
            {
                routing.AddRule(new TagRule
                {
                    Id = "log-first-sighting",
                    On = TagLifecycleEvent.Enter,
                    Run = tag => Debug.Log($"[QVS:Rule] First sighting of {tag.TagName} (#{tag.Id}) at {tag.WorldPose.position}")
                });
            }
        }

        /// <summary>Switch the detection renderer at runtime (also exposed for UI/inspector callers).</summary>
        public void SetRenderMode(DetectionRenderMode mode)
        {
            renderMode = mode;
            if (serviceManager != null &&
                serviceManager.TryGetService<IDetectionRendererService>(out var renderer))
            {
                renderer.SetActiveModule(mode == DetectionRenderMode.AnchoredTags ? AnchoredModuleName : EphemeralModuleName);
            }
        }

        /// <summary>
        /// The QuestVisionStream scene ships the full rig (XR Origin, tracked Main
        /// Camera with ARCameraManager, AR Session) — the AR camera path is what
        /// surfaces Meta passthrough camera frames under OpenXR; plain
        /// WebCamTexture does not work on Quest 3. This method only builds the rig
        /// when it is missing, so the component also works dropped into a bare scene.
        /// </summary>
        private void EnsureCameraRig()
        {
            if (FindFirstObjectByType<ARSession>() == null)
            {
                var sessionObject = new GameObject("AR Session");
                sessionObject.AddComponent<ARSession>();
            }

            if (FindFirstObjectByType<ARCameraManager>() != null)
            {
                return; // scene rig present — nothing to build
            }

            Debug.LogWarning("[QVS] No AR camera rig found in the scene — building one at runtime.");
            BuildCameraRig();
        }

        private void BuildCameraRig()
        {
            var originObject = new GameObject("XR Origin");
            var offsetObject = new GameObject("Camera Offset");
            offsetObject.transform.SetParent(originObject.transform, false);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(offsetObject.transform, false);

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear; // alpha 0 = passthrough shows through
            camera.nearClipPlane = 0.01f;
            cameraObject.AddComponent<AudioListener>();

            var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            var positionAction = new InputAction("QVS Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition");
            var rotationAction = new InputAction("QVS Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation");
            poseDriver.positionInput = new InputActionProperty(positionAction);
            poseDriver.rotationInput = new InputActionProperty(rotationAction);
            positionAction.Enable();
            rotationAction.Enable();

            var origin = originObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = offsetObject;
            origin.Camera = camera;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            // Enabling the AR Camera Manager is what turns passthrough on with the
            // Unity OpenXR Meta provider; it is also the CPU-image frame source.
            cameraObject.AddComponent<ARCameraManager>();
        }

        private void RegisterServices()
        {
            // --- Signaling (priority 10) ---
            var signalingProfile = ScriptableObject.CreateInstance<SignalingServiceProfile>();
            signalingProfile.ServerUrl = serverUrl;
            signalingProfile.AuthToken = authToken;
            serviceManager.TryCreateAndRegisterService<ISignalingService>(
                typeof(SignalingService), out var signaling, "Signaling", 10u, signalingProfile);

            // --- Camera (12) + Meta passthrough capture module ---
            var cameraProfile = ScriptableObject.CreateInstance<CameraStreamServiceProfile>();
            serviceManager.TryCreateAndRegisterService<ICameraStreamService>(
                typeof(CameraStreamService), out var cameraService, "Camera Stream", 12u, cameraProfile);
            serviceManager.TryCreateAndRegisterService<IMetaPassthroughCameraCaptureModule>(
                typeof(MetaPassthroughCameraCaptureModule), out _,
                "Meta Passthrough Camera", 0u, null, cameraService);

            // --- Image qualifier (15) + brightness module ---
            if (enableQualifier)
            {
                var qualifierProfile = ScriptableObject.CreateInstance<ImageQualifierServiceProfile>();
                serviceManager.TryCreateAndRegisterService<IImageQualifierService>(
                    typeof(ImageQualifierService), out var qualifierService, "Image Qualifier", 15u, qualifierProfile);
                var brightnessProfile = ScriptableObject.CreateInstance<BrightnessQualifierModuleProfile>();
                serviceManager.TryCreateAndRegisterService<IBrightnessQualifierModule>(
                    typeof(BrightnessQualifierModule), out _,
                    "Brightness", 0u, brightnessProfile, qualifierService);
            }

            // --- WebRTC (20) + Android plugin transport module ---
            var webrtcProfile = ScriptableObject.CreateInstance<WebRTCServiceProfile>();
            webrtcProfile.TargetFps = targetFps;
            webrtcProfile.SendEveryNthFrame = sendEveryNthFrame;
            webrtcProfile.GateStreamingOnQuality = gateStreamingOnQuality;
            serviceManager.TryCreateAndRegisterService<IWebRTCService>(
                typeof(WebRTCService), out var webrtcService, "WebRTC", 20u, webrtcProfile);
            serviceManager.TryCreateAndRegisterService<IAndroidWebRTCTransportModule>(
                typeof(AndroidWebRTCTransportModule), out _,
                "Android WebRTC Plugin", 0u, null, webrtcService);

            // --- Pose tracking (25) ---
            var poseProfile = ScriptableObject.CreateInstance<PoseTrackingServiceProfile>();
            serviceManager.TryCreateAndRegisterService<IPoseTrackingService>(
                typeof(PoseTrackingService), out _, "Pose Tracking", 25u, poseProfile);

            // --- Detections (30) ---
            var detectionProfile = ScriptableObject.CreateInstance<DetectionServiceProfile>();
            detectionProfile.InvertY = invertY;
            serviceManager.TryCreateAndRegisterService<IDetectionService>(
                typeof(DetectionService), out _, "Detections", 30u, detectionProfile);

            // --- Detection renderer (35) + both render modules (runtime-switchable) ---
            var rendererProfile = ScriptableObject.CreateInstance<DetectionRendererServiceProfile>();
            rendererProfile.InitialActiveModule =
                renderMode == DetectionRenderMode.AnchoredTags ? AnchoredModuleName : EphemeralModuleName;
            serviceManager.TryCreateAndRegisterService<IDetectionRendererService>(
                typeof(DetectionRendererService), out var rendererService, "Detection Renderer", 35u, rendererProfile);

            var boxProfile = ScriptableObject.CreateInstance<EphemeralBoxRenderModuleProfile>();
            boxProfile.PlacementDistanceMeters = placementDistanceMeters;
            serviceManager.TryCreateAndRegisterService<IEphemeralBoxRenderModule>(
                typeof(EphemeralBoxRenderModule), out _,
                EphemeralModuleName, 0u, boxProfile, rendererService);

            var anchoredProfile = ScriptableObject.CreateInstance<AnchoredTagRenderModuleProfile>();
            anchoredProfile.FallbackDistanceMeters = placementDistanceMeters;
            serviceManager.TryCreateAndRegisterService<IAnchoredTagRenderModule>(
                typeof(AnchoredTagRenderModule), out _,
                AnchoredModuleName, 1u, anchoredProfile, rendererService);

            // --- AprilTags (40-42) ---
            if (enableAprilTags)
            {
                var tagDetectionProfile = ScriptableObject.CreateInstance<TagDetectionServiceProfile>();
                serviceManager.TryCreateAndRegisterService<ITagDetectionService>(
                    typeof(TagDetectionService), out var tagDetection, "Tag Detection", 40u, tagDetectionProfile);

#if QVS_APRILTAG_KEIJIRO
                var keijiroProfile = ScriptableObject.CreateInstance<KeijiroAprilTagDetectorModuleProfile>();
                keijiroProfile.TagSizeMeters = tagSizeMeters;
                serviceManager.TryCreateAndRegisterService<IKeijiroAprilTagDetectorModule>(
                    typeof(KeijiroAprilTagDetectorModule), out _,
                    "Keijiro AprilTag", 0u, keijiroProfile, tagDetection);
#else
                Debug.LogWarning("[QVS] jp.keijiro.apriltag not installed — tag detection has no detector module.");
#endif

                var routingProfile = ScriptableObject.CreateInstance<TagRoutingServiceProfile>();
                serviceManager.TryCreateAndRegisterService<ITagRoutingService>(
                    typeof(TagRoutingService), out _, "Tag Routing", 41u, routingProfile);

                var placementProfile = ScriptableObject.CreateInstance<TagPlacementServiceProfile>();
                placementProfile.TagSizeMeters = tagSizeMeters;
                serviceManager.TryCreateAndRegisterService<ITagPlacementService>(
                    typeof(TagPlacementService), out _, "Tag Placement", 42u, placementProfile);
            }

            // --- Status (50) ---
            serviceManager.TryCreateAndRegisterService<IStatusService>(
                typeof(StatusService), out _, "Status", 50u);
        }
    }
}
