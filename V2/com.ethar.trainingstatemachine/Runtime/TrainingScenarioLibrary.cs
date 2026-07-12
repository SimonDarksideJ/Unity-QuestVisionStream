// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;

namespace Ethar.Training
{
    /// <summary>
    /// Built-in scenarios, so the flow runs with no authored configuration. The
    /// demo mirrors <c>Training_Scenario.xlsx</c> row for row: welcome → begin →
    /// find tv → confirm → find person → confirm → finish.
    /// </summary>
    public static class TrainingScenarioLibrary
    {
        public static TrainingScenario EtharDemo()
        {
            return new TrainingScenario("Ethar Training Demo", new[]
            {
                new TrainingStep(
                    waitingClass: string.Empty,
                    title: "Welcome",
                    description: "Ready to begin your Ethar training?",
                    options: new[] { "Begin" },
                    detectedClass: string.Empty,
                    label: string.Empty,
                    imageRef: "camera",
                    result: "begintraining"),
                new TrainingStep(
                    waitingClass: "begintraining",
                    title: "Look for a Monitor",
                    description: "Search your space and locate the monitor",
                    options: new[] { "Search" },
                    detectedClass: string.Empty,
                    label: string.Empty,
                    imageRef: "camera",
                    result: "tv"),
                new TrainingStep(
                    waitingClass: "tv",
                    title: "Found TV",
                    description: "You found the TV, can you now locate a person",
                    options: new[] { "Next" },
                    detectedClass: "tv",
                    label: "This is a tv",
                    imageRef: "camera",
                    result: "foundtv"),
                new TrainingStep(
                    waitingClass: "foundtv",
                    title: string.Empty,
                    description: string.Empty,
                    options: Array.Empty<string>(),
                    detectedClass: string.Empty,
                    label: string.Empty,
                    imageRef: string.Empty,
                    result: "person"),
                new TrainingStep(
                    waitingClass: "person",
                    title: "Found Person",
                    description: "You found a person",
                    options: new[] { "Finish" },
                    detectedClass: "person",
                    label: "This is a person",
                    imageRef: "camera",
                    result: "finishtraining"),
                new TrainingStep(
                    waitingClass: "finishtraining",
                    title: "Complete",
                    description: "You have found everything and the course is complete",
                    options: new[] { "End" },
                    detectedClass: string.Empty,
                    label: string.Empty,
                    imageRef: "camera",
                    result: string.Empty)
            });
        }

        /// <summary>The demo scenario as serializable configuration data.</summary>
        public static TrainingScenarioData EtharDemoData() => TrainingScenarioData.FromScenario(EtharDemo());

        /// <summary>A complete demo machine configuration with the default confidence gate.</summary>
        public static TrainingStateMachineConfig EtharDemoConfig(float minimumDetectionConfidence = 0.5f)
            => new TrainingStateMachineConfig
            {
                Scenario = EtharDemoData(),
                MinimumDetectionConfidence = minimumDetectionConfidence
            };
    }
}
