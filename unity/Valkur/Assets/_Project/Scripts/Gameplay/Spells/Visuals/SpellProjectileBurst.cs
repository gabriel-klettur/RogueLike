using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The three moments a projectile has that are not flight: crossing a body, locking onto
    /// one, and arriving. Each is a discrete EVENT, and each is shaped by the silhouette that
    /// produced it — a blade throws sparks and chips, a wisp exhales, a lance sheds shards.
    ///
    /// <para>Before this every one of those beats routed to <c>IVFXService.SpawnImpact</c>, so a
    /// pierce, a homing lock and a final hit were the same round blob in three sizes. A mechanic
    /// whose feedback is indistinguishable from every other mechanic's is one the player cannot
    /// learn.</para>
    /// </summary>
    internal static class SpellProjectileBurst
    {
        public static void Pierce(Vector3 position, Vector2 direction,
            ProjectileVisualProfile profile, float power)
        {
            Vector2 side = new Vector2(-direction.y, direction.x);
            int count = profile.Silhouette == ProjectileSilhouette.Blade ? 5 : 8;

            for (int i = 0; i < count; i++)
            {
                // Sprayed BACKWARD off the contact, because the projectile kept going: debris
                // thrown forward would read as the shot having stopped.
                Vector2 velocity = -direction * Random.Range(1.2f, 3.4f)
                                 + side * Random.Range(-2.2f, 2.2f);
                Spawn(position, velocity, profile,
                      Random.Range(0.055f, 0.115f) * power,
                      Random.Range(0.16f, 0.30f));
            }

            Flash(position, profile, 0.42f * power, 0.10f);
        }

        /// <summary>
        /// The one beat that happens on the TARGET rather than on the projectile. It is a ring
        /// rather than a burst on purpose: a burst says something arrived, and nothing has.
        /// </summary>
        public static void Lock(Vector3 position, ProjectileVisualProfile profile)
        {
            var go = new GameObject("HomingLock");
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ElementalSprites.Ring;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = 4;
            sr.color = profile.Palette.hotCore;

            var fade = go.AddComponent<SpellProjectileFlash>();
            fade.Begin(sr, profile.Palette.hotCore, 1.55f, 0.72f, 0.34f, contract: true);
        }

        public static void Impact(Vector3 position, Vector2 direction,
            ProjectileVisualProfile profile, float power)
        {
            Vector2 side = new Vector2(-direction.y, direction.x);
            int count = profile.Silhouette == ProjectileSilhouette.Wisp ? 6 : 14;

            for (int i = 0; i < count; i++)
            {
                float spread = Random.Range(-1f, 1f);
                Vector2 velocity = -direction * Random.Range(0.4f, 2.2f)
                                 + side * spread * Random.Range(1.4f, 3.8f);
                Spawn(position, velocity, profile,
                      Random.Range(0.06f, 0.14f),
                      Random.Range(0.20f, 0.42f));
            }

            Flash(position, profile, 0.85f * Mathf.Lerp(0.6f, 1f, power), 0.18f);
        }

        private static void Flash(Vector3 position, ProjectileVisualProfile profile,
            float scale, float life)
        {
            var go = new GameObject("ProjectileFlash");
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ElementalSprites.HotCore;
            // A blade's impact still flashes — steel striking anything throws a spark — but it
            // is the only bright frame in the whole spell, which is what keeps Martial Forms
            // distinguishable from eight schools of magic.
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = 6;

            var flash = go.AddComponent<SpellProjectileFlash>();
            flash.Begin(sr, profile.Palette.hotCore, scale, 0.95f, life, contract: false);
        }

        private static void Spawn(Vector3 position, Vector2 velocity,
            ProjectileVisualProfile profile, float size, float life)
        {
            Sprite sprite;
            bool additive;
            Color color;

            switch (profile.Silhouette)
            {
                case ProjectileSilhouette.Blade:
                    // Law L3, and here it is the whole point: chips of what was hit, dark and
                    // opaque, are the only thing that says a metal object struck a solid one.
                    sprite = KiSprites.Pebble;
                    additive = false;
                    color = Color.Lerp(profile.Palette.accent, Color.black, 0.45f);
                    break;
                case ProjectileSilhouette.Lance:
                    sprite = IceSprites.Debris;
                    additive = true;
                    color = profile.Palette.core;
                    break;
                case ProjectileSilhouette.Wisp:
                    sprite = ElementalSprites.Wisp;
                    additive = true;
                    color = profile.Palette.glow;
                    break;
                default:
                    sprite = ElementalSprites.Sparkle;
                    additive = true;
                    color = profile.Palette.core;
                    break;
            }

            var go = new GameObject("ProjectileDebris");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = 5;
            sr.color = color;

            var debris = go.AddComponent<SpellProjectileDebris>();
            debris.Begin(sr, velocity, size, life, color,
                         gravity: profile.Silhouette == ProjectileSilhouette.Wisp ? -0.4f : 1.1f);
        }
    }

    /// <summary>A single thrown chip. Self-destructs; nothing pools these because a burst is rare.</summary>
    internal sealed class SpellProjectileDebris : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Vector2 _velocity;
        private float _life, _age, _size, _spin, _gravity;
        private Color _color;

        public void Begin(SpriteRenderer sr, Vector2 velocity, float size, float life,
            Color color, float gravity)
        {
            _renderer = sr;
            _velocity = velocity;
            _size = size;
            _life = Mathf.Max(0.05f, life);
            _color = color;
            _gravity = gravity;
            _spin = Random.Range(-540f, 540f);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float u = Mathf.Clamp01(_age / _life);

            transform.position += (Vector3)(_velocity * Time.deltaTime);
            _velocity *= Mathf.Exp(-3.2f * Time.deltaTime);
            _velocity += Vector2.down * (_gravity * Time.deltaTime);
            transform.Rotate(0f, 0f, _spin * Time.deltaTime);
            transform.localScale = Vector3.one * (_size * Mathf.Lerp(1f, 0.2f, u));

            if (_renderer != null)
                _renderer.color = new Color(_color.r, _color.g, _color.b,
                                            _color.a * (1f - u) * (1f - u));

            if (_age >= _life) Destroy(gameObject);
        }
    }

    /// <summary>A one-shot expanding or contracting sprite. Used for flashes and lock rings.</summary>
    internal sealed class SpellProjectileFlash : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Color _color;
        private float _scale, _alpha, _life, _age;
        private bool _contract;

        public void Begin(SpriteRenderer sr, Color color, float scale, float alpha,
            float life, bool contract)
        {
            _renderer = sr;
            _color = color;
            _scale = scale;
            _alpha = alpha;
            _life = Mathf.Max(0.04f, life);
            _contract = contract;
            transform.localScale = Vector3.one * (contract ? scale : scale * 0.35f);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float u = Mathf.Clamp01(_age / _life);

            // A lock ring closes onto its target and a hit flash opens away from the contact.
            // The direction is the whole difference between "you are marked" and "you were hit".
            float s = _contract
                ? Mathf.Lerp(_scale, _scale * 0.55f, u)
                : Mathf.Lerp(_scale * 0.35f, _scale, u);
            transform.localScale = Vector3.one * s;

            if (_renderer != null)
                _renderer.color = new Color(_color.r, _color.g, _color.b,
                                            _alpha * (1f - u));

            if (_age >= _life) Destroy(gameObject);
        }
    }
}
