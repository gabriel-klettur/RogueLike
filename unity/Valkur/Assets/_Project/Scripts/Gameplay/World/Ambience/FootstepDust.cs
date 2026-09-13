using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.World.Ambience
{
    /// <summary>What a step lands on, which decides the colour and the shape of what it kicks up.</summary>
    public enum GroundKind
    {
        /// <summary>Earth, stone, grass: a warm puff that rises and thins.</summary>
        Dust = 0,
        /// <summary>Lying snow: a white puff, larger and slower.</summary>
        Snow = 1,
        /// <summary>Open water: a pale splash that does not rise.</summary>
        Water = 2,
    }

    /// <summary>
    /// The pool of puffs every <see cref="FootstepEmitter"/> draws from — one pool for the
    /// whole world, because thirty walkers each owning a pool is thirty pools mostly empty.
    ///
    /// A puff is a pooled <see cref="SpriteRenderer"/> with its own little life (rise, grow,
    /// fade, drift with the wind), never a ParticleSystem: a system per walker would be a
    /// system per walker, and one shared system cannot sort each puff under the feet that made
    /// it. Sorting is what makes this read as dust UNDER a creature rather than smoke on it.
    ///
    /// Domain Reload is OFF: the root and the pooled objects belong to the Play session that
    /// made them, and a stale reference would surface as a MissingReferenceException.
    /// </summary>
    public static class FootstepDust
    {
        private const int   PoolCap = 40;
        private const float PuffLife = 0.55f;

        private static readonly Stack<FootstepPuff> s_free = new Stack<FootstepPuff>();
        private static readonly List<FootstepPuff>  s_all  = new List<FootstepPuff>();
        private static Transform s_root;
        private static Sprite    s_sprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_free.Clear();
            s_all.Clear();
            s_root   = null;
            s_sprite = null;
        }

        /// <summary>Puffs alive right now. Test seam.</summary>
        public static int LiveCount
        {
            get { int n = 0; foreach (var p in s_all) if (p != null && p.IsLive) n++; return n; }
        }

        /// <summary>Colour a puff of <paramref name="kind"/> is born with. Pure.</summary>
        public static Color ColourFor(GroundKind kind)
        {
            switch (kind)
            {
                case GroundKind.Snow:  return new Color(0.95f, 0.96f, 1.00f, 0.50f);
                case GroundKind.Water: return new Color(0.62f, 0.76f, 0.88f, 0.55f);
                default:               return new Color(0.66f, 0.58f, 0.46f, 0.34f);
            }
        }

        /// <summary>
        /// Kick up one puff at <paramref name="position"/>, sorting on <paramref name="sortingLayerId"/>
        /// at <paramref name="sortingOrder"/>. Returns null when the pool is spent, which is not
        /// an error: dust that never appears is dust nobody misses.
        /// </summary>
        public static FootstepPuff Spawn(Vector3 position, GroundKind kind, int sortingLayerId, int sortingOrder)
        {
            if (!WorldLookSettings.Footsteps) return null;
            var puff = Take();
            if (puff == null) return null;
            puff.Begin(position, kind, sortingLayerId, sortingOrder, PuffLife);
            return puff;
        }

        internal static void Release(FootstepPuff puff)
        {
            if (puff == null) return;
            s_free.Push(puff);
        }

        private static FootstepPuff Take()
        {
            while (s_free.Count > 0)
            {
                var p = s_free.Pop();
                if (p != null) return p;
            }
            if (s_all.Count >= PoolCap) return null;

            EnsureRoot();
            var go = new GameObject("FootstepPuff");
            go.transform.SetParent(s_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite;
            // Lit, like the ground it came off: night dust that glowed would be a firefly.
            var mat = WorldSpriteMaterials.World;
            if (mat != null) sr.sharedMaterial = mat;
            var puff = go.AddComponent<FootstepPuff>();
            s_all.Add(puff);
            return puff;
        }

        private static void EnsureRoot()
        {
            if (s_root != null) return;
            var go = new GameObject("[FootstepDust]");
            var vfx = GameObject.Find("[VFX]");
            if (vfx != null) go.transform.SetParent(vfx.transform, false);
            s_root = go.transform;
        }

        /// <summary>A soft round dot, 1 world unit across at scale 1. Coloured by the renderer.</summary>
        public static Sprite Sprite
        {
            get
            {
                if (s_sprite != null) return s_sprite;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    name = "FootstepPuff", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
                };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r  = Mathf.Sqrt(dx * dx + dy * dy);
                    // A cushion, not a gaussian: a real puff has a body and a soft skirt.
                    float a  = 1f - Mathf.SmoothStep(0.25f, 1f, r);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
                s_sprite.hideFlags = HideFlags.HideAndDontSave;
                return s_sprite;
            }
        }

        /// <summary>Tear the pool down. Tests only; Play Mode lets the scene take it.</summary>
        public static void DestroyAllForTests()
        {
            foreach (var p in s_all) if (p != null) Object.DestroyImmediate(p.gameObject);
            s_all.Clear();
            s_free.Clear();
            if (s_root != null) Object.DestroyImmediate(s_root.gameObject);
            s_root = null;
        }
    }

    /// <summary>One puff of dust. Pooled; returns itself to <see cref="FootstepDust"/> when it dies.</summary>
    public sealed class FootstepPuff : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Color   _colour;
        private float   _life, _t;
        private GroundKind _kind;
        private Vector3 _origin;
        private float   _scaleFrom, _scaleTo, _rise;

        /// <summary>True while the puff is on screen.</summary>
        public bool IsLive { get; private set; }

        /// <summary>The kind this puff was born as. Test seam.</summary>
        public GroundKind Kind => _kind;

        /// <summary>The colour the puff was born with. Test seam.</summary>
        public Color BornColour => _colour;

        internal void Begin(Vector3 position, GroundKind kind, int sortingLayerId, int sortingOrder, float life)
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            _kind   = kind;
            _colour = FootstepDust.ColourFor(kind);
            _origin = position;
            _life   = life;
            _t      = 0f;
            IsLive  = true;

            bool splash = kind == GroundKind.Water;
            _scaleFrom = splash ? 0.30f : 0.22f;
            _scaleTo   = kind == GroundKind.Snow ? 0.70f : splash ? 0.55f : 0.50f;
            _rise      = splash ? 0f : kind == GroundKind.Snow ? 0.08f : 0.14f;

            _sr.sortingLayerID = sortingLayerId;
            _sr.sortingOrder   = sortingOrder;
            _sr.color          = _colour;
            transform.position   = position;
            transform.localScale = Vector3.one * _scaleFrom;
            gameObject.SetActive(true);
            _sr.enabled = true;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advance the puff. Public so a test can run one out without a player loop.</summary>
        public void Tick(float dt)
        {
            if (!IsLive) return;
            _t += dt;
            float u = Mathf.Clamp01(_t / _life);

            float scale = Mathf.Lerp(_scaleFrom, _scaleTo, 1f - (1f - u) * (1f - u));
            transform.localScale = Vector3.one * scale;

            // Rises, thins, and is carried by the same wind the leaves lean with.
            var p = _origin;
            p.y += _rise * u;
            p.x += WeatherWind.VelocityX * 0.25f * _t;
            transform.position = p;

            var c = _colour;
            c.a = _colour.a * (1f - u * u);
            _sr.color = c;

            if (u >= 1f) End();
        }

        private void End()
        {
            IsLive = false;
            _sr.enabled = false;
            gameObject.SetActive(false);
            FootstepDust.Release(this);
        }
    }
}
