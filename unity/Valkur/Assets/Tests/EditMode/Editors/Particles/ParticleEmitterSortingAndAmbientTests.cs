using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Pins the two things <see cref="ParticleEmitter"/> learned to read off a preset:
    /// WHERE it draws (sorting layer / order / fudge) and HOW BRIGHT it draws at night
    /// (the day-night ambient tint).
    ///
    /// Both replaced a hard-coded constant, which is what makes them dangerous. Sorting was
    /// literally <c>sortingLayerName = "VFX"; sortingOrder = 0;</c> for every system this
    /// emitter ever built, root and composite layer alike -- VFX sits above Entities,
    /// Decorations, WallsTop, ObjectsHigh and Projectiles, so every falling leaf and every
    /// mote of pollen drew in FRONT of the player, the NPCs, the wall tops and the tree
    /// canopy it fell from. The ambient tint was absent entirely: particle materials are
    /// unlit, so the Light2D that drives the world to a few percent brightness at night
    /// never touched these quads and the foliage kept rendering at noon values over a
    /// near-black tilemap.
    ///
    /// The failure mode of both features is silence. A preset that authors nothing must come
    /// out bit-for-bit as it did before, or all 131 shipped presets re-layer and re-colour at
    /// once with no error anywhere; a preset that authors something must have it actually
    /// reach the renderer, or the authoring UI is a placebo.
    ///
    /// EditMode: coroutines do not run, so the 0.5 s ambient tick never fires here. Every
    /// test drives the non-coroutine entry point, <see cref="ParticleEmitter.ApplyPreset"/>,
    /// which is also the path that bakes the FIRST tint -- the one the player actually sees
    /// on a freshly streamed-in emitter.
    /// </summary>
    [TestFixture]
    public class ParticleEmitterSortingAndAmbientTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        /// <summary>
        /// A SHIPPED preset whose .asset file predates the sorting fields and therefore omits
        /// every one of their keys on disk. It is the subject of
        /// <see cref="ApplyPreset_AShippedAssetThatOmitsTheSortingKeys_StillDeserializesToVfxAtOrderZero"/>,
        /// which is the only test in this fixture that exercises the deserializer rather than
        /// the C# field initialisers.
        ///
        /// Chosen because it is about as far from the vegetation work as a preset gets, and 118
        /// of the shipped presets are still in the same state, so if this one is ever re-authored
        /// point the const at another one — the test asserts the absence itself and will say so
        /// rather than quietly passing.
        ///
        /// It used to be PP_torch_flame, and the test did exactly what it promised: giving that
        /// preset a lightPresetKey meant saving it through Unity, and Unity re-serialises a whole
        /// asset, filling in every previously-absent field with its default. All four sorting keys
        /// appeared, every one of them at the value the absent-key path already produced, so
        /// nothing changed at runtime — but the file stopped exercising the path this fixture
        /// exists for, and it said so instead of passing for the wrong reason.
        /// </summary>
        private const string UNAUTHORED_DEPTH_ASSET_PATH =
            "Assets/_Project/Data/Catalogs/Particles/PP_chimney_smoke.asset";

        /// <summary>The id <see cref="UNAUTHORED_DEPTH_ASSET_PATH"/> must still carry.</summary>
        private const string UNAUTHORED_DEPTH_ASSET_ID = "chimney_smoke";

        private readonly List<GameObject> _createdGos = new List<GameObject>();
        private readonly List<ScriptableObject> _createdSos = new List<ScriptableObject>();

        [SetUp]
        public void SetUp()
        {
            // Building a ParticleSystem in EditMode logs renderer/material chatter that would
            // otherwise fail an assertion that passed. LogAssert.Expect still VERIFIES while
            // this is on -- it only stops UNEXPECTED messages from failing the test -- so the
            // two warning tests below remain real assertions.
            LogAssert.ignoreFailingMessages = true;

            // Both statics this fixture leans on outlive a test: Domain Reload is OFF and the
            // only reset hooks are [RuntimeInitializeOnLoadMethod], which never fire in
            // EditMode. Leaving either dirty makes the warn-once test order-dependent and
            // leaks a day/night cycle into every other fixture in the suite.
            ClearSortingLayerVerdictCache();
            ClearDayNightSingleton();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdGos)
                if (go != null) Object.DestroyImmediate(go);
            _createdGos.Clear();

            foreach (var so in _createdSos)
                if (so != null) Object.DestroyImmediate(so);
            _createdSos.Clear();

            ClearDayNightSingleton();
            ClearSortingLayerVerdictCache();
            LogAssert.ignoreFailingMessages = false;
        }

        // -------------------------------------------------------------------- fixtures

        private ParticleEmitter CreateEmitter(string name = "SortingAmbientTestEmitter")
        {
            var go = new GameObject(name);
            _createdGos.Add(go);
            return go.AddComponent<ParticleEmitter>();
        }

        /// <summary>
        /// A minimal continuous emitter. Every field this fixture never asserts on is held
        /// constant so a failure can only be about sorting or about colour.
        /// </summary>
        private ParticlePresetDefinition MakePreset(
            string id,
            string sortingLayer = "",
            int sortingOrder = 0,
            float sortingFudge = 0f,
            bool respondsToAmbientLight = false,
            Color? authoredColor = null,
            float colorIntensity = 1f)
        {
            Color c = authoredColor ?? Color.white;
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _createdSos.Add(def);
            def.id = id;
            def.displayName = id;
            def.type = "aura";
            def.vfx = new ParticleVfxParams
            {
                kind                   = "aura",
                loops                  = true,
                emitRate               = 10f,
                count                  = 6,
                lifespan               = 0.5f,
                speed                  = 1f,
                sizeMin                = 0.1f,
                sizeMax                = 0.2f,
                sortingLayer           = sortingLayer,
                sortingOrder           = sortingOrder,
                sortingFudge           = sortingFudge,
                respondsToAmbientLight = respondsToAmbientLight,
                colorIntensity         = colorIntensity,
                // One entry, so BuildColorParameter takes the single-colour path and the
                // result is readable back as main.startColor.color.
                colors                 = new[] { c },
                color                  = c,
            };
            return def;
        }

        private static ParticlePresetCatalog LoadCatalog()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH);
            Assert.IsTrue(cat != null, "ParticlePresetCatalog not found at " + CATALOG_PATH + ".");
            return cat;
        }

        private static ParticleSystem RootPsOf(ParticleEmitter emitter)
        {
            // EnsureParticleSystem always names the root child "Particles"; the layer
            // children are "Layer_0", "Layer_1", ...
            var t = emitter.transform.Find("Particles");
            Assert.IsTrue(t != null, "ApplyPreset must have built the root ParticleSystem child 'Particles'.");
            var ps = t.GetComponent<ParticleSystem>();
            Assert.IsTrue(ps != null, "The 'Particles' child must carry the root ParticleSystem.");
            return ps;
        }

        private static ParticleSystemRenderer RendererOf(ParticleSystem ps)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            Assert.IsTrue(r != null, "Every ParticleSystem carries a ParticleSystemRenderer.");
            return r;
        }

        private static ParticleSystemRenderer RootRendererOf(ParticleEmitter emitter)
            => RendererOf(RootPsOf(emitter));

        private static Color StartColorOf(ParticleSystem ps) => ps.main.startColor.color;

        /// <summary>
        /// Per-channel comparison. The tolerance is one 8-bit step (1/255): a start colour
        /// makes a round trip through the ParticleSystem main module, and every value this
        /// fixture distinguishes is at least 0.15 apart, so the slack costs nothing.
        /// </summary>
        private const float CHANNEL_TOLERANCE = 0.005f;

        private static void AssertChannels(Color expected, Color actual, string because)
        {
            Assert.AreEqual(expected.r, actual.r, CHANNEL_TOLERANCE, "R: " + because);
            Assert.AreEqual(expected.g, actual.g, CHANNEL_TOLERANCE, "G: " + because);
            Assert.AreEqual(expected.b, actual.b, CHANNEL_TOLERANCE, "B: " + because);
            Assert.AreEqual(expected.a, actual.a, CHANNEL_TOLERANCE, "A: " + because);
        }

        // -------------------------------------------------------------------- reflection

        /// <summary>
        /// The warn-once ledger inside ParticleEmitter.Colors.cs. Its whole purpose is to
        /// survive a session, so a test that wants to observe the warning has to clear it.
        /// </summary>
        private static void ClearSortingLayerVerdictCache()
        {
            var f = typeof(ParticleEmitter).GetField(
                "_sortingLayerVerdicts", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f,
                "ParticleEmitter._sortingLayerVerdicts is gone -- the once-per-name warning " +
                "ledger this fixture pins no longer exists under that name.");
            var dict = f.GetValue(null) as IDictionary;
            Assert.IsNotNull(dict, "_sortingLayerVerdicts must be a dictionary keyed by authored name.");
            dict.Clear();
        }

        private static float AmbientChannelFloorConst()
        {
            var f = typeof(ParticleEmitter).GetField(
                "AmbientChannelFloor", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "ParticleEmitter.AmbientChannelFloor is gone -- the floor that " +
                                "keeps vegetation visible at night is no longer named that.");
            return (float)f.GetValue(null);
        }

        private static Color CurrentAmbientTintDirect()
        {
            var m = typeof(ParticleEmitter).GetMethod(
                "CurrentAmbientTint", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "ParticleEmitter.CurrentAmbientTint is gone.");
            return (Color)m.Invoke(null, null);
        }

        /// <summary>How many systems this emitter enrolled with the day/night tracker.</summary>
        private static int AmbientTargetCount(ParticleEmitter emitter)
        {
            var f = typeof(ParticleEmitter).GetField(
                "_ambientTargets", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "ParticleEmitter._ambientTargets is gone.");
            var list = f.GetValue(emitter) as ICollection;
            Assert.IsNotNull(list, "_ambientTargets must be a collection of enrolled systems.");
            return list.Count;
        }

        private static FieldInfo DayNightSingletonField()
        {
            // _instance lives on SingletonMonoBehaviour<DayNightCycle>, one hop up.
            var t = typeof(DayNightCycle).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            Assert.Fail("SingletonMonoBehaviour<DayNightCycle>._instance not found.");
            return null;
        }

        private static void ClearDayNightSingleton() => DayNightSingletonField().SetValue(null, null);

        /// <summary>
        /// A DayNightCycle reporting a fixed ambient colour. EditMode never runs Awake, so the
        /// singleton slot is filled by hand (cleared first: Awake's duplicate guard calls
        /// Destroy(), which is illegal outside Play Mode), and CurrentColor -- a private
        /// setter, normally written once per frame by Update -- is driven through its setter.
        /// </summary>
        private DayNightCycle CreateCycleAtColor(Color ambient)
        {
            var field = DayNightSingletonField();
            field.SetValue(null, null);

            var go = new GameObject("TestDayNightCycle");
            _createdGos.Add(go);
            var cycle = go.AddComponent<DayNightCycle>();
            field.SetValue(null, cycle);

            var prop = typeof(DayNightCycle).GetProperty("CurrentColor");
            Assert.IsNotNull(prop, "DayNightCycle.CurrentColor is gone -- it is the single value " +
                                   "the particle ambient tint reads.");
            var setter = prop.GetSetMethod(true);
            Assert.IsNotNull(setter, "DayNightCycle.CurrentColor must still be settable so a test " +
                                     "can hold the world at a fixed hour.");
            setter.Invoke(cycle, new object[] { ambient });

            Assert.IsTrue(DayNightCycle.Instance != null, "Fixture guard: the cycle must be reachable.");
            AssertChannels(ambient, DayNightCycle.Instance.CurrentColor,
                "fixture guard: CurrentColor was not installed.");
            return cycle;
        }

        // ====================================================================== SORTING

        /// <summary>
        /// A preset that authors no sorting layer must land on VFX at order 0 with no fudge --
        /// byte-for-byte what ParticleEmitter hard-coded for every system it ever built.
        ///
        /// NOTE WHAT THIS DOES AND DOES NOT COVER. The preset is built in C#, so the field
        /// initialisers on ParticleVfxParams run by construction and sortingLayer is "" because
        /// the compiler made it so. That pins ConfigureRenderer's empty-name path and nothing
        /// else. The story about "all 131 shipped presets silently change depth at once" rests
        /// on a different mechanism entirely -- Unity restoring those same initialisers for keys
        /// that are ABSENT from a .asset file, which no C#-constructed preset can reach.
        /// <see cref="ApplyPreset_AShippedAssetThatOmitsTheSortingKeys_StillDeserializesToVfxAtOrderZero"/>
        /// is the test that covers it.
        /// </summary>
        [Test]
        public void ApplyPreset_PresetAuthorsNoSortingLayer_StillLandsOnVfxAtOrderZero()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(MakePreset("sorting_unauthored"), 1f);

            var r = RootRendererOf(emitter);
            Assert.AreEqual(SortingConfig.LAYER_VFX, r.sortingLayerName,
                "An empty sortingLayer must reproduce the historical hard-coded 'VFX'.");
            Assert.AreEqual(0, r.sortingOrder,
                "An unauthored sortingOrder must reproduce the historical hard-coded 0.");
            Assert.AreEqual(0f, r.sortingFudge, 0.0001f,
                "An unauthored sortingFudge must leave Unity's own transparency sort alone.");
        }

        /// <summary>
        /// THE REAL BACKWARD-COMPATIBILITY TEST. Roughly 120 of the shipped preset assets were
        /// written before the sorting fields existed, so their YAML carries no sortingLayer, no
        /// sortingOrder and no sortingFudge key at all -- and none of them was rewritten when
        /// the fields landed. What keeps them where they were is Unity's deserializer restoring
        /// the C# field initialisers for keys it does not find in the file. Nothing in the repo
        /// asserted that: every other "defaults reproduce the old behaviour" test in this
        /// fixture builds its preset with <c>new ParticleVfxParams { ... }</c>, which runs those
        /// initialisers by construction and so cannot observe the deserializer at all.
        ///
        /// The failure this catches is a field that stops answering "" / 0 / 0 when its key is
        /// missing -- a [SerializeField] initialiser dropped, a type changed, a field renamed
        /// with [FormerlySerializedAs] forgotten. Every one of those 120 presets would re-layer
        /// at once, silently, with no asset touched and nothing logged.
        ///
        /// The absence of the keys is asserted from the file text FIRST, so that re-authoring
        /// this asset through F1 -- which writes all four -- fails here loudly instead of
        /// turning the test into a tautology that reads a value the file now supplies.
        /// </summary>
        [Test]
        public void ApplyPreset_AShippedAssetThatOmitsTheSortingKeys_StillDeserializesToVfxAtOrderZero()
        {
            string diskPath = Path.Combine(
                Application.dataPath,
                UNAUTHORED_DEPTH_ASSET_PATH.Substring("Assets/".Length));

            Assert.IsTrue(File.Exists(diskPath),
                "The unauthored-depth subject asset is missing from disk at " + diskPath +
                ". Point UNAUTHORED_DEPTH_ASSET_PATH at another shipped preset whose YAML " +
                "carries no sorting keys -- most of the catalog still qualifies.");

            string yaml = File.ReadAllText(diskPath);
            foreach (string key in new[]
                     { "sortingLayer", "sortingOrder", "sortingFudge", "respondsToAmbientLight" })
            {
                Assert.IsFalse(Regex.IsMatch(yaml, @"^\s*" + key + ":", RegexOptions.Multiline),
                    "'" + UNAUTHORED_DEPTH_ASSET_PATH + "' now writes a '" + key + "' key, so it " +
                    "no longer exercises the absent-key path this test exists for -- it would " +
                    "pass by reading a value the file supplies. Repoint UNAUTHORED_DEPTH_ASSET_PATH " +
                    "at a preset that still omits all four.");
            }

            var preset = AssetDatabase.LoadAssetAtPath<ParticlePresetDefinition>(UNAUTHORED_DEPTH_ASSET_PATH);
            Assert.IsTrue(preset != null, "Could not load " + UNAUTHORED_DEPTH_ASSET_PATH + ".");
            Assert.AreEqual(UNAUTHORED_DEPTH_ASSET_ID, preset.id,
                "The subject asset was re-identified; this test is meant to run against a preset " +
                "that actually ships.");
            Assert.AreSame(preset, LoadCatalog().GetById(UNAUTHORED_DEPTH_ASSET_ID),
                "The subject asset is no longer the one the catalog serves under '" +
                UNAUTHORED_DEPTH_ASSET_ID + "'. An orphan asset proves nothing about what ships.");

            // The deserializer's answer, before any emitter is involved. The three numeric and
            // boolean fields are where the mechanism actually shows: Unity has no null string,
            // so sortingLayer would come back "" even with its initialiser deleted -- it is
            // asserted anyway (without a ?? guard, so a null fails visibly rather than being
            // swallowed) because it is the field ConfigureRenderer branches on.
            Assert.AreEqual("", preset.vfx.sortingLayer,
                "An absent 'sortingLayer' key no longer deserializes to \"\". Every preset " +
                "written before the field existed just changed depth.");
            Assert.AreEqual(0, preset.vfx.sortingOrder,
                "An absent 'sortingOrder' key no longer restores the field initialiser 0.");
            Assert.AreEqual(0f, preset.vfx.sortingFudge, 0.0001f,
                "An absent 'sortingFudge' key no longer restores the field initialiser 0.");

            // NOTE: the loaded asset is deliberately NOT registered with _createdSos. TearDown
            // calls DestroyImmediate on everything in that list, which on a shipped asset is a
            // destroyed ScriptableObject the rest of the suite then reads through.
            var emitter = CreateEmitter();
            emitter.ApplyPreset(preset, 1f);

            var r = RootRendererOf(emitter);
            Assert.AreEqual(SortingConfig.LAYER_VFX, r.sortingLayerName,
                "A shipped preset with no sortingLayer key on disk must still land on the " +
                "historical hard-coded 'VFX'.");
            Assert.AreEqual(0, r.sortingOrder,
                "A shipped preset with no sortingOrder key on disk must still land on 0.");
            Assert.AreEqual(0f, r.sortingFudge, 0.0001f,
                "A shipped preset with no sortingFudge key on disk must leave Unity's own " +
                "transparency sort alone.");

            // respondsToAmbientLight is absent from the same file and defaults to false by the
            // same mechanism, and its failure is the mirror image: an effect that was never
            // meant to notice the hour starts dimming at dusk.
            Assert.IsFalse(preset.vfx.respondsToAmbientLight,
                "An absent 'respondsToAmbientLight' key no longer restores false.");
            Assert.AreEqual(0, AmbientTargetCount(emitter),
                "A preset that never opted in must not be enrolled with the day/night tracker.");
        }

        /// <summary>
        /// A null sortingLayer string -- what a preset deserialized before the field existed
        /// can hand over -- must be treated as unauthored, not thrown on. A throw inside
        /// ConfigureRenderer aborts ApplyPreset, so the emitter would end up half-built and
        /// invisible rather than merely mis-layered.
        /// </summary>
        [Test]
        public void ApplyPreset_NullSortingLayerString_IsTreatedAsUnauthored_NotAsAThrow()
        {
            var emitter = CreateEmitter();
            var preset = MakePreset("sorting_null");
            preset.vfx.sortingLayer = null;

            Assert.DoesNotThrow(() => emitter.ApplyPreset(preset, 1f),
                "A null authored name must go down the IsNullOrEmpty path, not into SortingLayer lookup.");
            Assert.AreEqual(SortingConfig.LAYER_VFX, RootRendererOf(emitter).sortingLayerName,
                "Null is 'unauthored', which means VFX.");
        }

        /// <summary>
        /// An authored layer that DOES exist must reach the renderer, together with its order
        /// and its fudge. If any of the three is dropped, the F1 Sorting fields are a placebo:
        /// the author sets ObjectsLow, sees no change, and re-authors the preset around a
        /// depth that never applied.
        /// </summary>
        [Test]
        public void ApplyPreset_AuthoredExistingLayer_ReachesTheRendererWithItsOrderAndFudge()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(
                MakePreset("sorting_authored", SortingConfig.LAYER_OBJECTS_LOW, 3, 1.5f), 1f);

            var r = RootRendererOf(emitter);
            Assert.AreEqual(SortingConfig.LAYER_OBJECTS_LOW, r.sortingLayerName,
                "The authored sorting layer never reached ParticleSystemRenderer.sortingLayerName.");
            Assert.AreEqual(3, r.sortingOrder,
                "The authored sortingOrder never reached the renderer.");
            Assert.AreEqual(1.5f, r.sortingFudge, 0.0001f,
                "sortingFudge is the ONLY tool that orders the co-located systems of a composite " +
                "against each other -- the instance loader pins every emitter to z = 0.");
        }

        /// <summary>
        /// A typo in an authored layer name must fall back to VFX and warn -- never throw, and
        /// never resolve through the ID path to "Default". Default sits behind the entire
        /// world, so the emitter would read as having failed to spawn at all, sending the
        /// author hunting a spawn bug that does not exist.
        /// </summary>
        [Test]
        public void ApplyPreset_UnknownSortingLayer_FallsBackToVfx_NeverToDefault_AndWarns()
        {
            const string bad = "NoSuchSortingLayer_Fallback";
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
                "[ParticleEmitter] Sorting layer '" + bad + "' does not exist in " +
                "ProjectSettings > Tags and Layers")));

            var emitter = CreateEmitter();
            Assert.DoesNotThrow(() => emitter.ApplyPreset(MakePreset("sorting_bad", bad), 1f),
                "Assigning an unknown name straight to sortingLayerName throws -- the resolver " +
                "must validate against SortingLayer.layers first.");

            var r = RootRendererOf(emitter);
            Assert.AreEqual(SortingConfig.LAYER_VFX, r.sortingLayerName,
                "A typo must degrade to the historical VFX default, which is still visible.");
            Assert.AreNotEqual("Default", r.sortingLayerName,
                "NameToID answers 0 for a typo AND for the real 'Default' -- resolving through " +
                "it would bury the emitter behind the whole world.");
        }

        /// <summary>
        /// One authored typo must cost ONE warning line for the session, not one per emitter.
        /// The vegetation pass places roughly 150 emitters off a handful of presets, and this
        /// project treats a warning that repeats for a steady state as a bug in the warning --
        /// it trains the reader to scroll past the console.
        /// </summary>
        [Test]
        public void ApplyPreset_SameUnknownLayerOnFourEmitters_WarnsExactlyOnce_NotOncePerEmitter()
        {
            const string bad = "NoSuchSortingLayer_WarnOnce";
            int warnings = 0;
            Application.LogCallback handler = (condition, stack, type) =>
            {
                if (type == LogType.Warning && condition != null && condition.Contains(bad)) warnings++;
            };

            Application.logMessageReceived += handler;
            try
            {
                LogAssert.Expect(LogType.Warning,
                    new Regex(Regex.Escape("Sorting layer '" + bad + "'")));

                for (int i = 0; i < 4; i++)
                {
                    CreateEmitter("WarnOnce_" + i)
                        .ApplyPreset(MakePreset("sorting_bad_" + i, bad), 1f);
                }
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            Assert.AreEqual(1, warnings,
                "Four emitters off one bad name produced " + warnings + " warnings. The verdict " +
                "cache in ParticleEmitter.Colors.cs must answer from memory after the first miss.");
        }

        /// <summary>
        /// The F1 preview emitter serves EVERY preset the user clicks, and the instance loader
        /// reuses pooled emitters. A preset that authors no depth must therefore RESET the
        /// renderer to VFX/0/0 rather than inherit the previous preset's -- otherwise a
        /// preview looks correct only when it happens to follow the right selection, and the
        /// author tunes a depth that will not survive a restart.
        /// </summary>
        [Test]
        public void ApplyPreset_ReusedEmitter_APresetWithNoDepthClearsThePreviousPresetsDepth()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(
                MakePreset("sorting_reuse_a", SortingConfig.LAYER_FLOOR_DECALS, 5, 2.5f), 1f);
            Assert.AreEqual(SortingConfig.LAYER_FLOOR_DECALS, RootRendererOf(emitter).sortingLayerName,
                "Sanity: the first preset's depth applied.");

            emitter.ApplyPreset(MakePreset("sorting_reuse_b"), 1f);

            var r = RootRendererOf(emitter);
            Assert.AreEqual(SortingConfig.LAYER_VFX, r.sortingLayerName,
                "A reused emitter kept the previous preset's sorting layer.");
            Assert.AreEqual(0, r.sortingOrder, "A reused emitter kept the previous preset's order.");
            Assert.AreEqual(0f, r.sortingFudge, 0.0001f,
                "A reused emitter kept the previous preset's fudge.");
        }

        /// <summary>
        /// Every system of a composite must wear ITS OWN preset's depth, not the root's. The
        /// pollen field is authored as a four-deep stack that straddles the player: haze and
        /// pollen under him on FloorDecals, drifting motes and glints over him on ObjectsLow.
        /// If the layers inherited the root's depth (or the old hard-coded VFX) the whole
        /// stack collapses onto one plane and the effect stops reading as depth at all.
        /// </summary>
        [Test]
        public void ApplyPreset_PollenComposite_GivesEveryLayerItsOwnAuthoredDepth_NotTheRoots()
        {
            // Read from the shipped assets; the expected values below are asserted against the
            // assets too, so a re-author fails here loudly instead of silently agreeing.
            var expected = new[]
            {
                new Depth("flowers_pollen_soft",       SortingConfig.LAYER_FLOOR_DECALS, 2), // root
                new Depth("flowers_pollen_glints_add", SortingConfig.LAYER_OBJECTS_LOW,  3), // layers[0]
                new Depth("flowers_pollen_haze_soft",  SortingConfig.LAYER_FLOOR_DECALS, 0), // layers[1]
                new Depth("flowers_pollen_drift_add",  SortingConfig.LAYER_OBJECTS_LOW,  2), // layers[2]
            };

            var root = LoadCatalog().GetById(expected[0].Id);
            Assert.IsTrue(root != null, "Preset '" + expected[0].Id + "' is not in the catalog.");

            var emitter = CreateEmitter();
            emitter.ApplyPreset(root, 1f);

            AssertDepth(RootRendererOf(emitter), root, expected[0], "root");

            Assert.AreEqual(expected.Length - 1, root.layers.Count,
                "The pollen composite is authored as a " + expected.Length + "-system stack.");
            Assert.AreEqual(expected.Length - 1, emitter.LayerSystems.Count,
                "Every authored layer must have produced its own ParticleSystem.");

            var occupied = new HashSet<string>();
            occupied.Add(expected[0].Layer + "#" + expected[0].Order);

            for (int i = 0; i < root.layers.Count; i++)
            {
                var layerPreset = root.layers[i];
                Assert.IsTrue(layerPreset != null, "Layer " + i + " of the pollen composite is null.");
                Assert.AreEqual(expected[i + 1].Id, layerPreset.id,
                    "The pollen stack was re-ordered; slot " + i + " no longer holds the preset " +
                    "whose depth this test pins.");

                AssertDepth(RendererOf(emitter.LayerSystems[i]), layerPreset, expected[i + 1],
                    "layer " + i + " (" + layerPreset.id + ")");
                occupied.Add(expected[i + 1].Layer + "#" + expected[i + 1].Order);
            }

            Assert.AreEqual(expected.Length, occupied.Count,
                "All four systems ended up on the same layer+order. That is exactly what the old " +
                "hard-coded VFX/0 did, and it flattens the stack the author built.");
            Assert.AreNotEqual(SortingConfig.LAYER_VFX, RootRendererOf(emitter).sortingLayerName,
                "The whole point of the pollen re-author is that it no longer draws on VFX, in " +
                "front of the player.");
        }

        private static void AssertDepth(ParticleSystemRenderer r, ParticlePresetDefinition preset,
                                        Depth expected, string what)
        {
            Assert.AreEqual(expected.Layer, preset.vfx.sortingLayer,
                "Asset '" + preset.id + "' no longer authors the sorting layer this test pins.");
            Assert.AreEqual(expected.Order, preset.vfx.sortingOrder,
                "Asset '" + preset.id + "' no longer authors the sorting order this test pins.");

            Assert.AreEqual(expected.Layer, r.sortingLayerName,
                "The " + what + " system did not land on its own authored sorting layer.");
            Assert.AreEqual(expected.Order, r.sortingOrder,
                "The " + what + " system did not land on its own authored sorting order.");
        }

        private struct Depth
        {
            public readonly string Id;
            public readonly string Layer;
            public readonly int Order;
            public Depth(string id, string layer, int order) { Id = id; Layer = layer; Order = order; }
        }

        /// <summary>
        /// No shipped preset may author a sorting layer that ProjectSettings does not have.
        /// Such a preset still renders -- on VFX, in front of everything -- so the only signal
        /// is one console warning per session that nobody reads. A sorting layer renamed in
        /// Tags and Layers breaks every preset that named it, and this is the test that says so.
        /// </summary>
        [Test]
        public void EveryCatalogPreset_AuthorsASortingLayerThatExists_SoNoneBootsIntoTheFallback()
        {
            var known = new HashSet<string>();
            foreach (var l in SortingLayer.layers) known.Add(l.name);

            foreach (var p in LoadCatalog().Presets)
            {
                if (p == null || p.vfx == null) continue;
                string authored = p.vfx.sortingLayer;
                if (string.IsNullOrEmpty(authored)) continue;

                Assert.IsTrue(known.Contains(authored),
                    "Preset '" + p.id + "' authors sorting layer '" + authored + "', which is not " +
                    "in ProjectSettings > Tags and Layers. It will fall back to VFX and draw in " +
                    "front of the player, warning once and then never again.");
            }
        }

        // ================================================================ AMBIENT LIGHT

        /// <summary>
        /// With no DayNightCycle anywhere -- the boot window, the Particles editor in edit
        /// mode, every test fixture -- the tint must be EXACTLY white, so a preset that opts
        /// in renders identically to one that does not. Anything else means opting in silently
        /// re-colours the effect wherever the cycle has not been created yet.
        /// </summary>
        [Test]
        public void ApplyPreset_OptedIn_WithNoDayNightCycle_LeavesTheStartColourExactlyAsAuthored()
        {
            Assert.IsFalse(DayNightCycle.HasInstance, "Fixture guard: no cycle may exist here.");

            Color tint = CurrentAmbientTintDirect();
            Assert.AreEqual(1f, tint.r, 0f, "No cycle must give an exact identity multiply (R).");
            Assert.AreEqual(1f, tint.g, 0f, "No cycle must give an exact identity multiply (G).");
            Assert.AreEqual(1f, tint.b, 0f, "No cycle must give an exact identity multiply (B).");

            var authored = new Color(0.80f, 0.65f, 0.40f, 0.90f);
            var emitter = CreateEmitter();
            emitter.ApplyPreset(
                MakePreset("ambient_nocycle", respondsToAmbientLight: true, authoredColor: authored), 1f);

            AssertChannels(authored, StartColorOf(RootPsOf(emitter)),
                "opting in must cost nothing when there is no cycle to read.");
        }

        /// <summary>
        /// Domain Reload is OFF, so the singleton slot can hold a DESTROYED cycle from a
        /// previous session or a torn-down scene. The null check has to be Unity's overloaded
        /// one: a C# ReferenceEquals check would sail past the fake-null and read CurrentColor
        /// off a dead component, throwing MissingReferenceException inside ApplyPreset and
        /// leaving the emitter half-configured and invisible.
        /// </summary>
        [Test]
        public void ApplyPreset_OptedIn_WithADestroyedCycleLeftInTheSingletonSlot_StillRendersUntinted()
        {
            var cycle = CreateCycleAtColor(new Color(0.05f, 0.05f, 0.10f, 1f));
            Object.DestroyImmediate(cycle.gameObject);
            // OnDestroy nulls the slot; put the dead reference back -- that is the state
            // SingletonMonoBehaviour documents as the hazard of Domain Reload being off.
            DayNightSingletonField().SetValue(null, cycle);

            var authored = new Color(0.90f, 0.80f, 0.70f, 1f);
            var emitter = CreateEmitter();
            var preset = MakePreset("ambient_deadcycle", respondsToAmbientLight: true,
                                    authoredColor: authored);

            Assert.DoesNotThrow(() => emitter.ApplyPreset(preset, 1f),
                "A destroyed cycle must read as absent, not be dereferenced.");
            AssertChannels(authored, StartColorOf(RootPsOf(emitter)),
                "a dead cycle must fall back to the identity multiply, exactly like no cycle.");
        }

        /// <summary>
        /// A preset that does NOT opt in must be untouched even at deep night. This is the
        /// guarantee that shipping the feature changed nothing for the presets that never
        /// asked for it -- spell impacts, projectile trails and portals keep their authored
        /// brightness whatever hour it is.
        /// </summary>
        [Test]
        public void ApplyPreset_NotOptedIn_AtNight_LeavesTheStartColourExactlyAsAuthored()
        {
            CreateCycleAtColor(new Color(0.20f, 0.25f, 0.45f, 1f));

            var authored = new Color(0.90f, 0.80f, 0.70f, 1f);
            var emitter = CreateEmitter();
            emitter.ApplyPreset(
                MakePreset("ambient_optout", respondsToAmbientLight: false, authoredColor: authored), 1f);

            AssertChannels(authored, StartColorOf(RootPsOf(emitter)),
                "respondsToAmbientLight is false -- the multiply must be the identity.");
            Assert.AreEqual(0, AmbientTargetCount(emitter),
                "A preset that never opted in must not be enrolled with the tracker at all; the " +
                "loop only stays free because nothing is started when nothing opted in.");
        }

        /// <summary>
        /// The feature itself: at night, an opted-in preset gets darker. Without it the leaves
        /// and pollen render at noon values on top of a tilemap the Light2D has driven to a few
        /// percent brightness -- foliage that glows in the dark.
        /// </summary>
        [Test]
        public void ApplyPreset_OptedIn_AtNight_DarkensTheStartColour()
        {
            var night = new Color(0.20f, 0.25f, 0.45f, 1f);
            CreateCycleAtColor(night);

            var authored = Color.white;
            var emitter = CreateEmitter();
            emitter.ApplyPreset(
                MakePreset("ambient_night", respondsToAmbientLight: true, authoredColor: authored), 1f);

            Color actual = StartColorOf(RootPsOf(emitter));
            float floor = AmbientChannelFloorConst();

            Assert.Less(actual.r, authored.r, "Night must dim the red channel of an opted-in preset.");
            Assert.Less(actual.g, authored.g, "Night must dim the green channel of an opted-in preset.");
            Assert.Less(actual.b, authored.b, "Night must dim the blue channel of an opted-in preset.");

            // The cycle's blue keyframe (0.45) clears the floor, so it must survive intact --
            // that residual blue is what makes night read as night rather than as grey.
            AssertChannels(
                new Color(Mathf.Max(night.r, floor), Mathf.Max(night.g, floor), Mathf.Max(night.b, floor), 1f),
                actual,
                "the tint must be the cycle's live colour, floored per channel, multiplied into " +
                "the authored start colour.");

            Assert.AreEqual(1, AmbientTargetCount(emitter),
                "The opted-in system must be enrolled so the tracking loop keeps it in step as " +
                "dawn ramps over the following 432 seconds.");
        }

        /// <summary>
        /// The Lighting editor (Ctrl+F3) can drag the night keyframe to literal black. Without
        /// the per-channel floor that multiplies the vegetation out of existence, and a leaf
        /// that stops rendering reads as a bug, not as night. The floor is read by reflection
        /// so tuning it stays a one-line change instead of a two-file one.
        /// </summary>
        [Test]
        public void ApplyPreset_OptedIn_WithAmbientDrivenToBlack_NeverDimsAChannelBelowTheFloor()
        {
            CreateCycleAtColor(Color.black);
            float floor = AmbientChannelFloorConst();
            Assert.Greater(floor, 0f, "A floor of zero would not be a floor.");

            var emitter = CreateEmitter();
            emitter.ApplyPreset(
                MakePreset("ambient_black", respondsToAmbientLight: true, authoredColor: Color.white), 1f);

            Color actual = StartColorOf(RootPsOf(emitter));
            Assert.GreaterOrEqual(actual.r, floor - CHANNEL_TOLERANCE, "R fell below the floor -- the leaf is gone, not dark.");
            Assert.GreaterOrEqual(actual.g, floor - CHANNEL_TOLERANCE, "G fell below the floor -- the leaf is gone, not dark.");
            Assert.GreaterOrEqual(actual.b, floor - CHANNEL_TOLERANCE, "B fell below the floor -- the leaf is gone, not dark.");

            AssertChannels(new Color(floor, floor, floor, 1f), actual,
                "a black ambient must clamp to exactly the floor on every channel.");
        }

        /// <summary>
        /// The ambient rides RGB only. Alpha is coverage, not brightness -- scaling it would
        /// fade every opted-in particle towards fully transparent as night falls, so the
        /// vegetation would not darken, it would disappear.
        /// </summary>
        [Test]
        public void ApplyPreset_OptedIn_AtNight_LeavesAlphaUntouched()
        {
            CreateCycleAtColor(Color.black);

            var authored = new Color(1f, 1f, 1f, 0.35f);
            var emitter = CreateEmitter();
            emitter.ApplyPreset(
                MakePreset("ambient_alpha", respondsToAmbientLight: true, authoredColor: authored), 1f);

            Assert.AreEqual(0.35f, StartColorOf(RootPsOf(emitter)).a, CHANNEL_TOLERANCE,
                "The ambient multiply must skip alpha -- it is coverage, not brightness.");
        }

        /// <summary>
        /// The ambient must MULTIPLY into colorIntensity, not replace it. An additive preset
        /// that overdrives to 2x for its glow would otherwise lose the glow the moment it opts
        /// in to the day/night cycle, which reads as the opt-in having broken the effect.
        /// </summary>
        [Test]
        public void ApplyPreset_OptedIn_ComposesWithColorIntensity_RatherThanReplacingIt()
        {
            var half = new Color(0.5f, 0.5f, 0.5f, 1f); // above the floor, so it survives as-is
            CreateCycleAtColor(half);

            var authored = new Color(0.4f, 0.4f, 0.4f, 1f);
            var emitter = CreateEmitter();
            emitter.ApplyPreset(
                MakePreset("ambient_intensity", respondsToAmbientLight: true,
                           authoredColor: authored, colorIntensity: 2f), 1f);

            AssertChannels(new Color(0.4f, 0.4f, 0.4f, 1f), StartColorOf(RootPsOf(emitter)),
                "expected authored 0.4 x intensity 2 x ambient 0.5; the two multipliers must compose.");
        }

        /// <summary>
        /// A composite enrols PER SYSTEM: a stack whose light layer opts in must track the
        /// cycle even when its mass layer does not. Enrolling the whole stack off the root's
        /// flag would dim layers the author deliberately left at full brightness (an additive
        /// glint is authored to stay a glint at night).
        /// </summary>
        [Test]
        public void ApplyPreset_OnlyTheLayerOptsIn_TintsThatLayerAndLeavesTheRootAlone()
        {
            CreateCycleAtColor(new Color(0.20f, 0.25f, 0.45f, 1f));

            var authored = Color.white;
            var root = MakePreset("ambient_stack_root", respondsToAmbientLight: false,
                                  authoredColor: authored);
            root.layers.Add(MakePreset("ambient_stack_layer", respondsToAmbientLight: true,
                                       authoredColor: authored));

            var emitter = CreateEmitter();
            emitter.ApplyPreset(root, 1f);

            Assert.AreEqual(1, emitter.LayerSystems.Count, "Sanity: the layer built its own system.");
            AssertChannels(authored, StartColorOf(RootPsOf(emitter)),
                "the root did not opt in and must be untouched.");
            Assert.Less(StartColorOf(emitter.LayerSystems[0]).r, authored.r,
                "The layer opted in on its own and must be tinted even though the root did not.");
            Assert.AreEqual(1, AmbientTargetCount(emitter),
                "Exactly one system opted in, so exactly one may be enrolled.");
        }

        /// <summary>
        /// The F1 preview emitter serves every preset the user clicks. If ApplyPreset did not
        /// drop the previous preset's enrolment, the tracking loop would keep rewriting the
        /// start colour of systems that now belong to a different preset -- a preset that
        /// never opted in would start darkening at dusk with nothing in its data to explain it.
        /// </summary>
        [Test]
        public void ApplyPreset_ReusedEmitter_DropsThePreviousPresetsAmbientEnrolment()
        {
            CreateCycleAtColor(new Color(0.20f, 0.25f, 0.45f, 1f));
            var emitter = CreateEmitter();

            emitter.ApplyPreset(
                MakePreset("ambient_reuse_in", respondsToAmbientLight: true), 1f);
            Assert.AreEqual(1, AmbientTargetCount(emitter), "Sanity: the first preset enrolled.");

            emitter.ApplyPreset(
                MakePreset("ambient_reuse_out", respondsToAmbientLight: false), 1f);

            Assert.AreEqual(0, AmbientTargetCount(emitter),
                "A reused emitter kept tracking the previous preset's systems.");
            AssertChannels(Color.white, StartColorOf(RootPsOf(emitter)),
                "the second preset never opted in and must render at its authored brightness.");
        }
    }
}
