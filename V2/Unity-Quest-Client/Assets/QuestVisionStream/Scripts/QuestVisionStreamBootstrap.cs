// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using Ethar.DebugDrawingBBox;
using Ethar.UXTraining.Settings;
using QuestVisionStream.Core;
using QuestVisionStream.Services;
using QuestVisionStream.Training;
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
        [Tooltip("Remote-config endpoint (Cloudflare Pages /api/config backed by KV). The server host publishes its live Tailscale signaling URL there — clients discover it at connect time, no rebuilds. Empty disables discovery.")]
        private string remoteConfigUrl = "https://questvisionstream.pages.dev/api/config";

        [SerializeField]
        [Tooltip("Fallback signaling WebSocket URL when remote config is disabled or unreachable — e.g. ws://100.x.x.x:3000 (Tailscale IP of the Mac) or wss://machine.tailnet.ts.net")]
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

        [SerializeField]
        [Tooltip("Simple-mode alignment aid — pitches detection rays down to offset the passthrough camera mount (positive = boxes down). 11° is the on-device tuned value for arm's-length desk objects; hold Y + L-stick to re-tune live, or 0 to disable. A fixed-depth approximation; the depth/anchored mode is the accurate fix.")]
        private float cameraPitchCompensationDegrees = 11f;

        [Header("UX")]
        [SerializeField]
        [Tooltip("UX tuning asset (Create → Ethar → UX Training → UX Settings): world label sizing, window placement mode (fixed / head-locked) and follow behaviour. Empty tries Resources/UxSettings, then the package defaults.")]
        private UxSettings uxSettings;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Start with the debug visuals (detection HUD window, detection boxes, connection dot) visible. OFF by default — the trainee sees only the training UX and world labels. Toggle at runtime with the LEFT controller MENU button.")]
        private bool debugVisualsAtStart = false;

        [Header("Quality")]
        [SerializeField]
        private bool enableQualifier = true;

        [SerializeField]
        [Tooltip("Actually pause frame pushes on low quality. OFF by default — the raw passthrough feed reads darker than the tone-mapped view.")]
        private bool gateStreamingOnQuality = false;

        [Header("Training")]
        [SerializeField]
        [Tooltip("Register the training state + presentation services (the authoritative training flow driven by detections).")]
        private bool enableTraining = true;

        [SerializeField]
        [Tooltip("Scenario asset (Create → QuestVisionStream → Training Scenario). Empty loads Resources/EtharTrainingScenario, falling back to the built-in demo.")]
        private TrainingScenarioAsset trainingScenario;

        [SerializeField]
        [Tooltip("Built-in Ethar UX Training palette for the training UX: 0 = Dark·Cyan, 1 = Light·Teal, 2 = Hi-Vis·Orange.")]
        private int trainingThemeIndex = 0;

        [SerializeField]
        [Tooltip("Model catalog for training steps: maps a step's Model Ref key to the prefab spawned aligned to the step's AprilTag when the step activates.")]
        private List<TrainingModelEntry> trainingModels = new List<TrainingModelEntry>();

        [Header("AprilTags")]
        [SerializeField]
        private bool enableAprilTags = true;

        [SerializeField]
        [Tooltip("Physical printed tag width in meters (tagStandard41h12 sheets from V2/tools/generate-apriltags.py).")]
        private float tagSizeMeters = 0.1f;

        [SerializeField]
        [Tooltip("Republish tag sightings into the detection pipeline as ClassName detections (label = registry Class Name), so AprilTags render, log and drive the training flow exactly like server detections — including fully offline.")]
        private bool bridgeTagsToDetections = true;

        private const float PitchTuneRateDegreesPerSecond = 20f;

        private ServiceManager serviceManager;
        private InputAction switchRenderAction;
        private InputAction tagBehaviourAction;
        private InputAction tunePitchHoldAction;
        private InputAction tunePitchAxisAction;
        private InputAction debugToggleAction;
        private GameObject connectionDotObject;
        private GameObject detectionHudObject;
        private bool debugVisualsVisible;
        private IPoseTrackingService poseTracking;
        private RoomScanController roomScan;
        private EnvironmentDepthProvider environmentDepth;
        private bool pitchTuning;

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
            // HUD + warm-up screen after services are up (Start order: manager starts
            // services first by component order; these only poll, so order is not critical).
            var camera = Camera.main;
            if (camera != null &&
                serviceManager.TryGetService<IStatusService>(out var status))
            {
                serviceManager.TryGetService<IWebRTCService>(out var webrtcService);
                serviceManager.TryGetService<ISignalingService>(out var signalingService);

                // At-a-glance connection dot (green/red, top right). Text-free — all
                // logging/messaging lives in the log window below.
                connectionDotObject = new GameObject("QVS_ConnectionDot");
                connectionDotObject.transform.SetParent(camera.transform, false);
                connectionDotObject.AddComponent<ConnectionDotController>().Initialize(status);

                // THE log window: the left-third translucent panel with the live
                // detection feed plus the status/diagnostics section. All logging/
                // messaging lands here — no free-floating 3D text; the only other
                // world text is the detector tags on objects.
                if (serviceManager.TryGetService<IDetectionService>(out var detectionService))
                {
                    serviceManager.TryGetService<IDetectionRendererService>(out var rendererService);
                    serviceManager.TryGetService<IPoseTrackingService>(out poseTracking);
                    serviceManager.TryGetService<IAnchoredTagRenderModule>(out var anchoredModule);
                    detectionHudObject = new GameObject("QVS_DetectionHud");
                    detectionHudObject.transform.SetParent(camera.transform, false);
                    detectionHudObject.AddComponent<DetectionHudController>().Initialize(
                        detectionService, rendererService, poseTracking, anchoredModule,
                        status, webrtcService, signalingService);
                }

                // Warm-up flow: confirm the connection, then Enter (laser click) starts frames.
                if (webrtcService != null &&
                    signalingService != null &&
                    serviceManager.TryGetService<ICameraStreamService>(out var cameraService))
                {
                    serviceManager.TryGetService<IUxSettingsService>(out var uxSettingsService);
                    var warmup = new GameObject("QVS_StartupFlow");
                    warmup.transform.SetParent(camera.transform, false);
                    warmup.AddComponent<StartupFlowController>()
                        .Initialize(status, webrtcService, signalingService, cameraService,
                            uxSettingsService?.Settings);
                }
            }

            // MENU (left controller) toggles ALL debug visuals — the detection HUD
            // window, the drawn detection boxes and the connection dot. OFF by
            // default: the trainee sees only the training UX and world labels.
            debugToggleAction = new InputAction("QVS Debug Toggle", InputActionType.Button);
            // Control name differs across XR layouts — bind both so it resolves.
            debugToggleAction.AddBinding("<XRController>{LeftHand}/menu");
            debugToggleAction.AddBinding("<XRController>{LeftHand}/menuButton");
            debugToggleAction.performed += _ => SetDebugVisuals(!debugVisualsVisible);
            debugToggleAction.Enable();
            SetDebugVisuals(debugVisualsAtStart);

            // X (left controller) toggles between the two detection render modules
            // at runtime — outline boxes <-> anchored tags — for A/B testing on device.
            switchRenderAction = new InputAction("QVS Switch Render", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
            switchRenderAction.performed += _ => ToggleRenderMode();
            switchRenderAction.Enable();

            // B (right controller) toggles anchored-tag behaviour: persistent world
            // pins (default) <-> update-in-place tracking. Only affects anchored mode.
            tagBehaviourAction = new InputAction("QVS Tag Behaviour", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            tagBehaviourAction.performed += _ => ToggleTagBehaviour();
            tagBehaviourAction.Enable();

            // Hold Y (left controller) + push the left thumbstick up/down to tune the
            // ephemeral pitch compensation live in-headset (no rebuild). The final
            // value is logged on release so it can be baked into the inspector default.
            serviceManager.TryGetService<IPoseTrackingService>(out poseTracking);
            tunePitchHoldAction = new InputAction("QVS Tune Pitch Hold", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
            tunePitchAxisAction = new InputAction("QVS Tune Pitch Axis", InputActionType.Value, expectedControlType: "Vector2");
            // Both control names appear across XR/OpenXR layouts — bind both so the stick resolves.
            tunePitchAxisAction.AddBinding("<XRController>{LeftHand}/thumbstick");
            tunePitchAxisAction.AddBinding("<XRController>{LeftHand}/primary2DAxis");
            tunePitchHoldAction.Enable();
            tunePitchAxisAction.Enable();

            // Raw per-frame environment depth (Meta Depth API) — the PRIMARY depth
            // source for anchored tags, so objects land at their true distance with no
            // room scan. Falls back to the scanned scene mesh, then a fixed distance.
            var depthObject = new GameObject("QVS_EnvironmentDepth");
            environmentDepth = depthObject.AddComponent<EnvironmentDepthProvider>();
            environmentDepth.Initialize(camera);

            // Advanced/depth mode: ensures a room scan exists so the anchored tags have
            // a scene-mesh FALLBACK when environment depth is unavailable at a pixel.
            // Created disabled; only the anchored mode drives a scan.
            var scanObject = new GameObject("QVS_RoomScan");
            roomScan = scanObject.AddComponent<RoomScanController>();
            roomScan.Initialize(FindFirstObjectByType<XROrigin>());
            if (renderMode == DetectionRenderMode.AnchoredTags)
            {
                roomScan.EnsureSceneReady();
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

        /// <summary>
        /// Show/hide ALL debug visuals: the detection HUD window, the drawn
        /// detection boxes (renderer service master switch — visuals cleared on
        /// off, module selection kept) and the connection dot. The training UX
        /// (warm-up card, step forms, hand menu, world labels) is never touched.
        /// </summary>
        public void SetDebugVisuals(bool visible)
        {
            debugVisualsVisible = visible;

            if (detectionHudObject != null)
            {
                detectionHudObject.SetActive(visible);
            }

            if (connectionDotObject != null)
            {
                connectionDotObject.SetActive(visible);
            }

            if (serviceManager != null &&
                serviceManager.TryGetService<IDetectionRendererService>(out var renderer))
            {
                renderer.RenderingEnabled = visible;
            }

            Debug.Log($"[QVS] Debug visuals {(visible ? "ON" : "OFF")} (left-controller MENU toggles)");
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

            // Entering the depth-anchored mode: make sure the room is scanned.
            if (mode == DetectionRenderMode.AnchoredTags)
            {
                roomScan?.EnsureSceneReady();
            }
        }

        /// <summary>Flip between the two render modules — bound to the X button.</summary>
        public void ToggleRenderMode()
        {
            var next = renderMode == DetectionRenderMode.EphemeralBoxes
                ? DetectionRenderMode.AnchoredTags
                : DetectionRenderMode.EphemeralBoxes;
            SetRenderMode(next);
            Debug.Log($"[QVS] Detection render mode -> {next} (X)");
        }

        /// <summary>
        /// Flip anchored tags between persistent world-pins and update-in-place
        /// tracking — bound to the B button. Only affects the anchored render module.
        /// </summary>
        public void ToggleTagBehaviour()
        {
            if (serviceManager != null &&
                serviceManager.TryGetService<IAnchoredTagRenderModule>(out var anchored))
            {
                anchored.PersistentTags = !anchored.PersistentTags;
                anchored.Clear(); // drop existing placements so the new behaviour is visible immediately
                Debug.Log($"[QVS] Anchored tag behaviour -> {(anchored.PersistentTags ? "Persistent (world-pinned)" : "Update-in-place (tracking)")} (B)");
            }
        }

        private void Update()
        {
            // Live pitch tuning: hold Y, move the left thumbstick up/down.
            var holding = tunePitchHoldAction != null && tunePitchHoldAction.IsPressed();
            if (holding)
            {
                if (poseTracking == null)
                {
                    serviceManager.TryGetService<IPoseTrackingService>(out poseTracking);
                }

                var stick = tunePitchAxisAction.ReadValue<Vector2>();
                if (poseTracking != null && Mathf.Abs(stick.y) >= 0.15f)
                {
                    // Stick up = boxes up (less down-pitch); positive pitch = boxes down.
                    var next = poseTracking.CameraPitchCompensationDegrees - stick.y * PitchTuneRateDegreesPerSecond * Time.deltaTime;
                    poseTracking.CameraPitchCompensationDegrees = Mathf.Clamp(next, -45f, 45f);
                }

                pitchTuning = true;
            }
            else if (pitchTuning)
            {
                pitchTuning = false;
                if (poseTracking != null)
                {
                    Debug.Log($"[QVS] Camera pitch compensation set to {poseTracking.CameraPitchCompensationDegrees:0.0}° — bake this into the bootstrap's 'Camera Pitch Compensation Degrees'.");
                }
            }
        }

        private void OnDestroy()
        {
            switchRenderAction?.Dispose();
            tagBehaviourAction?.Dispose();
            tunePitchHoldAction?.Dispose();
            tunePitchAxisAction?.Dispose();
            debugToggleAction?.Dispose();
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
            // --- UX settings (5) — registered first so every UX consumer can read
            // the cached settings (the training presentation service takes it as a
            // constructor dependency).
            var uxSettingsProfile = ScriptableObject.CreateInstance<UxSettingsServiceProfile>();
            uxSettingsProfile.Settings = uxSettings;
            serviceManager.TryCreateAndRegisterService<IUxSettingsService>(
                typeof(UxSettingsService), out _, "UX Settings", 5u, uxSettingsProfile);

            // --- Signaling (priority 10) ---
            var signalingProfile = ScriptableObject.CreateInstance<SignalingServiceProfile>();
            signalingProfile.RemoteConfigUrl = remoteConfigUrl;
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
            // Warm-up screen: the session negotiates eagerly (connection warmed and
            // READY behind the card), but no camera frames leave the device until
            // the user confirms with Enter / (A).
            webrtcProfile.AutoStartSession = false;
            serviceManager.TryCreateAndRegisterService<IWebRTCService>(
                typeof(WebRTCService), out var webrtcService, "WebRTC", 20u, webrtcProfile);
            serviceManager.TryCreateAndRegisterService<IAndroidWebRTCTransportModule>(
                typeof(AndroidWebRTCTransportModule), out _,
                "Android WebRTC Plugin", 0u, null, webrtcService);

            // --- Pose tracking (25) ---
            var poseProfile = ScriptableObject.CreateInstance<PoseTrackingServiceProfile>();
            poseProfile.CameraPitchCompensationDegrees = cameraPitchCompensationDegrees;
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

            // --- Training flow (45-46): state queue + presentation UX ---
            if (enableTraining)
            {
                var trainingStateProfile = ScriptableObject.CreateInstance<TrainingStateServiceProfile>();
                trainingStateProfile.Scenario = trainingScenario != null
                    ? trainingScenario
                    : Resources.Load<TrainingScenarioAsset>("EtharTrainingScenario");
                serviceManager.TryCreateAndRegisterService<ITrainingStateService>(
                    typeof(TrainingStateService), out _, "Training State", 45u, trainingStateProfile);

                var trainingPresentationProfile = ScriptableObject.CreateInstance<TrainingPresentationServiceProfile>();
                trainingPresentationProfile.ThemeIndex = trainingThemeIndex;
                trainingPresentationProfile.LabelPlacementDistanceMeters = placementDistanceMeters;
                serviceManager.TryCreateAndRegisterService<ITrainingPresentationService>(
                    typeof(TrainingPresentationService), out _, "Training Presentation", 46u, trainingPresentationProfile);
            }

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

                // Tag → detection bridge (43): republish sightings into the
                // detection pipeline as ClassName detections. Tags detect, the
                // bridge translates, the training engine decides.
                if (bridgeTagsToDetections)
                {
                    var bridgeProfile = ScriptableObject.CreateInstance<TagDetectionBridgeServiceProfile>();
                    serviceManager.TryCreateAndRegisterService<ITagDetectionBridgeService>(
                        typeof(TagDetectionBridgeService), out _, "Tag Detection Bridge", 43u, bridgeProfile);
                }

                // Training model placement (47): steps carrying a Model Ref spawn
                // their catalog prefab aligned to the step's tag (registered after
                // both the tag and training services it consumes).
                if (enableTraining)
                {
                    var modelProfile = ScriptableObject.CreateInstance<TrainingModelPlacementServiceProfile>();
                    modelProfile.Catalog = trainingModels;
                    serviceManager.TryCreateAndRegisterService<ITrainingModelPlacementService>(
                        typeof(TrainingModelPlacementService), out _, "Training Model Placement", 47u, modelProfile);
                }
            }

            // --- Status (50) ---
            serviceManager.TryCreateAndRegisterService<IStatusService>(
                typeof(StatusService), out _, "Status", 50u);
        }
    }
}
