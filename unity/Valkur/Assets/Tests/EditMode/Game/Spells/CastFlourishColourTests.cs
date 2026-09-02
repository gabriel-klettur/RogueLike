using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// What colour a cast flourish comes out, and that it agrees with the spell it announces.
    ///
    /// <para>The gather runs in the half-second BEFORE the effect appears, so a mismatch is
    /// only ever visible as a moment of the wrong colour that is then replaced — which is
    /// exactly why every one of these shipped unnoticed. A slash swung a white blade after a
    /// violet gather for as long as the flourish existed.</para>
    /// </summary>
    public class CastFlourishColourTests
    {
        private const string Folder = "Assets/_Project/Data/Catalogs/Spells/";

        private static SpellDefinition Load(string key)
            => AssetDatabase.LoadAssetAtPath<SpellDefinition>(Folder + key + ".asset");

        private static IEnumerable<SpellDefinition> AllSpells()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SpellDefinition", new[] { Folder.TrimEnd('/') }))
            {
                var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (spell != null) yield return spell;
            }
        }

        // ── the blade and the gather are one colour ─────────────────────────────────

        [Test]
        public void EverySpellGathersTheColourItIsAboutToDrawWith()
        {
            var violations = new List<string>();

            foreach (var spell in AllSpells())
            {
                if (!SpellCastFlourishFX.AppliesTo(spell)) continue;

                Color drawn = SpellCastFlourishFX.ResolveSwatch(spell);
                var gather = ElementPalette.For(SpellElement.Arcane).RecolouredTo(drawn);

                Color.RGBToHSV(drawn, out float drawnHue, out float drawnSat, out _);
                Color.RGBToHSV(gather.core, out float gatherHue, out float gatherSat, out _);

                // Checked FIRST, because opaque white is achromatic too and would otherwise be
                // caught by the grey branch below. It does not mean "grey" — it means NO
                // SWATCH, and such a spell correctly keeps its element's own colour.
                if (KiPalette.IsUnauthored(drawn)) continue;

                if (drawnSat <= 0.02f)
                {
                    // An achromatic effect asks for the ABSENCE of colour, and hue is
                    // meaningless on it — RGBToHSV reports 0, which is red. What must hold is
                    // that the gather stays neutral too.
                    if (gatherSat > 0.05f)
                        violations.Add($"{spell.spellKey}: draws grey {drawn}, but the gather " +
                                       $"came out saturated at {gatherSat:F2} ({gather.core})");
                    continue;
                }

                float delta = Mathf.Abs(Mathf.DeltaAngle(drawnHue * 360f, gatherHue * 360f));
                if (delta > 5f)
                    violations.Add($"{spell.spellKey}: draws hue {drawnHue * 360f:F0}deg, " +
                                   $"gathers hue {gatherHue * 360f:F0}deg");
            }

            Assert.IsEmpty(violations,
                "A spell must gather the colour it is about to produce. The gather runs in the "
                + "half-second BEFORE the effect appears, so a mismatch shows as a moment of the "
                + "wrong colour that is then replaced.\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void ATypeWithItsOwnResolvedTintIsAskedForIt()
        {
            // The two types that apply a default of their own. Reading the raw field for either
            // lets the gather disagree with the thing it announces, which is precisely what
            // `slash` did for as long as the flourish existed.
            var slash = Load("slash");
            Assert.AreEqual(SlashExecutor.ResolveTint(slash), SpellCastFlourishFX.ResolveSwatch(slash),
                "a slash must be asked for its blade tint, not for particleColor");

            var totem = Load("healing_totem");
            Assert.AreEqual(TotemExecutor.ResolveTint(totem), SpellCastFlourishFX.ResolveSwatch(totem),
                "a totem must be asked for its resolved tint, not for particleColor");
        }

        [Test]
        public void TheHealingSpellsAreGreen()
        {
            // Both heal, and both are announced by the same gather. `healing_aura` is
            // deliberately gold AND green — "sacred ground" — so its swatch is set to the exact
            // GreenCore its inner rune already draws, which is the healing half of that pair.
            foreach (var key in new[] { "healing_aura", "healing_totem" })
            {
                var spell = Load(key);
                Assert.IsNotNull(spell, key + " is missing");

                Color drawn = SpellCastFlourishFX.ResolveSwatch(spell);
                Assert.Greater(drawn.g, drawn.r, key + " is not green: " + drawn);
                Assert.Greater(drawn.g, drawn.b, key + " is not green: " + drawn);
            }
        }

        [Test]
        public void AnUnauthoredSlashFallsBackToTheBladesOwnDefaultNotToTheRawWhite()
        {
            // `slash` leaves particleColor untouched. The executor's "unset" test used to be
            // `!= Color.clear`, which NO shipped spell matches, so DefaultTint was unreachable
            // and the blade drew pure white while the flourish — which uses the real sentinel,
            // opaque white — read the same field as unauthored and gathered arcane violet.
            var spell = Load("slash");
            Assert.IsNotNull(spell);
            Assert.IsTrue(KiPalette.IsUnauthored(spell.particleColor),
                "this test is about the unauthored case; author a colour and it no longer applies");

            Color blade = SlashExecutor.ResolveTint(spell);
            Assert.IsFalse(KiPalette.IsUnauthored(blade),
                "an unauthored slash must resolve to the executor's own default, not stay white — "
                + "otherwise the flourish reads it as unauthored and falls back to the element");
        }

        [Test]
        public void TheClearSentinelIsGoneFromTheSlashExecutor()
        {
            // Guards the specific dead branch, not just its symptom: `Color.clear` can only be
            // hit by a swatch with alpha 0, and nothing ships one.
            foreach (var spell in AllSpells())
                Assert.Greater(spell.particleColor.a, 0f,
                    spell.spellKey + " has an alpha-zero swatch — the old Color.clear sentinel "
                    + "would be reachable again, and it disagrees with KiPalette.IsUnauthored");
        }

        // ── the retint's own contract ───────────────────────────────────────────────

        [Test]
        public void AnAchromaticSwatchStaysNeutral()
        {
            // RGBToHSV reports hue 0 — RED — for any grey, so blending toward it the naive way
            // lights a grey spell with a pale pink gather. Measured on hostile_slash_gray
            // before the fix: a 0.59 grey blade against a (1.00, 0.84, 0.84) core.
            foreach (var grey in new[] { new Color(0.59f, 0.59f, 0.59f, 1f),
                                         new Color(0.04f, 0.04f, 0.04f, 1f) })
            {
                var tinted = ElementPalette.For(SpellElement.Arcane).RecolouredTo(grey);
                foreach (var field in new[] { tinted.hotCore, tinted.core, tinted.glow,
                                              tinted.halo, tinted.accent, tinted.lightColor })
                {
                    Color.RGBToHSV(field, out _, out float saturation, out _);
                    Assert.LessOrEqual(saturation, 0.05f,
                        $"grey swatch {grey} produced {field}, saturation {saturation:F2}");
                }
            }
        }

        [Test]
        public void AnUnauthoredSwatchLeavesTheElementPaletteAlone()
        {
            var basePalette = ElementPalette.For(SpellElement.Fire);
            var tinted = basePalette.RecolouredTo(Color.white);

            // Opaque white is what the field holds when nobody has touched it. Treating it as a
            // deliberate colour would drain the hue out of every unauthored spell at once.
            Assert.AreEqual(basePalette.core, tinted.core);
            Assert.AreEqual(basePalette.halo, tinted.halo);
            Assert.AreEqual(basePalette.lightColor, tinted.lightColor);
        }

        [Test]
        public void NoAuthoredSwatchCanProduceAnInvisibleFlourish()
        {
            // Every layer is additive, where near-black adds nothing: a flourish driven to zero
            // value would not dim, it would disappear. Sweeps the shipped set rather than one
            // sample, because the darkest swatch is the one nobody thinks to check.
            foreach (var spell in AllSpells())
            {
                if (!SpellCastFlourishFX.AppliesTo(spell)) continue;

                Color swatch = spell.type == SpellType.Slash
                    ? SlashExecutor.ResolveTint(spell)
                    : spell.particleColor;
                var tinted = ElementPalette.For(SpellElement.Arcane).RecolouredTo(swatch);

                foreach (var field in new[] { tinted.hotCore, tinted.core, tinted.glow,
                                              tinted.halo, tinted.accent })
                {
                    Color.RGBToHSV(field, out _, out _, out float value);
                    Assert.Greater(value, 0.4f,
                        $"{spell.spellKey}: swatch {swatch} produced {field} at value " +
                        $"{value:F2} — additive, that is invisible");
                }
            }
        }
    }
}
