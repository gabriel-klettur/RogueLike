using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Lighting
{
    /// <summary>
    /// Guards the plumbing that carries the day/night phases to the screen.
    ///
    /// Every assertion here corresponds to a defect that actually shipped and went unnoticed for
    /// months, because each one fails SILENTLY: no exception, no console error, just a world that
    /// renders at noon brightness at 03:00. The audit is in
    /// <c>.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md</c>; the short version is that three wrong enum
    /// literals and one unconditional fallback were enough to make the whole subsystem decorative.
    ///
    /// If one of these fails, the phases are gone even though every phase test still passes —
    /// the ramp is computed correctly and then delivered to nothing.
    /// </summary>
    [TestFixture]
    public class DayNightPipelineWiringTests
    {
        private const string ScenePath    = "Assets/_Project/Scenes/MainGameplay.unity";
        private const string RendererPath = "Assets/Settings/Renderer2D.asset";

        // ── 1. The enum constants that were wrong ─────────────────────────────────

        [Test]
        public void Light2DLightType_HasTheValuesTheProjectAssumes()
        {
            // The original bug in one line: code wrote 1 believing it meant Global (it is
            // Freeform) and 2 believing it meant Point (it is Sprite). The scene light ended up
            // a 1-unit Point at the world origin and every torch a cookie-less Sprite light that
            // rasterised nothing. If a URP upgrade ever renumbers these, this fails loudly
            // instead of the world quietly going flat again.
            Assert.AreEqual(0, (int)Light2D.LightType.Parametric, "Parametric must be 0.");
            Assert.AreEqual(1, (int)Light2D.LightType.Freeform,   "Freeform must be 1.");
            Assert.AreEqual(2, (int)Light2D.LightType.Sprite,     "Sprite must be 2.");
            Assert.AreEqual(3, (int)Light2D.LightType.Point,      "Point must be 3.");
            Assert.AreEqual(4, (int)Light2D.LightType.Global,     "Global must be 4.");
        }

        // ── 2. The authored scene light ───────────────────────────────────────────

        [Test]
        public void TheSceneCarriesExactlyOneGlobalLight2D()
        {
            var text = ReadSceneText();
            var lightTypes = ValuesOf(text, "m_LightType");

            Assert.IsNotEmpty(lightTypes,
                $"{ScenePath} contains no Light2D at all. The day/night ambient has nothing to drive.");

            int globals = 0;
            foreach (var v in lightTypes)
                if (v == (int)Light2D.LightType.Global) globals++;

            Assert.AreEqual(1, globals,
                $"Expected exactly one Global (={(int)Light2D.LightType.Global}) Light2D in the " +
                $"scene; found {globals} among m_LightType values [{string.Join(", ", lightTypes)}]. " +
                "Zero means the ambient reaches nothing; more than one makes URP log a duplicate-" +
                "global-light error and pick arbitrarily.");
        }

        [Test]
        public void TheSceneLightIlluminatesEveryWorldSortingLayer()
        {
            var maskIds = SceneAmbientMaskIds();
            Assert.IsNotNull(maskIds, $"No m_ApplyToSortingLayers found in {ScenePath}.");

            var missing = new List<string>();
            foreach (var layerName in AmbientLitLayerNames())
            {
                int id = SortingLayer.NameToID(layerName);
                if (!maskIds.Contains(id)) missing.Add(layerName);
            }

            Assert.IsEmpty(missing,
                "The scene's ambient light does not cover these sorting layers: " +
                string.Join(", ", missing) + ". A LIT renderer on a layer outside the mask does " +
                "not render dim — it renders BLACK, so this is how a correct-looking change makes " +
                "half the world disappear at once.");
        }

        // ── 3. The lit / unlit decision ───────────────────────────────────────────

        [Test]
        public void WorldSpriteMaterials_GoesLit_WhenAmbientLightIsAvailable()
        {
            ResetWorldSpriteMaterials();
            WorldSpriteMaterials.NotifyAmbientLightReady();

            Assert.IsTrue(WorldSpriteMaterials.AmbientLightingAvailable);
            AssertShaderKind(WorldSpriteMaterials.World, lit: true,
                "With ambient light available the world MUST go lit. This is the exact decision " +
                "that a method literally named ApplyUnlitFallbackIfNeeded used to answer 'unlit' " +
                "unconditionally for months, which is what made the day/night cycle decorative.");

            ResetWorldSpriteMaterials();
        }

        [Test]
        public void WorldSpriteMaterials_FallsBackToUnlit_WhenThereIsNoAmbientLight()
        {
            // The probe is forced rather than inferred: an EditMode test runs in whatever scene
            // the Editor happens to have open, so "is there a Global Light2D right now" is not
            // something a test may assume either way.
            ForceAmbientAvailability(false);

            Assert.IsFalse(WorldSpriteMaterials.AmbientLightingAvailable);
            AssertShaderKind(WorldSpriteMaterials.World, lit: false,
                "Without ambient light the world MUST fall back to unlit. A lit sprite with no " +
                "light does not render dim, it renders BLACK.");

            ResetWorldSpriteMaterials();
        }

        [Test]
        public void WorldSpriteMaterials_MaterialAlwaysMatchesItsOwnProbe()
        {
            // Scene-independent invariant: whatever the probe answers, the material must agree.
            ResetWorldSpriteMaterials();
            bool available = WorldSpriteMaterials.AmbientLightingAvailable;
            AssertShaderKind(WorldSpriteMaterials.World, lit: available,
                $"The probe says ambient light is {(available ? "available" : "absent")}, so the " +
                "shared world material must be the matching one.");
        }

        [Test]
        public void EveryTilemapLayer_RendersOnAnAmbientLitSortingLayer()
        {
            // The tilemaps are what the player mostly sees. If one of their sorting layers ever
            // drops out of the ambient mask, that layer turns black the moment it goes lit.
            var mask = new HashSet<string>(AmbientLitLayerNames());
            var offenders = new List<string>();

            foreach (TilemapLayerSetup.TilemapLayer layer in
                     System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer)))
            {
                string sortingLayer = SortingLayerNameFor(layer);
                if (string.IsNullOrEmpty(sortingLayer)) continue;   // collision-only layers
                if (!mask.Contains(sortingLayer))
                    offenders.Add($"{layer} -> {sortingLayer}");
            }

            Assert.IsEmpty(offenders,
                "These tilemap layers render on a sorting layer the ambient light does not " +
                "cover: " + string.Join(", ", offenders));
        }

        // ── 4. The renderer asset ─────────────────────────────────────────────────

        [Test]
        public void BlendStyleOne_IsAdditive_BecausePlacedLightsRenderIntoIt()
        {
            var so = new SerializedObject(LoadRendererData());
            var styles = so.FindProperty("m_LightBlendStyles");
            Assert.IsNotNull(styles, "Renderer2D has no m_LightBlendStyles.");
            Assert.GreaterOrEqual(styles.arraySize, 2,
                "Placed lights render into blend style 1; it has to exist.");

            var style1 = styles.GetArrayElementAtIndex(1);
            var blendMode = style1.FindPropertyRelative("blendMode");
            Assert.IsNotNull(blendMode, "Blend style 1 has no blendMode.");
            Assert.AreEqual(0, blendMode.intValue,
                "Blend style 1 must stay Additive (blendMode 0). Torches and lamps are registered " +
                "into it precisely so they ADD light to a dark world; on a Multiply style a torch " +
                "could only ever darken what it touches, which is the opposite of a torch.");
        }

        [Test]
        public void TheScreenGradeFeature_IsInstalledAndHasItsShader()
        {
            var data = LoadRendererData();

            ScreenGradeFeature grade = null;
            foreach (var f in data.rendererFeatures)
                if (f is ScreenGradeFeature sg) grade = sg;

            Assert.IsNotNull(grade,
                "Renderer2D.asset no longer carries ScreenGradeFeature. Without it the phases " +
                "lose their saturation drain, their contrast and their vignette — everything a " +
                "Multiply Light2D structurally cannot do.");

            var so = new SerializedObject(grade);
            var shader = so.FindProperty("shader");
            Assert.IsNotNull(shader, "ScreenGradeFeature has no serialized 'shader' field.");
            Assert.IsNotNull(shader.objectReferenceValue,
                "ScreenGradeFeature has no shader assigned, so it skips its pass every frame and " +
                "reports nothing. Assign Hidden/Valkur/ScreenGrade.");
        }

        [Test]
        public void TheScreenGradeShader_ExistsAndCompiles()
        {
            var shader = Shader.Find("Hidden/Valkur/ScreenGrade");
            Assert.IsNotNull(shader, "Hidden/Valkur/ScreenGrade is missing.");
            Assert.IsTrue(shader.isSupported, "Hidden/Valkur/ScreenGrade does not compile on this platform.");
        }

        // ── 5. The light presets placed lights depend on ──────────────────────────

        [Test]
        public void EveryLightPreset_HasAFalloffURPWillActuallyHonour()
        {
            // URP clamps falloffIntensity to [0,1]. The shipped presets used to carry 1.6-2.2,
            // so all of them clamped to an identical hard edge and the three presets were
            // indistinguishable from each other.
            foreach (var preset in AllLightPresets())
            {
                Assert.That(preset.falloff, Is.InRange(0f, 1f),
                    $"'{preset.presetKey}' has falloff {preset.falloff}. URP clamps this to [0,1], " +
                    "so anything outside the range silently becomes 1 and the preset stops being " +
                    "distinguishable from every other one.");
            }
        }

        [Test]
        public void NoLightPreset_HasItsInnerRadiusAtTheOuterRadius()
        {
            foreach (var preset in AllLightPresets())
            {
                Assert.Less(preset.centerScale, 1f,
                    $"'{preset.presetKey}' has centerScale {preset.centerScale}: inner radius == " +
                    "outer radius, which is a hard-edged disc with no falloff at all.");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static string ReadSceneText()
        {
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ScenePath);
            Assert.IsTrue(File.Exists(full), $"{ScenePath} not found on disk.");
            return File.ReadAllText(full);
        }

        /// <summary>Every integer value of a serialized key in a YAML text.</summary>
        private static List<int> ValuesOf(string yaml, string key)
        {
            var found = new List<int>();
            foreach (var raw in yaml.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith(key + ":")) continue;
                if (int.TryParse(line.Substring(key.Length + 1).Trim(), out int v)) found.Add(v);
            }
            return found;
        }

        /// <summary>
        /// Decode the sorting-layer mask Unity serialises as a little-endian int32 hex blob.
        /// </summary>
        private static HashSet<int> SceneAmbientMaskIds()
        {
            foreach (var raw in ReadSceneText().Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("m_ApplyToSortingLayers:")) continue;
                var hex = line.Substring("m_ApplyToSortingLayers:".Length).Trim();
                if (hex.Length < 8 || hex.Length % 8 != 0) continue;

                var ids = new HashSet<int>();
                for (int i = 0; i < hex.Length; i += 8)
                {
                    // little-endian bytes -> int
                    int value = 0;
                    for (int b = 3; b >= 0; b--)
                        value = (value << 8) | System.Convert.ToInt32(hex.Substring(i + b * 2, 2), 16);
                    ids.Add(value);
                }
                return ids;
            }
            return null;
        }

        /// <summary>The layer names the bootstrap writes onto the ambient light.</summary>
        private static string[] AmbientLitLayerNames()
        {
            var field = typeof(Valkur.Gameplay.GameplaySceneSetup).GetField(
                "AmbientLitSortingLayers",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field,
                "GameplaySceneSetup.AmbientLitSortingLayers is gone. It is the single source of " +
                "truth for which layers the ambient light may darken.");
            var names = field.GetValue(null) as string[];
            Assert.IsNotNull(names);
            Assert.IsNotEmpty(names);
            return names;
        }

        private static string SortingLayerNameFor(TilemapLayerSetup.TilemapLayer layer)
        {
            // Mirrors TilemapLayerSetup's own switch. Collision layers render nothing.
            switch (layer)
            {
                case TilemapLayerSetup.TilemapLayer.Ground:          return "Ground";
                case TilemapLayerSetup.TilemapLayer.FloorDecals:     return "FloorDecals";
                case TilemapLayerSetup.TilemapLayer.ObjectsLow:      return "ObjectsLow";
                case TilemapLayerSetup.TilemapLayer.WallsBottom:     return "WallsBottom";
                case TilemapLayerSetup.TilemapLayer.Decorations:     return "Decorations";
                case TilemapLayerSetup.TilemapLayer.WallsTop:        return "WallsTop";
                case TilemapLayerSetup.TilemapLayer.ObjectsHigh:     return "ObjectsHigh";
                case TilemapLayerSetup.TilemapLayer.OverheadDetails: return "Overhead";
                default:                                              return null;
            }
        }

        private static ScriptableRendererData LoadRendererData()
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            Assert.IsNotNull(data, $"{RendererPath} is missing or is not a ScriptableRendererData.");
            return data;
        }

        private static IEnumerable<Valkur.Data.LightPresetDefinition> AllLightPresets()
        {
            var guids = AssetDatabase.FindAssets("t:LightPresetDefinition");
            Assert.IsNotEmpty(guids, "No LightPresetDefinition assets found.");
            foreach (var g in guids)
            {
                var p = AssetDatabase.LoadAssetAtPath<Valkur.Data.LightPresetDefinition>(
                    AssetDatabase.GUIDToAssetPath(g));
                if (p != null) yield return p;
            }
        }

        private static void AssertShaderKind(Material material, bool lit, string because)
        {
            Assert.IsNotNull(material, "WorldSpriteMaterials returned no material. " + because);
            string name = material.shader != null ? material.shader.name : "<null shader>";
            if (lit) Assert.IsTrue(name.Contains("Sprite-Lit"),  $"{because} Got '{name}'.");
            else     Assert.IsTrue(name.Contains("Sprite-Unlit"), $"{because} Got '{name}'.");
        }

        /// <summary>
        /// Domain Reload is OFF and the SubsystemRegistration reset does not run between EditMode
        /// tests, so the probe has to be cleared by hand or the first test to touch it decides the
        /// answer for every test after it.
        /// </summary>
        /// <summary>Pin the probe's answer without depending on the open scene.</summary>
        private static void ForceAmbientAvailability(bool available)
        {
            ResetWorldSpriteMaterials();
            var t = typeof(WorldSpriteMaterials);
            var resolved = t.GetField("_ambientResolved",  BindingFlags.NonPublic | BindingFlags.Static);
            var value    = t.GetField("_ambientAvailable", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(resolved, "WorldSpriteMaterials._ambientResolved is gone.");
            Assert.IsNotNull(value,    "WorldSpriteMaterials._ambientAvailable is gone.");
            resolved.SetValue(null, true);
            value.SetValue(null, available);
        }

        private static void ResetWorldSpriteMaterials()
        {
            var reset = typeof(WorldSpriteMaterials).GetMethod("ResetStatics",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(reset, "WorldSpriteMaterials.ResetStatics is gone — Domain Reload is " +
                                     "OFF in this project, so that hook is not optional.");
            reset.Invoke(null, null);
        }
    }
}
