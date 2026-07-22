// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Linq;
using Ethar.Training;
using NUnit.Framework;

namespace Ethar.Training.Tests
{
    /// <summary>
    /// Training builder — mermaid dialect, CSV import and validation report.
    /// Mirrors the Python <c>tests/test_builder.py</c>; the dialect is shared,
    /// so these tests pin cross-language compatibility of the authoring format.
    /// </summary>
    public class TrainingBuilderTests
    {
        // The demo scenario, hand-authored in the mermaid dialect.
        private const string DemoMarkdown = @"# Ethar Training Demo

```mermaid
flowchart TD
  %% minimumDetectionConfidence: 0.5
  welcome[""Welcome|Ready to begin your Ethar training?|!camera""]
  lookfor[""Look for a Monitor|Search your space and locate the monitor|!camera""]
  foundtv[""Found TV|Great - that is the monitor|@tv: This is a tv|!camera""]
  bridge["" ""]
  person[""Found Person|Now find a person|@person: This is a person|!camera""]
  done[""Complete|Training complete|!camera""]

  welcome -->|Begin @begintraining| lookfor
  lookfor -->|Search @tv| foundtv
  foundtv -->|Next @foundtv| bridge
  bridge -->|@person| person
  person -->|Finish @finishtraining| done
  done -->|End| END
```
";

        [Test]
        public void ParseMarkdown_ReadsTheFullDemoQueue()
        {
            var ok = TrainingMermaidBuilder.TryParseMarkdown(DemoMarkdown, out var scenario, out var confidence, out var report);

            Assert.That(ok, Is.True, report.ToString());
            Assert.That(report.HasErrors, Is.False);
            Assert.That(confidence, Is.EqualTo(0.5f));
            Assert.That(scenario.Name, Is.EqualTo("Ethar Training Demo"));
            Assert.That(scenario.Steps.Count, Is.EqualTo(6));

            Assert.That(scenario.Steps[0].WaitingClass, Is.Empty);
            Assert.That(scenario.Steps[0].Title, Is.EqualTo("Welcome"));
            Assert.That(scenario.Steps[0].Options, Is.EqualTo(new[] { "Begin" }));
            Assert.That(scenario.Steps[0].Result, Is.EqualTo("begintraining"));
            Assert.That(scenario.Steps[0].ImageRef, Is.EqualTo("camera"));

            Assert.That(scenario.Steps[1].WaitingClass, Is.EqualTo("begintraining"));
            Assert.That(scenario.Steps[1].Result, Is.EqualTo("tv"));

            Assert.That(scenario.Steps[2].WaitingClass, Is.EqualTo("tv"));
            Assert.That(scenario.Steps[2].DetectedClass, Is.EqualTo("tv"));
            Assert.That(scenario.Steps[2].Label, Is.EqualTo("This is a tv"));

            // Pass-through: no title/options, detection-advanced.
            Assert.That(scenario.Steps[3].WaitingClass, Is.EqualTo("foundtv"));
            Assert.That(scenario.Steps[3].HasPresentation, Is.False);
            Assert.That(scenario.Steps[3].Result, Is.EqualTo("person"));

            // Final step: edge to END = empty result.
            Assert.That(scenario.Steps[5].Options, Is.EqualTo(new[] { "End" }));
            Assert.That(scenario.Steps[5].Result, Is.Empty);
        }

        [Test]
        public void ParseMarkdown_ActionEdgeWithoutClass_MintsTheTargetNodeId()
        {
            var ok = TrainingMermaidBuilder.TryParseMarkdown(
                "# T\n```mermaid\nflowchart TD\n  a[\"A\"]\n  b[\"B\"]\n  a -->|Go| b\n  b -->|Done| END\n```",
                out var scenario, out _, out var report);

            Assert.That(ok, Is.True, report.ToString());
            Assert.That(scenario.Steps[0].Result, Is.EqualTo("b"));
            Assert.That(scenario.Steps[1].WaitingClass, Is.EqualTo("b"));
        }

        [Test]
        public void ParseMarkdown_ModelRefSegment_IsRead()
        {
            var ok = TrainingMermaidBuilder.TryParseMarkdown(
                "# T\n```mermaid\nflowchart TD\n" +
                "  a[\"Station 1|Fit the pump|@station1: Station 1|#pumpAssembly\"]\n" +
                "  a -->|Done| END\n```",
                out var scenario, out _, out var report);

            Assert.That(ok, Is.True, report.ToString());
            Assert.That(scenario.Steps[0].ModelRef, Is.EqualTo("pumpAssembly"));
            Assert.That(scenario.Steps[0].DetectedClass, Is.EqualTo("station1"));
        }

