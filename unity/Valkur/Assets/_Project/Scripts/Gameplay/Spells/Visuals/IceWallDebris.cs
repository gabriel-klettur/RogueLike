using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// One flying chunk of a broken crystal: thrown outward, pulled down, spinning, fading.
    ///
    /// <para>The pieces are deliberately NOT parented to the wall. The killing blow destroys
    /// the wall's GameObject in the same frame it shatters it, and a child would be destroyed
    /// mid-flight — the shatter would play for exactly one frame and vanish, which is the
    /// same class of bug as an <see cref="AreaFXRig"/> whose particles are cut off by the
    /// object being destroyed underneath them.</para>
    /// </summary>
    public sealed class IceWallDebris : MonoBehaviour
    {
        private const float Gravity = 9.5f;
        private const float LifeSeconds = 0.75f;

        private SpriteRenderer _renderer;
        private Vector2 _velocity;
        private float _spin;
        private float _age;
        private float _life;

        /// <summary>
        /// Throw <paramref name="count"/> chunks from <paramref name="origin"/>, biased along
        /// <paramref name="outward"/> — the chunks of a wall fly off its FACE, not along the
        /// line it stands on.
        /// </summary>
        public static void Burst(Vector3 origin, Vector2 outward, int count, float speed, float size)
        {
            // Object.Destroy is an error outside Play Mode, and EditMode tests build gameplay
            // objects freely: a burst there would log errors indistinguishable from real ones.
            if (!Application.isPlaying) return;

            IceSprites.EnsureAll();
            if (outward.sqrMagnitude < 1e-4f) outward = Vector2.up;
            outward.Normalize();

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("IceDebris");
                go.transform.position = origin + (Vector3)(Random.insideUnitCircle * size * 0.8f);
                go.transform.localScale = Vector3.one * Random.Range(size * 0.6f, size * 1.5f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = IceSprites.Debris;
                renderer.color = Color.white;
                renderer.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
                renderer.sortingLayerName = SortingConfig.LAYER_VFX;
                renderer.sortingOrder = 64;

                var debris = go.AddComponent<IceWallDebris>();
                debris._renderer = renderer;
                // A cone around the face normal, plus a real upward component: chunks that
                // only travel sideways read as sliding rather than as being blown off.
                float spread = Random.Range(-0.85f, 0.85f);
                Vector2 direction = (outward * (1f - Mathf.Abs(spread) * 0.5f) +
                                     new Vector2(-outward.y, outward.x) * spread).normalized;
                debris._velocity = direction * Random.Range(speed * 0.45f, speed) +
                                   Vector2.up * Random.Range(speed * 0.35f, speed * 0.8f);
                debris._spin = Random.Range(-720f, 720f);
                debris._life = LifeSeconds * Random.Range(0.7f, 1.3f);
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
            transform.localScale *= 1f - 0.6f * Time.deltaTime;
        }
    }
}
