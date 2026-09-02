using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The tornado the two vortex spells gather.
    ///
    /// <para>It is one gesture with two directions: <c>forceMode</c> decides which way the
    /// funnel turns and whether its debris is dragged in or thrown out. Everything here guards
    /// something that would still look like a tornado while being the WRONG tornado — a cone
    /// that does not taper, a spin that does not reverse, a band that cannot show rotation.</para>
    /// </summary>
    public class VortexFlourishTests
    {
        private const string Folder = "Assets/_Project/Data/Catalogs/Spells/";

        private static SpellDefinition Load(string key)
            => AssetDatabase.LoadAssetAtPath<SpellDefinition>(Folder + key + ".asset");

        private static CastFlourishProfile ProfileFor(string key)
            => CastFlourishProfile.Build(Load(key));

        [Test]
        public void BothVortexSpellsGatherAFunnel()
        {
            foreach (var key in new[] { "vortex_pull", "vortex_push" })
            {
                var spell = Load(key);
                Assert.IsNotNull(spell, key + " is missing");
                Assert.AreEqual(SpellType.VortexField, spell.type, key);

                var profile = ProfileFor(key);
                Assert.AreEqual("Vortex", profile.FamilyName, key + " no longer resolves to Vortex");
                Assert.Greater(profile.FunnelBands, 1,
                    key + ": one band is a ring, not a funnel — the cone needs a stack");
                Assert.AreEqual(MoteApproach.SpiralFunnel, profile.Approach,
                    key + ": debris must ride the funnel, or it orbits a shape nothing is drawing");
                Assert.AreEqual(LanceAim.None, profile.Lance,
                    key + ": a vortex does not point anywhere, it turns");
            }
        }

        [Test]
        public void TheFunnelIsACone()
        {
            foreach (var key in new[] { "vortex_pull", "vortex_push" })
            {
                var profile = ProfileFor(key);

                // A tornado is recognised by its OUTLINE before any debris in it is noticed,
                // and the outline is the taper. Equal radii would be a cylinder.
                Assert.Greater(profile.FunnelTopRadius, profile.FunnelBaseRadius * 2f,
                    key + ": base " + profile.FunnelBaseRadius + " to top " + profile.FunnelTopRadius
                    + " barely tapers — that reads as a column, not a funnel");
                Assert.Greater(profile.FunnelHeight, profile.FunnelTopRadius,
                    key + ": wider than it is tall is a whirlpool, not a tornado");
            }
        }

        [Test]
        public void PullAndPushTurnOppositeWays()
        {
            var pull = ProfileFor("vortex_pull");
            var push = ProfileFor("vortex_push");

            Assert.AreNotEqual(0f, pull.FunnelSpin, "a funnel that does not spin is a cone");
            Assert.Less(pull.FunnelSpin * push.FunnelSpin, 0f,
                "pull and push must turn opposite ways — it is the only thing that separates "
                + "them on screen, since both are the same funnel");

            // The ground circle has to agree with the funnel standing on it. Opposite senses
            // read as two effects that happen to overlap.
            Assert.Greater(pull.FunnelSpin * pull.SigilSpin, 0f, "pull: sigil turns against its funnel");
            Assert.Greater(push.FunnelSpin * push.SigilSpin, 0f, "push: sigil turns against its funnel");
        }

        [Test]
        public void TheDebrisGoesWhereTheForceGoes()
        {
            Assert.AreEqual(MoteDeparture.PullInward, ProfileFor("vortex_pull").Departure,
                "a pull that flings its debris outward is telling the player the opposite of "
                + "what the spell does");
            Assert.AreEqual(MoteDeparture.PushOutward, ProfileFor("vortex_push").Departure);
        }

        [Test]
        public void PullIsRedAndPushIsBlue()
        {
            Color pull = SpellCastFlourishFX.ResolveSwatch(Load("vortex_pull"));
            Assert.Greater(pull.r, pull.g, "vortex_pull is not red: " + pull);
            Assert.Greater(pull.r, pull.b, "vortex_pull is not red: " + pull);

            Color push = SpellCastFlourishFX.ResolveSwatch(Load("vortex_push"));
            Assert.Greater(push.b, push.r, "vortex_push is not blue: " + push);
            Assert.Greater(push.b, push.g, "vortex_push is not blue: " + push);
        }

        [Test]
        public void TheBandIsAnArcSoItsRotationCanBeSeen()
        {
            // THE POINT OF THE SPRITE. A full ring is rotationally symmetric, so spinning one
            // is invisible and the funnel reads as a static cone. Every variant must leave a
            // real gap in its sweep.
            TornadoSprites.EnsureAll();

            for (int variant = 0; variant < TornadoSprites.BandVariants; variant++)
            {
                var sprite = TornadoSprites.Band(variant);
                Assert.IsNotNull(sprite, "band variant " + variant + " is missing");

                var texture = sprite.texture;
                int size = texture.width;
                float centre = size * 0.5f;
                float ringPixels = TornadoSprites.BandRadius * centre;

                int covered = 0;
                const int samples = 72;
                for (int s = 0; s < samples; s++)
                {
                    float angle = s / (float)samples * Mathf.PI * 2f;
                    int x = Mathf.RoundToInt(centre + Mathf.Cos(angle) * ringPixels);
                    int y = Mathf.RoundToInt(centre + Mathf.Sin(angle) * ringPixels);
                    x = Mathf.Clamp(x, 0, size - 1);
                    y = Mathf.Clamp(y, 0, size - 1);
                    if (texture.GetPixel(x, y).a > 0.15f) covered++;
                }

                Assert.Greater(covered, 8,
                    "variant " + variant + " draws almost nothing on its ring — it would be invisible");
                Assert.Less(covered, samples - 6,
                    "variant " + variant + " covers " + covered + "/" + samples + " of its ring. A closed "
                    + "ring is rotationally symmetric, so spinning it shows nothing.");
            }
        }

        [Test]
        public void TheVariantsAreActuallyDifferent()
        {
            // A funnel stacked from one repeated band resolves into concentric circles and
            // reads as a spring. The variants exist to break that up.
            TornadoSprites.EnsureAll();

            var coverage = new List<int>();
            for (int variant = 0; variant < TornadoSprites.BandVariants; variant++)
            {
                var texture = TornadoSprites.Band(variant).texture;
                int size = texture.width;
                float centre = size * 0.5f;
                float ringPixels = TornadoSprites.BandRadius * centre;

                int covered = 0;
                for (int s = 0; s < 72; s++)
                {
                    float angle = s / 72f * Mathf.PI * 2f;
                    int x = Mathf.Clamp(Mathf.RoundToInt(centre + Mathf.Cos(angle) * ringPixels), 0, size - 1);
                    int y = Mathf.Clamp(Mathf.RoundToInt(centre + Mathf.Sin(angle) * ringPixels), 0, size - 1);
                    if (texture.GetPixel(x, y).a > 0.15f) covered++;
                }
                coverage.Add(covered);
            }

            Assert.Greater(new HashSet<int>(coverage).Count, 1,
                "every band variant sweeps the same arc: " + string.Join(", ", coverage));
        }

        [Test]
        public void NoOtherFamilyDrawsAFunnel()
        {
            // The rig only builds pieces a family asks for, and the funnel is seven transform
            // pairs. A default that leaked into every spell would be seven wasted objects per
            // cast and a tornado on a fireball.
            foreach (var guid in AssetDatabase.FindAssets("t:SpellDefinition", new[] { Folder.TrimEnd('/') }))
            {
                var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (spell == null || spell.type == SpellType.VortexField) continue;

                Assert.AreEqual(0, CastFlourishProfile.Build(spell).FunnelBands,
                    spell.spellKey + " (" + spell.type + ") asks for a funnel and should not");
            }
        }
    }
}
