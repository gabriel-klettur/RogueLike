using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Visual feedback for active mana regeneration. Spawns a child
    /// <see cref="ParticleEmitter"/> driven by a particle preset (blue aura by
    /// default) and toggles its emission so the player sees a soft halo
    /// whenever <see cref="Mana.IsRegenerating"/> is true — i.e. the pool is
    /// below max AND the post-cast grace window has elapsed.
    ///
    /// Added by <see cref="EntitySetup"/> right after Mana is initialized on
    /// the player. Self-contained: the preset is resolved lazily from
    /// <see cref="VFXManager.GetParticlePreset"/> once that singleton is
    /// available, so the bootstrap order between EntitySetup and
    /// EnsureVFXManager is irrelevant.
    /// </summary>
    [RequireComponent(typeof(Mana))]
    public class ManaRegenAura : MonoBehaviour
    {
        [SerializeField, Tooltip("Particle preset id used for the regen aura. Must exist in ParticlePresetCatalog.")]
        private string _presetId = "mana_regen_aura";

        [SerializeField, Tooltip("Local offset of the aura emitter relative to the player root. " +
            "Leave as zero to auto-center on the sprite bounds at first resolution.")]
        private Vector3 _localOffset = Vector3.zero;

        private Mana _mana;
        private ParticleEmitter _emitter;
        private bool _wasRegenerating;
        private bool _resolveAttempted;

        private void Awake()
        {
            _mana = GetComponent<Mana>();
        }

        private void Update()
        {
            if (_emitter == null)
            {
                if (_resolveAttempted) return;
                TryCreateEmitter();
                if (_emitter == null) return;
            }

            bool regen = _mana != null && _mana.IsRegenerating;
            if (regen == _wasRegenerating) return;

            _wasRegenerating = regen;
            if (regen) _emitter.StartEmitting();
            else _emitter.StopEmitting();
        }

        private void TryCreateEmitter()
        {
            var vfx = VFXManager.Instance;
            if (vfx == null || !vfx.HasParticleCatalog) return;

            // From here on we've seen the catalog at least once — don't
            // retry forever if the preset is genuinely missing from it.
            _resolveAttempted = true;

            var preset = vfx.GetParticlePreset(_presetId);
            if (preset == null)
            {
                Debug.LogWarning($"[ManaRegenAura] Particle preset '{_presetId}' not found in catalog — aura disabled on {name}.");
                return;
            }

            var child = new GameObject("ManaRegenAuraEmitter");
            child.transform.SetParent(transform, false);
            child.transform.localPosition = ResolveAnchorOffset();

            _emitter = child.AddComponent<ParticleEmitter>();
            _emitter.ApplyPreset(preset);
            _emitter.StopEmitting();
        }

        // Defaults to the sprite's bounds-center so the particles emanate from
        // the torso instead of the feet (which is where the root transform sits
        // for foot-pivot pixel-art rigs). An explicit non-zero inspector value
        // overrides this auto-centering.
        private Vector3 ResolveAnchorOffset()
        {
            if (_localOffset != Vector3.zero) return _localOffset;

            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return Vector3.zero;

            return transform.InverseTransformPoint(sr.bounds.center);
        }
    }
}
