// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using Ethar.Training;
using NUnit.Framework;

namespace Ethar.Training.Tests
{
    /// <summary>
    /// Core queue behaviour, validated against the built-in demo configuration
    /// (the current test configuration): welcome → begin → find tv → confirm →
    /// find person → confirm → finish.
    /// </summary>
    public class TrainingStateMachineTests
    {
        private TrainingStateMachine machine;

        [SetUp]
        public void SetUp()
        {
            machine = new TrainingStateMachine(TrainingScenarioLibrary.EtharDemoConfig());
        }

        [Test]
        public void Initialize_FromConfigStruct_LoadsScenarioAndConfidenceGate()
        {
            Assert.That(machine.Scenario, Is.Not.Null);
            Assert.That(machine.Scenario.Name, Is.EqualTo("Ethar Training Demo"));
            Assert.That(machine.Scenario.Steps.Count, Is.EqualTo(6));
            Assert.That(machine.MinimumDetectionConfidence, Is.EqualTo(0.5f));
        }

        [Test]
        public void Initialize_ClampsConfidenceGate_AndDefaultsNullArrays()
        {
            var config = new TrainingStateMachineConfig
            {
                Scenario = new TrainingScenarioData
                {
                    Name = null,
                    Steps = new[] { new TrainingStepData { Title = "Only", Options = null } }
                },
                MinimumDetectionConfidence = 3f
            };

            machine.Initialize(config);

            Assert.That(machine.MinimumDetectionConfidence, Is.EqualTo(1f), "confidence clamped to 0..1");
            Assert.That(machine.Scenario.Name, Is.Empty, "null strings default to empty");
            Assert.That(machine.Scenario.Steps[0].Options, Is.Empty, "null arrays default to empty");
        }

        [Test]
        public void Load_IsIdle_NothingExpected()
        {
            Assert.That(machine.Status, Is.EqualTo(TrainingFlowStatus.Idle));
            Assert.That(machine.CurrentStep, Is.Null);
            Assert.That(machine.ExpectedClass, Is.Empty);
            Assert.That(machine.Offer("tv", TrainingClassSource.Detection), Is.Null, "idle machine discards everything");
        }

        [Test]
        public void Begin_ActivatesWelcome_CachesExpectedClass()
        {
            Assert.That(machine.Begin(), Is.True);

            Assert.That(machine.Status, Is.EqualTo(TrainingFlowStatus.Running));
            Assert.That(machine.CurrentStepIndex, Is.EqualTo(0));
            Assert.That(machine.CurrentStep.Title, Is.EqualTo("Welcome"));
            Assert.That(machine.ExpectedClass, Is.EqualTo("begintraining"), "next expected state statically cached");
        }

        [Test]
        public void Begin_FiresStepActivated_ForTheEntryStep()
        {
            TrainingStepActivated activated = null;
            machine.StepActivated += payload => activated = payload;

            machine.Begin();

            Assert.That(activated, Is.Not.Null);
            Assert.That(activated.StepIndex, Is.EqualTo(0));
            Assert.That(activated.StepCount, Is.EqualTo(6));
            Assert.That(activated.ArrivedClass, Is.Empty, "the entry step is activated by Begin, not a class");
        }

        [Test]
        public void Offer_NonExpectedClass_IsDiscarded()
        {
            machine.Begin();

            // The detector will happily report persons and tvs before Begin is pressed.
            Assert.That(machine.Offer("person", TrainingClassSource.Detection), Is.Null);
            Assert.That(machine.Offer("tv", TrainingClassSource.Detection), Is.Null);
            Assert.That(machine.CurrentStepIndex, Is.EqualTo(0), "discards never move the queue");
        }