        [Test]
        public void ParseMarkdown_Branching_IsAnError()
        {
            var ok = TrainingMermaidBuilder.TryParseMarkdown(
                "# T\n```mermaid\nflowchart TD\n  a[\"A\"]\n  b[\"B\"]\n  c[\"C\"]\n  a -->|x| b\n  a -->|y| c\n```",
                out _, out _, out var report);

            Assert.That(ok, Is.False);
            Assert.That(report.HasErrors, Is.True);
            Assert.That(report.Messages.Any(m => m.Message.Contains("branches")), Is.True);
        }

        [Test]
        public void ParseMarkdown_ConflictingIncomingClasses_IsAnError()
        {
            var ok = TrainingMermaidBuilder.TryParseMarkdown(
                "# T\n```mermaid\nflowchart TD\n  a[\"A\"]\n  b[\"B\"]\n  c[\"C\"]\n  a -->|@x| c\n  b -->|@y| c\n```",
                out _, out _, out var report);

            Assert.That(ok, Is.False);
            Assert.That(report.Messages.Any(m => m.Message.Contains("different classes")), Is.True);
        }

        [Test]
        public void ParseMarkdown_MissingMermaidBlock_IsAnError()
        {
            var ok = TrainingMermaidBuilder.TryParseMarkdown(
                "# Just a heading\n\nNo diagram here.", out _, out _, out var report);

            Assert.That(ok, Is.False);
            Assert.That(report.HasErrors, Is.True);
        }

        [Test]
        public void ParseMarkdown_ExplicitWaitingOverride()
        {
            var ok = TrainingMermaidBuilder.TryParseMarkdown(
                "# T\n```mermaid\nflowchart TD\n  a[\"A\"]\n  b[\"B|?special\"]\n  a -->|@special| END\n```",
                out var scenario, out _, out var report);

            Assert.That(ok, Is.True, report.ToString());
            Assert.That(scenario.Steps[1].WaitingClass, Is.EqualTo("special"));
        }

        [Test]
        public void ToMarkdown_DemoScenario_RoundTrips()
        {
            var demo = TrainingScenarioLibrary.EtharDemo();
            var markdown = TrainingMermaidBuilder.ToMarkdown(demo, 0.5f);

            var ok = TrainingMermaidBuilder.TryParseMarkdown(markdown, out var reparsed, out var confidence, out var report);

            Assert.That(ok, Is.True, report.ToString());
            Assert.That(report.HasErrors, Is.False, report.ToString());
            Assert.That(confidence, Is.EqualTo(0.5f));
            Assert.That(reparsed.Name, Is.EqualTo(demo.Name));
            AssertScenariosEqual(reparsed, demo);
        }

        [Test]
        public void ToMarkdown_EmptyTitleWithDescription_RoundTrips()
        {
            var scenario = new TrainingScenario("T", new[]
            {
                new TrainingStep("", "A", "", new[] { "Go" }, "", "", "", "x"),
                new TrainingStep("x", "", "description only", null, "", "", "", "")
            });

            var ok = TrainingMermaidBuilder.TryParseMarkdown(
                TrainingMermaidBuilder.ToMarkdown(scenario), out var reparsed, out _, out var report);

            Assert.That(ok, Is.True, report.ToString());
            AssertScenariosEqual(reparsed, scenario);
        }

        [Test]
        public void ToMarkdown_ContainsDiagramAndTable()
        {
            var markdown = TrainingMermaidBuilder.ToMarkdown(TrainingScenarioLibrary.EtharDemo());

            Assert.That(markdown, Does.Contain("```mermaid"));
            Assert.That(markdown, Does.Contain("flowchart TD"));
            Assert.That(markdown, Does.Contain("| # | Step |"));
            Assert.That(markdown, Does.Contain("END"));
        }

        private const string DemoCsv =
            "waitingClass,title,description,options,detectedClass,label,imageRef,modelRef,result\n" +
            ",Welcome,\"Ready to begin your Ethar training?\",Begin,,,camera,,begintraining\n" +
            "begintraining,Look for a Monitor,\"Search your space and locate the monitor\",Search,,,camera,,tv\n" +
            "tv,Found TV,\"Great - that is the monitor\",Next,tv,This is a tv,camera,,foundtv\n" +
            "foundtv,,,,,,,,person\n" +
            "person,Found Person,\"Now find a person\",Finish,person,This is a person,camera,,finishtraining\n" +
            "finishtraining,Complete,\"Training complete\",End,,,camera,,\n";

