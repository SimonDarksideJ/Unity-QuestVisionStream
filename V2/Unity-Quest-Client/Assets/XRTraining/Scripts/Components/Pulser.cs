using UnityEngine;
using UnityEngine.UI;

namespace XRTraining.Components
{
    /// <summary>
    /// Drives the two looping animations from the design's CSS keyframes:
    ///  • <c>Dot</c>   — scale + opacity pulse (dotPulse) for hint markers.
    ///  • <c>Ring</c>  — an expanding, fading outline ring behind a callout
    ///                   (hintPulse / hintPulseOrange), drawn via a child Image so
    ///                   it can grow past the plate without clipping.
    /// Attach at runtime; it self-configures.
    /// </summary>
    public class Pulser : MonoBehaviour
    {
        public enum Mode { Dot, Ring }

        public Mode mode = Mode.Dot;
        public float period = 1.8f;
        public Color ringColor = Color.cyan;
        public float ringMaxExtra = 14f; // px the ring expands beyond the target

        RectTransform _rt;
        Image _ring;
        RectTransform _ringRt;
        float _phase;
        bool _init;

        public static Pulser AddDot(GameObject go, float period)
        {
            var p = go.AddComponent<Pulser>();
            p.mode = Mode.Dot;
            p.period = period;
            return p;
        }

        public static Pulser AddRing(GameObject go, Color color, float period)
        {
            var p = go.AddComponent<Pulser>();
            p.mode = Mode.Ring;
            p.period = period;
            p.ringColor = color;
            return p;
        }

        // Initialised lazily on the first Update: AddComponent runs Awake before the
        // static AddDot/AddRing helpers finish setting `mode`, so we can't build the
        // ring in Awake — by the first Update all fields are set correctly.
        void Init()
        {
            _rt = GetComponent<RectTransform>();
            if (mode == Mode.Ring) BuildRing();
            _init = true;
        }

        void BuildRing()
        {
            var go = new GameObject("PulseRing", typeof(RectTransform));
            _ringRt = (RectTransform)go.transform;
            _ringRt.SetParent(transform, false);
            _ringRt.SetAsFirstSibling(); // behind the callout content
            UI.UIFactory.Stretch(_ringRt);
            _ring = go.AddComponent<Image>();
            _ring.sprite = UI.RoundedSprite.Get(14);
            _ring.type = Image.Type.Sliced;
            _ring.color = ringColor;
            _ring.raycastTarget = false;
        }

        void Update()
        {
            if (!_init) Init();
            _phase += Time.unscaledDeltaTime / Mathf.Max(0.0001f, period);
            if (_phase > 1f) _phase -= 1f;

            if (mode == Mode.Dot)
            {
                // 0..1..0 triangle → scale 1→1.35, opacity 1→0.55.
                float tri = 1f - Mathf.Abs(2f * _phase - 1f);
                float scale = Mathf.Lerp(1f, 1.35f, tri);
                _rt.localScale = new Vector3(scale, scale, 1f);
                var g = GetComponent<Graphic>();
                if (g != null)
                {
                    var c = g.color;
                    c.a = Mathf.Lerp(1f, 0.55f, tri);
                    g.color = c;
                }
            }
            else if (_ring != null)
            {
                // Expanding fade-out ring, restarting each period (ease-out feel).
                float e = 1f - Mathf.Pow(1f - _phase, 2f);
                float extra = e * ringMaxExtra;
                _ringRt.offsetMin = new Vector2(-extra, -extra);
                _ringRt.offsetMax = new Vector2(extra, extra);
                var c = _ring.color;
                c.a = Mathf.Lerp(0.55f, 0f, e) * (ringColor.a <= 0f ? 1f : 1f);
                _ring.color = new Color(ringColor.r, ringColor.g, ringColor.b, Mathf.Lerp(0.5f, 0f, e));
            }
        }
    }
}
