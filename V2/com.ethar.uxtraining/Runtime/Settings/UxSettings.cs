// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using UnityEngine;

namespace Ethar.UXTraining.Settings
{
    /// <summary>How a UX window (startup card, training step form) is placed in the world.</summary>
    public enum WindowPlacementMode
    {
        /// <summary>Anchored in front of the user when (re)shown, then left in place.</summary>
        Fixed = 0,

        /// <summary>Smoothly follows the user's view, stopping short of active world labels.</summary>
        HeadLocked = 1
    }

    /// <summary>
    /// The UX tuning asset — the single place for the kit's sizing and behaviour
    /// numbers that were previously hard-coded. Author one via
    /// <c>Assets → Create → Ethar → UX Training → UX Settings</c> and hand it to the
    /// <see cref="UxSettingsService"/> (or its profile); hosts read the cached
    /// settings from <see cref="IUxSettingsService.Settings"/> at startup.
    /// </summary>
    [CreateAssetMenu(menuName = "Ethar/UX Training/UX Settings", fileName = "UxSettings")]
    public class UxSettings : ScriptableObject
    {
        private static UxSettings defaults;

        [Header("World Labels")]
        [SerializeField]
        [Tooltip("World-canvas scale (pixels → metres) for the label pill. The kit's original pill used 0.001; the default is 3× that so labels read clearly at room distance.")]
        private float labelScale = 0.003f;

        [SerializeField]
        [Tooltip("Width in metres of the leader line connecting the placement dot to the label pill.")]
        private float labelLineWidthMeters = 0.008f;

        [SerializeField]
        [Tooltip("Diameter in metres of the placement dot at the end of the label line.")]
        private float labelDotDiameterMeters = 0.056f;

        [Header("Windows")]
        [SerializeField]
        [Tooltip("Fixed = anchor the window in front of the user when shown (current behaviour). HeadLocked = the window smooth-follows the user's view.")]
        private WindowPlacementMode windowPlacement = WindowPlacementMode.Fixed;

        [SerializeField]
        [Tooltip("Smooth-follow response time in seconds for head-locked windows (time constant of the lazy follow).")]
        private float windowFollowSeconds = 0.3f;

        [SerializeField]
        [Tooltip("Head-locked windows keep their view direction at least this many degrees away from any active world label — the window slides to a stop at the boundary and resumes when the user looks back.")]
        private float labelClearanceDegrees = 20f;

        public float LabelScale { get => labelScale; set => labelScale = value; }
        public float LabelLineWidthMeters { get => labelLineWidthMeters; set => labelLineWidthMeters = value; }
        public float LabelDotDiameterMeters { get => labelDotDiameterMeters; set => labelDotDiameterMeters = value; }
        public WindowPlacementMode WindowPlacement { get => windowPlacement; set => windowPlacement = value; }
        public float WindowFollowSeconds { get => windowFollowSeconds; set => windowFollowSeconds = value; }
        public float LabelClearanceDegrees { get => labelClearanceDegrees; set => labelClearanceDegrees = value; }

        /// <summary>A shared instance carrying the default values, for hosts running without an authored asset.</summary>
        public static UxSettings Defaults
        {
            get
            {
                if (defaults == null)
                {
                    defaults = CreateInstance<UxSettings>();
                    defaults.name = "UxSettings (defaults)";
                    defaults.hideFlags = HideFlags.HideAndDontSave;
                }

                return defaults;
            }
        }
    }
}
