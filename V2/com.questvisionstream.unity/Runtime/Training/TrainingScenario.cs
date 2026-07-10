// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace QuestVisionStream.Training
{
    /// <summary>
    /// One entry in a training scenario's queue of expected states.
    ///
    /// A step ACTIVATES when its <see cref="WaitingClass"/> arrives on the class
    /// pipeline — either a real detection from the server ("tv", "person") or a
    /// synthetic class produced by pressing an action on the previous step's form
    /// ("begintraining", "foundtv"). While active, the step's <see cref="Result"/>
    /// is the single statically-cached class the state machine waits for next.
    /// </summary>
    public sealed class TrainingStep
    {
        public TrainingStep(
            string waitingClass,
            string title,
            string description,
            IReadOnlyList<string> options,
            string detectedClass,
            string label,
            string imageRef,
            string result)
        {
            WaitingClass = waitingClass ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Options = options ?? Array.Empty<string>();
            DetectedClass = detectedClass ?? string.Empty;
            Label = label ?? string.Empty;
            ImageRef = imageRef ?? string.Empty;
            Result = result ?? string.Empty;
        }

        /// <summary>The class whose arrival activates this step. Empty on the entry step (activated by Begin).</summary>
        public string WaitingClass { get; }

        /// <summary>Form headline. A step with no title and no options is a pass-through: no UX is shown.</summary>
        public string Title { get; }

        /// <summary>Form body text.</summary>
        public string Description { get; }

        /// <summary>Action labels. Pressing any option sends <see cref="Result"/> down the class pipeline (default one).</summary>
        public IReadOnlyList<string> Options { get; }

        /// <summary>Detection class to annotate in the world while this step is active (empty = none).</summary>
        public string DetectedClass { get; }

        /// <summary>Text for the world label + connector placed at the detected box centre.</summary>
        public string Label { get; }

        /// <summary>Client-side image reference for the form (a shared placeholder image for now).</summary>
        public string ImageRef { get; }

        /// <summary>
        /// The next expected class. Arrives either as a real detection or as the
        /// synthetic "detected class from pressing an action". Empty on the final
        /// step — pressing its action completes the scenario.
        /// </summary>
        public string Result { get; }

        /// <summary>False for pass-through steps (e.g. "foundtv") that advance without showing a form.</summary>
        public bool HasPresentation => Title.Length > 0 || Options.Count > 0;

        /// <summary>True when this step places a world label on a detection.</summary>
        public bool HasWorldLabel => DetectedClass.Length > 0 && Label.Length > 0;
    }

    /// <summary>An ordered queue of <see cref="TrainingStep"/>s — one procedure at a time.</summary>
    public sealed class TrainingScenario
    {
        public TrainingScenario(string name, IReadOnlyList<TrainingStep> steps)
        {
            Name = name ?? string.Empty;
            Steps = steps ?? Array.Empty<TrainingStep>();
        }

        public string Name { get; }

        public IReadOnlyList<TrainingStep> Steps { get; }
    }

    /// <summary>
    /// Parser + serializer for the scenario queue's JSON format. Tolerant of
    /// missing fields (they default to empty — the wire rule used across the
    /// project), strict about the overall shape: a scenario must carry at least
    /// one step, and every step must be an object.
    /// </summary>
    public static class TrainingScenarioParser
    {
        /// <summary>Parse scenario JSON. Returns false (scenario null) on malformed input.</summary>
        public static bool TryParse(string json, out TrainingScenario scenario)
        {
            scenario = null;

            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                return false;
            }

            if (!(root["steps"] is JArray stepsArray) || stepsArray.Count == 0)
            {
                return false;
            }

            var steps = new List<TrainingStep>(stepsArray.Count);
            foreach (var item in stepsArray)
            {
                if (!(item is JObject step))
                {
                    return false;
                }

                var options = new List<string>();
                if (step["options"] is JArray optionsArray)
                {
                    foreach (var option in optionsArray)
                    {
                        if (option.Type == JTokenType.String)
                        {
                            options.Add(option.Value<string>());
                        }
                    }
                }

                steps.Add(new TrainingStep(
                    step.Value<string>("waitingClass"),
                    step.Value<string>("title"),
                    step.Value<string>("description"),
                    options,
                    step.Value<string>("detectedClass"),
                    step.Value<string>("label"),
                    step.Value<string>("imageRef"),
                    step.Value<string>("result")));
            }

            scenario = new TrainingScenario(root.Value<string>("name"), steps);
            return true;
        }

        /// <summary>Serialize a scenario back to its JSON queue format.</summary>
        public static string ToJson(TrainingScenario scenario, bool indented = true)
        {
            var stepsArray = new JArray();
            foreach (var step in scenario.Steps)
            {
                var options = new JArray();
                foreach (var option in step.Options)
                {
                    options.Add(option);
                }

                stepsArray.Add(new JObject
                {
                    ["waitingClass"] = step.WaitingClass,
                    ["title"] = step.Title,
                    ["description"] = step.Description,
                    ["options"] = options,
                    ["detectedClass"] = step.DetectedClass,
                    ["label"] = step.Label,
                    ["imageRef"] = step.ImageRef,
                    ["result"] = step.Result
                });
            }

            var root = new JObject
            {
                ["name"] = scenario.Name,
                ["steps"] = stepsArray
            };

            return root.ToString(indented ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None);
        }
    }

    /// <summary>
    /// Built-in scenarios, so the flow runs with no authored JSON asset. The demo
    /// mirrors <c>Training_Scenario.xlsx</c> row for row: welcome → begin → find
    /// tv → confirm → find person → confirm → finish.
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
    }
}
