using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Particles-only projectile visual. The trail is the spell's trail preset STACK —
    /// <c>vfxPreset</c> plus <c>vfxPresetLayers</c> — parented to the moving projectile;
    /// no SpriteRenderer is drawn on the projectile itself.
    ///
    /// A stack rather than a single preset because one ParticleSystem is one material and
    /// one behaviour: a convincing projectile needs a bright additive core, a wake behind
    /// it, sparks that fall away and an alpha-blended smoke mass, and no single emitter
    /// can be all four at once.
    ///
    /// Impact FX is handled by <see cref="Projectile.OnExpire"/>, which already
    /// spawns <c>SpellDefinition.impactPreset</c> at the hit point — so this
    /// visual only needs to (a) start/stop the trail across pool reuse and
    /// (b) keep any leftover SpriteRenderer disabled.
    /// </summary>
    public class ParticleProjectileVisual : MonoBehaviour, IProjectileVisual
    {
        private readonly List<string> _trailPresetIds = new List<string>();
        private readonly List<GameObject> _trailGos = new List<GameObject>();
        private bool _impacted;

        /// <summary>
        /// Configure the trail preset for this projectile. Safe to call before
        /// <c>OnEnable</c> (cached) or after (re-arms a new preset on the fly).
        /// </summary>
        public void SetSpell(SpellDefinition spell)
        {
            var wanted = spell != null ? spell.CollectVfxPresets() : new List<string>();
            if (SameAsCurrent(wanted)) return;

            _trailPresetIds.Clear();
            _trailPresetIds.AddRange(wanted);
            if (!gameObject.activeInHierarchy) return;

            StopTrail();
            StartTrail();
        }

        private bool SameAsCurrent(List<string> wanted)
        {
            if (wanted.Count != _trailPresetIds.Count) return false;
            for (int i = 0; i < wanted.Count; i++)
                if (wanted[i] != _trailPresetIds[i]) return false;
            return true;
        }

        private void Awake()
        {
            HideRootSpriteRenderer();
        }

        private void OnEnable()
        {
            _impacted = false;
            StartTrail();
        }

        private void OnDisable()
        {
            StopTrail();
        }

        public void OnImpact(Vector3 worldPos)
        {
            // The impact particle preset itself is spawned by Projectile.OnExpire,
            // so this visual just stops emission so the trail doesn't keep
            // pumping particles after the projectile is gone.
            if (_impacted) return;
            _impacted = true;
            StopTrail();
        }

        private void StartTrail()
        {
            if (_trailPresetIds.Count == 0) return;
            if (VFXManager.Instance == null) return;
            if (_trailGos.Count > 0) return;

            for (int i = 0; i < _trailPresetIds.Count; i++)
            {
                var go = VFXManager.Instance.SpawnParticlePreset(
                    _trailPresetIds[i], transform.position, duration: -1f);
                if (go == null) continue;

                go.transform.SetParent(transform, worldPositionStays: true);
                go.transform.localPosition = Vector3.zero;
                _trailGos.Add(go);
            }
        }

        private void StopTrail()
        {
            for (int i = 0; i < _trailGos.Count; i++)
            {
                var go = _trailGos[i];
                if (go == null) continue;

                // Detach before stopping so existing particles can fade out where
                // they were last emitted instead of teleporting back to the pool.
                go.transform.SetParent(null, worldPositionStays: true);

                var emitter = go.GetComponent<ParticleEmitter>();
                if (emitter != null) emitter.StopEmitting();
            }
            _trailGos.Clear();
        }

        private void HideRootSpriteRenderer()
        {
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null) rootSr.enabled = false;
        }
    }
}
