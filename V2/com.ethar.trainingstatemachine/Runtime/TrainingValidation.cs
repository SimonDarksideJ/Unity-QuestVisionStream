// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ethar.Training
{
    /// <summary>Severity of a <see cref="TrainingValidationMessage"/>.</summary>
    public enum TrainingValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>One validation finding.</summary>
    public sealed class TrainingValidationMessage
    {
        public TrainingValidationMessage(TrainingValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message ?? string.Empty;
        }

        public TrainingValidationSeverity Severity { get; }

        public string Message { get; }

        public override string ToString() => $"[{Severity.ToString().ToUpperInvariant()}] {Message}";
    }

    /// <summary>
    /// An ordered list of findings. Errors mean the input could not produce a
    /// usable scenario; warnings mean the scenario loads but the chain has
    /// holes; info lines aid verification. 1:1 twin of the Python
    /// <c>ValidationReport</c>.
    /// </summary>
    public sealed class TrainingValidationReport
    {
        private readonly List<TrainingValidationMessage> messages = new List<TrainingValidationMessage>();

        public IReadOnlyList<TrainingValidationMessage> Messages => messages;

        public bool HasErrors => messages.Any(m => m.Severity == TrainingValidationSeverity.Error);

        public bool HasWarnings => messages.Any(m => m.Severity == TrainingValidationSeverity.Warning);

        public void Error(string message) => messages.Add(new TrainingValidationMessage(TrainingValidationSeverity.Error, message));

        public void Warning(string message) => messages.Add(new TrainingValidationMessage(TrainingValidationSeverity.Warning, message));

        public void Info(string message) => messages.Add(new TrainingValidationMessage(TrainingValidationSeverity.Info, message));

        public override string ToString()
        {
            if (messages.Count == 0)
            {
                return "No findings.";
            }

            var builder = new StringBuilder();
            for (var i = 0; i < messages.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(messages[i]);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Scenario validation: static chain-rule checks mirroring the machine's
    /// forward-only <see cref="TrainingStateMachine.Offer"/> walk, so authoring
    /// mistakes surface in a report instead of on device. The Unity inspector
    /// and the builder both run these; the Python port is behaviourally
    /// identical.
    /// </summary>
    public static class TrainingScenarioValidator
    {
        /// <summary>Validate the chain: unreachable steps, early completion, dead-end results, plus the resolved queue as info.</summary>
        public static TrainingValidationReport Validate(TrainingScenario scenario, TrainingValidationReport report = null)
        {
            report = report ?? new TrainingValidationReport();
            var steps = scenario?.Steps ?? Array.Empty<TrainingStep>();

            if (steps.Count == 0)
            {
                report.Warning("Scenario has no steps.");
                return report;
            }

            if (steps[0].WaitingClass.Length > 0)
            {
                report.Warning(
                    $"Step 1 waits for '{steps[0].WaitingClass}', but the entry step is " +
                    "activated by begin — its waiting class is ignored.");
            }

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var stepName = step.Title.Length > 0 ? step.Title : $"step {i + 1}";

                if (i > 0 && step.WaitingClass.Length == 0)
                {
                    report.Warning($"'{stepName}' has no waiting class — no arrival can ever activate it.");
                }

                if (step.Result.Length == 0)
                {
                    if (i < steps.Count - 1)
                    {
                        report.Warning(
                            $"'{stepName}' has an empty result — pressing its action completes the " +
                            $"scenario, so the {steps.Count - 1 - i} step(s) after it are unreachable.");
                    }

                    continue;
                }

                var resolved = false;
                for (var j = i + 1; j < steps.Count; j++)
                {
                    if (string.Equals(steps[j].WaitingClass, step.Result, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = true;
                        break;
                    }
                }

                if (!resolved)
                {
                    var message =
                        $"'{stepName}' expects '{step.Result}', which matches no later step's " +
                        "waiting class — its arrival will end the scenario there.";
                    if (i < steps.Count - 1)
                    {
                        report.Warning(message);
                    }
                    else
                    {
                        report.Info(message);
                    }
                }
            }

            var queue = string.Join(" → ", steps.Select(step => step.Result.Length > 0 ? step.Result : "complete"));
            report.Info($"Expected class queue: begin → {queue}.");
            return report;
        }
    }
}
