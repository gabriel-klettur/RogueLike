using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Editors.Spells
{
    /// <summary>
    /// Locks in the editor-friendly accessors added to <see cref="SpellCaster"/>
    /// so the Spells Editor's live-preview can fire spells without going through
    /// the production cast path (which would consume mana and start cooldowns).
    ///
    /// Specifically:
    ///   • <c>internal static SpellCaster.GetExecutor(SpellType)</c> — returns the
    ///     executor strategy registered for each SpellType. The preview pipeline
    ///     resolves this once per cycle instead of duplicating the dictionary.
    ///   • <c>SpellCaster.ProjectilePrefab</c> getter — surfaces the serialized
    ///     prefab so the preview can synthesise a SpellContext without reflection.
    /// </summary>
    [TestFixture]
    public class SpellCasterExecutorAccessTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
        }

        // ── GetExecutor ──────────────────────────────────────────────────────────

        [Test]
        public void GetExecutor_ReturnsCorrectStrategyPerType()
        {
            // Sample one representative type from each "family" — projectile, AOE,
            // beam, persistent area, summon — covers every executor branch the
            // preview will hit.
            (SpellType type, System.Type expected)[] cases =
            {
                (SpellType.Projectile,        typeof(ProjectileExecutor)),
                (SpellType.Slash,             typeof(SlashExecutor)),
                (SpellType.Area,              typeof(AreaExecutor)),
                (SpellType.Dash,              typeof(DashExecutor)),
                (SpellType.Teleport,          typeof(TeleportExecutor)),
                (SpellType.Boomerang,         typeof(BoomerangExecutor)),
                (SpellType.Lightning,         typeof(LightningExecutor)),
                (SpellType.Beam,              typeof(LaserBeamExecutor)),
                (SpellType.Wall,              typeof(WallExecutor)),
                (SpellType.Mine,              typeof(MineExecutor)),
                (SpellType.SphereMagicShield, typeof(ShieldExecutor)),
                (SpellType.Aura,              typeof(AuraExecutor)),
                (SpellType.Puddle,            typeof(PuddleExecutor)),
                (SpellType.Totem,             typeof(TotemExecutor)),
            };

            var method = typeof(SpellCaster).GetMethod("GetExecutor",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method,
                "SpellCaster must expose an internal static GetExecutor(SpellType).");

            foreach (var (type, expected) in cases)
            {
                var executor = method.Invoke(null, new object[] { type });
                Assert.IsNotNull(executor,
                    $"GetExecutor({type}) must not return null (preview would silently no-op).");
                Assert.IsInstanceOf(expected, executor,
                    $"GetExecutor({type}) must resolve to {expected.Name}.");
            }
        }

        [Test]
        public void GetExecutor_ReturnsNull_ForUnregisteredType()
        {
            var method = typeof(SpellCaster).GetMethod("GetExecutor",
                BindingFlags.NonPublic | BindingFlags.Static);

            // Cast an out-of-range int to SpellType — there is no executor for this.
            var bogus = (SpellType)999;
            var executor = method.Invoke(null, new object[] { bogus });

            Assert.IsNull(executor,
                "GetExecutor must return null for unknown SpellType values (callers null-check).");
        }

        // ── ProjectilePrefab getter ──────────────────────────────────────────────

        [Test]
        public void ProjectilePrefab_DefaultsToNull()
        {
            _go = new GameObject("Caster");
            var caster = _go.AddComponent<SpellCaster>();

            Assert.IsNull(caster.ProjectilePrefab,
                "A fresh SpellCaster has no prefab assigned — getter must mirror that.");
        }

        [Test]
        public void ProjectilePrefab_ReflectsSerializedField_AfterSetProjectilePrefab()
        {
            _go = new GameObject("Caster");
            var caster = _go.AddComponent<SpellCaster>();
            var prefab = new GameObject("FakePrefab");

            try
            {
                caster.SetProjectilePrefab(prefab);
                Assert.AreSame(prefab, caster.ProjectilePrefab,
                    "ProjectilePrefab getter must return whatever SetProjectilePrefab assigned.");
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }
    }
}
