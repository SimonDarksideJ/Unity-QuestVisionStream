// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ethar.Training
{
    /// <summary>
    /// CSV import for the training builder — accelerates spreadsheet-based
    /// authoring (the demo scenario's heritage is a Training_Scenario.xlsx).
    /// One row per step, one column per step field; <c>options</c> are
    /// <c>;</c>-separated inside their cell. Header matching is
    /// case-insensitive and ignores spaces/underscores, so "Waiting Class",
    /// "waiting_class" and "waitingClass" all resolve. A 1:1 behavioural twin
    /// of the Python <c>training_state_machine.csv_io</c> module.
    /// </summary>
    public static class TrainingCsvBuilder
    {
        public const char OptionsSeparator = ';';

        private static readonly string[] Columns =
        {
            "waitingclass", "title", "description", "options",
            "detectedclass", "label", "imageref", "modelref", "result"
        };

        /// <summary>Parse step rows from CSV. False (scenario null) when the report contains errors.</summary>
        public static bool TryParseCsv(
            string text,
            out TrainingScenario scenario,
            out TrainingValidationReport report,
            string name = "")
        {
            scenario = null;
            report = new TrainingValidationReport();

            if (string.IsNullOrWhiteSpace(text))
            {
                report.Error("Empty CSV document.");
                return false;
            }

            var rows = ReadCsv(text)
                .Where(row => row.Any(cell => cell.Trim().Length > 0))
                .ToList();
            if (rows.Count < 2)
            {
                report.Error("CSV needs a header row plus at least one step row.");
                return false;
            }

            var header = rows[0];
            var mapping = new Dictionary<string, int>();
            for (var index = 0; index < header.Count; index++)
            {
                var key = Normalize(header[index]);
                if (Columns.Contains(key))
                {
                    if (mapping.ContainsKey(key))
                    {
                        report.Warning($"Duplicate column '{header[index].Trim()}' — the first occurrence wins.");
                    }
                    else
                    {
                        mapping[key] = index;
                    }
                }
                else if (key.Length > 0)
                {
                    report.Info($"Ignoring unknown column '{header[index].Trim()}'.");
                }
            }

            if (mapping.Count == 0)
            {
                report.Error(
                    "No recognised columns in the header row. Expected any of: " +
                    "waitingClass, title, description, options, detectedClass, label, " +
                    "imageRef, modelRef, result.");
                return false;
            }

            foreach (var key in new[] { "waitingclass", "result" })
            {
                if (!mapping.ContainsKey(key))
                {
                    report.Warning($"Column '{key}' is missing — every step will default it to empty.");
                }
            }

            var steps = new List<TrainingStep>();
            for (var r = 1; r < rows.Count; r++)
            {
                var row = rows[r];

                string Cell(string key)
                    => mapping.TryGetValue(key, out var index) && index < row.Count
                        ? row[index].Trim()
                        : string.Empty;

                var options = Cell("options").Split(OptionsSeparator)
                    .Select(o => o.Trim())
                    .Where(o => o.Length > 0)
                    .ToArray();

                steps.Add(new TrainingStep(
                    Cell("waitingclass"), Cell("title"), Cell("description"), options,
                    Cell("detectedclass"), Cell("label"), Cell("imageref"),
                    Cell("result"), Cell("modelref")));
            }

            scenario = new TrainingScenario(name, steps);
            TrainingScenarioValidator.Validate(scenario, report);
            return true;
        }

        /// <summary>Minimal RFC-4180 reader: quoted fields, escaped quotes, commas and newlines inside quotes.</summary>
        private static List<List<string>> ReadCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(cell.ToString());
                        cell.Length = 0;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(cell.ToString());
                        cell.Length = 0;
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default:
                        cell.Append(c);
                        break;
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private static string Normalize(string cell)
            => cell.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
    }
}
