using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Launches a firework projectile (Fire-element procedural trail). At cast time
    /// emits a colorful spark fountain at the caster (launch flash). Mirrors Python's
    /// FireworkLaunchResolver. Reuses ProjectileExecutor for physics; the projectile
    /// carries an <see cref="ElementalProjectileVisual"/> in <c>Fire</c> mode so impact
    /// produces a fiery starburst via <see cref="ElementalImpactFX"/>.
    /// </summary>
    public class FireworkLaunchExecutor : ISpellExecutor
    {
        private static readonly ProjectileExecutor _projExecutor = new ProjectileExecutor();

        // Multi-color firework palette (red, gold, green, magenta, cyan)
        private static readonly Color[] FireworkColors =
        {
            new Color(1.00f, 0.30f, 0.20f, 1f),
            new Color(1.00f, 0.85f, 0.20f, 1f),
            new Color(0.40f, 1.00f, 0.30f, 1f),
            new Color(1.00f, 0.45f, 1.00f, 1f),
            new Color(0.30f, 0.85f, 1.00f, 1f),
        };

        public void Execute(SpellContext ctx)
        {
            _projExecutor.Execute(ctx);

            // Launch flash shares the projectile's Fireball-derived muzzle point.
            SpawnLaunchBurst(ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell));

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_firework_launch");

        }

        private static void SpawnLaunchBurst(Vector3 pos)
        {
            ElementalSprites.EnsureAll();
            const int sparkCount = 18;
            for (int i = 0; i < sparkCount; i++)
            {
                var go = new GameObject("FireworkSpark");
                go.transform.position = pos;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ElementalSprites.SparkleStar;
                sr.color = FireworkColors[Random.Range(0, FireworkColors.Length)];
                sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = 50;
                sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;

                float ang = (i / (float)sparkCount) * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                Vector2 vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(2.5f, 4.5f);

                var spark = go.AddComponent<FireworkSpark>();
                spark.Init(vel, Random.Range(0.45f, 0.70f), Random.Range(0.18f, 0.30f));
            }

            // Central flash on Light2D (if URP)
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                var lgo = new GameObject("FireworkFlash");
                lgo.transform.position = pos;
                try
                {
                    var l = lgo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(l, System.Enum.ToObject(lt.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(l, new Color(1f, 0.85f, 0.4f, 1f));
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(l, 2.5f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(l, 2.4f);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(l, 0.4f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(l, 0.85f);
                }
                catch { }
                Object.Destroy(lgo, 0.20f);
            }
        }
    }

    /// <summary>Multi-color firework spark with drag, gravity, fade, scale-down.</summary>
    internal class FireworkSpark : MonoBehaviour
    {
        private Vector2 _vel;
        private float _life, _age, _scale;
        private SpriteRenderer _sr;

        public void Init(Vector2 velocity, float lifetime, float scale)
        {
            _vel = velocity;
            _life = Mathf.Max(0.05f, lifetime);
            _scale = scale;
            transform.localScale = Vector3.one * scale;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            float t = _age / _life;
            if (t >= 1f) { Destroy(gameObject); return; }

            _vel *= 1f - 1.6f * dt;
            _vel.y -= 4f * dt;     // gravity
            transform.position += (Vector3)(_vel * dt);

            transform.localScale = Vector3.one * _scale * (1f - 0.5f * t);
            transform.Rotate(0f, 0f, 360f * dt);
            if (_sr != null)
            {
                var c = _sr.color;
                c.a = (1f - t);
                _sr.color = c;
            }
        }
    }
}
