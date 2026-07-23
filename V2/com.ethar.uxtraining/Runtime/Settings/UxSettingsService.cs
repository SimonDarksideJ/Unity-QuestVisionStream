// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using RealityCollective.ServiceFramework.Definitions;
using RealityCollective.ServiceFramework.Services;
using UnityEngine;

namespace Ethar.UXTraining.Settings
{
    /// <summary>Configuration for <see cref="UxSettingsService"/>.</summary>
    [CreateAssetMenu(menuName = "Ethar/UX Training/UX Settings Service Profile", fileName = "UxSettingsServiceProfile")]
    public class UxSettingsServiceProfile : BaseProfile
    {
        [SerializeField]
        [Tooltip("The UX settings asset to serve. Empty tries Resources at the fallback path below, then the built-in defaults.")]
        private UxSettings settings;

        [SerializeField]
        [Tooltip("Resources path probed when no settings asset is assigned directly.")]
        private string resourcesFallbackPath = "UxSettings";

        public UxSettings Settings { get => settings; set => settings = value; }
        public string ResourcesFallbackPath { get => resourcesFallbackPath; set => resourcesFallbackPath = value; }
    }

    /// <summary>
    /// <see cref="IUxSettingsService"/>: resolves the <see cref="UxSettings"/>
    /// asset once at startup (profile reference → Resources fallback → built-in
    /// defaults) and serves the cached instance for the app's lifetime.
    /// </summary>
    [System.Runtime.InteropServices.Guid("b8a5c9e1-2d47-4f6b-9a3e-7c1d5e8f0a24")]
    public sealed class UxSettingsService : BaseServiceWithConstructor, IUxSettingsService
    {
        private readonly UxSettingsServiceProfile profile;
        private UxSettings cached;

        public UxSettingsService(string name, uint priority, UxSettingsServiceProfile profile)
            : base(name, priority)
        {
            this.profile = profile;
        }

        /// <inheritdoc />
        public UxSettings Settings
        {
            get
            {
                if (cached == null)
                {
                    cached = Load();
                }

                return cached;
            }
        }

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            // Eager load so the asset is cached before any UX is built.
            _ = Settings;
        }

        private UxSettings Load()
        {
            if (profile != null && profile.Settings != null)
            {
                Debug.Log($"[UXTraining] UX settings loaded from profile asset '{profile.Settings.name}'");
                return profile.Settings;
            }

            var path = profile != null ? profile.ResourcesFallbackPath : "UxSettings";
            if (!string.IsNullOrEmpty(path))
            {
                var fromResources = Resources.Load<UxSettings>(path);
                if (fromResources != null)
                {
                    Debug.Log($"[UXTraining] UX settings loaded from Resources/{path}");
                    return fromResources;
                }
            }

            Debug.Log("[UXTraining] No UX settings asset found — using built-in defaults");
            return UxSettings.Defaults;
        }
    }
}
