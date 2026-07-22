// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace QuestVisionStream.Services
{
    /// <summary>One model catalog entry: a scenario <c>modelRef</c> key and the prefab it resolves to.</summary>
    [Serializable]
    public sealed class TrainingModelEntry
    {
        [Tooltip("The scenario step's Model Ref key this entry resolves.")]
        public string modelRef = string.Empty;

        [Tooltip("Prefab instantiated aligned to the step's tag when the step activates.")]
        public GameObject prefab;
    }

    /// <summary>Configuration for <see cref="TrainingModelPlacementService"/>.</summary>
    [CreateAssetMenu(menuName = "QuestVisionStream/Training Model Placement Service Profile", fileName = "TrainingModelPlacementServiceProfile")]
    public class TrainingModelPlacementServiceProfile : RealityCollective.ServiceFramework.Definitions.BaseProfile
    {
        [SerializeField]
        [Tooltip("The host's model catalog: maps scenario Model Ref keys to prefabs.")]
        private List<TrainingModelEntry> catalog = new List<TrainingModelEntry>();

        [SerializeField]
        [Tooltip("Despawn a step's model when the flow advances past it. OFF keeps station models standing until completion/restart — the usual training-line reading.")]
        private bool despawnOnStepAdvance = false;

        [SerializeField]
        [Tooltip("Despawn all models when the scenario completes.")]
        private bool despawnOnCompletion = true;

        [SerializeField]
        [Tooltip("Position smoothing time (s) for the tag-aligned follower.")]
        private float positionSmoothTime = 0.15f;

        [SerializeField]
        [Tooltip("Rotation lerp speed for the tag-aligned follower.")]
        private float rotationLerpSpeed = 12f;

        [SerializeField]
        [Tooltip("Hide the model while its tag is unseen past the TTL. OFF freezes it at the last pose (default).")]
        private bool hideWhenTagLost = false;

        public List<TrainingModelEntry> Catalog { get => catalog; set => catalog = value; }
        public bool DespawnOnStepAdvance { get => despawnOnStepAdvance; set => despawnOnStepAdvance = value; }
        public bool DespawnOnCompletion { get => despawnOnCompletion; set => despawnOnCompletion = value; }
        public float PositionSmoothTime { get => positionSmoothTime; set => positionSmoothTime = value; }
        public float RotationLerpSpeed { get => rotationLerpSpeed; set => rotationLerpSpeed = value; }
        public bool HideWhenTagLost { get => hideWhenTagLost; set => hideWhenTagLost = value; }
    }

    /// <summary>
    /// <see cref="ITrainingModelPlacementService"/>: listens to the training
    /// engine's step activations; when the active step carries a
    /// <c>modelRef</c>, resolves the prefab from the catalog and spawns it
    /// aligned to the AprilTag whose class name matches the step's target class
    /// — immediately if the tag is already tracked, otherwise the moment the tag
    /// enters view. Alignment is continuous via <see cref="TagPoseFollower"/>.
    /// </summary>
    [System.Runtime.InteropServices.Guid("9d47a6f1-83b2-4e6c-a1f5-0c6b2d9e8f13")]
    public class TrainingModelPlacementService : BaseServiceWithConstructor, ITrainingModelPlacementService
    {
        private sealed class SpawnedModel
        {
            public int StepIndex;
            public GameObject Root;
        }

        private readonly TrainingModelPlacementServiceProfile profile;
        private readonly ITrainingStateService training;
        private readonly ITagRoutingService routing;
        private readonly List<SpawnedModel> spawned = new List<SpawnedModel>();
        private GameObject container;

        // The active step's unfulfilled model request, armed until its tag enters.
        private int pendingStepIndex = -1;
        private string pendingModelRef;
        private string pendingClassName;

        public TrainingModelPlacementService(
            string name,
            uint priority,
            TrainingModelPlacementServiceProfile profile,
            ITrainingStateService training,
            ITagRoutingService routing)
            : base(name, priority)
        {
            this.profile = profile != null ? profile : throw new ArgumentNullException(nameof(profile));
            this.training = training ?? throw new ArgumentNullException(nameof(training));
            this.routing = routing ?? throw new ArgumentNullException(nameof(routing));
        }

        public int ActiveModelCount => spawned.Count;

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();
            training.StepActivated += OnStepActivated;
            training.ScenarioLoaded += OnScenarioLoaded;
            training.ScenarioCompleted += OnScenarioCompleted;
            routing.TagEntered += OnTagSeen;
            routing.TagUpdated += OnTagSeen;
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            training.StepActivated -= OnStepActivated;
            training.ScenarioLoaded -= OnScenarioLoaded;
            training.ScenarioCompleted -= OnScenarioCompleted;
            routing.TagEntered -= OnTagSeen;
            routing.TagUpdated -= OnTagSeen;

            ClearModels();

            if (container != null)
            {
                UnityEngine.Object.Destroy(container);
                container = null;
            }

            base.Destroy();
        }

        /// <inheritdoc />
        public void ClearModels()
        {
            foreach (var model in spawned)
            {
                if (model.Root != null)
                {
                    UnityEngine.Object.Destroy(model.Root);
                }
            }

            spawned.Clear();
            ClearPending();
        }

        private void OnScenarioLoaded(Ethar.Training.TrainingScenario scenario) => ClearModels();

        private void OnScenarioCompleted()
        {
            ClearPending();
            if (profile.DespawnOnCompletion)
            {
                ClearModels();
            }
        }

        private void OnStepActivated(TrainingStepActivation activation)
        {
            // A restart (Begin on step 0 with no triggering arrival) starts a
            // fresh run — models from the previous run come down.
            if (activation.StepIndex == 0 && activation.Match == null)
            {
                ClearModels();
            }
            else
            {
                ClearPending();
                if (profile.DespawnOnStepAdvance)
                {
                    DespawnBefore(activation.StepIndex);
                }
            }

            var step = activation.Step;
            if (step == null || !step.HasModel)
            {
                return;
            }

            // ENGINE DECIDED: this step wants a model. The alignment target is the
            // tag standing for the step's annotated class (or its waiting class).
            var targetClass = step.DetectedClass.Length > 0 ? step.DetectedClass : step.WaitingClass;
            if (targetClass.Length == 0)
            {
                Debug.LogWarning($"[QVS:TrainingModels] step {activation.StepIndex + 1} has modelRef '{step.ModelRef}' but no Detected/Waiting Class to align to — skipped");
                return;
            }

            // Tag already in view? Spawn now; otherwise arm and wait for it.
            foreach (var observation in routing.TrackedTags)
            {
                if (MatchesClass(observation, targetClass))
                {
                    Spawn(activation.StepIndex, step.ModelRef, observation);
                    return;
                }
            }

            pendingStepIndex = activation.StepIndex;
            pendingModelRef = step.ModelRef;
            pendingClassName = targetClass;
        }

        private void OnTagSeen(TagObservation observation)
        {
            if (pendingStepIndex < 0 || !MatchesClass(observation, pendingClassName))
            {
                return;
            }

            var stepIndex = pendingStepIndex;
            var modelRef = pendingModelRef;
            ClearPending();
            Spawn(stepIndex, modelRef, observation);
        }

        private void Spawn(int stepIndex, string modelRef, TagObservation observation)
        {
            var prefab = Resolve(modelRef);
            if (prefab == null)
            {
                Debug.LogWarning($"[QVS:TrainingModels] modelRef '{modelRef}' is not in the model catalog — nothing spawned");
                return;
            }

            if (container == null)
            {
                container = new GameObject("QVS_TrainingModels");
            }

            var root = UnityEngine.Object.Instantiate(prefab, container.transform);
            root.name = $"TrainingModel_{modelRef}_{observation.ClassName}";
            root.AddComponent<TagPoseFollower>().Bind(
                routing, observation,
                profile.PositionSmoothTime, profile.RotationLerpSpeed, profile.HideWhenTagLost);

            spawned.Add(new SpawnedModel { StepIndex = stepIndex, Root = root });
            Debug.Log($"[QVS:TrainingModels] spawned '{modelRef}' on tag '{observation.ClassName}' (#{observation.Id}) for step {stepIndex + 1}");
        }

        private void DespawnBefore(int stepIndex)
        {
            for (var i = spawned.Count - 1; i >= 0; i--)
            {
                if (spawned[i].StepIndex >= stepIndex)
                {
                    continue;
                }

                if (spawned[i].Root != null)
                {
                    UnityEngine.Object.Destroy(spawned[i].Root);
                }

                spawned.RemoveAt(i);
            }
        }

        private void ClearPending()
        {
            pendingStepIndex = -1;
            pendingModelRef = null;
            pendingClassName = null;
        }

        private GameObject Resolve(string modelRef)
        {
            foreach (var entry in profile.Catalog)
            {
                if (entry != null &&
                    entry.prefab != null &&
                    string.Equals(entry.modelRef, modelRef, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.prefab;
                }
            }

            return null;
        }

        private static bool MatchesClass(TagObservation observation, string className) =>
            !string.IsNullOrEmpty(observation.ClassName) &&
            string.Equals(observation.ClassName, className, StringComparison.OrdinalIgnoreCase);
    }
}
