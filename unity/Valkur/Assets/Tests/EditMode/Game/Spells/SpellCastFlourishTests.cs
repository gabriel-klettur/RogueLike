using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Pins the contract of the caster-side flourish — the parts that can be stated without
    /// a running frame.
    ///
    /// <para>The flourish fires from <c>SpellCaster.ExecuteSpell</c>, which is the one seam
    /// EVERY cast passes through, player and monster alike. That reach is the point and also
    /// the risk: two spell types must not get one, and neither refusal is visible in the
    /// effect itself — a probe covered in light still looks like a nice effect, it just makes
    /// the Spells Editor useless for the only thing a probe exists for.</para>
    /// </summary>
    public class SpellCastFlourishTests
    {
        private static SpellDefinition Spell(SpellType type)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = type.ToString().ToLowerInvariant();
            spell.type = type;
            return spell;
        }

        [Test]
        public void OrdinarySpells_GetAFlourish()
        {
            Assert.IsTrue(SpellCastFlourishFX.AppliesTo(Spell(SpellType.Projectile)));
            Assert.IsTrue(SpellCastFlourishFX.AppliesTo(Spell(SpellType.Wall)));
            Assert.IsTrue(SpellCastFlourishFX.AppliesTo(Spell(SpellType.Aura)));
            Assert.IsTrue(SpellCastFlourishFX.AppliesTo(Spell(SpellType.Slash)));
        }

        [Test]
        public void WeaponLoadout_IsRefused_BecauseTheSwapFlareOwnsThoseFrames()
        {
            Assert.IsFalse(SpellCastFlourishFX.AppliesTo(Spell(SpellType.WeaponLoadout)),
                "WeaponSwapFlashFX exists to cover the sprite-set cut and would be fighting " +
                "the flourish for the same pixels.");
        }

        [Test]
        public void AnimationProbe_IsRefused_BecauseAProbeExistsToBeWatched()
        {
            Assert.IsFalse(SpellCastFlourishFX.AppliesTo(Spell(SpellType.AnimationProbe)),
                "A probe's whole job is that the animation can be seen in the Spells Editor.");
        }

        [Test]
        public void NullSpell_IsRefused()
        {
            Assert.IsFalse(SpellCastFlourishFX.AppliesTo(null));
        }

        [Test]
        public void OutsidePlayMode_NothingIsBuilt()
        {
            var caster = new GameObject("Caster");
            try
            {
                SpellCastFlourishFX.Play(Spell(SpellType.Projectile), caster.transform, Vector2.right);

                // The rig tears itself down from Update, which never runs in Edit Mode, so one
                // built here would be a permanent cluster of objects rather than a 0.58 s
                // effect. EditMode tests construct gameplay objects freely — this is a leak
                // guard, not a nicety.
                Assert.IsNull(GameObject.Find("SpellCastFlourishFX"));
            }
            finally { Object.DestroyImmediate(caster); }
        }

        [Test]
        public void RingScale_PutsTheDrawnContourAtTheRequestedRadius()
        {
            // ElementalSprites.Ring peaks at normalized radius 0.78 of a 1x1 sprite, so the
            // drawn band sits at scale * 0.39 world units. Getting this wrong is invisible in
            // code and wrong on screen at every size — CLAUDE.md records it for that reason.
            const float radius = 1.2f;
            float scale = SpellCastFlourishFX.RingScaleFor(radius);
            Assert.AreEqual(radius, scale * 0.39f, 0.0001f);
        }

        [Test]
        public void CastTint_IsItsOwnLayer()
        {
            // Layers multiply, so a spell cast during a weapon swap has to compose with the
            // swap rather than overwrite it. Sharing a layer is how the pre-SpriteTintStack
            // bugs worked: whoever finished last restored the other one's tint as "original".
            Assert.AreNotEqual(TintLayer.Cast, TintLayer.Equip);
            Assert.AreNotEqual(TintLayer.Cast, TintLayer.Freeze);
            Assert.IsTrue(System.Enum.IsDefined(typeof(TintLayer), TintLayer.Cast));
        }

        [Test]
        public void EveryElement_ResolvesAPaletteWithAnAccentAndALight()
        {
            foreach (SpellElement element in System.Enum.GetValues(typeof(SpellElement)))
            {
                var palette = ElementPalette.For(element);
                Assert.IsNotNull(palette.accentSprite,
                    element + ": the flourish's motes are drawn with the accent sprite, so an " +
                    "element without one would gather invisibly.");
                Assert.Greater(palette.lightIntensity, 0f, element + " has no light to pop.");
            }
        }
    }
}
