// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Linq;
using NUnit.Framework;

namespace Ethar.Training.Tests
{
    /// <summary>
    /// The shared chain-rule validator — the same checks the Unity inspector
    /// shows live and the Python builder prints in its CLI report
    /// (behavioural twin: <c>training_state_machine.validation</c>).
    /// </summary>
    public class TrainingValidatorTests
    {
        [Test]
        public void ValidDemo_HasNoWarnings_AndReportsTheQueue()
        {
            var report = TrainingScenarioValidator.Validate(TrainingScenarioLibrary.EtharDemo());

            Assert.That(report.HasErrors, Is.False);
            Assert.That(report.HasWarnings, Is.False);
            Assert.That(report.Messages.Any(m =>
                m.Severity == TrainingValidationSeverity.Info &&
                m.Message.Contains("begin → begintraining → tv")), Is.True);
        }

        [Test]
        public void DeadEndResult_IsAWarning()
        {
            var scenario = new TrainingScenario("T", new[]
            {
                new TrainingStep("", "A", "", new[] { "Go" }, "", "", "", "ghost"),
                new TrainingStep("x", "B", "", null, "", "", "", "")
            });

            var report = TrainingScenarioValidator.Validate(scenario);

            Assert.That(report.HasWarnings, Is.True);
            Assert.That(report.Messages.Any(m => m.Message.Contains("ghost")), Is.True);
        }

        [Test]
        public void EmptyResultMidQueue_WarnsEarlyCompletion()
        {
            var scenario = new TrainingScenario("T", new[]
            {
                new TrainingStep("", "A", "", new[] { "Go" }, "", "", "", ""),
                new TrainingStep("x", "B", "", null, "", "", "", "")
            });

            var report = TrainingScenarioValidator.Validate(scenario);

            Assert.That(report.Messages.Any(m =>
                m.Severity == TrainingValidationSeverity.Warning &&
                m.Message.Contains("unreachable")), Is.True);
        }

        [Test]
        public void NonEntryStepWithoutWaitingClass_WarnsUnreachable()
        {
            var scenario = new TrainingScenario("T", new[]
            {
                new TrainingStep("", "A", "", new[] { "Go" }, "", "", "", "x"),
                new TrainingStep("", "B", "", null, "", "", "", "")
            });

            var report = TrainingScenarioValidator.Validate(scenario);

            Assert.That(report.Messages.Any(m =>
                m.Severity == TrainingValidationSeverity.Warning &&
                m.Message.Contains("no arrival can ever activate")), Is.True);
        }

        [Test]
        public void EmptyScenario_IsAWarningNotAnError()
        {
            var report = TrainingScenarioValidator.Validate(new TrainingScenario("T", null));

            Assert.That(report.HasErrors, Is.False);
            Assert.That(report.HasWarnings, Is.True);
        }
    }
}
