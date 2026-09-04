using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The two one-shots a failing barrier throws: chips of dead weave, and the flash-and-ring
    /// where a panel let go.
    ///
    /// <para>UNPARENTED ON PURPOSE, for the reason <see cref="IceWallDebris"/> records: the
    /// killing blow destroys the wall's GameObject in the same frame it shatters it, so a
    /// child would be destroyed mid-flight and the shatter would play for exactly one frame.
    /// </para>
    ///
    /// <para>WHY NOT REUSE THE ICE ONES. Both bake their sprite and their colour — a chip is
    /// <c>IceSprites.Debris</c> tinted white, a burst is two fixed pale blues. Adding colour
    /// parameters to them would be a change to a shipped effect for the benefit of a different
    /// one; these are forty lines each and take the palette as an argument, which is the whole
    /// difference.</para>
    /// </summary>
    public sealed class ArcaneWeaveFX : MonoBehaviour
    {
        private const float Gravity = 6.2f;

        private SpriteRenderer _renderer;
        private Vector2 _velocity;
        private float _spin;
        private float _age;
        private float _life;

        /// <summary>
        /// Throw chips of failed weave from <paramref name="origin"/>, biased along
        /// <paramref name="outward"/> — a barrier sheds off its FACE, not along its own line.
        ///
        /// <para>Gravity is deliberately weaker than the ice wall's 9.5: these are not lumps of
        /// matter, they are pieces of a spell coming apart, and they should hang before they
        /// dissolve. They also fade on an ADDITIVE material, so they go out like embers rather
        /// than dropping out of sight like rubble.</para>
        /// </summary>
        public static void Chips(Vector3 origin, Vector2 outward, int count, float speed,
            float size, Color tint)
        {
            // Object.Destroy is an error outside Play Mode and EditMode tests build gameplay
            // objects freely: a burst there would log errors indistinguishable from real ones.
            if (!Application.isPlaying) return;

            ArcaneSprites.EnsureAll();
            if (outward.sqrMagnitude < 1e-4f) outward = Vector2.up;
            outward.Normalize();

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("ArcaneWeaveChip");
                go.transform.position = origin + (Vector3)(Random.insideUnitCircle * size * 0.8f);
                go.transform.localScale = Vector3.one * Random.Range(size * 0.6f, size * 1.5f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = ArcaneSprites.Shard;
                renderer.color = tint;
                renderer.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                renderer.sortingLayerName = SortingConfig.LAYER_VFX;
                renderer.sortingOrder = 64;

                var chip = go.AddComponent<ArcaneWeaveFX>();
                chip._renderer = renderer;
                float spread = Random.Range(-0.85f, 0.85f);
                Vector2 direction = (outward * (1f - Mathf.Abs(spread) * 0.5f) +
                                     new Vector2(-outward.y, outward.x) * spread).normalized;
                chip._velocity = direction * Random.Range(speed * 0.45f, speed) +
                                 Vector2.up * Random.Range(speed * 0.30f, speed * 0.75f);
                chip._spin = Random.Range(-540f, 540f);
                chip._life = 0.85f * Random.Range(0.7f, 1.3f);
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _life) { Destroy(gameObject); return; }

            _velocity.y -= Gravity * Time.deltaTime;
            transform.position += (Vector3)(_velocity * Time.deltaTime);
            transform.Rotate(0f, 0f, _spin * Time.deltaTime);

            float remaining = 1f - _age / _life;
            var color = _renderer.color;
            color.a = Mathf.Clamp01(remaining * 1.6f);
            _renderer.color = color;
            transform.localScale *= 1f - 0.5f * Time.deltaTime;
        }

        /// <summary>
        /// A flash and an expanding ring where the weave gave way, elongated along the
        /// barrier so it reads as damage to a LINE rather than as a generic explosion.
        /// </summary>
        public static void Burst(Vector3 origin, float radius, float seconds, Vector2 axis,
            Color hot, Color ring)
        {
            if (!Application.isPlaying) return;

            ElementalSprites.EnsureAll();

            var go = new GameObject("ArcaneWeaveBurst");
            go.transform.position = origin;

            var fx = go.AddComponent<ArcaneWeaveBurst>();
            fx.Initialize(Mathf.Max(0.1f, radius), Mathf.Max(0.05f, seconds), axis, hot, ring);
        }
    }
}
