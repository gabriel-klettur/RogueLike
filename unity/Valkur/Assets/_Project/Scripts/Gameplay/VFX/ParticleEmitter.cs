using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Configures and drives a Unity ParticleSystem based on a ParticlePresetDefinition.
    ///
    /// Place on any GameObject.  A child ParticleSystem is created automatically.
    /// For lightning kind, a LineRenderer is created instead of a ParticleSystem.
    ///
    /// Coordinate conventions:
    ///   All numeric fields in ParticleVfxParams are in Unity world-units / seconds.
    /// </summary>
    public partial class ParticleEmitter : MonoBehaviour
    {
        private const float UNITY_GRAVITY = 9.81f;

        [Header("Preset")]
        [SerializeField, Tooltip("Particle preset to apply. Drives kind, speed, colors, shapes, etc.")]
        private ParticlePresetDefinition _preset;

        [SerializeField, Tooltip("Scale multiplier for sizes and radii.")]
        [Range(0.01f, 10f)]
        private float _scaleMultiplier = 1f;

        [SerializeField, Tooltip("Play automatically on Start.")]
        private bool _playOnAwake = true;

        [SerializeField, Tooltip("Per-instance size overrides applied on top of the preset. " +
                                 "Default (all ratios 1) shares the preset's own blocks.")]
        private ParticleInstanceOverrides _overrides = ParticleInstanceOverrides.None;

        [SerializeField, Tooltip("This instance's OWN configuration, copied from the preset when " +
                                 "it was placed. When set, the preset is only an origin label — " +
                                 "editing the asset no longer reaches this emitter.")]
        private ParticleInstanceConfig _config;

        // Runtime components
        private ParticleSystem _ps;
        private LineRenderer _lr;
        private Coroutine _lightningCoroutine;
        private Coroutine _burstLoopCoroutine;

        // ------------------------------------------------------------------ lifecycle

        private void Start()
        {
            if (_preset != null && _playOnAwake)
                ApplyPreset(_preset, _scaleMultiplier);
        }

        // Resume playback whenever the GameObject is re-enabled (e.g. by the
        // ParticleInstancesLoader's viewport culling). Without this, an emitter
        // that gets SetActive(false) shortly after spawn never plays again when
        // it re-enters the camera frustum — looping presets appear "static".
        private void OnEnable()
        {
            // Play() is idempotent — calling it on an already-playing system is a
            // no-op. We don't gate on _ps.isPlaying because that flag is unreliable
            // right after a SetActive(false→true) cycle.
            if (_ps != null && _preset != null)
            {
                // The child may have been deactivated by a burst's stopAction; Play()
                // would be silently ignored while it is.
                if (!_ps.gameObject.activeSelf) _ps.gameObject.SetActive(true);
                _ps.Play();
            }
            // Same treatment for every composite layer — a re-enabled emitter must
            // resume its whole stack, not just the root.
            for (int i = 0; i < _layerSystems.Count; i++)
            {
                var layerPs = _layerSystems[i];
                if (layerPs == null) continue;
                if (!layerPs.gameObject.activeSelf) layerPs.gameObject.SetActive(true);
                layerPs.Play();
            }
            // Coroutines do not survive a SetActive(false), and the viewport culling above is
            // exactly what deactivates these objects — the day/night tint loop has to be
            // restarted here or an emitter tracks the cycle only until it first leaves frame.
            ResumeAmbientTracking();
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>The preset this emitter is currently rendering, or null before the
        /// first ApplyPreset. Read-only: swapping the effect goes through ApplyPreset,
        /// which rebuilds every module.</summary>
        public ParticlePresetDefinition Preset => _preset;

        /// <summary>Scale the current preset was applied at — the placed instance's
        /// scale_multiplier. Needed by anything measuring the emitter's real extent, since
        /// every radius and size in the preset is multiplied by it.</summary>
        public float ScaleMultiplier => _scaleMultiplier;

        /// <summary>
        /// Per-instance size overrides in force. Applied on top of the preset every time the
        /// systems are built, so the preset stays shared and only this placement changes.
        /// </summary>
        public ParticleInstanceOverrides Overrides => _overrides;

        /// <summary>
        /// This instance's own configuration, or null while it still follows its preset.
        /// </summary>
        public ParticleInstanceConfig Config => _config;

        /// <summary>True when this emitter runs a configuration of its own.</summary>
        public bool HasOwnConfig => _config != null && !_config.IsEmpty;

        /// <summary>
        /// The root block this emitter is actually running: its own if it has one, otherwise
        /// the preset's with the instance's size overrides applied. Anything measuring or
        /// drawing this emitter reads THIS rather than the preset, or it describes an effect
        /// the emitter is not running.
        /// </summary>
        public ParticleVfxParams EffectiveVfx =>
            HasOwnConfig ? ParticleOverrideApplier.Apply(_config.vfx, _overrides)
                         : (_preset == null ? null : ParticleOverrideApplier.Apply(_preset.vfx, _overrides));

        /// <summary>
        /// Every block this emitter runs, root first then layers, in the order its systems are
        /// built. The footprint and the resize handles walk this rather than the preset, which
        /// is what keeps the marker on the effect once the two diverge.
        /// </summary>
        public IReadOnlyList<ParticleVfxParams> EffectiveBlocks
        {
            get
            {
                var blocks = new List<ParticleVfxParams>();
                var root = EffectiveVfx;
                if (root == null) return blocks;

                blocks.Add(root);

                if (HasOwnConfig)
                {
                    for (int i = 0; i < _config.LayerCount; i++)
                        if (_config.layers[i] != null)
                            blocks.Add(ParticleOverrideApplier.Apply(_config.layers[i], _overrides));
                    return blocks;
                }

                if (_preset?.layers != null)
                {
                    for (int i = 0; i < _preset.layers.Count; i++)
                    {
                        var layer = _preset.layers[i];
                        if (!ParticleInstanceConfig.IsSnapshotableLayer(_preset, layer)) continue;
                        blocks.Add(ParticleOverrideApplier.Apply(layer.vfx, _overrides));
                    }
                }

                return blocks;
            }
        }

        /// <summary>
        /// Run this emitter from a configuration it owns. The preset is still passed because it
        /// remains the origin label — the picker groups by it, the same-preset outlines match on
        /// it, and the two "reapply preset" actions read it — and because sprite fields, which
        /// cannot be snapshotted into a JSON file, are still taken from it.
        ///
        /// A null or empty config falls back to <see cref="ApplyPreset"/>: an instance placed
        /// before copy-on-place, or one whose config failed to parse, still has to render.
        /// </summary>
        public void ApplyConfig(ParticlePresetDefinition preset, ParticleInstanceConfig config,
                                float scaleMultiplier = 1f)
        {
            if (config == null || config.IsEmpty)
            {
                ApplyPreset(preset, scaleMultiplier);
                return;
            }

            _playOnAwake = false;
            _preset = preset;
            _config = config;
            _scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);

            // The overrides are NOT cleared here. A resize drag rides on them frame by frame
            // and bakes them into the config when the mouse is released (BakeOverrides), so a
            // rebuild mid-gesture has to keep applying them or the box snaps back under the
            // cursor. At rest they are the identity for any instance that owns a config.
            string kind = config.vfx.kind ?? "";

            BeginAmbientPass();

            if (kind == "lightning")
            {
                SetupLightning(config.vfx);
                TeardownLayers();
                EndAmbientPass();
                return;
            }

            TeardownLightning();

            EnsureParticleSystem();
            if (!_ps.gameObject.activeSelf) _ps.gameObject.SetActive(true);
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ConfigureParticleSystem(_ps, ParticleOverrideApplier.Apply(config.vfx, _overrides),
                                    _scaleMultiplier);

            SyncLayerBlocks(config.layers, _scaleMultiplier, _overrides);

            EndAmbientPass();

            if (IsBurstWithInterval(kind) && config.vfx.burstIntervalSeconds > 0f)
            {
                if (_burstLoopCoroutine != null) StopCoroutine(_burstLoopCoroutine);
                _burstLoopCoroutine = StartCoroutine(BurstLoop(config.vfx.burstIntervalSeconds));
            }
            else
            {
                _ps.Play();
            }
        }

        /// <summary>
        /// Rebuild from the same config after its blocks were edited in place — the F1
        /// properties panel writes straight into them. Cheap: it is the same rebuild
        /// ApplyConfig does.
        /// </summary>
        public void ReapplyConfig()
        {
            if (HasOwnConfig) ApplyConfig(_preset, _config, _scaleMultiplier);
            else if (_preset != null) ApplyPreset(_preset, _scaleMultiplier, _overrides);
        }

        /// <summary>
        /// Resize this instance without interrupting it. Only the modules an override can move
        /// are rewritten — shape, throw, gravity, velocity, turbulence — on systems that keep
        /// playing, so the particles already in the air stay in the air and immediately follow
        /// the new geometry.
        ///
        /// NOT a rebuild, and that is the whole point. ApplyPreset opens with
        /// <c>Stop(StopEmittingAndClear)</c>; called from a drag handle that fires every frame
        /// it destroyed every live particle sixty times a second, so a leaf field stopped
        /// raining for as long as the author was resizing it and took a full lifespan to
        /// refill afterwards.
        ///
        /// Falls back to the rebuild when there is nothing running yet to update — an emitter
        /// whose preset has never been applied, or a lightning kind, which has no
        /// ParticleSystem at all.
        /// </summary>
        public void SetOverrides(ParticleInstanceOverrides overrides)
        {
            _overrides = overrides.Sanitized();
            if (_preset == null) return;

            if (_ps == null)
            {
                ApplyPreset(_preset, _scaleMultiplier, _overrides);
                return;
            }

            // An instance that owns its configuration is resized against THAT, not against the
            // preset it was born from — the two have been free to diverge since the moment it
            // was placed.
            if (HasOwnConfig)
            {
                ApplyGeometry(_ps, ParticleOverrideApplier.Apply(_config.vfx, _overrides),
                              _scaleMultiplier);

                for (int i = 0; i < _config.LayerCount && i < _layerSystems.Count; i++)
                {
                    if (_config.layers[i] == null) continue;
                    ApplyGeometry(_layerSystems[i],
                                  ParticleOverrideApplier.Apply(_config.layers[i], _overrides),
                                  _scaleMultiplier);
                }
                return;
            }

            ApplyGeometry(_ps, ParticleOverrideApplier.Apply(_preset.vfx, _overrides), _scaleMultiplier);

            // Layers are walked in the same order SyncLayers built them, so index i of the
            // valid-layer sequence is index i of _layerSystems. Anything else would resize one
            // layer with another's numbers.
            if (_preset.layers == null) return;

            int writeIndex = 0;
            for (int i = 0; i < _preset.layers.Count && writeIndex < _layerSystems.Count; i++)
            {
                var layer = _preset.layers[i];
                if (!IsValidLayer(_preset, layer)) continue;

                ApplyGeometry(_layerSystems[writeIndex],
                              ParticleOverrideApplier.Apply(layer.vfx, _overrides),
                              _scaleMultiplier);
                writeIndex++;
            }
        }

        /// <summary>
        /// Writes the size ratios currently in force into the owned configuration and resets
        /// them to the identity.
        ///
        /// The ratios are a GESTURE, not state: they exist so a drag has something continuous
        /// to solve against while the mouse is down. Left in place they would be a second
        /// source of truth for the same numbers — the config saying one size and the ratios
        /// silently multiplying it — and the serializer would have to choose which to believe.
        /// Baking on release leaves exactly one answer, in the file, in world units.
        /// </summary>
        public void BakeOverrides()
        {
            if (!HasOwnConfig || _overrides.IsDefault) return;

            _config.vfx = ParticleOverrideApplier.Clone(
                ParticleOverrideApplier.Apply(_config.vfx, _overrides));

            for (int i = 0; i < _config.LayerCount; i++)
            {
                if (_config.layers[i] == null) continue;
                _config.layers[i] = ParticleOverrideApplier.Clone(
                    ParticleOverrideApplier.Apply(_config.layers[i], _overrides));
            }

            _overrides = ParticleInstanceOverrides.None;
        }

        /// <summary>
        /// World-space bounds of every particle this emitter currently has alive, across the
        /// root system and every composite layer. False when nothing is alive — a freshly
        /// placed emitter, a finished burst, a system culled out of frame.
        ///
        /// This is the ground truth for "what area does this effect cover": Unity computes it
        /// from the live particles, so it already contains the drift, the noise, the actual
        /// random speeds each particle drew and the size of the quads — none of which an
        /// analytic guess from the preset gets exactly right. Measured against
        /// <see cref="ParticleFootprint"/>'s worst-case arithmetic, the two agree within a
        /// few percent for a drifting field and disagree by 70% for a preset whose sparks
        /// draw their speed from a wide random range: the maths has to assume every particle
        /// drew the maximum, and almost none of them do.
        ///
        /// Reads the cached systems rather than GetComponentsInChildren: the editor's hit
        /// test calls this for every emitter in the scene, every frame.
        /// </summary>
        public bool TryGetLiveBounds(out Bounds bounds)
        {
            bounds = default(Bounds);
            bool any = false;

            Accumulate(_ps, ref bounds, ref any);
            for (int i = 0; i < _layerSystems.Count; i++)
                Accumulate(_layerSystems[i], ref bounds, ref any);

            return any;
        }

        private static void Accumulate(ParticleSystem ps, ref Bounds bounds, ref bool any)
        {
            if (ps == null || ps.particleCount == 0) return;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;

            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        /// <summary>
        /// Apply a preset, rebuilding the particle system from scratch.
        /// Can be called at runtime to hot-swap the effect.
        /// Maps to Python's ParticlePresetRenderSystem resolving a ParticlePresetComponent.
        /// </summary>
        public void ApplyPreset(ParticlePresetDefinition preset, float scaleMultiplier = 1f)
            => ApplyPreset(preset, scaleMultiplier, _overrides);

        /// <summary>
        /// Apply a preset with per-instance size overrides. The overrides are stored, so a
        /// later ApplyPreset without them (a preset edit re-applied by the F1 editor, a
        /// re-enable after culling) keeps this instance at the size its author dragged it to.
        /// </summary>
        public void ApplyPreset(ParticlePresetDefinition preset, float scaleMultiplier,
                                ParticleInstanceOverrides overrides)
        {
            _playOnAwake = false; // prevent double-apply when called programmatically before Start()
            _preset = preset;
            _scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
            _overrides = overrides.Sanitized();

            // Applying a PRESET means running the preset: an instance that had a configuration
            // of its own is being deliberately put back onto the asset (the editor's "reapply
            // preset" action, or a preview emitter being handed the next selection), and a
            // config left in place would silently win over what was just asked for.
            _config = null;

            string kind = preset.vfx.kind ?? "";

            // Drop the previous preset's day/night opt-ins before anything is configured;
            // ConfigureParticleSystem re-enrols whatever this preset asks for as it goes, and
            // EndAmbientPass starts or stops the tracking loop once the stack is complete.
            BeginAmbientPass();

            if (kind == "lightning")
            {
                SetupLightning(preset.vfx);
                // Lightning draws with a LineRenderer and never calls SyncLayers, so any
                // layers left over from a PREVIOUS composite preset applied to this same
                // (reused) emitter must be torn down explicitly or they keep simulating
                // underneath the bolt.
                TeardownLayers();
                // Lightning draws with a LineRenderer, so nothing enrolled above — this stops
                // any tint loop the previously applied preset left running on this emitter.
                EndAmbientPass();
                return;
            }

            // Leaving the lightning path has to be explicit. AnimateLightning is a
            // while(true) coroutine that keeps re-enabling the LineRenderer forever, so
            // an emitter reused across presets (the editor's preview emitter is reused
            // for every selection) would keep drawing the old bolt on top of every
            // preset chosen afterwards.
            TeardownLightning();

            EnsureParticleSystem();
            // A finished burst sets stopAction = Disable, which deactivates the child
            // holding the ParticleSystem. Play() on an inactive GameObject is a no-op,
            // so without this the emitter is dead for good after its first one-shot.
            if (!_ps.gameObject.activeSelf) _ps.gameObject.SetActive(true);
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            // The instance's own size overrides are folded in HERE, once, rather than being
            // threaded through ConfigureParticleSystem: the applier returns the preset's own
            // block untouched when the overrides are default, so an emitter that has never
            // been resized still shares the preset's data and allocates nothing.
            ConfigureParticleSystem(_ps, ParticleOverrideApplier.Apply(preset.vfx, _overrides),
                                    _scaleMultiplier);

            // Rebuild the layer stack (if any) against the newly configured root. Each
            // layer plays itself as it is configured, so this covers both the "compose
            // a new stack" and "shrink an existing one" cases before the root's own
            // Play() below.
            SyncLayers(preset, _scaleMultiplier);

            // After SyncLayers: the enrolment list is only complete once every layer has been
            // through ConfigureParticleSystem.
            EndAmbientPass();

            if (IsBurstWithInterval(kind) && preset.vfx.burstIntervalSeconds > 0f)
            {
                // Repeating burst (e.g. explosion placed as ambient effect)
                if (_burstLoopCoroutine != null) StopCoroutine(_burstLoopCoroutine);
                _burstLoopCoroutine = StartCoroutine(BurstLoop(preset.vfx.burstIntervalSeconds));
            }
            else
            {
                _ps.Play();
            }
        }

        /// <summary>
        /// Stops new particle emission while letting already-alive particles finish
        /// their natural lifespan (no clear). Used by short-lived "trail" emitters
        /// — e.g. the dash trail emitter that travels from origin to destination
        /// and must stop spawning new dust the instant it arrives, instead of
        /// pooling particles at the endpoint until VFXManager destroys the GO.
        /// Also halts the repeating-burst coroutine if one is running. Stops every
        /// composite layer alongside the root — a trail whose light layer kept firing
        /// after its mass layer stopped would visibly decouple the stack.
        /// </summary>
        public void StopEmitting()
        {
            if (_burstLoopCoroutine != null)
            {
                StopCoroutine(_burstLoopCoroutine);
                _burstLoopCoroutine = null;
            }
            if (_ps != null)
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            for (int i = 0; i < _layerSystems.Count; i++)
            {
                var layerPs = _layerSystems[i];
                if (layerPs != null) layerPs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// Inverse of <see cref="StopEmitting"/>: resume emitting using the preset
        /// already applied. Idempotent — calling on a system that's already playing
        /// is a no-op. For burst-with-interval presets the repeating coroutine is
        /// re-started so the cadence resumes; for plain continuous emitters this
        /// just re-plays the underlying ParticleSystem. Used by long-lived togglers
        /// like <see cref="Valkur.Gameplay.ManaRegenAura"/> that switch the effect
        /// on and off without rebuilding the emitter. Replays every composite layer
        /// alongside the root, mirroring <see cref="StopEmitting"/>.
        /// </summary>
        public void StartEmitting()
        {
            if (_ps == null) return;
            if (_preset != null
                && IsBurstWithInterval(_preset.vfx.kind ?? "")
                && _preset.vfx.burstIntervalSeconds > 0f
                && _burstLoopCoroutine == null)
            {
                _burstLoopCoroutine = StartCoroutine(BurstLoop(_preset.vfx.burstIntervalSeconds));
            }
            _ps.Play();
            for (int i = 0; i < _layerSystems.Count; i++)
            {
                var layerPs = _layerSystems[i];
                if (layerPs != null) layerPs.Play();
            }
        }

        /// <summary>
        /// Override the underlying ParticleSystem's continuous emission rate.
        /// Used when a preset's authored rate is too low for a short-lived
        /// motion-driven emitter (e.g. the dash trail, which travels start→end
        /// in ~0.18 s — at the preset's stock 10/s only 1-2 particles drop along
        /// the path; bumping the rate while moving gives a continuous wake).
        /// No-op if the ParticleSystem has not been built yet. ROOT ONLY — composite
        /// layers keep their authored rate. Every current caller drives a single-preset
        /// motion trail, so this has never needed to fan out to layers; extend it if a
        /// caller shows up that does.
        /// </summary>
        public void SetEmissionRate(float ratePerSecond)
        {
            if (_ps == null) return;
            var emission = _ps.emission;
            emission.rateOverTime = Mathf.Max(0f, ratePerSecond);
        }
    }
}
