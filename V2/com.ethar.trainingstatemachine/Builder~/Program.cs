// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Globalization;
using System.IO;
using Ethar.Training;

namespace Ethar.Training.Builder
{
    /// <summary>
    /// Training builder CLI — convert between the training configuration
    /// formats, with a validation report on every run. The C# twin of the
    /// Python package's <c>builder.py</c>: same commands, same flags, same
    /// exit codes, same dialect (Documentation/Training-Builder.md).
    ///
    ///   dotnet run -- md2json  flow.md       [-o scenario.json] [--config] [--confidence 0.5]
    ///   dotnet run -- json2md  scenario.json [-o flow.md]
    ///   dotnet run -- csv2md   steps.csv     [-o flow.md]      [--name "My Scenario"]
    ///   dotnet run -- csv2json steps.csv     [-o scenario.json] [--name "My Scenario"] [--config] [--confidence 0.5]
    ///
    /// Exit codes: 0 = OK (warnings allowed), 2 = validation errors (nothing written).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string command = null, input = null, output = null, name = "";
            var emitConfig = false;
            float? confidence = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o":
                    case "--output":
                        output = Next(args, ref i, "-o/--output");
                        break;
                    case "--name":
                        name = Next(args, ref i, "--name");
                        break;
                    case "--config":
                        emitConfig = true;
                        break;
                    case "--confidence":
                        confidence = float.Parse(Next(args, ref i, "--confidence"), CultureInfo.InvariantCulture);
                        break;
                    case "-h":
                    case "--help":
                        return Usage(0);
                    default:
                        if (command == null) { command = args[i]; }
                        else if (input == null) { input = args[i]; }
                        else { return Usage(1, $"Unexpected argument '{args[i]}'."); }
                        break;
                }
            }

            if (command == null || input == null)
            {
                return Usage(1, "A command and an input file are required.");
            }

            if (!File.Exists(input))
            {
                Console.Error.WriteLine($"[ERROR] Input file not found: {input}");
                return 2;
            }

            var text = File.ReadAllText(input);
            string result;

            switch (command)
            {
                case "md2json":
                {
                    var ok = TrainingMermaidBuilder.TryParseMarkdown(text, out var scenario, out var parsedConfidence, out var report);
                    PrintReport(report);
                    if (!ok)
                    {
                        return 2;
                    }

                    result = ToJson(scenario, emitConfig, confidence ?? parsedConfidence);
                    break;
                }

                case "json2md":
                {
                    TrainingScenario scenario;
                    float? parsedConfidence = null;
                    if (TrainingScenarioParser.TryParseConfig(text, out var config))
                    {
                        scenario = config.Scenario.ToScenario();
                        parsedConfidence = config.MinimumDetectionConfidence;
                    }
                    else if (!TrainingScenarioParser.TryParse(text, out scenario))
                    {
                        Console.Error.WriteLine("[ERROR] Input is neither a scenario nor a machine config JSON.");
                        return 2;
                    }

                    result = TrainingMermaidBuilder.ToMarkdown(scenario, parsedConfidence);
                    break;
                }

                case "csv2md":
                case "csv2json":
                {
                    var ok = TrainingCsvBuilder.TryParseCsv(text, out var scenario, out var report, name);
                    PrintReport(report);
                    if (!ok)
                    {
                        return 2;
                    }

                    result = command == "csv2md"
                        ? TrainingMermaidBuilder.ToMarkdown(scenario, confidence)
                        : ToJson(scenario, emitConfig, confidence);
                    break;
                }

                default:
                    return Usage(1, $"Unknown command '{command}'.");
            }

            if (output != null)
            {
                File.WriteAllText(output, result.EndsWith("\n", StringComparison.Ordinal) ? result : result + "\n");
                Console.Error.WriteLine($"Wrote {output}");
            }
            else
            {
                Console.WriteLine(result);
            }

            return 0;
        }

        private static string ToJson(TrainingScenario scenario, bool emitConfig, float? confidence)
        {
            if (emitConfig || confidence.HasValue)
            {
                var config = new TrainingStateMachineConfig
                {
                    Scenario = TrainingScenarioData.FromScenario(scenario),
                    MinimumDetectionConfidence = confidence ?? 0f
                };
                return TrainingScenarioParser.ConfigToJson(config);
            }

            return TrainingScenarioParser.ToJson(scenario);
        }

        private static void PrintReport(TrainingValidationReport report)
        {
            foreach (var message in report.Messages)
            {
                Console.Error.WriteLine(message.ToString());
            }
        }

        private static string Next(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"{flag} needs a value.");
            }

            return args[++i];
        }

        private static int Usage(int code, string error = null)
        {
            if (error != null)
            {
                Console.Error.WriteLine($"[ERROR] {error}");
            }

            Console.Error.WriteLine(
                "Training builder: mermaid/CSV/JSON conversions (dialect: Documentation/Training-Builder.md).\n\n" +
                "  ethar-training-builder md2json  flow.md       [-o scenario.json] [--config] [--confidence 0.5]\n" +
                "  ethar-training-builder json2md  scenario.json [-o flow.md]\n" +
                "  ethar-training-builder csv2md   steps.csv     [-o flow.md]       [--name \"My Scenario\"]\n" +
                "  ethar-training-builder csv2json steps.csv     [-o scenario.json] [--name \"My Scenario\"] [--config] [--confidence 0.5]\n\n" +
                "Exit codes: 0 = OK (warnings allowed), 2 = validation errors (nothing written).");
            return code;
        }
    }
}
