using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Gameplay.Spells
{
    /// <summary>
    /// A slash is born at the caster's WAIST, not at their shoulders.
    ///
    /// <para>Every slash shipped with <c>castAnchor: Hands</c>, which
    /// <c>ProjectileExecutor.ResolveCastOrigin</c> resolves as the sprite's visual centre plus
    /// <c>0.45 x half-height</c>. That is right for a conjuring — a fireball should leave the
    /// hands — and wrong for a swing: on a 2.5-unit character it puts the arc's origin about
    /// 72 % of the way up the body, so the blade appeared to grow out of the head rather than
    /// to be swung by the arms. Reported as "the slash comes out of the top of the character".</para>
    ///
    /// <para>The fix was DATA, not a special case in code, and deliberately so: a hard-coded
    /// "if it is a Slash, use Center" beside an authored field would make the field
    /// unfalsifiable — the Spells Editor would show an Anchor dropdown that could not change
    /// anything for the whole slash family. This project has that exact bug written down
    /// (<c>VortexFieldExecutor</c>'s <c>spawnAtMouse || isPull</c>).</para>
    ///
    /// <para>What data cannot do by itself is survive the NEXT slash. A new one is created with
    /// the enum's default, which is Hands, so it would regress silently and look correct in
    /// every file. That is what this test is for.</para>
    /// </summary>
    public class SlashCastAnchorTests
    {
        private const string SpellFolder = "Assets/_Project/Data/Catalogs/Spells";

        private static IEnumerable<SpellDefinition> ShippedSlashes()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:SpellDefinition", new[] { SpellFolder }))
            {
                var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (spell != null && spell.type == SpellType.Slash)
                    yield return spell;
            }
        }

        [Test]
        public void EveryShippedSlash_IsAnchoredAtTheCentre()
        {
            var slashes = ShippedSlashes().ToList();
            Assert.IsNotEmpty(slashes, "no Slash spells found — the folder or the type moved");

            var wrong = slashes
                .Where(s => s.castAnchor != SpellCastAnchor.Center)
                .Select(s => $"  {s.spellKey}: {s.castAnchor}")
                .ToList();

            Assert.IsEmpty(wrong,
                "A slash is swung from the body, so its arc must start at the waist. These "
                + "author something else, and Hands is what a NEW spell defaults to:\n"
                + string.Join("\n", wrong));
        }

        [Test]
        public void TheCentreAnchor_ResolvesToTheSpriteMiddle_AndHandsSitsWellAboveIt()
        {
            // The two halves of the claim, measured rather than assumed: Center really is the
            // visual middle, and the Hands anchor this moved away from really was high enough
            // to read as "the top of the character".
            var go = new GameObject("SlashCastAnchorTests.Caster");
            try
            {
                var sr = go.AddComponent<SpriteRenderer>();
                var tex = new Texture2D(16, 40);
                // 40 px at 16 PPU = a 2.5-unit body, the shipped dwarf's height. Pivot at the
                // feet, which is where every entity transform in this project sits.
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, 16, 40), new Vector2(0.5f, 0f), 16f);
                go.transform.position = Vector3.zero;

                Vector3 centre = Valkur.Gameplay.Spells.ProjectileExecutor
                    .ResolveCastOrigin(go.transform, SpellCastAnchor.Center);
                Vector3 hands = Valkur.Gameplay.Spells.ProjectileExecutor
                    .ResolveCastOrigin(go.transform, SpellCastAnchor.Hands);

                Assert.AreEqual(1.25f, centre.y, 0.02f, "Center is the middle of a 2.5-unit body");
                Assert.Greater(hands.y, centre.y, "Hands sits above the middle");
                // ~1.81 of 2.5 — about 72 % up the body, which is the head on a stocky character.
                Assert.Greater(hands.y - centre.y, 0.5f,
                    "the gap this change closes is over half a world unit, not a rounding error");

                Object.DestroyImmediate(tex);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BothSlashPaths_ResolveTheOriginThroughTheAnchor_NeverInline()
        {
            // slash_regular keeps its own authored implementation (RegularSlashAttack) while
            // every other slash goes through SlashAttack, and BOTH are handed their origin by
            // SlashExecutor. An inline origin in either would leave half the family anchored
            // at the shoulders while the data said otherwise — the shape that survives review
            // because each half reads correctly on its own.
            string path = Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Spells/Executors/SlashExecutor.cs");
            string code = string.Join("\n", File.ReadAllLines(path)
                .Where(l => !l.TrimStart().StartsWith("//")));

            StringAssert.Contains("ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell)", code,
                "the origin must come from the overload that reads the spell's own anchor");
            StringAssert.Contains("RegularSlashAttack.Spawn(ctx, castStart", code,
                "the regular slash must be handed that same origin");
            StringAssert.Contains("SlashAttack.Spawn(ctx, castStart", code,
                "and so must every other slash");
        }
    }
}
