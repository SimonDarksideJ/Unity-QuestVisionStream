// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Ethar.Training
{
    /// <summary>
    /// Parser + serializer for the scenario queue's JSON format, and for the
    /// serializable <see cref="TrainingStateMachineConfig"/> struct that wraps it.
    /// Tolerant of missing fields (they default to empty — the wire rule used
    /// across the project), strict about the overall shape: a scenario must carry
    /// at least one step, and every step must be an object.
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

            return TryReadScenario(root, out scenario);
        }

        /// <summary>Serialize a scenario back to its JSON queue format.</summary>
        public static string ToJson(TrainingScenario scenario, bool indented = true)
            => WriteScenario(scenario).ToString(indented ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None);

        /// <summary>
        /// Parse a full machine configuration: <c>{ "minimumDetectionConfidence": 0.5,
        /// "scenario": { … } }</c>. Returns false (config default) on malformed input.
        /// </summary>
        public static bool TryParseConfig(string json, out TrainingStateMachineConfig config)
        {
            config = default;

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

            if (!(root["scenario"] is JObject scenarioObject) || !TryReadScenario(scenarioObject, out var scenario))
            {
                return false;
            }

            config = new TrainingStateMachineConfig
            {
                Scenario = TrainingScenarioData.FromScenario(scenario),
                MinimumDetectionConfidence = root.Value<float?>("minimumDetectionConfidence") ?? 0f
            };
            return true;
        }

        /// <summary>Serialize a machine configuration struct to JSON.</summary>
        public static string ConfigToJson(TrainingStateMachineConfig config, bool indented = true)
        {
            var root = new JObject
            {
                ["minimumDetectionConfidence"] = config.MinimumDetectionConfidence,
                ["scenario"] = WriteScenario(config.Scenario.ToScenario())
            };

            return root.ToString(indented ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None);
        }

        private static bool TryReadScenario(JObject root, out TrainingScenario scenario)
        {
            scenario = null;

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
                    step.Value<string>("result"),
                    step.Value<string>("modelRef")));
            }

            scenario = new TrainingScenario(root.Value<string>("name"), steps);
            return true;
        }

        private static JObject WriteScenario(TrainingScenario scenario)
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
                    ["result"] = step.Result,
                    ["modelRef"] = step.ModelRef
                });
            }

            return new JObject
            {
                ["name"] = scenario.Name,
                ["steps"] = stepsArray
            };
        }
    }
}
