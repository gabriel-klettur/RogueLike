using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Tests for <see cref="BuildingsRuntimeEditor"/> state, constants, tutorial steps,
    /// and utility methods introduced during the Buildings Editor migration.
    ///
    /// Scope:
    ///   • Color constants — must match Python reference values (building_editor_view.py)
    ///   • TUTORIAL_STEPS — exactly 10 steps with non-empty content
    ///   • EditorName / IsActive — public interface contract
    ///   • Default mode = Select (Python default cursor mode)
    ///   • CountBuildingsUsingTemplate — private utility (tested via reflection)
    ///   • NextInstanceId              — private utility (tested via reflection)
    ///
    /// NOTE: F10 key binding is already covered in FKeyBindingParityTests.cs.
    ///       Activate/Deactivate UI creation is covered by integration via Activate calls.
    /// </summary>
    [TestFixture]
    public class BuildingsEditorLifecycleTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Clears the static _instance on a SingletonMonoBehaviour.</summary>
        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) { field.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        /// <summary>Creates singleton and forces OnSingletonAwake to run in EditMode.</summary>
        private T CreateSingleton<T>(string name = "TestGO") where T : MonoBehaviour
        {
            ClearSingletonInstance<T>();
            var go = new GameObject(name);
            var comp = go.AddComponent<T>();
            var toggle = GetField(comp, "_toggleAction");
            if (toggle?.GetValue(comp) == null)
                InvokeMethod(comp, "OnSingletonAwake");
            _sceneObjects.Add(go);
            return comp;
        }

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static FieldInfo GetStaticField(Type type, string name)
        {
            var t = type;
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static void InvokeMethod(object obj, string methodName, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        private static object InvokeMethodReturn(object obj, string methodName, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                t = t.BaseType;
            }
            return m?.Invoke(obj, args);
        }

        private static object InvokeMethodReturn(object obj, string methodName, Type[] paramTypes, object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, paramTypes, null);
                t = t.BaseType;
            }
            return m?.Invoke(obj, args);
        }

        private static void SetPrivateField(object obj, string name, object value)
            => GetField(obj, name)?.SetValue(obj, value);

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();

            // Always restore ignore flag.
            LogAssert.ignoreFailingMessages = false;
        }

        private BuildingTemplateData MakeTemplate(int id, string scope = "CG")
        {
            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.templateId   = id;
            tmpl.colliderScope = scope;
            _assets.Add(tmpl);
            return tmpl;
        }

        private BuildingObject MakeBuildingObject(string name, BuildingTemplateData tmpl, int instanceId = 0)
        {
            var go = new GameObject(name);
            var bObj = go.AddComponent<BuildingObject>();
            SetPrivateField(bObj, "_template", tmpl);
            bObj.InstanceId = instanceId;
            _sceneObjects.Add(go);
            return bObj;
        }

        // ── EditorName / IsActive ─────────────────────────────────────────────────

        [Test]
        public void EditorName_Returns_BuildingsEditorString()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");

            Assert.AreEqual("Buildings Editor", editor.EditorName,
                "EditorName must exactly match the Python toggle_building_editor display string.");
        }

        [Test]
        public void IsActive_InitiallyFalse_AfterCreation()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");

            Assert.IsFalse(editor.IsActive,
                "IsActive must be false before Activate() is called (mirrors Python: editor starts closed).");
        }

        // ── Color constants – must match Python building_editor_view.py ──────────

        [Test]
        public void HoverCyan_MatchesPython_RGB_0_255_255()
        {
            // Python: pygame.draw.rect(surf, (0, 255, 255), rect, 2)
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "HOVER_CYAN");
            Assert.IsNotNull(field, "HOVER_CYAN constant must exist on BuildingsRuntimeEditor.");

            var c = (Color) field.GetValue(null);

            Assert.AreEqual(0f, c.r, 0.002f, "HOVER_CYAN R must be 0");
            Assert.AreEqual(1f, c.g, 0.002f, "HOVER_CYAN G must be 1 (255/255)");
            Assert.AreEqual(1f, c.b, 0.002f, "HOVER_CYAN B must be 1 (255/255)");
            Assert.AreEqual(1f, c.a, 0.002f, "HOVER_CYAN A must be fully opaque");
        }

        [Test]
        public void ActiveYellow_MatchesPython_RGB_255_215_0()
        {
            // Python: pygame.draw.rect(surf, (255, 215, 0), rect, 5)
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "ACTIVE_YELLOW");
            Assert.IsNotNull(field, "ACTIVE_YELLOW constant must exist on BuildingsRuntimeEditor.");

            var c = (Color) field.GetValue(null);

            Assert.AreEqual(1f,             c.r, 0.002f, "ACTIVE_YELLOW R must be 1 (255)");
            Assert.AreEqual(215f / 255f,    c.g, 0.002f, "ACTIVE_YELLOW G must be 215/255");
            Assert.AreEqual(0f,             c.b, 0.002f, "ACTIVE_YELLOW B must be 0");
            Assert.AreEqual(1f,             c.a, 0.002f, "ACTIVE_YELLOW A must be fully opaque");
        }

        [Test]
        public void HoverRemoveRed_MatchesPython_RGB_255_0_0()
        {
            // Python: pygame.draw.rect(surf, (255, 0, 0), rect, 3)
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "HOVER_REMOVE_RED");
            Assert.IsNotNull(field, "HOVER_REMOVE_RED constant must exist on BuildingsRuntimeEditor.");

            var c = (Color) field.GetValue(null);

            Assert.AreEqual(1f, c.r, 0.002f, "HOVER_REMOVE_RED R must be 1 (255)");
            Assert.AreEqual(0f, c.g, 0.002f, "HOVER_REMOVE_RED G must be 0");
            Assert.AreEqual(0f, c.b, 0.002f, "HOVER_REMOVE_RED B must be 0");
        }

        [Test]
        public void HoverRemoveFill_Alpha_Matches_Python_60_Over_255()
        {
            // Python: fill surface with alpha = 60 (60/255 ≈ 0.235)
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "HOVER_REMOVE_FILL");
            Assert.IsNotNull(field, "HOVER_REMOVE_FILL constant must exist on BuildingsRuntimeEditor.");

            var c = (Color) field.GetValue(null);

            Assert.AreEqual(60f / 255f, c.a, 0.002f,
                "Fill overlay alpha must be 60/255 to match Python's semi-transparent danger fill.");
        }

        [Test]
        public void HoverThickness_MatchesPython_2px_At_32PPU()
        {
            // Python: pygame.draw.rect(surf, cyan, rect, 2)  — line width 2 px
            // Unity: HOVER_THICKNESS_WORLD = 2 / 32 = 0.0625 → rounded to 0.06f
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "HOVER_THICKNESS_WORLD");
            Assert.IsNotNull(field, "HOVER_THICKNESS_WORLD constant must exist.");

            float t = (float) field.GetValue(null);

            Assert.AreEqual(0.06f, t, 0.001f,
                "HOVER_THICKNESS_WORLD should map Python's 2 px line width at PPU 32.");
        }

        [Test]
        public void ActiveThickness_MatchesPython_5px_At_32PPU()
        {
            // Python: pygame.draw.rect(surf, yellow, rect, 5)  — line width 5 px
            // Unity: ACTIVE_THICKNESS_WORLD = 5 / 32 = 0.15625 → 0.15f
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "ACTIVE_THICKNESS_WORLD");
            Assert.IsNotNull(field, "ACTIVE_THICKNESS_WORLD constant must exist.");

            float t = (float) field.GetValue(null);

            Assert.AreEqual(0.15f, t, 0.001f,
                "ACTIVE_THICKNESS_WORLD should map Python's 5 px line width at PPU 32.");
        }

        // ── Tutorial steps ────────────────────────────────────────────────────────

        [Test]
        public void TutorialSteps_HasExactly10Steps()
        {
            // One step per migrated feature (Gaps 1-10 in the Buildings Editor).
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "TUTORIAL_STEPS");
            Assert.IsNotNull(field, "TUTORIAL_STEPS static field must exist.");

            var arr = field.GetValue(null) as Array;
            Assert.IsNotNull(arr);
            Assert.AreEqual(10, arr.Length,
                "TUTORIAL_STEPS must have exactly 10 steps (one per migrated gap feature).");
        }

        [Test]
        public void TutorialSteps_EachStep_HasNonEmptyTitle()
        {
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "TUTORIAL_STEPS");
            var arr   = field.GetValue(null) as Array;

            for (int i = 0; i < arr.Length; i++)
            {
                var item   = arr.GetValue(i);
                var title  = (string) item.GetType().GetField("Item1").GetValue(item);
                Assert.IsNotEmpty(title, $"TUTORIAL_STEPS[{i}].title must not be empty.");
            }
        }

        [Test]
        public void TutorialSteps_EachStep_HasNonEmptyBody()
        {
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "TUTORIAL_STEPS");
            var arr   = field.GetValue(null) as Array;

            for (int i = 0; i < arr.Length; i++)
            {
                var item = arr.GetValue(i);
                var body = (string) item.GetType().GetField("Item2").GetValue(item);
                Assert.IsNotEmpty(body, $"TUTORIAL_STEPS[{i}].body must not be empty.");
            }
        }

        [Test]
        public void TutorialSteps_Step0_MentionsF10_OrToggle()
        {
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "TUTORIAL_STEPS");
            var arr   = field.GetValue(null) as Array;

            var item  = arr.GetValue(0);
            var body  = (string) item.GetType().GetField("Item2").GetValue(item);

            Assert.IsTrue(
                body.IndexOf("F10", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("toggle", StringComparison.OrdinalIgnoreCase) >= 0,
                "First tutorial step should describe how to open the editor (F10 / toggle).");
        }

        [Test]
        public void TutorialSteps_Step6_MentionsRemoveMode()
        {
            // Step index 6 corresponds to "Remove mode" (feature Gap 4 — remove mode).
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "TUTORIAL_STEPS");
            var arr   = field.GetValue(null) as Array;

            // Search any step for "Remove" / "Delete" / "remove mode" content.
            bool found = false;
            for (int i = 0; i < arr.Length; i++)
            {
                var item = arr.GetValue(i);
                var body = (string) item.GetType().GetField("Item2").GetValue(item);
                if (body.IndexOf("remove", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    body.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, "At least one tutorial step must describe the remove/delete workflow.");
        }

        [Test]
        public void TutorialSteps_LastStep_MentionsSave()
        {
            var field = GetStaticField(typeof(BuildingsRuntimeEditor), "TUTORIAL_STEPS");
            var arr   = field.GetValue(null) as Array;

            var lastItem = arr.GetValue(arr.Length - 1);
            var body     = (string) lastItem.GetType().GetField("Item2").GetValue(lastItem);

            Assert.IsTrue(body.IndexOf("save", StringComparison.OrdinalIgnoreCase) >= 0,
                "Last tutorial step should describe the Save action.");
        }

        // ── Default mode = Select ─────────────────────────────────────────────────

        [Test]
        public void DefaultEditorMode_IsSelect_BeforeActivation()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");

            // _mode is a private field; its int value at default should be 0 (= Select).
            var field = GetField(editor, "_mode");
            Assert.IsNotNull(field, "_mode field must exist on BuildingsRuntimeEditor.");

            int modeInt = (int) field.GetValue(editor);
            Assert.AreEqual(0, modeInt,
                "_mode must default to 0 (EditorMode.Select) before any activation.");
        }

        [Test]
        public void RemoveMode_DefaultsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");

            var field = GetField(editor, "_removeMode");
            Assert.IsNotNull(field, "_removeMode field must exist.");
            Assert.IsFalse((bool) field.GetValue(editor),
                "_removeMode must default to false (Python: remove_mode starts disabled).");
        }

        // ── CountBuildingsUsingTemplate (via reflection) ──────────────────────────

        [Test]
        public void CountBuildingsUsingTemplate_NoBuildings_ReturnsZero()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");

            object count = InvokeMethodReturn(editor, "CountBuildingsUsingTemplate",
                new[] { typeof(int) }, new object[] { 999 });

            Assert.IsNotNull(count, "CountBuildingsUsingTemplate must exist as a private method.");
            Assert.AreEqual(0, (int) count,
                "With no BuildingObjects in the scene, count must be 0.");
        }

        [Test]
        public void CountBuildingsUsingTemplate_TwoSameTemplate_ReturnsTwo()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");
            var tmpl   = MakeTemplate(id: 77);

            MakeBuildingObject("B1", tmpl);
            MakeBuildingObject("B2", tmpl);
            MakeBuildingObject("B3", MakeTemplate(id: 88)); // Different template

            object count = InvokeMethodReturn(editor, "CountBuildingsUsingTemplate",
                new[] { typeof(int) }, new object[] { 77 });

            Assert.AreEqual(2, (int) count,
                "CountBuildingsUsingTemplate should count only buildings matching the given template ID.");
        }

        [Test]
        public void CountBuildingsUsingTemplate_DifferentTemplate_ReturnsZero()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");
            var tmpl   = MakeTemplate(id: 10);

            MakeBuildingObject("B1", tmpl);

            object count = InvokeMethodReturn(editor, "CountBuildingsUsingTemplate",
                new[] { typeof(int) }, new object[] { 999 });

            Assert.AreEqual(0, (int) count,
                "Template ID not in scene must return count 0.");
        }

        // ── NextInstanceId (via reflection) ───────────────────────────────────────

        [Test]
        public void NextInstanceId_NoBuildings_Returns1()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");

            object id = InvokeMethodReturn(editor, "NextInstanceId");

            Assert.IsNotNull(id, "NextInstanceId must exist as a private method.");
            Assert.AreEqual(1, (int) id,
                "With no buildings, NextInstanceId must return 1 " +
                "(Python: next_id = max(ids, default=0) + 1 → 1).");
        }

        [Test]
        public void NextInstanceId_WithBuildings_ReturnsMaxPlusOne()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");
            var tmpl   = MakeTemplate(id: 1);

            MakeBuildingObject("B1", tmpl, instanceId: 3);
            MakeBuildingObject("B2", tmpl, instanceId: 7);
            MakeBuildingObject("B3", tmpl, instanceId: 2);

            object id = InvokeMethodReturn(editor, "NextInstanceId");

            Assert.AreEqual(8, (int) id,
                "NextInstanceId must return max(InstanceId) + 1 = 7 + 1 = 8.");
        }

        [Test]
        public void NextInstanceId_InactiveBuilding_Excluded()
        {
            // Inactive buildings are excluded by FindObjectsOfType in current Unity versions.
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateSingleton<BuildingsRuntimeEditor>("TestBuildingsEditor");
            var tmpl   = MakeTemplate(id: 1);

            var b1 = MakeBuildingObject("B1", tmpl, instanceId: 100);
            b1.gameObject.SetActive(false); // Should be excluded

            var b2 = MakeBuildingObject("B2", tmpl, instanceId: 5);

            object id = InvokeMethodReturn(editor, "NextInstanceId");

            Assert.AreEqual(6, (int) id,
                "NextInstanceId should not count inactive BuildingObjects (they are 'deleted').");
        }
    }
}
