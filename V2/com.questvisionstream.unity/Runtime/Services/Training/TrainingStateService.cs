// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
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
        [Tooltip("Scenario queue in JSON format (see Documentation/Training-Flow.md). Empty falls back to the built-in Ethar demo scenario.")]
        private TextAsset scenarioJson;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Detections below this confidence never advance the flow (synthetic action responses are always 1.0).")]
        private float minimumDetectionConfidence = 0.5f;

        [SerializeField]
        [Tooltip("Log every transition and discard summary, prefixed [QVS:Training] for logcat filtering.")]
        private bool verboseLogging = true;

        public TextAsset ScenarioJson { get => scenarioJson; set => scenarioJson = value; }
        public float MinimumDetectionConfidence { get => minimumDetectionConfidence; set => minimumDetectionConfidence = value; }
        public bool VerboseLogging { get => verboseLogging; set => verboseLogging = value; }
    }

    /// <summary>
    /// <see cref="ITrainingStateService"/>: wraps the pure
    /// <see cref="TrainingStateMachine"/> and wires it to the detection pipeline.
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
        public long DiscardedCount { get; private set; }

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
            => ProcessBatch(arrival.Batch, arrival.ArrivalTimeMs, TrainingClassSource.Detection);

        /// <summary>
        /// The single class-arrival handler — real detections and synthetic action
        /// responses both land here. The hot path is one cached-string comparison
        /// per detection; everything that doesn't match the expected state (or the
        /// active step's annotated class) is discarded without touching the table.
        /// </summary>
        private void ProcessBatch(RenderBatch batch, double arrivalTimeMs, TrainingClassSource source)
        {
            if (!machine.IsRunning)
            {
                return;
            }

            var minConf = source == TrainingClassSource.Detection ? profile.MinimumDetectionConfidence : 0f;

            foreach (var detection in batch.Detections)
            {
                if (detection.Conf < minConf)
                {
                    continue;
                }

                // Keep the world label tracking the active step's annotated class.
                if (machine.MatchesCurrentDetectedClass(detection.Label))
                {
                    CurrentClassSighted?.Invoke(new TrainingDetectionMatch(
                        detection.Label, detection.Conf, detection.Center, detection.Rect, arrivalTimeMs, source));
                }

                if (!machine.MatchesExpected(detection.Label))
                {
                    DiscardedCount++;
                    continue;
                }

                var match = new TrainingDetectionMatch(
                    detection.Label, detection.Conf, detection.Center, detection.Rect, arrivalTimeMs, source);
                var advance = machine.Offer(detection.Label, source);
                if (advance == null)
                {
                    DiscardedCount++;
                    continue;
                }

                if (advance.Completed)
                {
                    Log($"scenario complete on '{advance.ArrivedClass}' ({source})");
                    ScenarioCompleted?.Invoke();
                    return;
                }

                Log($"'{advance.ArrivedClass}' ({source}) → step {advance.StepIndex + 1}/{machine.Scenario.Steps.Count} " +
                    $"'{advance.Step.Title}', expecting '{machine.ExpectedClass}', discarded so far {DiscardedCount}");
                StepActivated?.Invoke(new TrainingStepActivation(
                    advance.Step, advance.StepIndex, machine.Scenario.Steps.Count, match));

                // One transition per batch — the rest of the frame's detections
                // were computed against the previous state.
                return;
            }
        }

        private void LoadConfiguredScenario()
        {
            if (profile.ScenarioJson != null && LoadScenarioJson(profile.ScenarioJson.text))
            {
                return;
            }

            if (profile.ScenarioJson != null)
            {
                Debug.LogWarning($"[QVS:Training] '{profile.ScenarioJson.name}' rejected — falling back to the built-in demo scenario");
            }

            LoadScenario(TrainingScenarioLibrary.EtharDemo());
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
