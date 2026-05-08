using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Particles-only projectile visual. The trail is the spell's
    /// <c>vfxPreset</c> particle system parented to the moving projectile;
    /// no SpriteRenderer is drawn on the projectile itself.
    ///
    /// Impact FX is handled by <see cref="Projectile.OnExpire"/>, which already
    /// spawns <c>SpellDefinition.impactPreset</c> at the hit point — so this
    /// visual only needs to (a) start/stop the trail across pool reuse and
    /// (b) keep any leftover SpriteRenderer disabled.
    /// </summary>
    public class ParticleProjectileVisual : MonoBehaviour, IProjectileVisual
    {
        private string _trailPresetId;
        private GameObject _trailGo;
        private bool _impacted;

        /// <summary>
        /// Configure the trail preset for this projectile. Safe to call before
        /// <c>OnEnable</c> (cached) or after (re-arms a new preset on the fly).
        /// </summary>
        public void SetSpell(SpellDefinition spell)
        {
            string newPreset = spell != null ? spell.vfxPreset : null;
            if (_trailPresetId == newPreset) return;

            _trailPresetId = newPreset;
            if (!gameObject.activeInHierarchy) return;

            StopTrail();
            StartTrail();
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
            if (string.IsNullOrEmpty(_trailPresetId)) return;
            if (VFXManager.Instance == null) return;
            if (_trailGo != null) return;

            _trailGo = VFXManager.Instance.SpawnParticlePreset(
                _trailPresetId, transform.position, duration: -1f);
            if (_trailGo == null) return;

            _trailGo.transform.SetParent(transform, worldPositionStays: true);
            _trailGo.transform.localPosition = Vector3.zero;
        }

        private void StopTrail()
        {
            if (_trailGo == null) return;

            // Detach before stopping so existing particles can fade out where
            // they were last emitted instead of teleporting back to the pool.
            _trailGo.transform.SetParent(null, worldPositionStays: true);

            var emitter = _trailGo.GetComponent<ParticleEmitter>();
            if (emitter != null) emitter.StopEmitting();

            _trailGo = null;
        }

        private void HideRootSpriteRenderer()
        {
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null) rootSr.enabled = false;
        }
    }
}
