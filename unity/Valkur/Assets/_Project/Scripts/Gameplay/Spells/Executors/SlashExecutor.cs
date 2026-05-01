using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Arc slash with epic VFX: physics arc-overlap + per-target combat feedback,
    /// plus a Light2D flash, swoosh particles, and screen shake.
    /// </summary>
    public class SlashExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float arc = ctx.Spell.arcRangeDegrees > 0 ? ctx.Spell.arcRangeDegrees : 90f;
            float hitRadius = ctx.Spell.hitRadius > 0 ? ctx.Spell.hitRadius : ctx.Spell.range;
            if (hitRadius <= 0) hitRadius = 1.5f;

            Vector2 center = (Vector2)ctx.Caster.position + ctx.Direction * (hitRadius * 0.5f);
            var hits = Physics2D.OverlapCircleAll(center, hitRadius, ctx.TargetLayers);

            int hitCount = 0;
            foreach (var hit in hits)
            {
                if (hit.gameObject == ctx.Caster.gameObject) continue;
                var health = hit.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                Vector2 toTarget = (hit.transform.position - ctx.Caster.position).normalized;
                float angle = Vector2.Angle(ctx.Direction, toTarget);
                if (angle <= arc * 0.5f)
                {
                    health.TakeDamage(Mathf.RoundToInt(ctx.Spell.damage));
                    var feedback = hit.GetComponent<CombatFeedback>();
                    if (feedback != null) feedback.ApplyKnockback(ctx.Caster.position);
                    hitCount++;
                }
            }

            // Color: prefer SpellDefinition tint
            Color slashColor = ctx.Spell.particleColor != Color.clear
                ? ctx.Spell.particleColor
                : new Color(1f, 1f, 1f, 0.85f);

            // Epic procedural arc swipe + Light2D + sparks
            SlashArcFX.Spawn((Vector2)ctx.Caster.position, ctx.Direction, hitRadius, arc, slashColor);

            // Legacy preset (additional VFX)
            if (VFXManager.Instance != null)
            {
                Vector3 vfxPos = ctx.Caster.position + (Vector3)(ctx.Direction * (hitRadius * 0.5f));
                VFXManager.Instance.SpawnSlashArc(vfxPos, ctx.Direction, slashColor, arc, hitRadius, 0.2f);
            }

            if (hitCount > 0) CameraShake.Trigger(0.18f, 0.18f);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById(hitCount > 0 ? "spell_slash_hit" : "spell_slash_swing");
        }
    }

    /// <summary>Procedural slash arc: swept ring strip + spark particles + Light2D pop.</summary>
    internal class SlashArcFX : MonoBehaviour
    {
        private const float Life = 0.30f;
        private float _age;
        private SpriteRenderer _arc;
        private Color _color;
        private float _radius;
        private GameObject _lightGo;
        private Component _light;

        public static void Spawn(Vector2 origin, Vector2 dir, float radius, float arcDeg, Color color)
        {
            var go = new GameObject("SlashArcFX");
            go.transform.position = origin + dir * (radius * 0.5f);
            go.transform.rotation = Quaternion.AngleAxis(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, Vector3.forward);
            var fx = go.AddComponent<SlashArcFX>();
            fx._color = color;
            fx._radius = radius;
            fx.Build(arcDeg);
        }

        private void Build(float arcDeg)
        {
            ElementalSprites.EnsureAll();
            var arcGo = new GameObject("Arc");
            arcGo.transform.SetParent(transform, false);
            arcGo.transform.localScale = new Vector3(_radius, _radius * 0.8f, 1f);
            _arc = arcGo.AddComponent<SpriteRenderer>();
            _arc.sprite = ElementalSprites.Blade != null ? ElementalSprites.Blade : ElementalSprites.Glow;
            _arc.color = _color;
            _arc.sortingLayerID = SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_VFX);
            _arc.sortingLayerName = Valkur.Core.SortingConfig.LAYER_VFX;
            _arc.sortingOrder = 60;
            _arc.material = ElementalSprites.SharedUnlitMaterial;

            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _lightGo = new GameObject("SlashLight");
                _lightGo.transform.SetParent(transform, false);
                try
                {
                    _light = _lightGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _color);
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.6f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.2f);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.2f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                }
                catch { }
            }
            _ = arcDeg; // future: shape arc to match degrees
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / Life;
            if (t >= 1f) { if (_lightGo != null) Destroy(_lightGo); Destroy(gameObject); return; }
            float fade = 1f - t;
            float swing = Mathf.Lerp(-0.4f, 0.4f, t);
            transform.localRotation = Quaternion.AngleAxis(swing * 30f, Vector3.forward) * transform.localRotation;
            if (_arc != null)
            {
                var c = _arc.color; c.a = _color.a * fade; _arc.color = c;
                _arc.transform.localScale = new Vector3(_radius * (0.9f + 0.2f * t), _radius * 0.8f * fade, 1f);
            }
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.6f * fade); }
                catch { }
            }
        }
    }
}
