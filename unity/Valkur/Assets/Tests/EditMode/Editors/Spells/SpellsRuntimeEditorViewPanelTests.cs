using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Editors.Spells
{
    /// <summary>
    /// Lifecycle / wiring tests for the Spells Editor (F4) "View" panel — the live
    /// looping spell preview. Locks in:
    ///
    ///   • The View panel exists in <c>UIRefs</c> after BuildUI and is hidden by default.
    ///   • Opening the panel creates the preview service and binds its RenderTexture
    ///     to the View panel's RawImage (so the user actually sees the loop).
    ///   • Closing the panel restores AudioListener.volume — opening had cached and
    ///     muted it.
    ///   • Deactivating the editor tears down the preview service / stage / camera.
    ///
    /// Same reflection scaffolding pattern as <c>ItemsRuntimeEditorLifecycleTests</c>.
    /// </summary>
    [TestFixture]
    public class SpellsRuntimeEditorViewPanelTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private float _audioVolumeAtSetup;

        [SetUp]
        public void SetUp()
        {
            _audioVolumeAtSetup = AudioListener.volume;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            ClearSingletonInstance<SpellsRuntimeEditor>();
            // Defensive — if a test failed before close, restore volume so we
            // don't leak a muted listener into other test fixtures.
            AudioListener.volume = _audioVolumeAtSetup;
        }

        // ── Reflection helpers ────────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        private static FieldInfo Field(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetField(object obj, string name) => Field(obj, name)?.GetValue(obj);

        private static object GetNested(object obj, string a, string b)
        {
            var fa = GetField(obj, a);
            return fa == null ? null : Field(fa, b)?.GetValue(fa);
        }

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public |
                                            BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, args); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{method}' not found on {obj.GetType().Name}");
        }

        private SpellsRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<SpellsRuntimeEditor>();
            var go = new GameObject("TestSpellsEditor");
            _scene.Add(go);
            var ed = go.AddComponent<SpellsRuntimeEditor>();
            // EditMode does not run Awake/Start automatically — invoke them.
            Invoke(ed, "OnSingletonAwake");
            Invoke(ed, "Start");
            return ed;
        }

        private static void ToggleDropdown(SpellsRuntimeEditor ed, string name)
            => Invoke(ed, "ToggleDropdown", name);

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Test]
        public void Activate_BuildsViewPanelHiddenByDefault()
        {
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();
            ed.Activate();

            var viewDropdown = GetNested(ed, "_uiRefs", "ViewDropdown") as GameObject;
            Assert.IsTrue(viewDropdown != null,
                "BuildAll must construct the View panel GameObject.");
            Assert.IsFalse(viewDropdown.activeSelf,
                "View panel must start hidden — it's a dropdown, opened via the View menu button.");

            var viewBtn = GetNested(ed, "_uiRefs", "ViewMenuBtnImg") as Image;
            Assert.IsTrue(viewBtn != null,
                "The 'View v' menu-bar button must be created so the user can toggle the panel.");

            var rawImg = GetNested(ed, "_uiRefs", "ViewRawImage") as RawImage;
            Assert.IsTrue(rawImg != null,
                "The View panel must contain a RawImage that displays the preview RenderTexture.");

            ed.Deactivate();
        }

        [Test]
        public void OpenViewPanel_CreatesPreviewService_AndBindsRawImageTexture()
        {
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();
            ed.Activate();

            Assert.IsNull(GetField(ed, "_previewService"),
                "Preview service must be lazily created — null until the View panel opens.");

            ToggleDropdown(ed, "view");

            var service = GetField(ed, "_previewService");
            Assert.IsNotNull(service,
                "Opening the View dropdown must construct the SpellPreviewService.");

            var raw = GetNested(ed, "_uiRefs", "ViewRawImage") as RawImage;
            Assert.IsTrue(raw != null && raw.texture != null,
                "RawImage.texture must be bound to the preview RT once the panel is open.");

            ed.Deactivate();
        }

        [Test]
        public void OpenViewPanel_MutesAudioListener_CloseRestoresIt()
        {
            LogAssert.ignoreFailingMessages = true;

            const float TEST_VOLUME = 0.42f;
            AudioListener.volume = TEST_VOLUME;

            var ed = CreateEditor();
            ed.Activate();

            ToggleDropdown(ed, "view");
            Assert.AreEqual(0f, AudioListener.volume, 1e-4f,
                "Opening the View panel must mute AudioListener (preview SFX would be intrusive).");

            ToggleDropdown(ed, "view");                 // close
            Assert.AreEqual(TEST_VOLUME, AudioListener.volume, 1e-4f,
                "Closing the View panel must restore the cached pre-mute volume.");

            ed.Deactivate();
        }

        [Test]
        public void Deactivate_TearsDownPreviewServiceAndRestoresAudio()
        {
            LogAssert.ignoreFailingMessages = true;

            const float TEST_VOLUME = 0.7f;
            AudioListener.volume = TEST_VOLUME;

            var ed = CreateEditor();
            ed.Activate();
            ToggleDropdown(ed, "view");
            Assert.IsNotNull(GetField(ed, "_previewService"));

            ed.Deactivate();

            Assert.IsNull(GetField(ed, "_previewService"),
                "Deactivate must shut down the preview service (Camera + RT freed).");
            Assert.AreEqual(TEST_VOLUME, AudioListener.volume, 1e-4f,
                "Deactivate must restore audio even if View panel was still open.");
        }

        [Test]
        public void OpenAllPanels_LeavesViewClosed()
        {
            // The View panel should NOT auto-open on Activate (mirrors Tutorial).
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();
            ed.Activate();           // calls OpenAllPanels internally

            var viewDropdown = GetNested(ed, "_uiRefs", "ViewDropdown") as GameObject;
            Assert.IsFalse(viewDropdown.activeSelf,
                "OpenAllPanels must skip 'view' — opens only modes/spells/props.");

            ed.Deactivate();
        }

        [Test]
        public void DirectionAndZoomButtons_HaveListeners()
        {
            // Regression for the UIButton.Make null-onClick NRE: the View panel
            // creates its buttons with null onClick (wired later by WireViewPanel)
            // and must end up with at least one persistent or runtime listener.
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();
            ed.Activate();

            var dirN = GetNested(ed, "_uiRefs", "ViewDirNBtn")  as Button;
            var dirS = GetNested(ed, "_uiRefs", "ViewDirSBtn")  as Button;
            var dirE = GetNested(ed, "_uiRefs", "ViewDirEBtn")  as Button;
            var dirW = GetNested(ed, "_uiRefs", "ViewDirWBtn")  as Button;
            var zIn  = GetNested(ed, "_uiRefs", "ViewZoomInBtn")  as Button;
            var zOut = GetNested(ed, "_uiRefs", "ViewZoomOutBtn") as Button;

            foreach (var (btn, label) in new[]
            {
                (dirN, "DirN"), (dirS, "DirS"), (dirE, "DirE"), (dirW, "DirW"),
                (zIn,  "ZoomIn"), (zOut, "ZoomOut")
            })
            {
                Assert.IsTrue(btn != null, $"Button '{label}' must exist after BuildUI.");
                // Invoking must not throw — the listeners are null-safe via `?.`
                // even when the preview service hasn't been created yet.
                Assert.DoesNotThrow(() => btn.onClick.Invoke(),
                    $"Button '{label}' onClick must not throw when service is uninitialized.");
            }

            ed.Deactivate();
        }
    }
}
