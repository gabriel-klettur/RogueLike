using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One placed spawner's OWN configuration — the whole behaviour, owned outright rather
    /// than read from the preset it was placed with.
    ///
    /// <para><b>COPY ON PLACE.</b> A <see cref="SpawnerTemplateData"/> is a starting point,
    /// not a live link: a placement takes a copy of it the moment it is made and is
    /// independent from then on, so editing the preset afterwards reaches the NEXT placement
    /// and none of the existing ones. Before this, a placed spawner's entire on-disk record
    /// was <c>template_id</c>/<c>zone</c>/<c>tile</c>/<c>id</c>, the runtime read every
    /// decision off the shared asset, and the F3 properties panel wrote to that same asset —
    /// so tuning the spawner you had just placed retuned every other placement of its kind.
    /// Directly parallel to <see cref="ParticleInstanceConfig"/>, and adopted for the same
    /// reasons.</para>
    ///
    /// <para>The preset id is still recorded on the instance. It names where this
    /// configuration came from, drives the picker's grouping, and is what the two "reapply
    /// preset" actions read to overwrite a config on request. It just no longer decides how
    /// the spawner behaves.</para>
    ///
    /// <para><b>Every field initializer here must match
    /// <see cref="SpawnerTemplateData"/>'s.</b> The serializer omits any value equal to the
    /// C# default and a reader restores that same default, so the two declarations are one
    /// contract — and the omission is measured against the CLASS default, never against the
    /// preset's value, or a later preset edit would reach back into every placement that
    /// happened to agree with it and copy-on-place would be undone through the file.
    /// <c>SpawnerInstanceConfigDefaultsTests</c> pins the pair.</para>
    /// </summary>
    [Serializable]
    public sealed class SpawnerInstanceConfig
    {
        // ── Roster ──────────────────────────────────────────────────────────────
        public List<WaveDefinition> waves = new List<WaveDefinition>();

        // ── Trigger ─────────────────────────────────────────────────────────────
        public TriggerType triggerType = TriggerType.Proximity;
        public float       triggerRadius = 10f;
        public bool        autoStart = true;
        public bool        proximityRearms;

        // ── Policy ──────────────────────────────────────────────────────────────
        public SpawnMode spawnMode = SpawnMode.Periodic;
        public float     cooldownSeconds = 1f;
        public float     betweenWavesCooldownSeconds = 5f;
        public AdvanceOn advanceOn = AdvanceOn.Clear;
        public int       maxActive;
        public bool      persistent;
        public bool      restartOnDone;
        public float     restartCooldownSeconds;

        // ── Area ────────────────────────────────────────────────────────────────
        public int          spawnRadius = 20;
        public SpawnerShape spawnerShape = SpawnerShape.Square;

        // ── Difficulty ──────────────────────────────────────────────────────────
        public int   levelBonus;
        public float scaleWithPlayerLevel;

        // ── Brain ───────────────────────────────────────────────────────────────
        public float  defendLeashRadius;
        public string fsmSetOverride = string.Empty;

        public SpawnerInstanceConfig() { }

        /// <summary>
        /// The configuration a fresh placement of <paramref name="preset"/> is born with.
        ///
        /// <para>The wave list is DEEP copied. A shallow copy would leave every placement
        /// pointing at the preset's own <see cref="WaveDefinition"/> objects, so editing one
        /// camp's roster would edit them all — copy-on-place defeated one level down, and
        /// invisible until two placements of one preset exist.</para>
        ///
        /// <para>A null preset yields a blank config rather than null, so a placement whose
        /// preset was deleted from the catalogue still loads and can still be authored.</para>
        /// </summary>
        public static SpawnerInstanceConfig SnapshotOf(SpawnerTemplateData preset)
        {
            var config = new SpawnerInstanceConfig();
            if (preset == null) return config;

            config.waves = new List<WaveDefinition>();
            if (preset.waves != null)
            {
                foreach (var wave in preset.waves)
                    if (wave != null) config.waves.Add(new WaveDefinition(wave));
            }

            config.triggerType                 = preset.triggerType;
            config.triggerRadius               = preset.triggerRadius;
            config.autoStart                   = preset.autoStart;
            config.proximityRearms             = preset.proximityRearms;
            config.spawnMode                   = preset.spawnMode;
            config.cooldownSeconds             = preset.cooldownSeconds;
            config.betweenWavesCooldownSeconds = preset.betweenWavesCooldownSeconds;
            config.advanceOn                   = preset.advanceOn;
            config.maxActive                   = preset.maxActive;
            config.persistent                  = preset.persistent;
            config.restartOnDone               = preset.restartOnDone;
            config.restartCooldownSeconds      = preset.restartCooldownSeconds;
            config.spawnRadius                 = preset.spawnRadius;
            config.spawnerShape                = preset.spawnerShape;
            config.levelBonus                  = preset.levelBonus;
            config.scaleWithPlayerLevel        = preset.scaleWithPlayerLevel;
            config.defendLeashRadius           = preset.defendLeashRadius;
            config.fsmSetOverride              = preset.fsmSetOverride ?? string.Empty;

            return config;
        }

        /// <summary>An independent copy — used by undo and by the "reapply preset" actions.</summary>
        public SpawnerInstanceConfig Clone()
        {
            var copy = (SpawnerInstanceConfig)MemberwiseClone();
            copy.waves = new List<WaveDefinition>();
            if (waves != null)
            {
                foreach (var wave in waves)
                    if (wave != null) copy.waves.Add(new WaveDefinition(wave));
            }
            return copy;
        }

        /// <summary>Total entities one full pass of every wave would produce. Panel display only.</summary>
        public int TotalEntityCount()
        {
            int total = 0;
            if (waves == null) return 0;
            foreach (var wave in waves)
            {
                if (wave?.spawns == null) continue;
                foreach (var entry in wave.spawns)
                    if (entry != null) total += Mathf.Max(0, entry.count);
            }
            return total;
        }

        /// <summary>
        /// The first roster entry's entity id, or null. What the picker and the properties
        /// header show so an author can tell two placements apart without opening the roster.
        /// </summary>
        public string PrimaryEntityId()
        {
            if (waves == null) return null;
            foreach (var wave in waves)
            {
                if (wave?.spawns == null) continue;
                foreach (var entry in wave.spawns)
                    if (entry != null && !string.IsNullOrEmpty(entry.entityId)) return entry.entityId;
            }
            return null;
        }
    }
}
