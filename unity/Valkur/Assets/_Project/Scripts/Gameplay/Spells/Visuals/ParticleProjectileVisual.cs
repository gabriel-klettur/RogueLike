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
        private bool _trailStartPending;

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
            _trailStartPending = true;   // deferred for the same reason as OnEnable
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

            // NOT StartTrail() directly: parenting a child to a GameObject that is in the
            // middle of being activated is illegal — Unity logs "Cannot set the parent of
            // the GameObject X while activating or deactivating the parent Y" and the
            // reparent silently does not happen, which left every trail emitter of every
            // pooled projectile stranded at the pool's origin. One frame's delay costs
            // nothing visually and is the only legal place to do it.
            _trailStartPending = true;
        }

        private void LateUpdate()
        {
            if (!_trailStartPending) return;
            _trailStartPending = false;
            if (_impacted) return;   // hit on the spawn frame: nothing left to trail
            StartTrail();
        }

        private void OnDisable()
        {
            _trailStartPending = false;

            // Same restriction in reverse: this runs while the projectile is being
            // deactivated, so SetParent(null) would throw the mirror error. The normal
            // path is OnImpact, which runs before the projectile is returned to the pool
            // and therefore CAN detach. Here we only stop emission — the emitters carry
            // their own despawn timer and their particles simulate in world space, so
            // they fade where they were emitted either way.
            StopEmittersOnly();
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

        /// <summary>
        /// Detach the emitters, then stop them. Detaching first is what lets the particles
        /// already in flight fade out where they were last emitted instead of being dragged
        /// back to the pool with the projectile.
        ///
        /// Only legal outside an activation callback — see <see cref="StopEmittersOnly"/>.
        /// </summary>
        private void StopTrail()
        {
            for (int i = 0; i < _trailGos.Count; i++)
            {
                var go = _trailGos[i];
                if (go == null) continue;
                go.transform.SetParent(null, worldPositionStays: true);
            }
            StopEmittersOnly();
        }

        /// <summary>
        /// Stop emission without reparenting, for the one caller that runs while the
        /// projectile is being deactivated and therefore cannot legally reparent anything.
        /// The emitters carry their own despawn timer, so leaving them attached costs a
        /// frame of the trail travelling back to the pool rather than a leak.
        /// </summary>
        private void StopEmittersOnly()
        {
            for (int i = 0; i < _trailGos.Count; i++)
            {
                var go = _trailGos[i];
                if (go == null) continue;

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
