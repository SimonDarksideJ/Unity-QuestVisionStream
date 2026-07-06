// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using QuestVisionStream.Core;
using RealityCollective.ServiceFramework.Interfaces;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// The client's single status surface — the V2 design mandate is that failures
    /// are never console-only. Watches every streaming service, maintains the
    /// <see cref="StatusModel"/> (for HUDs) and uplinks changed fields to the
    /// server over signaling (for remote diagnosis), resending everything after a
    /// reconnect.
    /// </summary>
    public interface IStatusService : IService
    {
        /// <summary>The live model. HUDs subscribe to <see cref="StatusModel.Changed"/> and render <see cref="StatusModel.Headline"/>.</summary>
        StatusModel Model { get; }

        /// <summary>Report app-level status (services report themselves automatically).</summary>
        void Report(string field, string value, StatusSeverity severity = StatusSeverity.Ok);
    }
}
