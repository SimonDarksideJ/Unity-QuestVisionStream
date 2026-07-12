// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ethar.UXTraining.Components
{
    /// <summary>
    /// Pointer-hover feedback for buttons: a slight expand plus a soft accent
    /// glow (a rounded overlay that reaches a few px past the button edge),
    /// animated in and out as the pointer ray enters and leaves. Complements the
    /// Button's ColorTint transition — the tint recolours the fill, this adds
    /// the "the laser is on me" glow. Attach at runtime via <see cref="Attach"/>.
    /// </summary>
    public class ButtonHoverGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        const float ExpandScale = 1.06f;
        const float GlowAlpha = 0.35f;
        const float GlowPadPx = 7f;
        const float AnimSeconds = 0.12f;

        Image _glow;
        Selectable _selectable;
        Vector3 _baseScale;
        float _amount;
        float _target;

        public static ButtonHoverGlow Attach(GameObject buttonRoot, Color glowColor, int radius = 14)
        {
            var g = buttonRoot.AddComponent<ButtonHoverGlow>();
            g.Build(glowColor, radius);
            return g;
        }

        void Build(Color color, int radius)
        {
            _baseScale = transform.localScale;
            _selectable = GetComponent<Selectable>();

            // uGUI renders parent-then-children, so the overlay draws above the
            // fill but below the label (which stays a later sibling).
            var rt = new GameObject("HoverGlow", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-GlowPadPx, -GlowPadPx);
            rt.offsetMax = new Vector2(GlowPadPx, GlowPadPx);

            _glow = rt.gameObject.AddComponent<Image>();
            _glow.sprite = UI.RoundedSprite.Get(radius);
            _glow.type = Image.Type.Sliced;
            _glow.color = new Color(color.r, color.g, color.b, 0f);
            _glow.raycastTarget = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_selectable != null && !_selectable.interactable) return;
            _target = 1f;
        }

        public void OnPointerExit(PointerEventData eventData) => _target = 0f;

        void Update()
        {
            if (Mathf.Approximately(_amount, _target)) return;
            _amount = Mathf.MoveTowards(_amount, _target, Time.deltaTime / AnimSeconds);
            Apply();
        }

        // Hidden mid-hover (form rebuild, menu fade): snap back so the button
        // never reappears stuck expanded.
        void OnDisable()
        {
            _target = 0f;
            _amount = 0f;
            Apply();
        }

        void Apply()
        {
            transform.localScale = _baseScale * Mathf.Lerp(1f, ExpandScale, _amount);
            var c = _glow.color;
            c.a = GlowAlpha * _amount;
            _glow.color = c;
        }
    }
}
