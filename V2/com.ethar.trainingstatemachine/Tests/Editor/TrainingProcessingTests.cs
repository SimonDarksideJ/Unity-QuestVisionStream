// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using Ethar.Training;
using NUnit.Framework;

namespace Ethar.Training.Tests
{
    /// <summary>
    /// <see cref="TrainingStateMachine.ProcessClass"/> and
    /// <see cref="TrainingStateMachine.CompleteStep"/> — the checks performed on
    /// every class arrival before a new action is sent to the client, validated
    /// against the built-in demo configuration. Covers the three processing
    /// contracts: valid processing (expected class advances), ignore processing
    /// (unexpected class discarded), and result processing (form action results
    /// flow back through the same path).
    /// </summary>
    public class TrainingProcessingTests
    {
        private TrainingStateMachine machine;

        [SetUp]
        public void SetUp()
        {
            machine = new TrainingStateMachine(TrainingScenarioLibrary.EtharDemoConfig());
            machine.Begin();
        }

        // -------------------------------------------------------- valid processing

        [Test]
        public void ProcessClass_ExpectedClass_AdvancesTheQueue()
        {
            var result = machine.ProcessClass("begintraining", 1f, TrainingClassSource.ActionResponse);

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.Advanced));
            Assert.That(result.Advance, Is.Not.Null);
            Assert.That(result.Advance.StepIndex, Is.EqualTo(1));
            Assert.That(machine.CurrentStep.Title, Is.EqualTo("Look for a Monitor"));
        }

        [Test]
        public void ProcessClass_ExpectedDetection_AtOrAboveTheGate_Advances()
        {
            machine.ProcessClass("begintraining", 1f, TrainingClassSource.ActionResponse);

            var result = machine.ProcessClass("tv", 0.5f, TrainingClassSource.Detection);

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.Advanced), "confidence equal to the gate passes");
            Assert.That(machine.CurrentStep.Title, Is.EqualTo("Found TV"));
        }

        [Test]
        public void ProcessClass_FiresStepActivated_WithArrivalContext()
        {
            var activations = new List<TrainingStepActivated>();
            machine.StepActivated += payload => activations.Add(payload);

            machine.ProcessClass("begintraining", 1f, TrainingClassSource.ActionResponse);

            Assert.That(activations, Has.Count.EqualTo(1));
            Assert.That(activations[0].ArrivedClass, Is.EqualTo("begintraining"));
            Assert.That(activations[0].Source, Is.EqualTo(TrainingClassSource.ActionResponse));
            Assert.That(activations[0].StepIndex, Is.EqualTo(1));
        }

        [Test]
        public void ProcessClass_SightsTheCurrentDetectedClass_WithoutAdvancing()
        {
            machine.ProcessClass("begintraining", 1f, TrainingClassSource.ActionResponse);
            machine.ProcessClass("tv", 0.9f, TrainingClassSource.Detection);

            TrainingClassSighting sighting = null;
            machine.CurrentClassSighted += payload => sighting = payload;

            // "Found TV" is active (detectedClass tv, expecting foundtv): a tv
            // re-sighting refreshes the world label but never moves the queue.
            var result = machine.ProcessClass("tv", 0.8f, TrainingClassSource.Detection);

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.Ignored));
            Assert.That(result.SightedCurrentClass, Is.True);
            Assert.That(sighting, Is.Not.Null);
            Assert.That(sighting.ClassName, Is.EqualTo("tv"));
            Assert.That(sighting.Confidence, Is.EqualTo(0.8f));
            Assert.That(machine.CurrentStep.Title, Is.EqualTo("Found TV"));
        }

        // ------------------------------------------------------- ignore processing

        [Test]
        public void ProcessClass_UnexpectedClass_IsIgnoredAndCounted()
        {
            // Welcome is active, expecting 'begintraining' — the detector's
            // person/tv firehose must be discarded.
            var person = machine.ProcessClass("person", 0.95f, TrainingClassSource.Detection);
            var tv = machine.ProcessClass("tv", 0.95f, TrainingClassSource.Detection);

            Assert.That(person.Outcome, Is.EqualTo(TrainingProcessOutcome.Ignored));
            Assert.That(tv.Outcome, Is.EqualTo(TrainingProcessOutcome.Ignored));
            Assert.That(person.Advance, Is.Null);
            Assert.That(machine.CurrentStepIndex, Is.EqualTo(0), "ignored classes never move the queue");
            Assert.That(machine.DiscardedCount, Is.EqualTo(2));
        }

        [Test]
        public void ProcessClass_ExpectedDetection_BelowTheGate_IsDiscarded()
        {
            machine.ProcessClass("begintraining", 1f, TrainingClassSource.ActionResponse);

            var result = machine.ProcessClass("tv", 0.49f, TrainingClassSource.Detection);

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.BelowConfidence));
            Assert.That(machine.CurrentStep.Title, Is.EqualTo("Look for a Monitor"), "low-confidence detections never advance");
        }

        [Test]
        public void ProcessClass_ActionResponse_IgnoresTheConfidenceGate()
        {
            var result = machine.ProcessClass("begintraining", 0f, TrainingClassSource.ActionResponse);

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.Advanced),
                "synthetic action responses always pass the gate");
        }

        [Test]
        public void ProcessClass_WhenIdleOrCompleted_ProcessesNothing()
        {
            machine.Reset();

            var result = machine.ProcessClass("begintraining", 1f, TrainingClassSource.ActionResponse);

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.NotRunning));
            Assert.That(machine.DiscardedCount, Is.Zero, "a machine that isn't running counts nothing");
        }

        [Test]
        public void ProcessClass_IgnoredClasses_NeverFireEvents()
        {
            var fired = false;
            machine.StepActivated += _ => fired = true;
            machine.ScenarioCompleted += _ => fired = true;

            machine.ProcessClass("person", 0.95f, TrainingClassSource.Detection);

            Assert.That(fired, Is.False);
        }

        // ------------------------------------------------------- result processing

        [Test]
        public void CompleteStep_FeedsTheResultClass_ThroughTheSamePath()
        {
            var step = machine.CurrentStep;

            var result = machine.CompleteStep(new TrainingStepResult(
                machine.CurrentStepIndex, step.WaitingClass, step.Result, step.Options[0]));

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.Advanced));
            Assert.That(result.Source, Is.EqualTo(TrainingClassSource.ActionResponse));
            Assert.That(machine.CurrentStep.Title, Is.EqualTo("Look for a Monitor"));
        }

        [Test]
        public void CompleteStep_StaleStepResult_IsRejected()
        {
            var welcome = machine.CurrentStep;
            machine.ProcessClass("begintraining", 1f, TrainingClassSource.ActionResponse);

            // A press from the Welcome form arriving after the queue moved on.
            var result = machine.CompleteStep(new TrainingStepResult(0, welcome.WaitingClass, welcome.Result, "Begin"));

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.StaleStep));
            Assert.That(machine.CurrentStepIndex, Is.EqualTo(1), "stale results never move the queue");
        }

        [Test]
        public void CompleteStep_OnTheFinalStep_CompletesTheScenario()
        {
            WalkToFinalStep();

            TrainingCompletion completion = null;
            machine.ScenarioCompleted += payload => completion = payload;

            var step = machine.CurrentStep;
            var result = machine.CompleteStep(new TrainingStepResult(
                machine.CurrentStepIndex, step.WaitingClass, step.Result, "End"));

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.Completed));
            Assert.That(machine.Status, Is.EqualTo(TrainingFlowStatus.Completed));
            Assert.That(completion, Is.Not.Null);
            Assert.That(completion.ArrivedClass, Is.Empty, "the final action completes without a class");
        }

        [Test]
        public void CompleteStep_WhenNotRunning_IsRefused()
        {
            machine.Reset();

            var result = machine.CompleteStep(new TrainingStepResult(0, string.Empty, "begintraining", "Begin"));

            Assert.That(result.Outcome, Is.EqualTo(TrainingProcessOutcome.NotRunning));
        }

        [Test]
        public void FullDemoFlow_ThroughProcessClassAndCompleteStep()
        {
            var completed = false;
            machine.ScenarioCompleted += _ => completed = true;

            PressCurrentAction();                                            // Welcome → begintraining
            machine.ProcessClass("tv", 0.9f, TrainingClassSource.Detection); // → Found TV
            PressCurrentAction();                                            // → foundtv → pass-through → expecting person
            machine.ProcessClass("person", 0.9f, TrainingClassSource.Detection); // → Found Person
            PressCurrentAction();                                            // → finishtraining → Complete
            PressCurrentAction();                                            // final action → done

            Assert.That(completed, Is.True);
            Assert.That(machine.Status, Is.EqualTo(TrainingFlowStatus.Completed));
        }

        private void WalkToFinalStep()
        {
            machine.ProcessClass("begintraining", 1f, TrainingClassSource.ActionResponse);
            machine.ProcessClass("tv", 0.9f, TrainingClassSource.Detection);
            machine.ProcessClass("foundtv", 1f, TrainingClassSource.ActionResponse);
            machine.ProcessClass("person", 0.9f, TrainingClassSource.Detection);
            machine.ProcessClass("finishtraining", 1f, TrainingClassSource.ActionResponse);
            Assert.That(machine.CurrentStep.Title, Is.EqualTo("Complete"));
        }

        private void PressCurrentAction()
        {
            var step = machine.CurrentStep;
            machine.CompleteStep(new TrainingStepResult(
                machine.CurrentStepIndex, step.WaitingClass, step.Result,
                step.Options.Count > 0 ? step.Options[0] : string.Empty));
        }
    }
}
