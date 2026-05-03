using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="ProjectileExecutor.ResolveElement"/> precedence so the
    /// data-driven `SpellDefinition.element` path stays authoritative over
    /// the legacy spellKey switch. Without this, a designer who sets
    /// `element` on a SO and gets a different visual than expected would
    /// have no easy way to debug the precedence.
    /// </summary>
    [TestFixture]
    public class ProjectileExecutorElementTests
    {
        private static SpellDefinition MakeSpell(string key, string element = null)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = key;
            s.element  = element;
            return s;
        }

        [Test]
        public void NullSpell_ReturnsNull()
        {
            Assert.IsFalse(ProjectileExecutor.ResolveElement(null).HasValue);
        }

        [Test]
        public void ElementField_TakesPriorityOverLegacyKey()
        {
            // Spell key would map to Dark via legacy switch; element field
            // says Ice. SO field must win — the whole point of moving to a
            // data-driven path is that designers can override.
            var s = MakeSpell("darkball", element: "Ice");
            try
            {
                var resolved = ProjectileExecutor.ResolveElement(s);
                Assert.AreEqual(SpellElement.Ice, resolved);
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void ElementField_IsCaseInsensitive()
        {
            var s = MakeSpell("custom_key", element: "ARCANE");
            try
            {
                var resolved = ProjectileExecutor.ResolveElement(s);
                Assert.AreEqual(SpellElement.Arcane, resolved,
                    "Designers will type the element string in any casing — " +
                    "the parser must accept it.");
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void ElementField_UnknownString_FallsBackToLegacyKey()
        {
            // 'plasma' isn't a SpellElement — must fall through to the
            // legacy spellKey switch ('darkball' → Dark).
            var s = MakeSpell("darkball", element: "plasma");
            try
            {
                var resolved = ProjectileExecutor.ResolveElement(s);
                Assert.AreEqual(SpellElement.Dark, resolved,
                    "Unknown element strings must not strand the visual; the " +
                    "legacy spellKey switch is the safety net.");
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void EmptyElement_FallsBackToLegacyKey()
        {
            var s = MakeSpell("iceball", element: string.Empty);
            try
            {
                var resolved = ProjectileExecutor.ResolveElement(s);
                Assert.AreEqual(SpellElement.Ice, resolved);
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void UnknownKeyAndNoElement_ReturnsNull()
        {
            // Generic projectile with no hint at all → null. Caller (the
            // Attach method) treats null as "leave the prefab visual alone",
            // which is the safe default.
            var s = MakeSpell("totally_new_spell", element: null);
            try
            {
                Assert.IsFalse(ProjectileExecutor.ResolveElement(s).HasValue);
            }
            finally { Object.DestroyImmediate(s); }
        }
    }
}
