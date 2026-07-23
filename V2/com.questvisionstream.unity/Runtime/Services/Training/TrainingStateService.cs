// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using Ethar.Training;
using QuestVisionStream.Core;
using QuestVisionStream.Protocol;
using QuestVisionStream.Training;
using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>Configuration for <see cref="TrainingStateService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Training State Service Profile", fileName = "TrainingStateServiceProfile")]
    public class TrainingStateServiceProfile : BaseProfile
    {
        [SerializeField]
        [Tooltip("The scenario asset to run (Create → QuestVisionStream → Training Scenario). Empty falls back to the built-in Ethar demo scenario.")]
        private TrainingScenarioAsset scenario;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Detections below this confidence never advance the flow (synthetic action responses are always 1.0).")]
        private float minimumDetectionConfidence = 0.5f;

        [SerializeField]
        [Tooltip("Log every transition and discard summary, prefixed [QVS:Training] for logcat filtering.")]
        private bool verboseLogging = true;

        public TrainingScenarioAsset Scenario { get => scenario; set => scenario = value; }
        public float MinimumDetectionConfidence { get => minimumDetectionConfidence; set => minimumDetectionConfidence = value; }
        public bool VerboseLogging { get => verboseLogging; set => verboseLogging = value; }

        /// <summary>
        /// Convert the authored ScriptableObject profile into the serializable
        /// <see cref="TrainingStateMachineConfig"/> struct the engine-agnostic
        /// state machine is initialized with. An unset or empty scenario asset
        /// falls back to the built-in demo scenario.
        /// </summary>
        public TrainingStateMachineConfig ToConfig()
        {
            var scenarioData = scenario != null && scenario.Steps.Count > 0
                ? scenario.ToScenarioData()
                : TrainingScenarioLibrary.EtharDemoData();

            return new TrainingStateMachineConfig
            {
                Scenario = scenarioData,
                MinimumDetectionConfidence = minimumDetectionConfidence
            };
        }
    }

    /// <summary>
    /// <see cref="ITrainingStateService"/>: wraps the engine-agnostic
    /// <see cref="TrainingStateMachine"/> (from <c>com.ethar.trainingstatemachine</c>)
    /// and wires it to the detection pipeline. The ScriptableObject profile is
    /// converted to the machine's serializable config struct at initialization.
    /// Every detections batch flows through <see cref="ProcessBatch"/>; training
    /// form responses are converted to a wire-shaped detections payload
    /// (<see cref="TrainingResponseMessage"/>) and pushed through the SAME parser
    /// and handler, so an action press is literally a detected class arriving.
    /// </summary>
    [System.Runtime.InteropServices.Guid("3f6c1f3a-9d2e-4b8a-b5c4-2f1f4f7f6a01")]
    public class TrainingStateService : BaseServiceWithConstructor, ITrainingStateService
    {
        private readonly TrainingStateServiceProfile profile;
        private readonly IDetectionService detections;
        private readonly TrainingStateMachine machine = new TrainingStateMachine();

        public TrainingStateService(
            string name,
            uint priority,
            TrainingStateServiceProfile profile,
            IDetectionService detections)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.detections = detections ?? throw new ArgumentNullException(nameof(detections));
        }

        public event Action<TrainingScenario> ScenarioLoaded;
        public event Action<TrainingStepActivation> StepActivated;
        public event Action<TrainingDetectionMatch> CurrentClassSighted;
        public event Action ScenarioCompleted;

        public TrainingScenario Scenario => machine.Scenario;
        public TrainingFlowStatus Status => machine.Status;
        public TrainingStep CurrentStep => machine.CurrentStep;
        public int CurrentStepIndex => machine.CurrentStepIndex;
        public string ExpectedClass => machine.ExpectedClass;
        public long DiscardedCount => machine.DiscardedCount;

        /// <inheritdoc />
        public string ImageBasePath { get; private set; } = string.Empty;

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            // The detection service is always sending — the queue filter below is
            // what turns the firehose into an authoritative flow.
            detections.DetectionsReceived += OnDetectionsReceived;

            if (machine.Scenario == null)
            {
                LoadConfiguredScenario();
            }
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            detections.DetectionsReceived -= OnDetectionsReceived;
            base.Destroy();
        }

        /// <inheritdoc />
        public void LoadScenario(TrainingScenario scenario)
        {
            machine.Load(scenario);
            Log($"scenario '{scenario.Name}' loaded ({scenario.Steps.Count} steps)");
            ScenarioLoaded?.Invoke(scenario);
        }

        /// <inheritdoc />
        public bool LoadScenarioJson(string json)
        {
            if (!TrainingScenarioParser.TryParse(json, out var scenario))
            {
                Debug.LogWarning("[QVS:Training] scenario JSON rejected — keeping the current scenario");
                return false;
            }

            LoadScenario(scenario);
            return true;
        }

        /// <inheritdoc />
        public void Begin()
        {
            if (machine.Scenario == null)
            {
                LoadConfiguredScenario();
            }

            if (!machine.Begin())
            {
                Debug.LogWarning("[QVS:Training] Begin() with no scenario loaded");
                return;
            }

            Log($"begin — step 1/{machine.Scenario.Steps.Count} '{machine.CurrentStep.Title}', expecting '{machine.ExpectedClass}'");
            StepActivated?.Invoke(new TrainingStepActivation(
                machine.CurrentStep, machine.CurrentStepIndex, machine.Scenario.Steps.Count, match: null));
        }

        /// <inheritdoc />
        public void CompleteStep(TrainingStepResult result)
        {
            if (result == null || !machine.IsRunning)
            {
                return;
            }

            if (result.StepIndex != machine.CurrentStepIndex)
            {
                Debug.LogWarning($"[QVS:Training] stale step result for step {result.StepIndex} ignored (current {machine.CurrentStepIndex})");
                return;
            }

            if (result.ResultClass.Length == 0)
            {
                // Final step — there is no class to flow, the action itself completes.
                var advance = machine.CompleteScenario(TrainingClassSource.ActionResponse);
                if (advance != null)
                {
                    Log($"scenario complete (option '{result.OptionLabel}')");
                    ScenarioCompleted?.Invoke();
                }

                return;
            }

            // The training response follows the same path as a detection message:
            // wire-shaped JSON through the channel parser into the batch handler.
            var json = TrainingResponseMessage.ToDetectionsJson(result.ResultClass);
            if (DetectionChannelParser.Parse(json, out var payload) != DetectionChannelMessageKind.Detections)
            {
                Debug.LogError($"[QVS:Training] synthetic response payload failed to parse: {json}");
                return;
            }

            Log($"action '{result.OptionLabel}' → response class '{result.ResultClass}'");
            ProcessBatch(DetectionMath.ToRenderBatch(payload), arrivalTimeMs: -1, TrainingClassSource.ActionResponse);
        }

        /// <inheritdoc />
        public void ResetScenario()
        {
            machine.Reset();
            Log("reset to idle");
        }

        private void OnDetectionsReceived(DetectionArrival arrival)
            => ProcessBatch(arrival.Batch, arrival.ArrivalTimeMs,
                arrival.Origin == DetectionOrigin.AprilTag
                    ? TrainingClassSource.AprilTag
                    : TrainingClassSource.Detection);

        /// <summary>
        /// The single class-arrival handler — real detections and synthetic action
        /// responses both land here. Each detection is offered to the state
        /// machine, whose hot path is one cached-string comparison; the machine's
        /// result report is enriched with the detection's geometry for the
        /// presentation events.
        /// </summary>
        private void ProcessBatch(RenderBatch batch, double arrivalTimeMs, TrainingClassSource source)
        {
            if (!machine.IsRunning)
            {
                return;
            }

            foreach (var detection in batch.Detections)
            {
                var result = machine.ProcessClass(detection.Label, detection.Conf, source);

                // Keep the world label tracking the active step's annotated class.
                if (result.SightedCurrentClass)
                {
                    CurrentClassSighted?.Invoke(new TrainingDetectionMatch(
                        detection.Label, detection.Conf, detection.Center, detection.Rect, arrivalTimeMs, source));
                }

                if (result.Outcome == TrainingProcessOutcome.Completed)
                {
                    Log($"scenario complete on '{result.Advance.ArrivedClass}' ({source})");
                    ScenarioCompleted?.Invoke();
                    return;
                }

                if (result.Outcome != TrainingProcessOutcome.Advanced)
                {
                    continue;
                }

                var advance = result.Advance;
                var match = new TrainingDetectionMatch(
                    detection.Label, detection.Conf, detection.Center, detection.Rect, arrivalTimeMs, source);
                Log($"'{advance.ArrivedClass}' ({source}) → step {advance.StepIndex + 1}/{machine.Scenario.Steps.Count} " +
                    $"'{advance.Step.Title}', expecting '{machine.ExpectedClass}', discarded so far {machine.DiscardedCount}");
                StepActivated?.Invoke(new TrainingStepActivation(
                    advance.Step, advance.StepIndex, machine.Scenario.Steps.Count, match));

                // One transition per batch — the rest of the frame's detections
                // were computed against the previous state.
                return;
            }
        }

        private void LoadConfiguredScenario()
        {
            if (profile.Scenario != null && profile.Scenario.Steps.Count == 0)
            {
                Debug.LogWarning($"[QVS:Training] '{profile.Scenario.name}' has no steps — falling back to the built-in demo scenario");
            }

            // Step images live in a Resources sub-folder named after the scenario
            // asset (empty for the built-in demo fallback — no images).
            ImageBasePath = profile.Scenario != null && profile.Scenario.Steps.Count > 0
                ? profile.Scenario.name
                : string.Empty;

            // ScriptableObject profile → serializable config struct → machine.
            machine.Initialize(profile.ToConfig());
            Log($"scenario '{machine.Scenario.Name}' loaded ({machine.Scenario.Steps.Count} steps)");
            ScenarioLoaded?.Invoke(machine.Scenario);
        }

        private void Log(string message)
        {
            if (profile.VerboseLogging)
            {
                Debug.Log($"[QVS:Training] {message}");
            }
        }
    }
}
