using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.MainMenu
{
    /// <summary>The shape of one menu mote, all drawn from the <see cref="MenuArt"/> atlas.</summary>
    public enum MenuMoteShape
    {
        Dot = 0,
        Spark = 1,
        Glow = 2,
        Ring = 3,
    }

    /// <summary>
    /// The menu's event particles: a pool of motes drawn as quads by ONE graphic.
    ///
    /// <para><b>Why not a ParticleSystem.</b> The rule this project wrote down in
    /// <c>HudMoteLayer</c> and again in <c>MinimapFx</c>: a <c>ParticleSystem</c> is a world
    /// renderer and does not sort against the <c>Graphic</c>s of a Screen Space Overlay canvas.
    /// It would sit either in front of every panel or behind the background art — never between
    /// them, which is the only place a mote belongs.</para>
    ///
    /// <para><b>Motes only ever answer an EVENT.</b> Moving the selection, confirming, cancelling,
    /// a panel opening, a class being picked, the title finishing its assembly. Nothing here
    /// emits while nothing happens — a menu that sparkles at rest is a menu whose sparkle means
    /// nothing when it matters. The one exception is declared and deliberate: the embers that
    /// rise off the settled title, which are kept below the attention floor by their rate
    /// (<c>MenuStyle.titleEmberRate</c>, seven a second over a whole word).</para>
    ///
    /// <para><b>Drawn additive</b> (<c>Valkur/UI/HudFx</c> with One / OneMinusSrcAlpha's src set
    /// to SrcAlpha and dst to One), so a gold spark over the panel brightens it instead of
    /// painting a hole in it. On an additive surface alpha is COVERAGE and colour is brightness —
    /// the rule <c>WeaponSwapFlashFX</c> records.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuFxLayer : MaskableGraphic
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
            public float Size;
            public MenuMoteShape Shape;
            public bool Twinkle;
        }

        private Mote[] _motes = System.Array.Empty<Mote>();
        private int _alive;
        private MenuArt _art;
        private float _time;

        /// <summary>How many motes are alive. For the tests and the budget.</summary>
        public int Alive => _alive;

        /// <summary>The most motes the layer will hold; an emit past it is dropped, never grown.</summary>
        public int Capacity => _motes.Length;

        /// <summary>
        /// Draw from the title's own small BILINEAR page instead of the menu atlas.
        ///
        /// <para>The menu atlas is filtered to POINT on purpose — it is what keeps the panels,
        /// the pills and the chevrons crisp — and a 2x2 dot point-sampled up to a few units is a
        /// hard square. A few dozen hard squares read as gravel, which is fine for a confirm
        /// burst on a button and wrong for anything meant to be an EMBER. The title already
        /// solved this by baking its dot and its glint onto one bilinear page; this borrows it
        /// rather than baking a third.</para>
        ///
        /// <para>It is a property of the LAYER and not of a mote, because a <c>Graphic</c> binds
        /// exactly one texture — a layer that mixed the two would silently draw every soft mote
        /// from whichever page it happened to bind, which is the failure the title's own
        /// one-page bake exists to avoid.</para>
        /// </summary>
        public bool UseSoftMotes { get; set; }

        private bool SoftAvailable => UseSoftMotes && _art != null && _art.TitleMote != null
                                      && _art.TitleMote.texture != null;

        public override Texture mainTexture
        {
            get
            {
                if (SoftAvailable) return _art.TitleMote.texture;
                return _art != null && _art.Atlas != null ? _art.Atlas : s_WhiteTexture;
            }
        }

        public static MenuFxLayer Create(Transform parent, MenuArt art, int capacity, Material material)
        {
            var go = new GameObject("MenuMotes", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var layer = go.AddComponent<MenuFxLayer>();
            layer._art = art;
            layer._motes = new Mote[Mathf.Max(0, capacity)];
            layer.raycastTarget = false;
            if (material != null) layer.material = material;
            return layer;
        }

        /// <summary>
        /// Emits one mote at <paramref name="position"/> in this layer's own rect (bottom-left
        /// origin) moving at <paramref name="velocity"/> units per second. False when full.
        /// </summary>
        public bool Emit(Vector2 position, Vector2 velocity, Color colour, float life,
                         MenuMoteShape shape, float gravity = 0f, float drag = 0f,
                         float size = 1f, bool twinkle = false)
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
                Size = Mathf.Max(0.1f, size),
                Shape = shape,
                Twinkle = twinkle,
            };
            SetVerticesDirty();
            return true;
        }

        /// <summary>A burst of <paramref name="count"/> motes thrown out of one point.</summary>
        public void Burst(Vector2 position, int count, Color colour, float speed, float life,
                          MenuMoteShape shape = MenuMoteShape.Dot, float spreadDegrees = 360f,
                          float direction = 90f, float gravity = 0f, float size = 1f)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (direction + Random.Range(-spreadDegrees, spreadDegrees) * 0.5f) * Mathf.Deg2Rad;
                float v = speed * Random.Range(0.55f, 1.25f);
                Emit(position, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * v,
                     colour, life * Random.Range(0.7f, 1.3f), shape, gravity, 1.4f, size,
                     twinkle: shape == MenuMoteShape.Spark);
            }
        }

        /// <summary>Removes every mote at once — used when a screen is left.</summary>
        public void Clear()
        {
            if (_alive == 0) return;
            _alive = 0;
            SetVerticesDirty();
        }

        /// <summary>Advances every mote. Public so an EditMode test can drive it without a clock.</summary>
        public void Tick(float dt)
        {
            if (_alive == 0 || dt <= 0f) return;
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
            var tex = SoftAvailable ? _art.TitleMote.texture : _art.Atlas;
            if (tex == null) return;
            var rect = GetPixelAdjustedRect();

            for (int i = 0; i < _alive; i++)
            {
                var m = _motes[i];
                var sprite = SpriteFor(m.Shape);
                if (sprite == null) continue;
                var r = sprite.textureRect;

                float t = m.Life / m.MaxLife;                       // 1 at birth, 0 at death
                // Arrive at once, leave on a curve: a mote that fades linearly spends half its
                // life looking like it is already gone.
                float a = t > 0.78f ? 1f : Mathf.SmoothStep(0f, 1f, t / 0.78f);
                if (m.Twinkle) a *= 0.6f + 0.4f * Mathf.Sin((_time + i * 0.41f) * 21f);
                var c = m.Colour;
                c.a *= a;
                Color32 c32 = c;

                float w = r.width * m.Size, h = r.height * m.Size;
                float x = rect.xMin + m.Position.x - w * 0.5f;
                float y = rect.yMin + m.Position.y - h * 0.5f;
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

        private Sprite SpriteFor(MenuMoteShape shape)
        {
            if (SoftAvailable)
            {
                // Only two shapes exist on that page. Anything else falls back to the dot rather
                // than to an atlas sprite, because a sprite from the OTHER texture would be read
                // through this one's UVs and draw a slice of whatever happens to sit there.
                return shape == MenuMoteShape.Spark && _art.TitleSpark != null
                       && _art.TitleSpark.texture == _art.TitleMote.texture
                    ? _art.TitleSpark
                    : _art.TitleMote;
            }
            switch (shape)
            {
                case MenuMoteShape.Spark: return _art.MoteSpark;
                case MenuMoteShape.Glow: return _art.MoteGlow;
                case MenuMoteShape.Ring: return _art.MoteRing;
                default: return _art.MoteDot;
            }
        }
    }
}
