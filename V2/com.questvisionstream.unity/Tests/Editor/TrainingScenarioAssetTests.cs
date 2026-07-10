// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using NUnit.Framework;
using QuestVisionStream.Training;
using UnityEngine;

namespace QuestVisionStream.Tests
{
    public class TrainingScenarioAssetTests
    {
        private TrainingScenarioAsset asset;

        [SetUp]
        public void SetUp() => asset = ScriptableObject.CreateInstance<TrainingScenarioAsset>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(asset);

        [Test]
        public void ToScenario_SnapshotsTheEditableSteps()
        {
            asset.ScenarioName = "Asset Demo";
            asset.Steps.Add(new TrainingStepDefinition
            {
                title = "Welcome",
                description = "Ready?",
                options = { "Begin" },
                imageRef = "camera",
                result = "begintraining"
            });
            asset.Steps.Add(new TrainingStepDefinition
            {
                waitingClass = "begintraining",
                title = "Find the TV",
                options = { "Search" },
                detectedClass = "tv",
                label = "This is a tv",
                result = ""
            });

            var scenario = asset.ToScenario();

            Assert.That(scenario.Name, Is.EqualTo("Asset Demo"));
            Assert.That(scenario.Steps.Count, Is.EqualTo(2));
            Assert.That(scenario.Steps[0].Result, Is.EqualTo("begintraining"));
            Assert.That(scenario.Steps[0].Options, Is.EqualTo(new[] { "Begin" }));
            Assert.That(scenario.Steps[1].WaitingClass, Is.EqualTo("begintraining"));
            Assert.That(scenario.Steps[1].HasWorldLabel, Is.True);

            // The snapshot is detached — later asset edits must not leak into it.
            asset.Steps[0].options.Add("Skip");
            Assert.That(scenario.Steps[0].Options.Count, Is.EqualTo(1));
        }

        [Test]
        public void FromScenario_RoundTripsThroughTheWireFormat()
        {
            var demo = TrainingScenarioLibrary.EtharDemo();

            asset.FromScenario(demo);
            var roundTripped = asset.ToScenario();

            Assert.That(roundTripped.Name, Is.EqualTo(demo.Name));
            Assert.That(roundTripped.Steps.Count, Is.EqualTo(demo.Steps.Count));
            for (var i = 0; i < demo.Steps.Count; i++)
            {
                Assert.That(roundTripped.Steps[i].WaitingClass, Is.EqualTo(demo.Steps[i].WaitingClass));
                Assert.That(roundTripped.Steps[i].Title, Is.EqualTo(demo.Steps[i].Title));
                Assert.That(roundTripped.Steps[i].Options, Is.EqualTo(demo.Steps[i].Options));
                Assert.That(roundTripped.Steps[i].DetectedClass, Is.EqualTo(demo.Steps[i].DetectedClass));
                Assert.That(roundTripped.Steps[i].Label, Is.EqualTo(demo.Steps[i].Label));
                Assert.That(roundTripped.Steps[i].ImageRef, Is.EqualTo(demo.Steps[i].ImageRef));
                Assert.That(roundTripped.Steps[i].Result, Is.EqualTo(demo.Steps[i].Result));
            }

            // And the asset's snapshot still serializes to valid wire JSON.
            var json = TrainingScenarioParser.ToJson(roundTripped);
            Assert.That(TrainingScenarioParser.TryParse(json, out _), Is.True);
        }
    }
}
