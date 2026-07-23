// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using Ethar.UXTraining;
using Ethar.UXTraining.Components;
using Ethar.UXTraining.Settings;
using Ethar.UXTraining.Theme;
using Ethar.Training;
using QuestVisionStream.Services;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Client
{
    /// <summary>Configuration for <see cref="TrainingPresentationService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Training Presentation Service Profile", fileName = "TrainingPresentationServiceProfile")]
    public class TrainingPresentationServiceProfile : BaseProfile
    {
        [SerializeField]
        [Tooltip("Built-in Ethar UX Training palette: 0 = Dark·Cyan, 1 = Light·Teal, 2 = Hi-Vis·Orange.")]
        private int themeIndex = 0;

        [SerializeField]
        [Tooltip("How far in front of the user the step form is anchored.")]
        private float formDistanceMeters = 1.25f;

        [SerializeField]
        [Tooltip("Distance along the capture-pose ray for the location indicator — keep equal to the detection boxes' placement distance so label and box coincide.")]
        private float labelPlacementDistanceMeters = 2f;

        [SerializeField]
        [Tooltip("Start the scenario automatically once streaming begins — on WebRTC connect (the moment after Enter) or the server's ready handshake, whichever lands first.")]
        private bool autoBegin = true;

        public int ThemeIndex { get => themeIndex; set => themeIndex = value; }
        public float FormDistanceMeters { get => formDistanceMeters; set => formDistanceMeters = value; }
        public float LabelPlacementDistanceMeters { get => labelPlacementDistanceMeters; set => labelPlacementDistanceMeters = value; }
        public bool AutoBegin { get => autoBegin; set => autoBegin = value; }
    }

    /// <summary>
    /// <see cref="ITrainingPresentationService"/>: receives UX requests from the
    /// training state flow and drives the com.ethar.uxtraining package's
    /// <see cref="TrainingUxController"/> — maps each step activation onto the
    /// package's presentation-only <see cref="TrainingStepView"/>, places the
    /// label + connector on the step's detected class via capture-pose
    /// unprojection, and keeps the hand menu's current-step readout fresh.
    /// Pressing an action builds the step's <see cref="TrainingStepResult"/> and
    /// feeds it back to the state service, which routes it down the detections
    /// path.
    /// </summary>
    [System.Runtime.InteropServices.Guid("7c2f4d15-6f38-4a02-9df0-58b6f6f0a9b2")]
    public sealed class TrainingPresentationService : BaseServiceWithConstructor, ITrainingPresentationService
    {
        private readonly TrainingPresentationServiceProfile profile;
        private readonly ITrainingStateService training;
        private readonly IDetectionService detections;
        private readonly IPoseTrackingService poseTracking;
        private readonly IWebRTCService webrtc;
        private readonly IUxSettingsService uxSettings;
        private readonly Dictionary<string, Texture2D> stepImageCache = new Dictionary<string, Texture2D>();
        private TrainingUxController controller;

        public TrainingPresentationService(
            string name,
            uint priority,
            TrainingPresentationServiceProfile profile,
            ITrainingStateService training,
            IDetectionService detections,
            IPoseTrackingService poseTracking,
            IWebRTCService webrtc,
            IUxSettingsService uxSettings)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.training = training ?? throw new ArgumentNullException(nameof(training));
            this.detections = detections ?? throw new ArgumentNullException(nameof(detections));
            this.poseTracking = poseTracking ?? throw new ArgumentNullException(nameof(poseTracking));
            this.webrtc = webrtc ?? throw new ArgumentNullException(nameof(webrtc));
            this.uxSettings = uxSettings ?? throw new ArgumentNullException(nameof(uxSettings));
        }

        public bool IsFormVisible => controller != null && controller.IsFormVisible;

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            var host = new GameObject("QVS_TrainingUx");
            controller = host.AddComponent<TrainingUxController>();
            controller.Initialize(
                ResolveTheme(),
                profile.FormDistanceMeters,
                TriggerOption,
                new HandMenu.Actions
                {
                    home = RestartScenario,
                    tasks = ShowCurrentStep,
                    hint = () => controller.PulseWorldLabel(),
                    redo = RestartScenario,
                    exit = ExitScenario
                },
                brand: "ETHAR TRAINING",
                uxSettings: uxSettings.Settings);
            // The state service starts first (lower priority) — its ScenarioLoaded
            // fired before this subscription, so read the loaded scenario directly.
            controller.SetHandMenuStep("TRAINING", training.Scenario != null ? training.Scenario.Name : "Waiting…");

            training.ScenarioLoaded += OnScenarioLoaded;
            training.StepActivated += OnStepActivated;
            training.CurrentClassSighted += OnCurrentClassSighted;
            training.ScenarioCompleted += OnScenarioCompleted;

            // Auto-begin the moment the user enters: the session is negotiated
            // during warm-up, so "Enter pressed" (StreamingBegan) is the normal
            // trigger; the state change and the server's ready handshake stay
            // wired for the rare orders where Enter lands before Connected.
            webrtc.StreamingBegan += OnStreamingBegan;
            webrtc.StateChanged += OnWebRTCStateChanged;
            detections.ServerReady += OnServerReady;

            // Already entered and connected (e.g. this service registered late)?
            AutoBegin();
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            training.ScenarioLoaded -= OnScenarioLoaded;
            training.StepActivated -= OnStepActivated;
            training.CurrentClassSighted -= OnCurrentClassSighted;
            training.ScenarioCompleted -= OnScenarioCompleted;
            webrtc.StreamingBegan -= OnStreamingBegan;
            webrtc.StateChanged -= OnWebRTCStateChanged;
            detections.ServerReady -= OnServerReady;

            if (controller != null)
            {
                UnityEngine.Object.Destroy(controller.gameObject);
                controller = null;
            }

            base.Destroy();
        }

        /// <inheritdoc />
        public void ShowCurrentStep()
        {
            if (training.CurrentStep == null)
            {
                return;
            }

            controller.RepresentForm();
        }

        /// <inheritdoc />
        public void TriggerOption(int optionIndex)
        {
            var step = training.CurrentStep;
            if (step == null || !IsFormVisible)
            {
                return;
            }

            var optionLabel = optionIndex >= 0 && optionIndex < step.Options.Count
                ? step.Options[optionIndex]
                : string.Empty;

            // The custom response class — fed back to the state service to notify
            // the action is complete; its result class travels the detections path.
            training.CompleteStep(new TrainingStepResult(
                training.CurrentStepIndex, step.WaitingClass, step.Result, optionLabel));
        }

        // ---------------------------------------------------------- flow events

        private void OnStreamingBegan() => AutoBegin();

        private void OnWebRTCStateChanged(WebRTCConnectionState state)
        {
            if (state == WebRTCConnectionState.Connected)
            {
                AutoBegin();
            }
        }

        private void OnServerReady() => AutoBegin();

        private void AutoBegin()
        {
            // Both gates: the user pressed Enter (frames enabled) AND the session
            // is connected. Negotiation happens during warm-up, so normally both
            // are already true the instant Enter is pressed — the welcome form
            // appears immediately, never behind the warm-up card.
            if (profile.AutoBegin &&
                training.Status == TrainingFlowStatus.Idle &&
                webrtc.StreamingRequested &&
                webrtc.State == WebRTCConnectionState.Connected)
            {
                training.Begin();
            }
        }

        private void OnScenarioLoaded(TrainingScenario scenario)
            => controller.SetHandMenuStep("TRAINING", scenario.Name);

        private void OnStepActivated(TrainingStepActivation activation)
        {
            controller.ShowStep(ToView(activation));
            controller.SetHandMenuStep(
                $"STEP {activation.StepIndex + 1}/{activation.StepCount}",
                activation.Step.HasPresentation ? activation.Step.Title : "…");

            // Location indicator: label the class that brought us here, at the
            // detected box centre unprojected through the capture-time pose.
            if (activation.Step.HasWorldLabel &&
                activation.Match != null &&
                TryGetWorldPoint(activation.Match, out var worldPoint))
            {
                controller.ShowWorldLabel(worldPoint, activation.Step.Label);
            }
            else
            {
                controller.HideWorldLabel();
            }
        }

        private void OnCurrentClassSighted(TrainingDetectionMatch match)
        {
            // Keep the indicator tracking the class while its step is active. If the
            // step advanced without a live match (e.g. begun mid-flow), place it now.
            if (!TryGetWorldPoint(match, out var worldPoint))
            {
                return;
            }

            var step = training.CurrentStep;
            if (step != null && step.HasWorldLabel)
            {
                controller.ShowWorldLabel(worldPoint, step.Label);
            }
        }

        private void OnScenarioCompleted()
        {
            controller.HideForm();
            controller.HideWorldLabel();
            controller.SetHandMenuStep("TRAINING", "Complete — HOME restarts");
        }

        private void RestartScenario()
        {
            controller.HideForm();
            controller.HideWorldLabel();
            training.ResetScenario();
            training.Begin();
        }

        private void ExitScenario()
        {
            controller.HideForm();
            controller.HideWorldLabel();
            training.ResetScenario();
            controller.SetHandMenuStep("TRAINING", "Exited — HOME restarts");
        }

        /// <summary>Map a state-service step activation onto the package's presentation-only view.</summary>
        private TrainingStepView ToView(TrainingStepActivation activation)
        {
            var step = activation.Step;
            return new TrainingStepView(
                activation.StepIndex,
                activation.StepCount,
                step.Title,
                step.Description,
                step.Options,
                step.ImageRef,
                ResolveStepImage(step.ImageRef));
        }

        /// <summary>
        /// Resolve a step's image reference to a texture. Images live in a
        /// Resources sub-folder named after the scenario configuration
        /// (<see cref="ITrainingStateService.ImageBasePath"/>) — e.g. the
        /// <c>EtharTrainingScenario</c> asset reads
        /// <c>Resources/EtharTrainingScenario/&lt;imageRef&gt;</c>. Unresolvable
        /// references return null, so the form shows no image area.
        /// </summary>
        private Texture2D ResolveStepImage(string imageRef)
        {
            if (string.IsNullOrEmpty(imageRef) || string.IsNullOrEmpty(training.ImageBasePath))
            {
                return null;
            }

            // Resources paths are extension-less — tolerate authored file names.
            var withoutExtension = System.IO.Path.ChangeExtension(imageRef, null);
            var path = $"{training.ImageBasePath}/{withoutExtension}";
            if (stepImageCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var texture = Resources.Load<Texture2D>(path);
            if (texture == null)
            {
                Debug.Log($"[QVS:Training] no UX image at Resources/{path} — the step shows no image area");
            }

            // Cache misses too, so a missing file is probed (and logged) once.
            stepImageCache[path] = texture;
            return texture;
        }

        private bool TryGetWorldPoint(TrainingDetectionMatch match, out Vector3 worldPoint)
        {
            worldPoint = default;
            if (!match.HasWorldPoint)
            {
                return false;
            }

            var snapshot = poseTracking.SnapshotForArrival(match.ArrivalTimeMs);
            if (!snapshot.HasValue)
            {
                return false;
            }

            worldPoint = snapshot.Value.UnprojectAtDistance(match.Center, profile.LabelPlacementDistanceMeters);
            return true;
        }

        private ThemePalette ResolveTheme()
        {
            var palettes = ThemeLibrary.BuildDefaults();
            var index = Mathf.Clamp(profile.ThemeIndex, 0, palettes.Count - 1);
            return palettes[index];
        }
    }
}
