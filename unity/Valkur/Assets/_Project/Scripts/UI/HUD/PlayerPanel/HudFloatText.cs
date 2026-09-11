using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// A few pixel labels that rise and fade — "+12" off the end of the XP bar when experience
    /// arrives. A small pool, reused oldest-first, so a burst of kills cannot allocate.
    ///
    /// <para>The number rises from where the bar GREW, which is the answer to "what did that give
    /// me" at the place the player is already looking.</para>
    /// </summary>
    public sealed class HudFloatText
    {
        private struct Entry
        {
            public HudPixelText Text;
            public Vector2 From;
            public float Life;
        }

        private const float LifeSeconds = 1.1f;
        private const float RiseTexels = 10f;
        private const int HalfWidth = 22;

        private readonly Entry[] _entries;
        private int _next;

        public HudFloatText(Transform parent, HudArt art, int count)
        {
            _entries = new Entry[Mathf.Max(1, count)];
            for (int i = 0; i < _entries.Length; i++)
            {
                var t = HudPixelText.Create(parent, "Float" + i, art, HudFontFace.Small, HudTextAlign.Centre,
                                            0, 0, HalfWidth * 2, 7);
                t.enabled = false;
                _entries[i] = new Entry { Text = t };
            }
        }

        /// <summary>How many labels are rising right now.</summary>
        public int Active
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Length; i++) if (_entries[i].Life > 0f) n++;
                return n;
            }
        }

        public void Spawn(string text, Vector2 at, Color colour)
        {
            ref var e = ref _entries[_next];
            _next = (_next + 1) % _entries.Length;
            e.From = at;
            e.Life = LifeSeconds;
            e.Text.SetText(text);
            e.Text.SetColour(colour);
            e.Text.enabled = true;
            Place(ref e, 0f);
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                ref var e = ref _entries[i];
                if (e.Life <= 0f) continue;
                e.Life -= dt;
                if (e.Life <= 0f)
                {
                    e.Text.enabled = false;
                    continue;
                }
                float t = 1f - e.Life / LifeSeconds;
                Place(ref e, t);
                var c = e.Text.color;
                c.a = t < 0.6f ? 1f : Mathf.SmoothStep(1f, 0f, (t - 0.6f) / 0.4f);
                e.Text.color = c;
            }
        }

        private static void Place(ref Entry e, float t)
        {
            // Ease out and step in whole texels, so the rise is pixel motion rather than a smear.
            float rise = Mathf.Floor(RiseTexels * (1f - (1f - t) * (1f - t)));
            var rt = e.Text.rectTransform;
            rt.anchoredPosition = new Vector2(Mathf.Floor(e.From.x - HalfWidth), Mathf.Floor(e.From.y + rise));
        }
    }
}
