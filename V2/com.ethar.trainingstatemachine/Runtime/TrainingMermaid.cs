// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Ethar.Training
{
    /// <summary>
    /// The training builder's mermaid dialect: parse a markdown document
    /// containing a mermaid flowchart into a training scenario, and generate
    /// such a document back from a scenario. A 1:1 behavioural twin of the
    /// Python <c>training_state_machine.mermaid</c> module — the dialect is the
    /// shared authoring format (full spec: Documentation/Training-Builder.md).
    ///
    /// Dialect summary: scenario name = the document's first <c># heading</c>;
    /// one node per step in first-appearance order, node text segments split on
    /// <c>|</c> — title, description, then sigil segments <c>@class: world
    /// label</c> (detectedClass + label), <c>#modelRef</c>, <c>!imageRef</c>,
    /// <c>?waitingClass</c> (explicit override). Edges carry flow: a plain
    /// label is the action button text (comma-separated for several options);
    /// an <c>@class</c> token in the label is the step's result class (without
    /// one, an action edge mints the target node's lowercased id). An edge to
    /// the <c>END</c> pseudo-node ends the scenario. Branching is not
    /// expressible in the current linear model and reports an error.
    /// </summary>
    public static class TrainingMermaidBuilder
    {
        public const string EndNodeId = "END";

        private static readonly Regex NodeToken = new Regex(
            @"^([A-Za-z_][\w-]*)\s*(?:(\(\(|\[\[|\[\(|\(\[|\[|\(|\{)\s*""?(.*?)""?\s*(\)\)|\]\]|\)\]|\]\)|\]|\)|\}))?\s*$",
            RegexOptions.Compiled);

        private static readonly Regex ClassToken = new Regex(@"@([^\s,|]+)", RegexOptions.Compiled);

        private static readonly Regex ConfidenceComment = new Regex(
            @"^%%\s*minimumDetectionConfidence\s*:\s*([0-9.]+)\s*$", RegexOptions.Compiled);

        private static readonly Regex Heading = new Regex(@"^#\s+(.+?)\s*$", RegexOptions.Compiled);

        private static readonly Regex NodeTextSigil = new Regex(@"^@([^\s:]+)\s*:?\s*(.*)$", RegexOptions.Compiled);

        private static readonly string[] SkipPrefixes =
            { "%%", "classDef", "class ", "style ", "linkStyle", "direction", "subgraph" };

        private sealed class Node
        {
            public string Id;
            public string Title = string.Empty;
            public string Description = string.Empty;
            public string DetectedClass = string.Empty;
            public string Label = string.Empty;
            public string ModelRef = string.Empty;
            public string ImageRef = string.Empty;
            public string ExplicitWaiting;
        }

        private sealed class Edge
        {
            public string Src;
            public string Dst;
            public List<string> Options;
            public string ResultClass; // null = mint from the target id
        }

        /// <summary>
        /// Parse a markdown+mermaid training document. Returns false (scenario
        /// null) when the report contains errors; <paramref name="minimumDetectionConfidence"/>
        /// carries the optional <c>%% minimumDetectionConfidence:</c> comment.
        /// </summary>
        public static bool TryParseMarkdown(
            string text,
            out TrainingScenario scenario,
            out float? minimumDetectionConfidence,
            out TrainingValidationReport report)
        {
            scenario = null;
            minimumDetectionConfidence = null;
            report = new TrainingValidationReport();

            if (string.IsNullOrEmpty(text))
            {
                report.Error("Empty document.");
                return false;
            }

            var lines = text.Replace("\r\n", "\n").Split('\n');
            var name = string.Empty;
            foreach (var line in lines)
            {
                var heading = Heading.Match(line.Trim());
                if (heading.Success)
                {
                    name = heading.Groups[1].Value;
                    break;
                }
            }

            var block = ExtractMermaidBlock(lines);
            if (block == null)
            {
                report.Error("No ```mermaid code block found in the document.");
                return false;
            }

            var nodes = new Dictionary<string, Node>();
            var order = new List<string>();
            var edges = new List<Edge>();
            var sawHeader = false;

            void Touch(string nodeId, string nodeText)
            {
                if (string.Equals(nodeId, EndNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!nodes.ContainsKey(nodeId))
                {
                    nodes[nodeId] = new Node { Id = nodeId };
                    order.Add(nodeId);
                }

                if (!string.IsNullOrEmpty(nodeText) && nodeText.Trim().Length > 0)
                {
                    ApplyText(nodes[nodeId], nodeText);
                }
            }

            foreach (var raw in block)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var confidence = ConfidenceComment.Match(line);
                if (confidence.Success)
                {
                    if (float.TryParse(confidence.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        minimumDetectionConfidence = value;
                    }
                    else
                    {
                        report.Error($"Invalid minimumDetectionConfidence value: '{confidence.Groups[1].Value}'.");
                    }

                    continue;
                }

                if (SkipPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal)) || line == "end")
                {
                    continue;
                }

                if (!sawHeader)
                {
                    if (line.StartsWith("flowchart", StringComparison.Ordinal) ||
                        line.StartsWith("graph", StringComparison.Ordinal))
                    {
                        sawHeader = true;
                        continue;
                    }

                    report.Error($"The mermaid block must start with 'flowchart' (got: '{line}').");
                    return false;
                }

                if (line.Contains("-->"))
                {
                    ParseEdgeLine(line, Touch, edges, report);
                }
                else
                {
                    var token = NodeToken.Match(line);
                    if (token.Success)
                    {
                        Touch(token.Groups[1].Value, token.Groups[3].Value);
                    }
                    else
                    {
                        report.Error($"Unrecognised mermaid line: '{line}'.");
                    }
                }
            }

            if (report.HasErrors)
            {
                return false;
            }

            if (order.Count == 0)
            {
                report.Error("The mermaid block defines no step nodes.");
                return false;
            }

            var steps = BuildSteps(nodes, order, edges, report);
            if (report.HasErrors)
            {
                return false;
            }

            scenario = new TrainingScenario(name, steps);
            TrainingScenarioValidator.Validate(scenario, report);
            return true;
        }

        /// <summary>
        /// Generate the markdown+mermaid training document for a scenario: the
        /// diagram (round-trippable through <see cref="TryParseMarkdown"/>)
        /// plus a human-readable verification table.
        /// </summary>
        public static string ToMarkdown(TrainingScenario scenario, float? minimumDetectionConfidence = null)
        {
            var steps = scenario.Steps;
            var ids = Enumerable.Range(0, steps.Count).Select(i => $"s{i + 1}").ToArray();

            // Mirror the machine's forward-only Offer: each result targets the
            // FIRST later step waiting on it.
            var targets = new int?[steps.Count];
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].Result.Length == 0)
                {
                    continue;
                }

                for (var j = i + 1; j < steps.Count; j++)
                {
                    if (string.Equals(steps[j].WaitingClass, steps[i].Result, StringComparison.OrdinalIgnoreCase))
                    {
                        targets[i] = j;
                        break;
                    }
                }
            }

            var reached = new HashSet<int>(targets.Where(t => t.HasValue).Select(t => t.Value));
            var lines = new List<string>();
            var title = scenario.Name.Length > 0 ? scenario.Name : "Training Scenario";
            lines.Add($"# {title}");
            lines.Add("");
            lines.Add("> Generated by the training builder — edit and re-import; the diagram is");
            lines.Add("> the configuration. Dialect: `Documentation/Training-Builder.md`.");
            lines.Add("");
            lines.Add("```mermaid");
            lines.Add("flowchart TD");
            if (minimumDetectionConfidence.HasValue)
            {
                lines.Add($"  %% minimumDetectionConfidence: {FormatNumber(minimumDetectionConfidence.Value)}");
            }

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var segments = new List<string> { Escape(step.Title) };
                if (step.Description.Length > 0)
                {
                    segments.Add(Escape(step.Description));
                }

                if (step.DetectedClass.Length > 0)
                {
                    segments.Add(step.Label.Length > 0
                        ? $"@{step.DetectedClass}: {Escape(step.Label)}"
                        : $"@{step.DetectedClass}");
                }

                if (step.ModelRef.Length > 0)
                {
                    segments.Add($"#{step.ModelRef}");
                }

                if (step.ImageRef.Length > 0)
                {
                    segments.Add($"!{step.ImageRef}");
                }

                if (step.WaitingClass.Length > 0 && (i == 0 || !reached.Contains(i)))
                {
                    segments.Add($"?{step.WaitingClass}");
                }

                var text = string.Join("|", segments);
                lines.Add($"  {ids[i]}[\"{(text.Trim(' ', '|').Length > 0 ? text : " ")}\"]");
            }

            lines.Add($"  {EndNodeId}([Scenario complete])");

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var labelParts = new List<string>();
                if (step.Options.Count > 0)
                {
                    labelParts.Add(string.Join(", ", step.Options));
                }

                if (step.Result.Length > 0)
                {
                    labelParts.Add($"@{step.Result}");
                }

                var label = string.Join(" ", labelParts);
                var dst = targets[i].HasValue ? ids[targets[i].Value] : EndNodeId;
                lines.Add(label.Length > 0 ? $"  {ids[i]} -->|{label}| {dst}" : $"  {ids[i]} --> {dst}");
            }

            lines.Add("```");
            lines.Add("");
            lines.Add("## Steps");
            lines.Add("");
            lines.Add("| # | Step | Waits for | Options | Result | World label | Model | Image |");
            lines.Add("|---|------|-----------|---------|--------|-------------|-------|-------|");
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var world = step.HasWorldLabel
                    ? $"`{step.DetectedClass}`: {step.Label}"
                    : step.DetectedClass.Length > 0 ? $"`{step.DetectedClass}`" : string.Empty;
                lines.Add(
                    $"| {i + 1} " +
                    $"| {(step.Title.Length > 0 ? step.Title : "*(pass-through)*")} " +
                    $"| {(step.WaitingClass.Length > 0 ? $"`{step.WaitingClass}`" : "*(begin)*")} " +
                    $"| {string.Join(", ", step.Options)} " +
                    $"| {(step.Result.Length > 0 ? $"`{step.Result}`" : "*(complete)*")} " +
                    $"| {world} " +
                    $"| {(step.ModelRef.Length > 0 ? $"`{step.ModelRef}`" : string.Empty)} " +
                    $"| {(step.ImageRef.Length > 0 ? $"`{step.ImageRef}`" : string.Empty)} |");
            }

            lines.Add("");
            return string.Join("\n", lines);
        }

        // -------------------------------------------------------- internals

        private static List<string> ExtractMermaidBlock(IReadOnlyList<string> lines)
        {
            var block = new List<string>();
            var inside = false;
            foreach (var line in lines)
            {
                var stripped = line.Trim();
                if (!inside && stripped.StartsWith("```mermaid", StringComparison.Ordinal))
                {
                    inside = true;
                    continue;
                }

                if (inside)
                {
                    if (stripped.StartsWith("```", StringComparison.Ordinal))
                    {
                        return block;
                    }

                    block.Add(line);
                }
            }

            return inside ? block : null;
        }

        private static void ApplyText(Node node, string text)
        {
            var plain = new List<string>();
            foreach (var raw in text.Split('|'))
            {
                var segment = raw.Trim();
                if (segment.Length == 0)
                {
                    // Positional: an empty leading segment keeps the title empty
                    // while a later segment carries the description.
                    plain.Add(string.Empty);
                    continue;
                }

                if (segment.StartsWith("@", StringComparison.Ordinal))
                {
                    var match = NodeTextSigil.Match(segment);
                    if (match.Success)
                    {
                        node.DetectedClass = match.Groups[1].Value;
                        node.Label = match.Groups[2].Value.Trim();
                    }
                }
                else if (segment.StartsWith("#", StringComparison.Ordinal))
                {
                    node.ModelRef = segment.Substring(1).Trim();
                }
                else if (segment.StartsWith("!", StringComparison.Ordinal))
                {
                    node.ImageRef = segment.Substring(1).Trim();
                }
                else if (segment.StartsWith("?", StringComparison.Ordinal))
                {
                    node.ExplicitWaiting = segment.Substring(1).Trim();
                }
                else
                {
                    plain.Add(segment);
                }
            }

            if (plain.Count > 0)
            {
                node.Title = plain[0];
                node.Description = plain.Count > 1
                    ? string.Join(" ", plain.Skip(1).Where(p => p.Length > 0))
                    : string.Empty;
            }
        }

        private static void ParseEdgeLine(string line, Action<string, string> touch, List<Edge> edges, TrainingValidationReport report)
        {
            var parts = Regex.Split(line, @"\s*-->\s*");
            var left = NodeToken.Match(parts[0].Trim());
            if (!left.Success)
            {
                report.Error($"Unrecognised edge source in: '{line}'.");
                return;
            }

            touch(left.Groups[1].Value, left.Groups[3].Value);
            var prev = left.Groups[1].Value;

            for (var i = 1; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                string label = null;
                if (part.StartsWith("|", StringComparison.Ordinal))
                {
                    var close = part.IndexOf('|', 1);
                    if (close < 0)
                    {
                        report.Error($"Unterminated edge label in: '{line}'.");
                        return;
                    }

                    label = part.Substring(1, close - 1);
                    part = part.Substring(close + 1).Trim();
                }

                var node = NodeToken.Match(part);
                if (!node.Success)
                {
                    report.Error($"Unrecognised edge target in: '{line}'.");
                    return;
                }

                touch(node.Groups[1].Value, node.Groups[3].Value);

                var (options, resultClass) = ParseLabel(label, report);
                edges.Add(new Edge { Src = prev, Dst = node.Groups[1].Value, Options = options, ResultClass = resultClass });
                prev = node.Groups[1].Value;
            }
        }

        private static (List<string> options, string resultClass) ParseLabel(string label, TrainingValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return (new List<string>(), null);
            }

            var classes = ClassToken.Matches(label).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            if (classes.Count > 1)
            {
                report.Error($"An edge label may carry at most one @class token (got: '{label}').");
            }

            var remainder = ClassToken.Replace(label, string.Empty).Trim();
            var options = remainder.Split(',')
                .Select(o => o.Trim())
                .Where(o => o.Length > 0)
                .ToList();
            return (options, classes.Count > 0 ? classes[0] : null);
        }

        private static TrainingStep[] BuildSteps(
            Dictionary<string, Node> nodes,
            List<string> order,
            List<Edge> edges,
            TrainingValidationReport report)
        {
            bool IsEnd(string nodeId) => string.Equals(nodeId, EndNodeId, StringComparison.OrdinalIgnoreCase);

            var steps = new TrainingStep[order.Count];
            for (var index = 0; index < order.Count; index++)
            {
                var nodeId = order[index];
                var node = nodes[nodeId];

                // --- outgoing: options + result --------------------------------
                var options = new List<string>();
                string dstSeen = null;
                string resultSeen = null;
                foreach (var edge in edges.Where(e => e.Src == nodeId))
                {
                    if (dstSeen != null && edge.Dst != dstSeen)
                    {
                        report.Error(
                            $"Node '{nodeId}' branches to both '{dstSeen}' and '{edge.Dst}' — the " +
                            "current linear model supports one target per step (per-option results " +
                            "are a planned schema extension).");
                        continue;
                    }

                    dstSeen = edge.Dst;
                    options.AddRange(edge.Options);
                    var edgeResult = edge.ResultClass ?? (IsEnd(edge.Dst) ? string.Empty : edge.Dst.ToLowerInvariant());
                    if (resultSeen != null && !string.Equals(edgeResult, resultSeen, StringComparison.OrdinalIgnoreCase))
                    {
                        report.Error(
                            $"Node '{nodeId}' has edges with conflicting result classes " +
                            $"('{resultSeen}' vs '{edgeResult}').");
                    }

                    resultSeen = edgeResult;
                }

                var result = resultSeen ?? string.Empty;

                // --- incoming: waiting class -----------------------------------
                string waiting;
                if (node.ExplicitWaiting != null)
                {
                    waiting = node.ExplicitWaiting;
                }
                else if (index == 0)
                {
                    waiting = string.Empty;
                }
                else
                {
                    waiting = string.Empty;
                    foreach (var edge in edges.Where(e => e.Dst == nodeId))
                    {
                        var edgeClass = edge.ResultClass ?? nodeId.ToLowerInvariant();
                        if (waiting.Length > 0 && !string.Equals(edgeClass, waiting, StringComparison.OrdinalIgnoreCase))
                        {
                            report.Error(
                                $"Node '{nodeId}' is reached by edges carrying different classes " +
                                $"('{waiting}' vs '{edgeClass}') — merge nodes must agree; use " +
                                "?waitingClass to override.");
                        }

                        if (waiting.Length == 0)
                        {
                            waiting = edgeClass;
                        }
                    }
                }

                steps[index] = new TrainingStep(
                    waiting, node.Title, node.Description, options,
                    node.DetectedClass, node.Label, node.ImageRef, result, node.ModelRef);
            }

            return steps;
        }

        private static string Escape(string text) => text.Replace('"', '\'').Replace('|', '/');

        private static string FormatNumber(float value)
        {
            var text = value.ToString("0.####", CultureInfo.InvariantCulture);
            return text.Length > 0 ? text : "0";
        }
    }
}