        [Test]
        public void ParseCsv_ReadsTheDemoFlow()
        {
            var ok = TrainingCsvBuilder.TryParseCsv(DemoCsv, out var scenario, out var report, name: "Ethar Training Demo");

            Assert.That(ok, Is.True, report.ToString());
            Assert.That(report.HasErrors, Is.False);
            Assert.That(scenario.Steps.Count, Is.EqualTo(6));
            Assert.That(scenario.Steps[2].DetectedClass, Is.EqualTo("tv"));
            Assert.That(scenario.Steps[2].Options, Is.EqualTo(new[] { "Next" }));
            Assert.That(scenario.Steps[5].Result, Is.Empty);
        }

        [Test]
        public void ParseCsv_MultipleOptions_SplitOnSemicolon()
        {
            var ok = TrainingCsvBuilder.TryParseCsv("title,options,result\nPick,Go;Skip,x\n", out var scenario, out var report);

            Assert.That(ok, Is.True, report.ToString());
            Assert.That(scenario.Steps[0].Options, Is.EqualTo(new[] { "Go", "Skip" }));
            Assert.That(report.Messages.Any(m => m.Message.ToLowerInvariant().Contains("waitingclass")), Is.True,
                "missing waitingClass column should be reported");
        }

        [Test]
        public void ParseCsv_UnknownColumn_IsReportedNotFatal()
        {
            var ok = TrainingCsvBuilder.TryParseCsv("title,result,notes\nA,,left over from the spreadsheet\n", out _, out var report);

            Assert.That(ok, Is.True);
            Assert.That(report.Messages.Any(m => m.Message.Contains("notes")), Is.True);
        }

        [Test]
        public void ParseCsv_WithoutRecognisedColumns_IsAnError()
        {
            var ok = TrainingCsvBuilder.TryParseCsv("foo,bar\n1,2\n", out _, out var report);

            Assert.That(ok, Is.False);
            Assert.That(report.HasErrors, Is.True);
        }

        [Test]
        public void ParseCsv_ToMarkdown_RoundTrips()
        {
            Assert.That(TrainingCsvBuilder.TryParseCsv(DemoCsv, out var scenario, out _, name: "Ethar Training Demo"), Is.True);

            var ok = TrainingMermaidBuilder.TryParseMarkdown(
                TrainingMermaidBuilder.ToMarkdown(scenario), out var reparsed, out _, out var report);

            Assert.That(ok, Is.True, report.ToString());
            AssertScenariosEqual(reparsed, scenario);
        }

        [Test]
        public void Validator_DeadEndResult_IsAWarning()
        {
            var ok = TrainingCsvBuilder.TryParseCsv("title,result\nA,ghost\nB,\n", out _, out var report);

            Assert.That(ok, Is.True);
            Assert.That(report.HasWarnings, Is.True);
            Assert.That(report.Messages.Any(m => m.Message.Contains("ghost")), Is.True);
        }

        [Test]
        public void Validator_ValidDemo_HasNoWarnings()
        {
            var report = TrainingScenarioValidator.Validate(TrainingScenarioLibrary.EtharDemo());

            Assert.That(report.HasErrors, Is.False);
            Assert.That(report.HasWarnings, Is.False);
            Assert.That(report.Messages.Any(m => m.Severity == TrainingValidationSeverity.Info), Is.True);
        }

        private static void AssertScenariosEqual(TrainingScenario actual, TrainingScenario expected)
        {
            Assert.That(actual.Steps.Count, Is.EqualTo(expected.Steps.Count));
            for (var i = 0; i < expected.Steps.Count; i++)
            {
                var a = actual.Steps[i];
                var e = expected.Steps[i];
                Assert.That(a.WaitingClass, Is.EqualTo(e.WaitingClass), $"step {i + 1} waitingClass");
                Assert.That(a.Title, Is.EqualTo(e.Title), $"step {i + 1} title");
                Assert.That(a.Description, Is.EqualTo(e.Description), $"step {i + 1} description");
                Assert.That(a.Options, Is.EqualTo(e.Options), $"step {i + 1} options");
                Assert.That(a.DetectedClass, Is.EqualTo(e.DetectedClass), $"step {i + 1} detectedClass");
                Assert.That(a.Label, Is.EqualTo(e.Label), $"step {i + 1} label");
                Assert.That(a.ImageRef, Is.EqualTo(e.ImageRef), $"step {i + 1} imageRef");
                Assert.That(a.ModelRef, Is.EqualTo(e.ModelRef), $"step {i + 1} modelRef");
                Assert.That(a.Result, Is.EqualTo(e.Result), $"step {i + 1} result");
            }
        }
    }
}
