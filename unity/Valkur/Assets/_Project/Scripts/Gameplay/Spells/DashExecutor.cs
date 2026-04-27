using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Dash with epic motion-blur ghost trail and speed-line VFX.
    /// Spawns 6 ghost frames between origin and destination, plus a Light2D streak
    /// and screen shake. Mirrors Python's DashResolver damage/knockback rules.
    /// </summary>
    public class DashExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;
            Vector2 startPos = ctx.Caster.position;
            var rb = ctx.Caster.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.MovePosition(rb.position + ctx.Direction * dist);
            Vector2 endPos = startPos + ctx.Direction * dist;

            // VFX: ghost trail
            var casterSr = ctx.Caster.GetComponentInChildren<SpriteRenderer>();
            DashTrailFX.Spawn(startPos, endPos, ctx.Direction, casterSr);

            CameraShake.Trigger(0.12f, 0.15f);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_dash_whoosh");

            // Collision damage + knockback
            if (ctx.Spell.collisionDamage > 0)
            {
                var hits = Physics2D.OverlapCircleAll(ctx.Caster.position, 1f, ctx.TargetLayers);
                foreach (var hit in hits)
                {
                    if (hit.gameObject == ctx.Caster.gameObject) continue;
                    var health = hit.GetComponent<Health>();
                    if (health != null && !health.IsDead)
                    {
                        health.TakeDamage(Mathf.RoundToInt(ctx.Spell.collisionDamage));
                        if (ctx.Spell.knockback > 0)
                        {
                            var hitRb = hit.GetComponent<Rigidbody2D>();
                            if (hitRb != null)
                            {
                                Vector2 knockDir = ((Vector2)hit.transform.position - (Vector2)ctx.Caster.position).normalized;
                                hitRb.AddForce(knockDir * ctx.Spell.knockback, ForceMode2D.Impulse);
                            }
                        }
                    }
                }
            }

            Debug.Log($"[SpellDebug] Dash from {startPos} dist={dist:F1}, collisionDmg={ctx.Spell.collisionDamage}");
        }
    }

    /// <summary>Spawns a chain of fading ghost sprites + speed-line Light2D streak.</summary>
    internal class DashTrailFX : MonoBehaviour
    {
        private const int GhostCount = 6;
        private const float Life = 0.35f;
        private float _age;
        private SpriteRenderer[] _ghosts;
        private GameObject _lightGo;
        private Component _light;

        public static void Spawn(Vector2 from, Vector2 to, Vector2 dir, SpriteRenderer source)
        {
            var go = new GameObject("DashTrailFX");
            go.transform.position = from;
            var fx = go.AddComponent<DashTrailFX>();
            fx.Build(from, to, dir, source);
        }

        private void Build(Vector2 from, Vector2 to, Vector2 dir, SpriteRenderer source)
        {
            _ghosts = new SpriteRenderer[GhostCount];
            ElementalSprites.EnsureAll();
            Sprite sprite = source != null && source.sprite != null ? source.sprite : ElementalSprites.Glow;
            float baseAlpha = 0.55f;

            for (int i = 0; i < GhostCount; i++)
            {
                float t = (i + 1f) / (GhostCount + 1f);
                var ghostGo = new GameObject($"Ghost_{i}");
                ghostGo.transform.SetParent(transform, false);
                ghostGo.transform.position = Vector2.Lerp(from, to, t);
                var sr = ghostGo.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = new Color(0.55f, 0.75f, 1f, baseAlpha * (1f - t));
                sr.sortingLayerID = SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_VFX);
                sr.sortingLayerName = Valkur.Core.SortingConfig.LAYER_VFX;
                sr.sortingOrder = 40;
                if (source != null) ghostGo.transform.localScale = source.transform.lossyScale;
                _ghosts[i] = sr;
            }

            // Light2D streak at midpoint
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _lightGo = new GameObject("DashLight");
                _lightGo.transform.SetParent(transform, false);
                _lightGo.transform.position = Vector2.Lerp(from, to, 0.5f);
                try
                {
                    _light = _lightGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, new Color(0.55f, 0.75f, 1f, 1f));
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.0f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, Mathf.Max(1.5f, Vector2.Distance(from, to) * 0.6f));
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.3f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.9f);
                }
                catch { }
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / Life;
            if (t >= 1f) { if (_lightGo != null) Destroy(_lightGo); Destroy(gameObject); return; }
            float fade = 1f - t;
            if (_ghosts != null)
            {
                foreach (var sr in _ghosts)
                {
                    if (sr == null) continue;
                    var c = sr.color; c.a *= 1f - Time.deltaTime * 3.5f; sr.color = c;
                }
            }
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.0f * fade); }
                catch { }
            }
        }
    }
}
