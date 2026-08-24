using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Exercises the picker filter logic and ALL/GROUP grouping toggle in
    /// <see cref="ParticlesRuntimeEditor"/>.
    ///
    /// Tests build the editor with a 5-preset catalog (3 kind="aura", 2 kind="explosion"),
    /// then call RefreshPicker via reflection and assert picker grid child count.
    /// </summary>
    [TestFixture]
    public class ParticlesPickerTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private ParticlesRuntimeEditor _editor;

        // ── Reflection helpers ───────────────────────────────────────────────────

        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static FieldInfo FindField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public |
                    BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetVal(object obj, string name) => FindField(obj, name)?.GetValue(obj);
        private static void SetVal(object obj, string name, object value) => FindField(obj, name)?.SetValue(obj, value);

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(method,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        // ── Catalog builder ───────────────────────────────────────────────────────

        private static ParticlePresetCatalog MakeCatalog5()
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            var presets = new List<ParticlePresetDefinition>();

            // 3 aura presets
            foreach (var id in new[] { "aura_fire", "aura_ice", "aura_poison" })
            {
                var d = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
                d.id = id; d.displayName = id;
                d.vfx = new ParticleVfxParams { kind = "aura" };
                presets.Add(d);
            }
            // 2 explosion presets
            foreach (var id in new[] { "explosion_small", "explosion_large" })
            {
                var d = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
                d.id = id; d.displayName = id;
                d.vfx = new ParticleVfxParams { kind = "explosion" };
                presets.Add(d);
            }
            catalog.SetPresets(presets);
            return catalog;
        }

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            ClearSingleton<ParticlesRuntimeEditor>();

            var go = new GameObject("PickerTestEditor");
            _sceneObjects.Add(go);
            _editor = go.AddComponent<ParticlesRuntimeEditor>();

            Invoke(_editor, "OnSingletonAwake");
            SetVal(_editor, "_catalog", MakeCatalog5());
            Invoke(_editor, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            ClearSingleton<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Filter tests ─────────────────────────────────────────────────────────

        [Test]
        public void RefreshPicker_NullFilter_ShowsAllFivePresets()
        {
            SetVal(_editor, "_searchFilter", "");
            Invoke(_editor, "RefreshPicker");

            var pickerContent = GetVal(_editor, "_ui");
            var uiType  = pickerContent.GetType();
            var content = uiType.GetField("PickerContent").GetValue(pickerContent) as RectTransform;

            Assert.IsNotNull(content, "PickerContent RectTransform must be populated.");
            Assert.AreEqual(5, content.childCount,
                "With no filter all 5 presets should appear in the picker grid.");
        }

        [Test]
        public void RefreshPicker_Filter_Aura_ShowsThreePresets()
        {
            SetVal(_editor, "_searchFilter", "aura");
            Invoke(_editor, "RefreshPicker");

            var ui = GetVal(_editor, "_ui");
            var content = ui.GetType().GetField("PickerContent").GetValue(ui) as RectTransform;

            Assert.AreEqual(3, content.childCount,
                "Filter 'aura' must show exactly 3 presets.");
        }

        [Test]
        public void RefreshPicker_Filter_Explosion_ShowsTwoPresets()
        {
            SetVal(_editor, "_searchFilter", "explosion");
            Invoke(_editor, "RefreshPicker");

            var ui = GetVal(_editor, "_ui");
            var content = ui.GetType().GetField("PickerContent").GetValue(ui) as RectTransform;

            Assert.AreEqual(2, content.childCount,
                "Filter 'explosion' must show exactly 2 presets.");
        }

        [Test]
        public void RefreshPicker_NoMatch_ShowsZero()
        {
            SetVal(_editor, "_searchFilter", "zzz_nonexistent");
            Invoke(_editor, "RefreshPicker");

            var ui = GetVal(_editor, "_ui");
            var content = ui.GetType().GetField("PickerContent").GetValue(ui) as RectTransform;

            // Counted as PLACEABLE TILES, not raw children: the grid now also draws a
            // one-off note when it has nothing to show, because a blank panel reads as a
            // broken editor and gives the author nowhere to learn that layer-only presets
            // were hidden. The invariant being protected is that no stale tile survives a
            // non-matching filter, so assert on the things that can actually be clicked
            // to place a preset.
            int placeable = 0;
            foreach (Transform child in content)
                if (child.GetComponent<UnityEngine.UI.Button>() != null) placeable++;

            Assert.AreEqual(0, placeable,
                "Non-matching filter must leave no placeable tile in the picker grid.");
            Assert.AreEqual(1, content.childCount,
                "The empty grid must still say why it is empty — exactly one note, no tiles.");
        }

    }
}
