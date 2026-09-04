using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the LAST link of the harvest animation chain: that the key a node asks for
    /// actually resolves to the variant the character has art for, once the binder has
    /// installed the reservation table.
    ///
    /// <para>The data end is pinned by <c>HarvestingDataTests</c> — the profile names a key,
    /// the dwarf reserves a variant claiming it, the variant has 64 non-null sprites. Every
    /// one of those can be true while the swing still renders the wrong pose, because what
    /// <see cref="PlayerController.PlayWorkSwing"/> actually calls is
    /// <c>DirectionalAnimator.VariantForSpell</c>, and that answers from a table the BINDER
    /// installs rather than from the asset. <c>SetVariants</c> also DROPS variants that
    /// resolved to no frames, so an index computed from the authored list slides off from the
    /// first empty slot on. Asserting the two halves separately proves nothing about the
    /// composition, which is the failure this project keeps recording.</para>
    ///
    /// <para>It runs in Edit Mode against a bare GameObject rather than in Play, because the
    /// question is about binding rather than about gameplay. Note the character is built by
    /// the same <see cref="EntityAnimationBinder"/> entry point the game boots through: a
    /// second way of installing the sets here would be a second opinion, and could pass while
    /// the real path failed.</para>
    /// </summary>
    [TestFixture]
    public class HarvestSwingAnimationTests
    {
        private const string DwarfPath = "Assets/_Project/Data/Catalogs/Players/dwarf.asset";

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
        }

        private DirectionalAnimator BindDwarf()
        {
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(DwarfPath);
            Assert.That(def, Is.Not.Null, $"Missing {DwarfPath}.");

            _go = new GameObject("DwarfProbe");
            _go.AddComponent<SpriteRenderer>();

            // In Edit Mode a component added outside Play never receives Awake, so anything
            // the binder needs must be present before it runs rather than created by one.
            var animator = _go.AddComponent<DirectionalAnimator>();

            Assert.That(EntityAnimationBinder.ApplyPlayerVisuals(_go, def), Is.True,
                "The binder refused the shipped dwarf definition.");
            return animator;
        }

        [Test]
        public void TheMineKeyResolvesToAVariantWithFrames()
        {
            var animator = BindDwarf();

            int variant = animator.VariantForSpell(DirectionalAnimator.AnimState.Attack,
                "harvest_mine");

            Assert.That(variant, Is.GreaterThanOrEqualTo(0),
                "'harvest_mine' resolves to no variant, so PlayWorkSwing falls back to the " +
                "rotation and the dwarf punches or kicks at the seam instead of mining it.");

            float length = animator.GetStateLength(DirectionalAnimator.AnimState.Attack, variant);
            Assert.That(length, Is.GreaterThan(0f),
                "The variant resolved but has no frames — the swing would render nothing at " +
                "all, which is worse than falling back to a pose that exists.");
        }

        [Test]
        public void TheShippedMineProfileAsksForAKeyThisCharacterCanAnswer()
        {
            // The composition, rather than either half: the value the seam actually carries,
            // resolved through the table the binder actually installs.
            var mine = AssetDatabase.LoadAssetAtPath<DestructionProfile>(
                "Assets/_Project/Data/Catalogs/Destruction/DP_mine_iron.asset");
            Assert.That(mine, Is.Not.Null);

            var animator = BindDwarf();

            Assert.That(animator.VariantForSpell(DirectionalAnimator.AnimState.Attack,
                    mine.swingAnimationKey),
                Is.GreaterThanOrEqualTo(0),
                $"DP_mine_iron asks for '{mine.swingAnimationKey}' and the dwarf cannot answer it.");
        }

        [Test]
        public void MiningAndChoppingResolveToDifferentVariants()
        {
            var animator = BindDwarf();

            int mine = animator.VariantForSpell(DirectionalAnimator.AnimState.Attack, "harvest_mine");
            int chop = animator.VariantForSpell(DirectionalAnimator.AnimState.Attack, "harvest_chop");

            Assert.That(mine, Is.GreaterThanOrEqualTo(0));
            Assert.That(chop, Is.GreaterThanOrEqualTo(0));
            Assert.That(mine, Is.Not.EqualTo(chop),
                "A pickaxe and an axe resolving to the same variant means one sheet is not " +
                "bound, and the symptom is a character chopping at a rock.");
        }

        [Test]
        public void AnUnknownKeyFallsBackRatherThanResolving()
        {
            // The fallback is what lets this ship with only the dwarf drawn: the elf and the
            // barbarian have neither sheet, and must keep swinging with their ordinary attack
            // rather than standing still while the node loses charges.
            var animator = BindDwarf();

            Assert.That(animator.VariantForSpell(DirectionalAnimator.AnimState.Attack,
                    "harvest_not_a_real_key"),
                Is.LessThan(0));
            Assert.That(animator.VariantForSpell(DirectionalAnimator.AnimState.Attack, ""),
                Is.LessThan(0));
        }
    }
}
