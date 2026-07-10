using System;
using UnityEngine;
using UnityEngine.UI;
using XRTraining.UI;

namespace XRTraining.Components
{
    /// <summary>
    /// The vertical, pinned-to-hand menu strip from design exploration 1d, extended
    /// with a current-step readout at the top so the active training step is always
    /// glanceable on the hand. In XR the strip is a world-space canvas anchored to
    /// the ulnar side of the palm with a lazy follow — the controller that owns the
    /// canvas drives that; this class only builds the strip.
    /// </summary>
    public static class HandMenu
    {
        public class Actions
        {
            public Action home, tasks, hint, redo, exit;
        }

        /// <summary>Live references into a built strip, for per-step updates without a rebuild.</summary>
        public class Handle
        {
            public RectTransform Root;
            public Text StepEyebrow;
            public Text StepTitle;

            public void SetStep(string eyebrow, string title)
            {
                if (StepEyebrow != null) StepEyebrow.text = eyebrow;
                if (StepTitle != null) StepTitle.text = title;
            }
        }

        public static Handle Build(UIFactory f, Transform parent, Actions a)
        {
            var T = f.T;

            var strip = f.Plate(parent, 20, T.Panel(), T.PanelBorder(), name: "HandMenu");
            // Controls child widths from their preferred size (56px tiles); both-axis
            // ContentSizeFitter makes the absolutely-positioned strip hug its content (~76px).
            f.VLayout(strip, new RectOffset(10, 10, 12, 12), 8, TextAnchor.UpperCenter,
                controlWidth: true, controlHeight: true, expandWidth: false);
            var fit = strip.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var handle = new Handle { Root = strip };

            // Current-step readout — the presentation layer updates this per step.
            var readout = f.Rect("StepReadout", strip);
            f.VLayout(readout, new RectOffset(2, 2, 0, 0), 2, TextAnchor.UpperCenter,
                controlWidth: true, controlHeight: true, expandWidth: false);
            f.Sized(readout, w: 56);
            handle.StepEyebrow = f.Label(readout, "STEP", 8, T.accent, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            f.Sized(handle.StepEyebrow.rectTransform, w: 56, h: 10);
            handle.StepTitle = f.Label(readout, "—", 9, T.textMid, FontStyle.Normal, TextAnchor.MiddleCenter, wrap: true);
            f.Sized(handle.StepTitle.rectTransform, w: 56, h: 30);

            Divider(f, strip);

            Item(f, strip, "⌂", "HOME", a.home, active: true);
            Item(f, strip, "☰", "TASKS", a.tasks);
            Item(f, strip, "?", "HINT", a.hint);
            Item(f, strip, "⟳", "REDO", a.redo);

            Divider(f, strip);

            Item(f, strip, "✕", "EXIT", a.exit, danger: true);
            return handle;
        }

        static void Divider(UIFactory f, Transform strip)
        {
            var divider = f.Rect("Divider", strip);
            f.Sized(divider, w: 40, h: 1);
            var dImg = divider.gameObject.AddComponent<Image>();
            dImg.color = f.T.Overlay(0.12f);
            dImg.raycastTarget = false;
        }

        static void Item(UIFactory f, Transform parent, string glyph, string label, Action onClick,
            bool active = false, bool danger = false)
        {
            var T = f.T;
            Color fill, fillHover, border, iconColor;
            if (active)
            {
                fill = T.AccentA(0.14f); fillHover = T.AccentA(0.22f);
                border = T.AccentA(0.5f); iconColor = T.accent;
            }
            else if (danger)
            {
                fill = T.DangerBg(); fillHover = new Color(T.danger.r, T.danger.g, T.danger.b, 0.2f);
                border = T.DangerBorder(); iconColor = T.DangerText();
            }
            else
            {
                fill = T.Overlay(0.05f); fillHover = T.Overlay(0.10f);
                border = T.Overlay(0.12f); iconColor = T.secondaryText;
            }

            var btn = f.MakeButton(parent, new UIFactory.ButtonSpec
            {
                text = "", fill = fill, fillHover = fillHover, textColor = iconColor,
                border = border, radius = 14, height = 56, onClick = onClick
            });
            var rt = btn.GetComponent<RectTransform>();
            f.Sized(rt, w: 56, h: 56);

            // Stacked glyph + micro label inside the 56×56 tile.
            var stack = f.Rect("Stack", rt);
            UIFactory.Stretch(stack);
            f.VLayout(stack, new RectOffset(0, 0, 0, 0), 2, TextAnchor.MiddleCenter);
            var gl = f.Label(stack, glyph, 18, iconColor, FontStyle.Normal, TextAnchor.MiddleCenter, wrap: false);
            f.Sized(gl.rectTransform, h: 20);
            var lb = f.Label(stack, label, 8, iconColor, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            f.Sized(lb.rectTransform, h: 10);
        }
    }
}
