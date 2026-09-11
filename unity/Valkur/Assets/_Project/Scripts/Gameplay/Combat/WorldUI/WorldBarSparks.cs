using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The event particles of one rig: shards off a blow, motes off a heal, sparks where mana was
    /// spent and a burst when the dash comes back. Each is one texel of the bar's own atlas.
    ///
    /// <para><b>Why not a <c>ParticleSystem</c>.</b> Three reasons, each sufficient. A particle
    /// system per creature is a simulation and a draw call per creature, and a world full of
    /// fighting monsters is exactly when the bars are on screen. Its quads are billboarded in
    /// sub-pixel space and filtered by their own material, so they would be the one soft thing
    /// in a readout built to land on the pixel grid. And the effects here are a handful of
    /// texels living half a second — a fixed pool of sprite renderers sharing the bars' material
    /// batches with the bars themselves and costs nothing when idle.</para>
    ///
    /// <para>Positions are quantised to the screen pixel every frame, never the texel: a spark
    /// moving in texel steps at 5 px a step reads as a stutter, at 1 px a step it reads as
    /// motion.</para>
    /// </summary>
    internal sealed class WorldBarSparks
    {
        private struct Spark
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
            public float Gravity;
            public Color Colour;
        }

        private readonly Transform _root;
        private readonly SpriteRenderer[] _renderers;
        private readonly Spark[] _sparks;
        private int _next;
        private int _alive;

        public WorldBarSparks(Transform parent, int capacity, int sortingOrder)
        {
            var go = new GameObject("Sparks");
            _root = go.transform;
            _root.SetParent(parent, false);
            _root.localPosition = Vector3.zero;
            _root.localScale = Vector3.one;

            capacity = Mathf.Max(1, capacity);
            _renderers = new SpriteRenderer[capacity];
            _sparks = new Spark[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var sgo = new GameObject("Spark" + i);
                sgo.transform.SetParent(_root, false);
                var sr = sgo.AddComponent<SpriteRenderer>();
                sr.sprite = WorldBarArt.Solid;
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = new Vector2(WorldBarGeometry.TEXEL, WorldBarGeometry.TEXEL);
                sr.sharedMaterial = WorldBarArt.Material;
                sr.sortingLayerName = SortingConfig.LAYER_UI_WORLD;
                sr.sortingOrder = sortingOrder;
                sgo.SetActive(false);
                _renderers[i] = sr;
            }
        }

        /// <summary>How many sparks are alive. For the tests.</summary>
        public int Alive => _alive;

        public void SetSortingOrder(int order)
        {
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].sortingOrder = order;
        }

        /// <summary>
        /// Throw one spark. The pool is a ring: when it is full the OLDEST spark is recycled, which
        /// is the right loss — a burst on top of a burst keeps the new one whole.
        /// </summary>
        public void Emit(Vector2 position, Vector2 velocity, Color colour, float life, float gravity)
        {
            int i = _next;
            _next = (_next + 1) % _sparks.Length;
            if (_sparks[i].Life <= 0f) _alive++;
            _sparks[i] = new Spark
            {
                Position = position,
                Velocity = velocity,
                Life = Mathf.Max(0.05f, life),
                MaxLife = Mathf.Max(0.05f, life),
                Gravity = gravity,
                Colour = colour,
            };
            if (!_renderers[i].gameObject.activeSelf) _renderers[i].gameObject.SetActive(true);
        }

        /// <summary>Put every spark away at once — a fade to nothing, a death, a despawn.</summary>
        public void Clear()
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                _sparks[i].Life = 0f;
                if (_renderers[i].gameObject.activeSelf) _renderers[i].gameObject.SetActive(false);
            }
            _alive = 0;
        }

        /// <summary>Advance every live spark. Returns true while any is alive.</summary>
        public bool Tick(float dt, float pixelsPerUnit, float rigAlpha)
        {
            if (_alive == 0) return false;
            int alive = 0;
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (_sparks[i].Life <= 0f) continue;
                var s = _sparks[i];
                s.Life -= dt;
                var sr = _renderers[i];
                if (s.Life <= 0f)
                {
                    s.Life = 0f;
                    _sparks[i] = s;
                    if (sr.gameObject.activeSelf) sr.gameObject.SetActive(false);
                    continue;
                }
                s.Velocity.y -= s.Gravity * dt;
                s.Velocity *= 1f - Mathf.Clamp01(2.2f * dt);   // a little air, so bursts settle
                s.Position += s.Velocity * dt;
                _sparks[i] = s;
                alive++;

                float x = WorldBarGeometry.QuantizeToPixel(s.Position.x, pixelsPerUnit);
                float y = WorldBarGeometry.QuantizeToPixel(s.Position.y, pixelsPerUnit);
                sr.transform.localPosition = new Vector3(x, y, 0f);

                // Full for the first half of its life, then out: a linear fade from birth reads as
                // a spark that was never bright.
                float k = s.Life / s.MaxLife;
                var c = s.Colour;
                c.a *= Mathf.Clamp01(k * 2f) * rigAlpha;
                if (sr.color != c) sr.color = c;
            }
            _alive = alive;
            return alive > 0;
        }
    }
}
