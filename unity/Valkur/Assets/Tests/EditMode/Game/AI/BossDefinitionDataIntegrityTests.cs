using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Shipped-data integrity for every <see cref="BossDefinition"/> asset: every
    /// per-phase <c>autoCastList</c> spell key, <c>activationSfxId</c> and
    /// <c>musicTrackId</c> must resolve against the live <see cref="SpellCatalog"/> /
    /// <see cref="AudioCatalogSO"/>, and <c>baseMonster</c> must be assigned.
    ///
    /// The audit (<c>.github/ENTITIES_FSM_PVM_AUDIT.md</c>, dimension 12) explicitly asks
    /// for this: a data-only test like this one would have caught every SampleBoss fault
    /// found while wiring boss casting — the empty <c>baseMonster</c>, the typo'd
    /// <c>meteor</c> spell key (real key is <c>meteor_shower</c>), and the two
    /// <c>activationSfxId</c>s (<c>spell_firework_launch</c> / <c>spell_smoke_emitter</c>)
    /// that resolved to nothing in <c>AudioCatalog.asset</c>, which
    /// <see cref="BossConfigurator"/> would have warned about on every phase transition
    /// (<c>PlaySfxById</c> is called unguarded there, unlike
    /// <c>SpellCaster.Execution</c>'s <c>HasSfx</c> gate). All are fixed as of this pass;
    /// this fixture is what stops them from coming back silently.
    /// </summary>
    [TestFixture]
    public class BossDefinitionDataIntegrityTests
    {
        private const string BossesFolder = "Assets/_Project/Data/Bosses";
        private const string SpellCatalogPath = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const string AudioCatalogPath = "Assets/_Project/Resources/AudioCatalog.asset";

        private SpellCatalog _spellCatalog;
        private AudioCatalogSO _audioCatalog;
        private BossDefinition[] _bosses;

        [SetUp]
        public void SetUp()
        {
            _spellCatalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellCatalogPath);
            _audioCatalog = AssetDatabase.LoadAssetAtPath<AudioCatalogSO>(AudioCatalogPath);

            var guids = AssetDatabase.FindAssets("t:BossDefinition", new[] { BossesFolder });
            _bosses = new BossDefinition[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                _bosses[i] = AssetDatabase.LoadAssetAtPath<BossDefinition>(path);
            }
        }

        // ── Fixture sanity ───────────────────────────────────────────────────────

        [Test]
        public void Fixture_CatalogsAndAtLeastOneBossResolve()
        {
            Assert.IsNotNull(_spellCatalog, $"'{SpellCatalogPath}' must load as a SpellCatalog.");
            Assert.IsNotNull(_audioCatalog, $"'{AudioCatalogPath}' must load as an AudioCatalogSO.");
            Assert.GreaterOrEqual(_bosses.Length, 1,
                $"Expected at least one BossDefinition under '{BossesFolder}' (SampleBoss.asset).");
        }

        // ── Every shipped boss ───────────────────────────────────────────────────

        [Test]
        public void EveryBoss_HasABaseMonsterAssigned()
        {
            foreach (var boss in _bosses)
            {
                Assert.IsNotNull(boss.baseMonster,
                    $"'{boss.name}'.baseMonster is unassigned — a boss with no base " +
                    "monster has no stats, sprites or FSM hooks.");
            }
        }

        [Test]
        public void EveryBoss_EveryPhaseAutoCastKey_ResolvesInSpellCatalog()
        {
            foreach (var boss in _bosses)
            {
                for (int p = 0; p < boss.phases.Length; p++)
                {
                    var phase = boss.phases[p];
                    if (phase.autoCastList == null) continue;

                    foreach (var key in phase.autoCastList)
                    {
                        if (string.IsNullOrWhiteSpace(key)) continue;
                        Assert.IsTrue(_spellCatalog.TryGet(key, out _),
                            $"'{boss.name}' phase {p} ('{phase.label}') autoCastList references " +
                            $"unknown spell key '{key}' — BossConfigurator.ConfigureRotation " +
                            "would silently skip it and warn at runtime.");
                    }
                }
            }
        }

        [Test]
        public void EveryBoss_EveryPhaseActivationSfxId_ResolvesInAudioCatalog()
        {
            foreach (var boss in _bosses)
            {
                for (int p = 0; p < boss.phases.Length; p++)
                {
                    var phase = boss.phases[p];
                    if (string.IsNullOrEmpty(phase.activationSfxId)) continue;

                    // HasSfx lives on IAudioService, not on the catalog asset — the asset's
                    // own miss signal is a null entry from GetSfx.
                    Assert.IsNotNull(_audioCatalog.GetSfx(phase.activationSfxId),
                        $"'{boss.name}' phase {p} ('{phase.label}') activationSfxId " +
                        $"'{phase.activationSfxId}' is not in AudioCatalog.asset — " +
                        "BossConfigurator.OnPhaseChanged calls PlaySfxById unguarded " +
                        "(no HasSfx check), so a miss here logs a console warning the " +
                        "first time this boss crosses into this phase.");
                }
            }
        }

        [Test]
        public void EveryBoss_EveryPhaseMusicTrackId_ResolvesInAudioCatalog()
        {
            foreach (var boss in _bosses)
            {
                for (int p = 0; p < boss.phases.Length; p++)
                {
                    var phase = boss.phases[p];
                    if (string.IsNullOrEmpty(phase.musicTrackId)) continue; // empty = keep current track

                    Assert.IsNotNull(_audioCatalog.GetTrack(phase.musicTrackId),
                        $"'{boss.name}' phase {p} ('{phase.label}') musicTrackId " +
                        $"'{phase.musicTrackId}' is not in AudioCatalog.asset — the crossfade " +
                        "would silently no-op (AudioManager only warns for SFX misses, not music).");
                }
            }
        }

        // ── SampleBoss specifics — pins this pass's fixes so they can't regress silently ──

        [Test]
        public void SampleBoss_OpeningPhase_HasARealBossMusicTrack()
        {
            var sampleBoss = FindByName("SampleBoss");
            Assert.IsNotNull(sampleBoss, "SampleBoss.asset must exist under Data/Bosses.");
            Assert.GreaterOrEqual(sampleBoss.phases.Length, 1);

            string trackId = sampleBoss.phases[0].musicTrackId;
            Assert.IsFalse(string.IsNullOrEmpty(trackId),
                "SampleBoss's entry phase used to leave musicTrackId empty, so a fresh boss " +
                "spawn played no distinct boss theme at all.");
            Assert.IsNotNull(_audioCatalog.GetTrack(trackId),
                $"SampleBoss phase 0's musicTrackId '{trackId}' must resolve in AudioCatalog.asset.");
        }

        [Test]
        public void SampleBoss_ActivationSfxIds_NoLongerReferenceMissingSpellSfx()
        {
            var sampleBoss = FindByName("SampleBoss");
            Assert.IsNotNull(sampleBoss);

            foreach (var phase in sampleBoss.phases)
            {
                Assert.AreNotEqual("spell_firework_launch", phase.activationSfxId,
                    "'spell_firework_launch' does not exist in AudioCatalog.asset — this was " +
                    "one of the two SampleBoss data faults the audit found.");
                Assert.AreNotEqual("spell_smoke_emitter", phase.activationSfxId,
                    "'spell_smoke_emitter' does not exist in AudioCatalog.asset — this was " +
                    "one of the two SampleBoss data faults the audit found.");
            }
        }

        private BossDefinition FindByName(string assetName)
        {
            foreach (var boss in _bosses)
                if (boss != null && boss.name == assetName)
                    return boss;
            return null;
        }
    }
}
