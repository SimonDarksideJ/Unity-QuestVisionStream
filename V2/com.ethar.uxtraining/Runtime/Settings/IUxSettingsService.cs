// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using RealityCollective.ServiceFramework.Interfaces;

namespace Ethar.UXTraining.Settings
{
    /// <summary>
    /// Loads and caches the <see cref="UxSettings"/> asset at app start so every
    /// UX consumer (windows, labels, hosts) reads one shared configuration
    /// instead of scattered magic numbers.
    /// </summary>
    public interface IUxSettingsService : IService
    {
        /// <summary>The cached settings — never null (falls back to <see cref="UxSettings.Defaults"/>).</summary>
        UxSettings Settings { get; }
    }
}
