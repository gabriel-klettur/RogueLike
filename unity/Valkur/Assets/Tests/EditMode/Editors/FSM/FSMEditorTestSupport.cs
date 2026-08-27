using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Enemies.FSM;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// Reflection + fixture helpers shared by the F12 editor test files below. Mirrors the
    /// private-member-access pattern already established in
    /// <see cref="FSMRuntimeEditorLifecycleTests"/> (Field/GetField/SetField/Invoke), extended
    /// with an overload that passes arguments — needed for the tool methods this batch of
    /// tests exercises (<c>AddNodeAt(Vector2)</c>, <c>HandleConnectClickFrom(string,bool)</c>,
    /// …), and with the temp-directory fixture every test here MUST use instead of the real
    /// <c>StreamingAssets/FSM/</c> — <c>FSMRuntimeEditor.Persistence</c> has no
    /// <c>RefuseWriteOutsidePlayMode</c> guard, so a test that forgets to redirect
    /// <see cref="FSMRuntimeEditor.TestDataDirOverride"/> would overwrite the shipped
    /// <c>Monster_Default</c> set the instant it calls a save path.
    /// </summary>
    internal static class FSMEditorTestSupport
    {
        public static FieldInfo Field(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            Assert.Fail($"Field '{name}' not found on {obj.GetType().Name}");
            return null;
        }

        public static object GetField(object obj, string name) => Field(obj, name).GetValue(obj);

        public static void SetField(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);

        public static T GetField<T>(object obj, string name) => (T)GetField(obj, name);

        /// <summary>Invokes a parameterless private/public instance method.</summary>
        public static object Invoke(object obj, string method)
        {
            var m = FindMethod(obj, method, Array.Empty<Type>());
            return m.Invoke(obj, null);
        }

        /// <summary>Invokes a private/public instance method with the given arguments.</summary>
        public static object Invoke(object obj, string method, params object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
                types[i] = args[i]?.GetType() ?? typeof(object);
            var m = FindMethod(obj, method, types) ?? FindMethodByNameOnly(obj, method);
            return m.Invoke(obj, args);
        }

        private static MethodInfo FindMethod(object obj, string name, Type[] argTypes)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(name,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                    binder: null, types: argTypes, modifiers: null);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        /// <summary>
        /// Fallback for the overload-by-exact-type lookup above — used when an argument's
        /// runtime type doesn't exactly match the parameter type (e.g. passing a boxed
        /// <c>bool</c> where the parameter is a plain <c>bool</c> is fine, but this covers
        /// any future signature drift without the test needing to know about it).
        /// </summary>
        private static MethodInfo FindMethodByNameOnly(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m != null) return m;
                t = t.BaseType;
            }
            Assert.Fail($"Method '{name}' not found on {obj.GetType().Name}");
            return null;
        }

        public static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        /// <summary>
        /// Creates a fresh <see cref="FSMRuntimeEditor"/> and points its persistence at a
        /// brand-new temp directory via <see cref="FSMRuntimeEditor.TestDataDirOverride"/>.
        /// Caller MUST dispose the returned handle in TearDown — that both clears the
        /// override (so a later, unrelated test can't inherit it) and deletes the temp
        /// files, and it does so even if the test throws.
        /// </summary>
        public static TempFsmEditor CreateEditorWithTempData(bool buildUI = true)
        {
            // Matches FSMRuntimeEditorLifecycleTests' own convention (e.g.
            // Activate_BuildsUIWithoutThrowing) — BuildUI/Start constructs a full
            // TMP/Canvas hierarchy outside Play Mode, which can log incidental
            // Editor-only warnings unrelated to anything this batch changed. Reset in
            // TempFsmEditor.Dispose().
            LogAssert.ignoreFailingMessages = true;

            ClearSingletonInstance<FSMRuntimeEditor>();
            var go = new GameObject("TestFSMEditor_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var ed = go.AddComponent<FSMRuntimeEditor>();
            Invoke(ed, "OnSingletonAwake");
            if (buildUI) Invoke(ed, "Start");

            string tempDir = Path.Combine(Path.GetTempPath(), "ValkurFsmTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            FSMRuntimeEditor.TestDataDirOverride = tempDir;

            return new TempFsmEditor(go, ed, tempDir);
        }

        // ── Minimal set/state/transition fixture builders ───────────────────────
        //
        // Shared by every test file below so a hand-built FSM set always has the same
        // shape a real Sets-panel action would leave behind (typed FSMSetData in sync
        // with its raw dict — see FSMRuntimeEditor.SyncSetToRaw for the fields a real
        // save writes).

        public static FSMRuntimeEditor.FSMSetData MakeTestSet(string id = "TestSet", string initial = "IdleState")
        {
            var raw = new Dictionary<string, object>
            {
                ["id"] = id, ["label"] = id, ["initial"] = initial,
                ["states"] = new List<object>(), ["transitions"] = new List<object>(),
            };
            return new FSMRuntimeEditor.FSMSetData { id = id, label = id, initial = initial, raw = raw };
        }

        public static FSMRuntimeEditor.FSMStateNode AddState(FSMRuntimeEditor.FSMSetData set, string stateId)
        {
            var sraw = new Dictionary<string, object>
            {
                ["id"] = stateId, ["label"] = stateId, ["class"] = "",
                ["terminal"] = false, ["props"] = new Dictionary<string, object>(),
            };
            ((List<object>)set.raw["states"]).Add(sraw);
            var node = new FSMRuntimeEditor.FSMStateNode { id = stateId, label = stateId, stateClass = "", raw = sraw };
            set.states.Add(node);
            return node;
        }

        public static FSMRuntimeEditor.FSMTransitionData AddTransition(
            FSMRuntimeEditor.FSMSetData set, string from, string to, string trId = "t1")
        {
            var traw = new Dictionary<string, object> { ["id"] = trId, ["from"] = from, ["to"] = to, ["guard"] = "" };
            ((List<object>)set.raw["transitions"]).Add(traw);
            var tr = new FSMRuntimeEditor.FSMTransitionData { id = trId, from = from, to = to, raw = traw };
            set.transitions.Add(tr);
            return tr;
        }

        /// <summary>Loads an empty typed model against the temp dir, then splices in a
        /// hand-built set — the direct-construction equivalent of what a designer would
        /// build interactively through the Sets panel.</summary>
        public static void InstallSet(FSMRuntimeEditor ed, FSMRuntimeEditor.FSMSetData set)
        {
            ed.LoadSetsFromDisk();
            var fsmSets = GetField<List<FSMRuntimeEditor.FSMSetData>>(ed, "_fsmSets");
            fsmSets.Add(set);
            var setsRoot = GetField<Dictionary<string, object>>(ed, "_setsRoot");
            ((List<object>)setsRoot["sets"]).Add(set.raw);
            SetField(ed, "_selectedSet", set);
        }

        public static void SizeGraphContent(FSMRuntimeEditor ed, float w, float h)
        {
            var graphContent = GetField<RectTransform>(ed, "_graphContent");
            graphContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            graphContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        }

        /// <summary>Depth-first child lookup by exact GameObject name.</summary>
        public static Transform FindChildRecursive(Transform root, string name)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public sealed class TempFsmEditor : IDisposable
        {
            public readonly GameObject GameObject;
            public readonly FSMRuntimeEditor Editor;
            public readonly string TempDir;

            public TempFsmEditor(GameObject go, FSMRuntimeEditor ed, string tempDir)
            {
                GameObject = go;
                Editor = ed;
                TempDir = tempDir;
            }

            public void Dispose()
            {
                FSMRuntimeEditor.TestDataDirOverride = null;
                if (GameObject != null) UnityEngine.Object.DestroyImmediate(GameObject);
                ClearSingletonInstance<FSMRuntimeEditor>();
                LogAssert.ignoreFailingMessages = false;
                try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }
    }
}
