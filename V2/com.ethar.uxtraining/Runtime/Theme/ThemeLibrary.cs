// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Collections.Generic;
using UnityEngine;

namespace Ethar.UXTraining.Theme
{
    /// <summary>
    /// Builds the three built-in palettes in code so a project runs with no
    /// pre-authored .asset files. In a shipping project a client would instead
    /// author a <see cref="ThemePalette"/> asset (Create ▸ Ethar ▸ UX Training ▸
    /// Theme Palette) and hand it to the UX host — the runtime treats an
    /// assigned asset and a code-built default identically.
    /// </summary>
    public static class ThemeLibrary
    {
        static Color Hex(string h) => ThemePalette.HexA(h);

        /// <summary>1a — MRTK dark, cyan accent.</summary>
        public static ThemePalette DarkCyan()
        {
            var t = ScriptableObject.CreateInstance<ThemePalette>();
            t.name = "DarkCyan";
            t.displayName = "Dark · Cyan";
            t.isLight = false;
            t.root = Hex("#10141b");
            t.plateColor = Hex("#1e2430");
            t.plateAlpha = 0.94f;
            t.overlayTint = Color.white;
            t.accent = Hex("#4cc3e0");
            t.accentHover = Hex("#72d3ea");
            t.onAccent = Hex("#07222c");
            t.textHi = Hex("#f2f5f9");
            t.textMid = Hex("#9aa4b2");
            t.textLo = Hex("#6b7686");
            t.secondaryText = Hex("#c7cfda");
            t.danger = Hex("#e25454");
            return t;
        }

        /// <summary>1b — light frosted, teal accent (single-palette theme swap).</summary>
        public static ThemePalette LightTeal()
        {
            var t = ScriptableObject.CreateInstance<ThemePalette>();
            t.name = "LightTeal";
            t.displayName = "Light · Teal";
            t.isLight = true;
            t.root = Hex("#d8dde3");
            t.plateColor = Hex("#fafcfd");
            t.plateAlpha = 0.90f;
            t.overlayTint = Hex("#1b2430");
            t.accent = Hex("#0e9488");
            t.accentHover = Hex("#12ab9d");
            t.onAccent = Hex("#f3faf9");
            t.textHi = Hex("#1b2430");
            t.textMid = Hex("#5a6470");
            t.textLo = Hex("#8a94a0");
            t.secondaryText = Hex("#3a4450");
            t.danger = Hex("#c0453f");
            return t;
        }

        /// <summary>1c — hi-vis "focus mode", orange accent for gloved / bright-floor use.</summary>
        public static ThemePalette HiVisOrange()
        {
            var t = ScriptableObject.CreateInstance<ThemePalette>();
            t.name = "HiVisOrange";
            t.displayName = "Hi-Vis · Orange";
            t.isLight = false;
            t.root = Hex("#141210");
            t.plateColor = Hex("#24201a");
            t.plateAlpha = 0.95f;
            t.overlayTint = Color.white;
            t.accent = Hex("#f27b2c");
            t.accentHover = Hex("#ff9548");
            t.onAccent = Hex("#241102");
            t.textHi = Hex("#f7f3ed");
            t.textMid = Hex("#b8b0a2");
            t.textLo = Hex("#8d8578");
            t.secondaryText = Hex("#d8d0c2");
            t.danger = Hex("#e25454");
            return t;
        }

        /// <summary>All built-in palettes in cycle order.</summary>
        public static List<ThemePalette> BuildDefaults()
        {
            return new List<ThemePalette> { DarkCyan(), LightTeal(), HiVisOrange() };
        }
    }
}
