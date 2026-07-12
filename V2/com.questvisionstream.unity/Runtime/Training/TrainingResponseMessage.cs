// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using Newtonsoft.Json.Linq;

namespace QuestVisionStream.Training
{
    /// <summary>
    /// Builds the synthetic detections-channel message for a training response, so
    /// action results follow the SAME path as server detections: the JSON below is
    /// run through <see cref="Protocol.DetectionChannelParser"/> and the normal
    /// batch handler, exactly like a data-channel payload.
    ///
    /// The training flow itself (state machine, scenario model, configuration)
    /// lives in the engine-agnostic <c>com.ethar.trainingstatemachine</c> package
    /// (<c>Ethar.Training</c>); this type stays here because it is specific to the
    /// QuestVisionStream wire protocol.
    /// </summary>
    public static class TrainingResponseMessage
    {
        /// <summary>Frame marker distinguishing synthetic response payloads from server frames.</summary>
        public const int SyntheticFrame = -1;

        /// <summary>
        /// A wire-shaped detections payload carrying the result class as a single
        /// full-confidence, centre-of-frame detection.
        /// </summary>
        public static string ToDetectionsJson(string resultClass)
        {
            var root = new JObject
            {
                ["type"] = "detections",
                ["frame"] = SyntheticFrame,
                ["width"] = 2,
                ["height"] = 2,
                ["detections"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = resultClass ?? string.Empty,
                        ["conf"] = 1.0,
                        ["bbox"] = new JArray { 1, 1, 1, 1 }
                    }
                }
            };

            return root.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
