using UnityEngine;

namespace XRTraining.Theme
{
    /// <summary>
    /// A client-swappable colour palette. In the MRTK-style design this is the
    /// single asset that re-skins every panel — "one ScriptableObject palette per
    /// client re-skins every panel; sprites never change".
    ///
    /// Only a small set of core tokens are stored; the many translucent surface,
    /// border and state tints used across the UI are derived from these at runtime
    /// (see the helper methods below) so a new theme only needs to set ~12 colours.
    /// </summary>
    [CreateAssetMenu(menuName = "XR Training/Theme Palette", fileName = "ThemePalette")]
    public class ThemePalette : ScriptableObject
    {
        public string displayName = "Theme";

        [Tooltip("True for light backgrounds — controls whether translucent overlays are drawn light-on-dark or dark-on-light.")]
        public bool isLight = false;

        [Header("Surfaces")]
        [Tooltip("Scene / canvas backdrop colour.")]
        public Color root = HexA("#10141b");
        [Tooltip("Opaque plate (panel) colour, before plate alpha is applied.")]
        public Color plateColor = HexA("#1e2430");
        [Range(0f, 1f)] public float plateAlpha = 0.94f;
        [Tooltip("RGB tint used for every translucent overlay/border. White on dark themes, near-black navy on light themes.")]
        public Color overlayTint = Color.white;

        [Header("Accent")]
        public Color accent = HexA("#4cc3e0");
        public Color accentHover = HexA("#72d3ea");
        [Tooltip("Text / icon colour drawn on top of a solid accent fill.")]
        public Color onAccent = HexA("#07222c");

        [Header("Text")]
        public Color textHi = HexA("#f2f5f9");
        public Color textMid = HexA("#9aa4b2");
        public Color textLo = HexA("#6b7686");
        [Tooltip("Label colour for secondary / outline buttons.")]
        public Color secondaryText = HexA("#c7cfda");

        [Header("Semantic")]
        public Color danger = HexA("#e25454");

        // ---- Derived tokens (kept out of the inspector; computed from the core set) ----

        /// <summary>Panel fill including the configured plate alpha.</summary>
        public Color Panel() => new Color(plateColor.r, plateColor.g, plateColor.b, plateAlpha);

        /// <summary>A translucent overlay of the theme tint at the given alpha.</summary>
        public Color Overlay(float a) => new Color(overlayTint.r, overlayTint.g, overlayTint.b, a);

        /// <summary>The accent colour at the given alpha.</summary>
        public Color AccentA(float a) => new Color(accent.r, accent.g, accent.b, a);

        public Color PanelBorder() => Overlay(0.14f);
        public Color SecondaryBg() => Overlay(0.06f);
        public Color SecondaryBorder() => Overlay(0.16f);

        public Color StepCurrentBg() => AccentA(0.12f);
        public Color StepDoneDotBg() => AccentA(0.18f);
        public Color StepTodoBg() => Overlay(0.03f);
        public Color StepTodoBorder() => Overlay(0.07f);
        public Color StepTodoDotBg() => Overlay(0.08f);

        public Color HintBg() => AccentA(0.08f);
        public Color HintBorder() => AccentA(0.35f);

        public Color AnnotationText() => new Color(textLo.r, textLo.g, textLo.b, 0.85f);
        public Color AnnotationRule() => Overlay(0.20f);

        public Color DangerBg() => new Color(danger.r, danger.g, danger.b, 0.10f);
        public Color DangerBorder() => new Color(danger.r, danger.g, danger.b, 0.35f);
        public Color DangerText() => Color.Lerp(danger, Color.white, isLight ? 0.0f : 0.45f);

        // ---- Small hex helper so theme code reads like the CSS it mirrors ----
        public static Color HexA(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return Color.magenta;
        }
    }
}
