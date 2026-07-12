// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using Ethar.Training;
using NUnit.Framework;

namespace Ethar.Training.Tests
{
    public class TrainingScenarioParserTests
    {
        // Mirrors the built-in demo configuration (Training_Scenario.xlsx).
        private const string ScenarioJson = @"{
            ""name"": ""Ethar Training Demo"",
            ""steps"": [
                { ""waitingClass"": """", ""title"": ""Welcome"", ""description"": ""Ready to begin your Ethar training?"", ""options"": [""Begin""], ""imageRef"": ""camera"", ""result"": ""begintraining"" },
                { ""waitingClass"": ""begintraining"", ""title"": ""Look for a Monitor"", ""description"": ""Search your space and locate the monitor"", ""options"": [""Search""], ""result"": ""tv"" },
                { ""waitingClass"": ""tv"", ""title"": ""Found TV"", ""options"": [""Next""], ""detectedClass"": ""tv"", ""label"": ""This is a tv"", ""result"": ""foundtv"" },
                { ""waitingClass"": ""foundtv"", ""result"": ""person"" },
                { ""waitingClass"": ""person"", ""title"": ""Found Person"", ""options"": [""Finish""], ""detectedClass"": ""person"", ""label"": ""This is a person"", ""result"": ""finishtraining"" },
                { ""waitingClass"": ""finishtraining"", ""title"": ""Complete"", ""options"": [""End""], ""result"": """" }
            ]
        }";

        [Test]
        public void Parse_ValidScenario_ReadsQueueInOrder()
        {
            Assert.That(TrainingScenarioParser.TryParse(ScenarioJson, out var scenario), Is.True);
            Assert.That(scenario.Name, Is.EqualTo("Ethar Training Demo"));
            Assert.That(scenario.Steps.Count, Is.EqualTo(6));

            Assert.That(scenario.Steps[0].WaitingClass, Is.Empty);
            Assert.That(scenario.Steps[0].Result, Is.EqualTo("begintraining"));
            Assert.That(scenario.Steps[0].Options, Is.EqualTo(new[] { "Begin" }));

            Assert.That(scenario.Steps[2].DetectedClass, Is.EqualTo("tv"));
            Assert.That(scenario.Steps[2].Label, Is.EqualTo("This is a tv"));
            Assert.That(scenario.Steps[2].HasWorldLabel, Is.True);
        }

        [Test]
        public void Parse_MissingFields_DefaultToEmpty_PassThroughStepHasNoPresentation()
        {
            TrainingScenarioParser.TryParse(ScenarioJson, out var scenario);
            var passThrough = scenario.Steps[3];

            Assert.That(passThrough.WaitingClass, Is.EqualTo("foundtv"));
            Assert.That(passThrough.Title, Is.Empty);
            Assert.That(passThrough.Options, Is.Empty);
            Assert.That(passThrough.HasPresentation, Is.False);
            Assert.That(passThrough.Result, Is.EqualTo("person"));
        }

        [Test]
        public void Parse_QueueChains_EachResultIsTheNextWaitingClass()
        {
            TrainingScenarioParser.TryParse(ScenarioJson, out var scenario);
            for (var i = 0; i < scenario.Steps.Count - 1; i++)
            {
                Assert.That(scenario.Steps[i + 1].WaitingClass, Is.EqualTo(scenario.Steps[i].Result),
                    $"step {i} result should activate step {i + 1}");
            }

            Assert.That(scenario.Steps[scenario.Steps.Count - 1].Result, Is.Empty, "final step completes the scenario");
        }

        [Test]
        public void Parse_MalformedInput_IsRejected()
        {
            Assert.That(TrainingScenarioParser.TryParse(null, out _), Is.False);
            Assert.That(TrainingScenarioParser.TryParse("", out _), Is.False);
            Assert.That(TrainingScenarioParser.TryParse("not json", out _), Is.False);
            Assert.That(TrainingScenarioParser.TryParse("{\"name\":\"x\"}", out _), Is.False, "no steps");
            Assert.That(TrainingScenarioParser.TryParse("{\"steps\":[]}", out _), Is.False, "empty steps");
            Assert.That(TrainingScenarioParser.TryParse("{\"steps\":[42]}", out _), Is.False, "step not an object");
        }

        [Test]
        public void ToJson_RoundTripsTheQueue()
        {
            TrainingScenarioParser.TryParse(ScenarioJson, out var original);
            var json = TrainingScenarioParser.ToJson(original);

            Assert.That(TrainingScenarioParser.TryParse(json, out var reparsed), Is.True);
            Assert.That(reparsed.Name, Is.EqualTo(original.Name));
            Assert.That(reparsed.Steps.Count, Is.EqualTo(original.Steps.Count));
            for (var i = 0; i < original.Steps.Count; i++)
            {
                Assert.That(reparsed.Steps[i].WaitingClass, Is.EqualTo(original.Steps[i].WaitingClass));
                Assert.That(reparsed.Steps[i].Title, Is.EqualTo(original.Steps[i].Title));
                Assert.That(reparsed.Steps[i].Options, Is.EqualTo(original.Steps[i].Options));
                Assert.That(reparsed.Steps[i].DetectedClass, Is.EqualTo(original.Steps[i].DetectedClass));
                Assert.That(reparsed.Steps[i].Label, Is.EqualTo(original.Steps[i].Label));
                Assert.That(reparsed.Steps[i].Result, Is.EqualTo(original.Steps[i].Result));
            }
        }

        [Test]
        public void ConfigToJson_RoundTripsTheSerializableConfigStruct()
        {
            var config = TrainingScenarioLibrary.EtharDemoConfig(0.65f);

            var json = TrainingScenarioParser.ConfigToJson(config);

            Assert.That(TrainingScenarioParser.TryParseConfig(json, out var reparsed), Is.True);
            Assert.That(reparsed.MinimumDetectionConfidence, Is.EqualTo(0.65f));
            Assert.That(reparsed.Scenario.Name, Is.EqualTo("Ethar Training Demo"));
            Assert.That(reparsed.Scenario.Steps.Length, Is.EqualTo(6));
            Assert.That(reparsed.Scenario.Steps[2].DetectedClass, Is.EqualTo("tv"));

            // And a machine initialized from the round-tripped struct runs the flow.
            var machine = new TrainingStateMachine(reparsed);
            Assert.That(machine.Begin(), Is.True);
            Assert.That(machine.ExpectedClass, Is.EqualTo("begintraining"));
        }

        [Test]
        public void ParseConfig_MalformedInput_IsRejected()
        {
            Assert.That(TrainingScenarioParser.TryParseConfig(null, out _), Is.False);
            Assert.That(TrainingScenarioParser.TryParseConfig("not json", out _), Is.False);
            Assert.That(TrainingScenarioParser.TryParseConfig("{}", out _), Is.False, "no scenario");
            Assert.That(TrainingScenarioParser.TryParseConfig("{\"scenario\":{\"steps\":[]}}", out _), Is.False, "empty steps");
        }

        [Test]
        public void Library_EtharDemo_MatchesTheExcelFlow()
        {
            var demo = TrainingScenarioLibrary.EtharDemo();

            Assert.That(demo.Steps.Count, Is.EqualTo(6));
            Assert.That(demo.Steps[0].Title, Is.EqualTo("Welcome"));
            Assert.That(demo.Steps[1].Result, Is.EqualTo("tv"));
            Assert.That(demo.Steps[3].HasPresentation, Is.False);
            Assert.That(demo.Steps[4].DetectedClass, Is.EqualTo("person"));
            Assert.That(demo.Steps[5].Result, Is.Empty);
        }
    }
}
