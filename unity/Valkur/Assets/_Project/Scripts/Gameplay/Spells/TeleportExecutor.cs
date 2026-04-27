using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Teleport with epic twin-portal VFX: an arcane ring at origin (collapsing) and
    /// at destination (expanding), plus a flash of star particles and a Light2D pop
    /// at each end. Mirrors Python's TeleportResolver for distance/snapping logic.
    /// </summary>
    public class TeleportExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 4f;
            Vector2 origin = ctx.Caster.position;
            Vector2 destination = origin + ctx.Direction * dist;

            var rb = ctx.Caster.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                const int blockingMask = (1 << 11) | (1 << 14); // World + Building
                var hit = Physics2D.CircleCast(origin, 0.3f, ctx.Direction, dist, blockingMask);
                if (hit.collider != null)
                    destination = origin + ctx.Direction * Mathf.Max(0f, hit.distance - 0.4f);

                rb.MovePosition(destination);
            }
            else
            {
                ctx.Caster.position = (Vector3)destination;
            }

            // Epic VFX
            ElementalImpactFX.Spawn(origin, SpellElement.Arcane);
            ElementalImpactFX.Spawn(destination, SpellElement.Arcane);

            // Twin spinning portals
            TeleportPortalFX.Spawn(origin, collapsing: true);
            TeleportPortalFX.Spawn(destination, collapsing: false);

            CameraShake.Trigger(0.18f, 0.20f);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null)
            {
                audio.PlaySfxById("spell_teleport_depart");
                audio.PlaySfxById("spell_teleport_arrive");
            }

            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, (Vector3)destination);
        }
    }

    /// <summary>Standalone spinning portal: ring + halo + Light2D, animated 0.45s.</summary>
    internal class TeleportPortalFX : MonoBehaviour
    {
        private bool _collapsing;
        private float _age;
        private const float Life = 0.45f;
        private SpriteRenderer _ring, _halo, _glow;
        private GameObject _lightGo;
        private Component _light;

        public static void Spawn(Vector2 pos, bool collapsing)
        {
            var go = new GameObject("TeleportPortalFX");
            go.transform.position = pos;
            var fx = go.AddComponent<TeleportPortalFX>();
            fx._collapsing = collapsing;
            fx.Build();
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();
            _ring = Make("Ring", ElementalSprites.Ring, new Color(0.55f, 0.30f, 1f, 0.95f), 1f, 51);
            _halo = Make("Halo", ElementalSprites.Halo, new Color(0.40f, 0.20f, 0.85f, 0.55f), 1.4f, 50);
            _glow = Make("Glow", ElementalSprites.Glow, new Color(0.75f, 0.50f, 1f, 0.85f), 0.6f, 52);

            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _lightGo = new GameObject("PortalLight");
                _lightGo.transform.SetParent(transform, false);
                try
                {
                    _light = _lightGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, new Color(0.65f, 0.40f, 1f, 1f));
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.6f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 2.4f);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.4f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                }
                catch { }
            }
        }

        private SpriteRenderer Make(string name, Sprite s, Color c, float scale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.color = c;
            sr.sortingLayerID = SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_VFX);
            sr.sortingLayerName = Valkur.Core.SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            sr.material = ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / Life;
            if (t >= 1f) { if (_lightGo != null) Destroy(_lightGo); Destroy(gameObject); return; }
            float scale = _collapsing ? Mathf.Lerp(2.0f, 0.05f, t) : Mathf.Lerp(0.05f, 2.0f, t);
            float alpha = Mathf.Sin(t * Mathf.PI);
            transform.localRotation = Quaternion.Euler(0f, 0f, t * 540f * (_collapsing ? -1f : 1f));
            if (_ring != null) { _ring.transform.localScale = Vector3.one * scale; var c = _ring.color; c.a = 0.95f * alpha; _ring.color = c; }
            if (_halo != null) { _halo.transform.localScale = Vector3.one * scale * 1.4f; var c = _halo.color; c.a = 0.55f * alpha; _halo.color = c; }
            if (_glow != null) { _glow.transform.localScale = Vector3.one * scale * 0.6f; var c = _glow.color; c.a = 0.85f * alpha; _glow.color = c; }
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.6f * alpha); }
                catch { }
            }
        }
    }
}
