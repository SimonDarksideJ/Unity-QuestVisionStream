// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.Collections.Generic;

namespace QuestVisionStream.Core
{
    /// <summary>Severity of a status field, used to pick the headline.</summary>
    public enum StatusSeverity
    {
        /// <summary>Everything fine — hidden from the headline.</summary>
        Ok = 0,

        /// <summary>Something is in progress (connecting, waiting for camera).</summary>
        Progress,

        /// <summary>Degraded but running (quality pause, reconnect pending).</summary>
        Warning,

        /// <summary>Broken until fixed (camera error, connection failed).</summary>
        Error
    }

    /// <summary>A single status field's current value.</summary>
    public readonly struct StatusEntry
    {
        public StatusEntry(string value, StatusSeverity severity)
        {
            Value = value;
            Severity = severity;
        }

        public string Value { get; }
        public StatusSeverity Severity { get; }
    }

    /// <summary>
    /// The client status model, ported from the V2 WebXR client's design mandate:
    /// failures are never console-only. Fields are the fixed set the server-side
    /// status uplink also uses; <see cref="Headline"/> reports the single
    /// highest-priority problem (camera error → connection failure → signaling drop
    /// → quality pause → progress) and returns null when everything is healthy so
    /// HUDs can hide entirely.
    /// </summary>
    public sealed class StatusModel
    {
        /// <summary>Well-known field names, in headline priority order.</summary>
        public static class Fields
        {
            public const string Camera = "camera";
            public const string Connection = "connection";
            public const string Signaling = "signaling";
            public const string Quality = "quality";
            public const string Detections = "detections";
            public const string Device = "device";
            public const string Server = "server";
        }

        // Headline priority: a camera error trumps a connection failure trumps a
        // signaling drop trumps a quality pause; ties break by this field order.
        private static readonly string[] HeadlinePriority =
        {
            Fields.Camera,
            Fields.Connection,
            Fields.Signaling,
            Fields.Quality,
            Fields.Detections,
            Fields.Device,
            Fields.Server
        };

        private readonly Dictionary<string, StatusEntry> entries = new Dictionary<string, StatusEntry>();

        /// <summary>Raised when a field actually changes (sets to the same value are deduped).</summary>
        public event Action<string, StatusEntry> Changed;

        public IReadOnlyDictionary<string, StatusEntry> Entries => entries;

        public void Set(string field, string value, StatusSeverity severity = StatusSeverity.Ok)
        {
            if (entries.TryGetValue(field, out var existing) &&
                existing.Value == value &&
                existing.Severity == severity)
            {
                return;
            }

            var entry = new StatusEntry(value, severity);
            entries[field] = entry;
            Changed?.Invoke(field, entry);
        }

        public StatusEntry? Get(string field)
            => entries.TryGetValue(field, out var entry) ? entry : (StatusEntry?)null;

        /// <summary>
        /// The single most important problem right now, or null when healthy.
        /// Higher severity wins; equal severities break by field priority order.
        /// </summary>
        public string Headline()
        {
            string best = null;
            var bestSeverity = StatusSeverity.Ok;
            var bestPriority = int.MaxValue;

            foreach (var pair in entries)
            {
                if (pair.Value.Severity == StatusSeverity.Ok)
                {
                    continue;
                }

                var priority = Array.IndexOf(HeadlinePriority, pair.Key);
                if (priority < 0)
                {
                    priority = HeadlinePriority.Length;
                }

                if (pair.Value.Severity > bestSeverity ||
                    (pair.Value.Severity == bestSeverity && priority < bestPriority))
                {
                    bestSeverity = pair.Value.Severity;
                    bestPriority = priority;
                    best = $"{pair.Key}: {pair.Value.Value}";
                }
            }

            return best;
        }
    }
}
