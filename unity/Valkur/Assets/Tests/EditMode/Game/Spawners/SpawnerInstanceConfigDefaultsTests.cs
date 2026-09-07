using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// The contract that holds up the whole v2 file format.
    ///
    /// <para><see cref="SpawnerInstanceSerializer"/> omits any config value equal to the CLASS
    /// default and a reader restores that same default. So the field initializers on
    /// <see cref="SpawnerInstanceConfig"/> and <see cref="SpawnerTemplateData"/> are one
    /// contract, not two declarations that happen to agree: if they drift, a preset whose
    /// value matches the template default is written as nothing and read back as the CONFIG
    /// default, and the placement quietly changes behaviour on its next load. Nothing throws,
    /// nothing logs, and the wrongness is only visible in the game.</para>
    ///
    /// <para>It is also why the omission is measured against the class default and never
    /// against the preset's value: omitting fields that merely agree with the preset would let
    /// a later preset edit reach back into every placement that happened to match, which is
    /// exactly the coupling copy-on-place removes, coming back in through the file.</para>
    /// </summary>
    [TestFixture]
    public class SpawnerInstanceConfigDefaultsTests
    {
        /// <summary>
        /// Config field name → the template field it must agree with. Written out by hand
        /// rather than matched by name, so renaming one side is a compile error here instead
        /// of a silently skipped pair.
        /// </summary>
        private static readonly (string Config, string Template)[] Pairs =
        {
            ("triggerType",                 "triggerType"),
            ("triggerRadius",               "triggerRadius"),
            ("autoStart",                   "autoStart"),
            ("proximityRearms",             "proximityRearms"),
            ("spawnMode",                   "spawnMode"),
            ("cooldownSeconds",             "cooldownSeconds"),
            ("betweenWavesCooldownSeconds", "betweenWavesCooldownSeconds"),
            ("advanceOn",                   "advanceOn"),
            ("maxActive",                   "maxActive"),
            ("persistent",                  "persistent"),
            ("restartOnDone",               "restartOnDone"),
            ("restartCooldownSeconds",      "restartCooldownSeconds"),
            ("spawnRadius",                 "spawnRadius"),
            ("spawnerShape",                "spawnerShape"),
            ("levelBonus",                  "levelBonus"),
            ("scaleWithPlayerLevel",        "scaleWithPlayerLevel"),
            ("defendLeashRadius",           "defendLeashRadius"),
            ("fsmSetOverride",              "fsmSetOverride"),
        };

        [Test]
        public void EveryConfigField_StartsAtItsTemplateCounterpartsDefault()
        {
            var config   = new SpawnerInstanceConfig();
            var template = ScriptableObject.CreateInstance<SpawnerTemplateData>();

            try
            {
                foreach (var (configName, templateName) in Pairs)
                {
                    var cf = typeof(SpawnerInstanceConfig).GetField(configName);
                    var tf = typeof(SpawnerTemplateData).GetField(templateName);

                    Assert.That(cf, Is.Not.Null, $"SpawnerInstanceConfig has no field '{configName}'.");
                    Assert.That(tf, Is.Not.Null, $"SpawnerTemplateData has no field '{templateName}'.");

                    Assert.That(cf.GetValue(config), Is.EqualTo(tf.GetValue(template)),
                        $"'{configName}' defaults differ between SpawnerInstanceConfig and " +
                        "SpawnerTemplateData. The serializer omits class defaults, so a drift " +
                        "here silently changes a placement's behaviour on its next load.");
                }
            }
            finally
            {
                Object.DestroyImmediate(template);
            }
        }

        /// <summary>
        /// Every overridable template field must be covered by <see cref="Pairs"/>.
        ///
        /// <para>This is the half that catches the NEXT field. Adding one to the preset and
        /// forgetting it on the config makes it un-overridable — the preset becomes partly a
        /// rule again — and the pair test above cannot see a field nobody listed. Same
        /// reasoning as <c>PlayerStatsWiringTests</c> walking <c>StatKind</c>.</para>
        /// </summary>
        [Test]
        public void EveryTemplateField_IsOverridablePerInstance()
        {
            // Identity and the roster are not knobs: templateId names the preset (a placement
            // records it separately) and `waves` is copied by SnapshotOf as a deep copy rather
            // than compared field-wise.
            var exempt = new HashSet<string> { "templateId", "waves" };

            var covered = new HashSet<string>(Pairs.Select(p => p.Template));

            var missing = typeof(SpawnerTemplateData)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.DeclaringType == typeof(SpawnerTemplateData))
                .Select(f => f.Name)
                .Where(n => !exempt.Contains(n) && !covered.Contains(n))
                .ToList();

            Assert.That(missing, Is.Empty,
                "These SpawnerTemplateData fields have no SpawnerInstanceConfig counterpart, so " +
                "a placement cannot override them: " + string.Join(", ", missing));
        }

        /// <summary>
        /// The config must carry no field the template does not, or a fresh placement is born
        /// with a value no preset can express and no author can set.
        /// </summary>
        [Test]
        public void TheConfig_CarriesNoFieldThePresetCannotSet()
        {
            var exempt = new HashSet<string> { "waves" };
            var covered = new HashSet<string>(Pairs.Select(p => p.Config));

            var extra = typeof(SpawnerInstanceConfig)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => f.Name)
                .Where(n => !exempt.Contains(n) && !covered.Contains(n))
                .ToList();

            Assert.That(extra, Is.Empty,
                "These SpawnerInstanceConfig fields have no preset counterpart: " +
                string.Join(", ", extra));
        }

        /// <summary>
        /// The whole Visual-spawner group and its siblings are gone. They were authored,
        /// serialized on twenty-five assets and read by nothing — and freezing twelve dead
        /// fields into every placement's config is what the v2 migration would have done if
        /// the cleanup had been deferred. Re-adding one is a red test, not a quiet regression.
        /// </summary>
        [Test]
        public void TheTwelveInertFields_AreGone()
        {
            string[] removed =
            {
                "spawnerType", "randomSpawnRadius", "defendSpawn", "defendLeash",
                "visibleInGame", "proximityInitialOnly", "damageable", "maxHp",
                "flashOnHit", "flashColor", "flashDurationSeconds", "hpResetOnEnter",
            };

            var present = removed
                .Where(n => typeof(SpawnerTemplateData).GetField(n) != null)
                .ToList();

            Assert.That(present, Is.Empty,
                "These fields had no runtime reader and were removed. Re-adding one means " +
                "either wiring it or leaving authored-and-inert data in the schema: " +
                string.Join(", ", present));
        }
    }
}
