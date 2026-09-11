using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>The shape of one mote, all drawn from the <see cref="HudArt"/> atlas.</summary>
    public enum HudMoteShape
    {
        Dot = 0,
        Plus = 1,
        Star = 2,
        Glow = 3,
    }

    /// <summary>
    /// The panel's event particles: a pool of pixel motes drawn as quads by ONE graphic.
    ///
    /// <para><b>Why not a ParticleSystem.</b> A ParticleSystem is a world renderer; in a
    /// Screen Space Overlay canvas it does not sort with the Images around it, and making it do so
    /// needs a UI camera whose ortho size would have to track <c>CameraSetup</c>. Seventy quads
    /// on the panel's own canvas sort by hierarchy, share the atlas, and cost one draw call.</para>
    ///
    /// <para><b>Motes only ever answer an EVENT</b> — a blow, a heal, a spell leaving the hands, a
    /// cooldown coming back, a level. Nothing here emits while nothing happens: a panel that
    /// sparkles at rest is a panel whose sparkle means nothing when it matters. That is the rule
    /// <c>FacingIndicator.Pulse</c> and the vortex's discharges already live by.</para>
    ///
    /// <para><b>Positions snap to whole texels</b>, so a mote is a pixel of light moving over
    /// pixel art rather than a smeared sub-pixel sprite. The layer is drawn with the additive
    /// HudFx material: on an additive surface alpha is COVERAGE and colour is brightness, so a
    /// gold spark over the dark frame brightens it instead of painting over it.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HudMoteLayer : MaskableGraphic
    {
        private struct Mote
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public Color Colour;
            public float Life;
            public float MaxLife;
            public float Gravity;
            public float Drag;
            public HudMoteShape Shape;
            public bool Twinkle;
        }

        private Mote[] _motes = new Mote[0];
        private int _alive;
        private HudArt _art;
        private float _time;

        /// <summary>How many motes are alive. For the tests and the budget.</summary>
        public int Alive => _alive;

        /// <summary>The most motes the layer will hold; an emit past it is dropped, never grown.</summary>
        public int Capacity => _motes.Length;

        public override Texture mainTexture => _art != null && _art.Atlas != null ? _art.Atlas : s_WhiteTexture;

        public static HudMoteLayer Create(Transform parent, HudArt art, int capacity, Material material)
        {
            var go = new GameObject("Motes", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            HudRect.Fill((RectTransform)go.transform);
            var layer = go.AddComponent<HudMoteLayer>();
            layer._art = art;
            layer._motes = new Mote[Mathf.Max(0, capacity)];
            layer.raycastTarget = false;
            if (material != null) layer.material = material;
            return layer;
        }

        /// <summary>
        /// Emits one mote at <paramref name="position"/> (panel texels, bottom-left origin) moving
        /// at <paramref name="velocity"/> texels per second. Returns false when the pool is full.
        /// </summary>
        public bool Emit(Vector2 position, Vector2 velocity, Color colour, float life,
                         HudMoteShape shape, float gravity = 0f, float drag = 0f, bool twinkle = false)
        {
            if (_alive >= _motes.Length || life <= 0f) return false;
            _motes[_alive++] = new Mote
            {
                Position = position,
                Velocity = velocity,
                Colour = colour,
                Life = life,
                MaxLife = life,
                Gravity = gravity,
                Drag = drag,
                Shape = shape,
                Twinkle = twinkle,
            };
            SetVerticesDirty();
            return true;
        }

        /// <summary>Removes every mote at once — used when the panel is hidden.</summary>
        public void Clear()
        {
            if (_alive == 0) return;
            _alive = 0;
            SetVerticesDirty();
        }

        /// <summary>Advances every mote. Public so an EditMode test can drive it without a clock.</summary>
        public void Tick(float dt)
        {
            if (_alive == 0) return;
            _time += dt;
            for (int i = _alive - 1; i >= 0; i--)
            {
                ref var m = ref _motes[i];
                m.Life -= dt;
                if (m.Life <= 0f)
                {
                    _motes[i] = _motes[--_alive];
                    continue;
                }
                if (m.Drag > 0f) m.Velocity *= Mathf.Max(0f, 1f - m.Drag * dt);
                m.Velocity.y -= m.Gravity * dt;
                m.Position += m.Velocity * dt;
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_alive == 0 || _art == null) return;
            var tex = _art.Atlas;
            if (tex == null) return;
            var rect = GetPixelAdjustedRect();

            for (int i = 0; i < _alive; i++)
            {
                var m = _motes[i];
                var sprite = SpriteFor(m.Shape);
                if (sprite == null) continue;
                var r = sprite.textureRect;
                float t = m.Life / m.MaxLife;                     // 1 at birth, 0 at death
                // Arrive at once, leave on a curve: a mote that fades linearly spends half its
                // life looking like it is already gone.
                float a = t > 0.8f ? 1f : Mathf.SmoothStep(0f, 1f, t / 0.8f);
                if (m.Twinkle) a *= 0.65f + 0.35f * Mathf.Sin((_time + i * 0.37f) * 23f);
                var c = m.Colour;
                c.a *= a;
                Color32 c32 = c;

                float w = r.width, h = r.height;
                float x = Mathf.Floor(rect.xMin + m.Position.x - w * 0.5f + 0.5f);
                float y = Mathf.Floor(rect.yMin + m.Position.y - h * 0.5f + 0.5f);
                var uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);

                int start = vh.currentVertCount;
                vh.AddVert(new Vector3(x, y), c32, new Vector2(uv.xMin, uv.yMin));
                vh.AddVert(new Vector3(x, y + h), c32, new Vector2(uv.xMin, uv.yMax));
                vh.AddVert(new Vector3(x + w, y + h), c32, new Vector2(uv.xMax, uv.yMax));
                vh.AddVert(new Vector3(x + w, y), c32, new Vector2(uv.xMax, uv.yMin));
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }

        private Sprite SpriteFor(HudMoteShape shape)
        {
            switch (shape)
            {
                case HudMoteShape.Plus: return _art.MotePlus;
                case HudMoteShape.Star: return _art.MoteStar;
                case HudMoteShape.Glow: return _art.MoteGlow;
                default: return _art.MoteDot;
            }
        }
    }
}