        [Test]
        public void FullDemoFlow_WalksTheQueueToCompletion()
        {
            machine.Begin();

            // 1. Welcome → Begin (action response).
            var advance = machine.Offer("begintraining", TrainingClassSource.ActionResponse);
            Assert.That(advance.StepIndex, Is.EqualTo(1));
            Assert.That(advance.Step.Title, Is.EqualTo("Look for a Monitor"));
            Assert.That(machine.ExpectedClass, Is.EqualTo("tv"));

            // 2. Search → tv detected by the server.
            advance = machine.Offer("tv", TrainingClassSource.Detection);
            Assert.That(advance.Step.Title, Is.EqualTo("Found TV"));
            Assert.That(machine.CurrentDetectedClass, Is.EqualTo("tv"), "world label tracks the found class");
            Assert.That(machine.ExpectedClass, Is.EqualTo("foundtv"));

            // Repeat sightings of tv are discarded while waiting for the user confirm.
            Assert.That(machine.Offer("tv", TrainingClassSource.Detection), Is.Null);

            // 3. User confirms → foundtv activates the pass-through step.
            advance = machine.Offer("foundtv", TrainingClassSource.ActionResponse);
            Assert.That(advance.Step.HasPresentation, Is.False, "no UX action, move next");
            Assert.That(machine.ExpectedClass, Is.EqualTo("person"), "immediately waiting on the next detection");
            Assert.That(machine.CurrentDetectedClass, Is.Empty,
                "the tv indicator unlinks when its dialog progresses — re-sightings can't resurrect it");

            // 4. person detected.
            advance = machine.Offer("person", TrainingClassSource.Detection);
            Assert.That(advance.Step.Title, Is.EqualTo("Found Person"));
            Assert.That(machine.ExpectedClass, Is.EqualTo("finishtraining"));

            // 5. User confirms → finish dialog.
            advance = machine.Offer("finishtraining", TrainingClassSource.ActionResponse);
            Assert.That(advance.Step.Title, Is.EqualTo("Complete"));
            Assert.That(machine.ExpectedClass, Is.Empty, "final step has no result class");

            // 6. End — the action itself completes the scenario.
            var completed = machine.CompleteScenario(TrainingClassSource.ActionResponse);
            Assert.That(completed.Completed, Is.True);
            Assert.That(machine.Status, Is.EqualTo(TrainingFlowStatus.Completed));
            Assert.That(machine.Offer("tv", TrainingClassSource.Detection), Is.Null, "completed machine discards everything");
        }

        [Test]
        public void Offer_ClassComparison_IsCaseInsensitive()
        {
            machine.Begin();
            machine.Offer("BeginTraining", TrainingClassSource.ActionResponse);

            var advance = machine.Offer("TV", TrainingClassSource.Detection);
            Assert.That(advance, Is.Not.Null);
            Assert.That(advance.Step.Title, Is.EqualTo("Found TV"));
        }

        [Test]
        public void CompleteScenario_OnANonFinalStep_IsRefused()
        {
            machine.Begin();
            Assert.That(machine.CompleteScenario(TrainingClassSource.ActionResponse), Is.Null,
                "a step with a result class must advance through Offer");
            Assert.That(machine.Status, Is.EqualTo(TrainingFlowStatus.Running));
        }

        [Test]
        public void Reset_ReturnsToIdle_KeepsScenarioAndConfiguration()
        {
            machine.Begin();
            machine.Offer("begintraining", TrainingClassSource.ActionResponse);

            machine.Reset();

            Assert.That(machine.Status, Is.EqualTo(TrainingFlowStatus.Idle));
            Assert.That(machine.Scenario, Is.Not.Null);
            Assert.That(machine.MinimumDetectionConfidence, Is.EqualTo(0.5f), "the confidence gate survives a reset");
            Assert.That(machine.Begin(), Is.True, "restart from the welcome step");
            Assert.That(machine.CurrentStepIndex, Is.EqualTo(0));
        }

        [Test]
        public void Load_FiresScenarioLoaded_AndResets()
        {
            TrainingScenario loaded = null;
            machine.ScenarioLoaded += scenario => loaded = scenario;
            machine.Begin();

            machine.Load(TrainingScenarioLibrary.EtharDemo());

            Assert.That(loaded, Is.Not.Null);
            Assert.That(machine.Status, Is.EqualTo(TrainingFlowStatus.Idle));
        }
    }
}
