using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRTraining.Theme
{
    /// <summary>
    /// Holds the active palette and the set of selectable palettes, and raises an
    /// event when the theme changes. The UI listens and rebuilds — a full rebuild
    /// keeps every widget in sync with zero per-widget re-theming bookkeeping,
    /// which mirrors how a uGUI project would re-bind a swapped palette asset.
    /// </summary>
    public class ThemeManager
    {
        readonly List<ThemePalette> _palettes;
        int _index;

        public event Action Changed;

        public ThemeManager(List<ThemePalette> palettes, int startIndex = 0)
        {
            _palettes = palettes;
            _index = Mathf.Clamp(startIndex, 0, _palettes.Count - 1);
        }

        public ThemePalette Active => _palettes[_index];
        public IReadOnlyList<ThemePalette> Palettes => _palettes;
        public int Index => _index;

        public void Select(int index)
        {
            index = Mathf.Clamp(index, 0, _palettes.Count - 1);
            if (index == _index) return;
            _index = index;
            Changed?.Invoke();
        }

        public void Next()
        {
            _index = (_index + 1) % _palettes.Count;
            Changed?.Invoke();
        }
    }
}
