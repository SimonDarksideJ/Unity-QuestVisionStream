// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using NUnit.Framework;
using QuestVisionStream.Core;
using UnityEngine;

namespace QuestVisionStream.Tests
{
    public class LatencyEstimatorTests
    {
        // 90 kHz clock: 90 units per ms.
        private static long PtsForMs(double ms) => (long)(ms * 90);

        [Test]
        public void NoObservations_ReturnsBase()
        {
            Assert.That(new LatencyEstimator(200).LatencyMs(), Is.EqualTo(200));
        }

        [Test]
        public void ConstantOffset_ReturnsBase()
        {
            var estimator = new LatencyEstimator(200);
            for (var i = 0; i < 10; i++)
            {
                // Arrival is always pts + 150ms — smooth flow, offset constant.
                estimator.Observe(1000 + i * 100, PtsForMs(850 + i * 100));
            }

            Assert.That(estimator.LatencyMs(), Is.EqualTo(200).Within(1e-6));
        }

        [Test]
        public void QueuingSpike_RaisesEstimate()
        {
            var estimator = new LatencyEstimator(200);
            estimator.Observe(1000, PtsForMs(850));
            // This frame queued 300ms behind slow inference.
            estimator.Observe(1400, PtsForMs(950));

            Assert.That(estimator.LatencyMs(), Is.EqualTo(500).Within(1e-6));
        }

        [Test]
        public void Recovery_ReturnsToBase()
        {
            var estimator = new LatencyEstimator(200);
            estimator.Observe(1000, PtsForMs(850));
            estimator.Observe(1400, PtsForMs(950)); // spike
            estimator.Observe(1500, PtsForMs(1350)); // recovered

            Assert.That(estimator.LatencyMs(), Is.EqualTo(200).Within(1e-6));
        }

        [Test]
        public void WindowEviction_ReanchorsTheMinimum()
        {
            var estimator = new LatencyEstimator(200, windowMs: 1000);
            estimator.Observe(1000, PtsForMs(850)); // offset 150
            // 2s later — the first observation left the window; drift becomes the new anchor.
            estimator.Observe(3000, PtsForMs(2750)); // offset 250

            Assert.That(estimator.LatencyMs(), Is.EqualTo(200).Within(1e-6));
        }

        [Test]
        public void NullPts_IsIgnored()
        {
            var estimator = new LatencyEstimator(200);
            estimator.Observe(1000, null);

            Assert.That(estimator.LatencyMs(), Is.EqualTo(200));
        }
    }

    public class PoseHistoryTests
    {
        [Test]
        public void Lookup_FindsNearestSnapshot()
        {
            var history = new PoseHistory();
            history.Record(100, Matrix4x4.Translate(new Vector3(1, 0, 0)), Matrix4x4.identity);
            history.Record(200, Matrix4x4.Translate(new Vector3(2, 0, 0)), Matrix4x4.identity);
            history.Record(300, Matrix4x4.Translate(new Vector3(3, 0, 0)), Matrix4x4.identity);

            var snapshot = history.Lookup(190);

            Assert.That(snapshot.HasValue, Is.True);
            Assert.That(snapshot.Value.Origin.x, Is.EqualTo(2f).Within(1e-5));
        }

        [Test]
        public void Record_EvictsBeyondMaxAge()
        {
            var history = new PoseHistory(maxAgeMs: 1000);
            history.Record(0, Matrix4x4.identity, Matrix4x4.identity);
            history.Record(2000, Matrix4x4.identity, Matrix4x4.identity);

            Assert.That(history.Count, Is.EqualTo(1));
        }

        [Test]
        public void Lookup_Empty_ReturnsNull()
        {
            Assert.That(new PoseHistory().Lookup(0), Is.Null);
        }

        [Test]
        public void ViewportCenter_RaysStraightAhead()
        {
            // Camera at origin, looking down -Z is Unity camera view space; use a real
            // projection so the unprojection round-trips through perspective divide.
            var projection = Matrix4x4.Perspective(60, 4f / 3f, 0.1f, 100f);
            var snapshot = new CameraPoseSnapshot(0, Matrix4x4.identity, projection.inverse);

            var ray = snapshot.ViewportPointToRay(new Vector2(0.5f, 0.5f));

            Assert.That(ray.origin.magnitude, Is.LessThan(1e-4f));
            // Unity view space looks down -Z.
            Assert.That(ray.direction.z, Is.LessThan(-0.99f));
        }

        [Test]
        public void UnprojectAtDistance_WalksTheRay()
        {
            var projection = Matrix4x4.Perspective(60, 4f / 3f, 0.1f, 100f);
            var pose = Matrix4x4.Translate(new Vector3(0, 1.6f, 0));
            var snapshot = new CameraPoseSnapshot(0, pose, projection.inverse);

            var point = snapshot.UnprojectAtDistance(new Vector2(0.5f, 0.5f), 2f);

            Assert.That(Vector3.Distance(point, new Vector3(0, 1.6f, -2f)), Is.LessThan(1e-3f));
        }
    }

    public class StatusModelTests
    {
        [Test]
        public void Headline_NullWhenHealthy()
        {
            var model = new StatusModel();
            model.Set(StatusModel.Fields.Camera, "capturing", StatusSeverity.Ok);

            Assert.That(model.Headline(), Is.Null);
        }

        [Test]
        public void Headline_PrioritizesBySeverityThenFieldOrder()
        {
            var model = new StatusModel();
            model.Set(StatusModel.Fields.Quality, "paused: too dark", StatusSeverity.Warning);
            model.Set(StatusModel.Fields.Signaling, "reconnecting", StatusSeverity.Warning);
            model.Set(StatusModel.Fields.Camera, "permission denied", StatusSeverity.Error);

            StringAssert.StartsWith("camera:", model.Headline());

            // Clear the camera error; signaling outranks quality at equal severity.
            model.Set(StatusModel.Fields.Camera, "capturing", StatusSeverity.Ok);
            StringAssert.StartsWith("signaling:", model.Headline());
        }

        [Test]
        public void Changed_DedupesIdenticalSets()
        {
            var model = new StatusModel();
            var raised = 0;
            model.Changed += (_, _) => raised++;

            model.Set("camera", "capturing", StatusSeverity.Ok);
            model.Set("camera", "capturing", StatusSeverity.Ok);

            Assert.That(raised, Is.EqualTo(1));
        }
    }
}
