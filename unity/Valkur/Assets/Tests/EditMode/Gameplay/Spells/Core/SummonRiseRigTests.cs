using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Gameplay.Spells.Core
{
    /// <summary>
    /// The summon's arrival rig, and the one-line omission that made every cast of it a
    /// permanent error source.
    ///
    /// <para><c>BuildMound</c> created ten clod renderers, parented them, positioned them —
    /// and never wrote them into <c>_clods</c>. The array stayed all-null, so the mound that
    /// is supposed to OCCLUDE the creature while it is still under the floor was drawn at
    /// alpha 0, and <c>UpdateClods</c> dereferenced <c>_clods[0]</c> every frame. Nothing
    /// about that is visible in the builder: the loop reads correctly, the sprites really
    /// exist in the hierarchy, and the only disagreement is between the objects and the array
    /// that is supposed to name them.</para>
    ///
    /// <para>Its second half is worse than the first, and is why the teardown is pinned here
    /// too. The throw came out of <c>Update</c> ABOVE the <c>_age &gt;= T_END</c> destroy at
    /// the foot of that method, so the rig never tore itself down: ONE broken summon logged an
    /// exception a frame for the rest of the session. Any layer that can throw has that power
    /// for as long as the teardown runs last.</para>
    /// </summary>
    public class SummonRiseRigTests
    {
        private const string UpdateSource =
            "Assets/_Project/Scripts/Gameplay/Spells/Visuals/SummonRiseFX.Update.cs";

        private static readonly BindingFlags Inst =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static System.Type FxType()
        {
            var t = typeof(Valkur.Gameplay.Spells.SpellCaster).Assembly
                        .GetType("Valkur.Gameplay.Spells.SummonRiseFX");
            Assert.IsNotNull(t, "SummonRiseFX was renamed or moved out of Valkur.Gameplay.");
            return t;
        }

        /// <summary>
        /// Build the rig the way <c>Play</c> does, minus the Play-Mode-only creature: the
        /// whole entrance geometry comes out of <c>BuildRig</c> and none of it needs a summon.
        /// </summary>
        private Component BuildRig()
        {
            var type = FxType();
            var go = new GameObject("summon_rise_probe");
            _spawned.Add(go);

            var fx = go.AddComponent(type);

            var paletteType = typeof(Valkur.Gameplay.Spells.SpellCaster).Assembly
                                  .GetType("Valkur.Gameplay.Spells.RootPalette");
            Assert.IsNotNull(paletteType, "RootPalette was renamed.");
            var from = paletteType.GetMethod("From", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(from, "RootPalette.From was renamed.");
            var palette = from.Invoke(null, new object[] { new Color(0.35f, 0.8f, 0.4f, 1f) });

            type.GetField("_palette", Inst).SetValue(fx, palette);
            type.GetMethod("BuildRig", Inst).Invoke(fx, null);
            return fx;
        }

        private static IEnumerable<FieldInfo> RendererArrayFields(System.Type type)
            => type.GetFields(Inst).Where(f => f.FieldType == typeof(SpriteRenderer[]));

        // ── the omission itself ──────────────────────────────────────────────────────

        [Test]
        public void EveryRendererArray_IsFullyPopulated()
        {
            var fx = BuildRig();
            var type = FxType();

            var fields = RendererArrayFields(type).ToList();
            Assert.GreaterOrEqual(fields.Count, 3,
                "The rig lost its renderer arrays, so this fixture is measuring nothing.");

            foreach (var f in fields)
            {
                var array = (SpriteRenderer[])f.GetValue(fx);
                Assert.IsNotNull(array, f.Name + " was never allocated.");
                Assert.Greater(array.Length, 0, f.Name + " is empty.");
                for (int i = 0; i < array.Length; i++)
                    Assert.IsNotNull(array[i],
                        f.Name + "[" + i + "] is null: the builder created the sprite and " +
                        "never stored it, which is exactly what shipped for _clods.");
            }
        }

        [Test]
        public void ClodArray_NamesTheClodsThatAreActuallyInTheHierarchy()
        {
            // The array and the hierarchy are two records of the same ten objects. Comparing
            // them is what catches a builder that stores SOME of what it made.
            var fx = BuildRig();
            var clods = (SpriteRenderer[])FxType().GetField("_clods", Inst).GetValue(fx);

            var inScene = fx.GetComponentsInChildren<SpriteRenderer>(true)
                            .Where(sr => sr.name.StartsWith("Clod"))
                            .ToList();

            Assert.AreEqual(inScene.Count, clods.Length,
                "The mound built a different number of clods than the array holds.");
            CollectionAssert.AreEquivalent(inScene, clods);
        }

        // ── the layers must survive being ticked ─────────────────────────────────────

        [Test]
        public void EveryLayer_TicksAtEveryBeat_WithoutThrowing()
        {
            var fx = BuildRig();
            var type = FxType();
            var age = type.GetField("_age", Inst);

            // Past T_SPAWN and T_THROW so the clod loop reaches its heaping and its thrown
            // branches, which is where the null deref sat. UpdateBody is skipped by its own
            // null-creature guard, correctly: there is no summon in Edit Mode.
            foreach (float t in new[] { 0.0f, 0.10f, 0.25f, 0.45f, 0.70f, 0.94f })
            {
                age.SetValue(fx, t);
                foreach (var name in new[] { "UpdateSigil", "UpdateTendrils", "UpdateBody",
                                             "UpdateClods", "UpdateMotes", "UpdateLight" })
                {
                    var m = type.GetMethod(name, Inst);
                    Assert.IsNotNull(m, name + " was renamed.");
                    var captured = m;
                    Assert.DoesNotThrow(() => captured.Invoke(fx, null),
                        name + " threw at age " + t + ".");
                }
            }
        }

        [Test]
        public void TheMound_IsOpaqueAndHidesTheRise()
        {
            // The clods are the only thing hiding the creature while it is below the floor
            // line, so they are opaque earth that reaches full alpha, not light.
            var fx = BuildRig();
            var type = FxType();
            var clods = (SpriteRenderer[])type.GetField("_clods", Inst).GetValue(fx);
            type.GetField("_age", Inst).SetValue(fx, 0.30f);   // heaping, before the burst
            type.GetMethod("UpdateClods", Inst).Invoke(fx, null);

            foreach (var sr in clods)
            {
                Assert.AreEqual(1f, sr.color.a, 1e-4f,
                    "A clod left at alpha 0 occludes nothing, which is what the unstored " +
                    "array produced.");
                Assert.AreSame(Valkur.Gameplay.Spells.ElementalSprites.SharedUnlitMaterial,
                               sr.sharedMaterial,
                               "Earth is the opaque family — additive soil adds light instead " +
                               "of hiding the body.");
            }
        }

        // ── the teardown must outrank the layers ─────────────────────────────────────

        [Test]
        public void Update_DecidesItsTeardownBeforeDrawingAnyLayer()
        {
            // A source check rather than a behavioural one, because Destroy is an outright
            // error in Edit Mode. What it pins is the ORDER: a layer that throws must not be
            // able to keep the rig alive, and moving the destroy back to the foot of Update is
            // the obvious tidy-up.
            Assert.IsTrue(File.Exists(UpdateSource), UpdateSource + " moved.");
            var text = File.ReadAllText(UpdateSource);

            int destroy = text.IndexOf("Destroy(gameObject)", System.StringComparison.Ordinal);
            Assert.Greater(destroy, -1, "SummonRiseFX.Update no longer destroys its rig.");

            foreach (var layer in new[] { "UpdateSigil()", "UpdateTendrils()", "UpdateClods()",
                                          "UpdateMotes()", "UpdateLight()" })
            {
                int call = text.IndexOf(layer, System.StringComparison.Ordinal);
                Assert.Greater(call, -1, layer + " is no longer called from Update.");
                Assert.Less(destroy, call,
                    "The T_END teardown must be decided before " + layer + ": a throw in a " +
                    "layer below it strands the rig, logging one exception a frame forever.");
            }
        }
    }
}
