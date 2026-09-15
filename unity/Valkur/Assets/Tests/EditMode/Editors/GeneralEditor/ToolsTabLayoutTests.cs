using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Editors.General;

namespace Valkur.Tests.EditMode.Editors.GeneralEditor
{
    /// <summary>
    /// The tab strip's structure, and the one property that cannot be asserted from a rendered
    /// size in EditMode.
    ///
    /// <para><b>uGUI performs no layout in EditMode</b>, so no test here can measure that the
    /// strip is 24 px tall — reading back <c>rect.height</c> returns whatever was written and
    /// never what a layout pass would make of it. That is exactly how the strip shipped
    /// rendering at <b>180 px</b> with 8192 tests green.</para>
    ///
    /// <para>What IS assertable is the INPUT to that layout pass: a row carrying both a
    /// <see cref="LayoutElement"/> and a <see cref="HorizontalLayoutGroup"/> must declare
    /// <c>flexibleHeight = 0</c>, because uGUI resolves each layout property independently and
    /// the group's own flexible height of 1 wins otherwise. Pinning the declaration is the
    /// closest a headless fixture can get to pinning the pixel, and it is the half that
    /// regressed.</para>
    /// </summary>
    [TestFixture]
    public class ToolsTabLayoutTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _host;
        private GeneralEditorManager _launcher;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("GeneralEditorHost");
            _launcher = _host.AddComponent<GeneralEditorManager>();

            // Awake does not run on a component added in Edit Mode, so the launcher is built
            // by hand — the same reason SnowSplatMap pairs EnsureBuilt with ReleaseBuffer.
            var awake = typeof(GeneralEditorManager).GetMethod("OnSingletonAwake", Any);
            awake?.Invoke(_launcher, null);
        }

        [TearDown]
        public void TearDown()
        {
            var canvas = (Canvas)typeof(GeneralEditorManager).GetField("_canvas", Any).GetValue(_launcher);
            if (canvas != null) Object.DestroyImmediate(canvas.gameObject);
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private GameObject PanelOf() =>
            (GameObject)typeof(GeneralEditorManager).GetField("_panelRoot", Any).GetValue(_launcher);

        // ── The defect that shipped ──────────────────────────────────────────

        [Test]
        public void TheTabStrip_DeclaresZeroFlexibleHeight()
        {
            var tabs = PanelOf().transform.Find("Content/Tabs");
            Assert.NotNull(tabs, "The tab strip must be the first child of the panel content.");

            var le  = tabs.GetComponent<LayoutElement>();
            var hlg = tabs.GetComponent<HorizontalLayoutGroup>();
            Assert.NotNull(le,  "The strip declares its own height.");
            Assert.NotNull(hlg, "The strip lays its three buttons out in a row.");

            Assert.AreEqual(GeneralEditorManager.TAB_HEIGHT, le.preferredHeight, 0.01f);
            Assert.AreEqual(0f, le.flexibleHeight, 0.01f,
                "MEASURED IN PLAY MODE AT 180 px WITH THIS UNSET. uGUI resolves each layout " +
                "property independently: the LayoutElement wins the preferred height and leaves " +
                "flexibleHeight at -1, so the value used comes from the HorizontalLayoutGroup on " +
                "the same object, which reports 1 because childForceExpandHeight is on. The strip " +
                "was then the content's only flexible child and swallowed every spare pixel.");
        }

        [Test]
        public void EveryRow_ThatCarriesBothComponents_PinsItsFlexibleHeight()
        {
            // The general form of the rule, so the next row added anywhere in this panel is
            // caught by the same test rather than by another rendered frame.
            foreach (var hlg in PanelOf().GetComponentsInChildren<HorizontalLayoutGroup>(true))
            {
                var le = hlg.GetComponent<LayoutElement>();
                if (le == null) continue;
                Assert.AreEqual(0f, le.flexibleHeight, 0.01f,
                    $"'{hlg.name}' carries a LayoutElement and a HorizontalLayoutGroup. Without " +
                    "an explicit flexibleHeight = 0 the group's 1 wins and the row expands.");
            }
        }

        // ── One tab, one panel height ────────────────────────────────────────

        [Test]
        public void ThePanel_IsSizedForTheOpenTab_NotTheTallestOne()
        {
            var entries = GeneralEditorRegistry.BuildEntries();
            float editors = GeneralEditorManager.ComputePanelHeight(entries, GeneralEditorSection.Editors);
            float tools   = GeneralEditorManager.ComputePanelHeight(entries, GeneralEditorSection.Tools);

            Assert.Greater(editors, tools,
                "Editors holds nineteen entries and Tools five; if they compute the same height " +
                "the panel is padding one of them, which measured 60 % empty on screen.");
            Assert.AreEqual(GeneralEditorManager.TallestTabHeight(entries), editors, 0.01f,
                "Editors is the tallest tab, so it must be what TallestTabHeight reports.");
        }

        [Test]
        public void SwitchingTab_ResizesThePanel()
        {
            var select = typeof(GeneralEditorManager).GetMethod("SelectTab", Any);
            var rt = (RectTransform)PanelOf().transform;

            select.Invoke(_launcher, new object[] { GeneralEditorSection.Editors });
            float tall = rt.sizeDelta.y;
            select.Invoke(_launcher, new object[] { GeneralEditorSection.Tools });
            float shortTab = rt.sizeDelta.y;

            Assert.Less(shortTab, tall,
                "The panel holds ONE tab, so opening a shorter one has to shrink it — otherwise " +
                "the window is mostly hole and reads as half-built.");
        }

        // ── One tab visible at a time ────────────────────────────────────────

        [Test]
        public void ExactlyOneSection_IsVisibleAtATime()
        {
            var select = typeof(GeneralEditorManager).GetMethod("SelectTab", Any);
            foreach (GeneralEditorSection section in System.Enum.GetValues(typeof(GeneralEditorSection)))
            {
                select.Invoke(_launcher, new object[] { section });

                var roots = typeof(GeneralEditorManager).GetField("_sectionRoots", Any)
                    .GetValue(_launcher) as System.Collections.IDictionary;

                int visible = 0;
                foreach (System.Collections.DictionaryEntry kv in roots)
                    if (((GameObject)kv.Value).activeSelf) visible++;

                Assert.AreEqual(1, visible, $"Opening {section} must leave exactly one section on screen.");
                Assert.AreEqual(section, _launcher.ActiveTab);
            }
        }

        [Test]
        public void NoSection_RepeatsItsOwnTabLabelAsAHeader()
        {
            // The lit tab already names the open section; printing the same word 4 px under it
            // spent 18 px saying nothing.
            foreach (GeneralEditorSection section in System.Enum.GetValues(typeof(GeneralEditorSection)))
            {
                var root = PanelOf().transform.Find($"Content/Section_{section}");
                Assert.NotNull(root, $"Section_{section} must exist.");
                Assert.IsNull(root.Find($"Hdr_{section}"),
                    $"Section {section} still carries a header duplicating its tab label.");
            }
        }

        [Test]
        public void EveryTab_HasALabel_AndEverySectionHasATab()
        {
            var tabs = typeof(GeneralEditorManager).GetField("_tabs", Any)
                .GetValue(_launcher) as System.Collections.IDictionary;

            foreach (GeneralEditorSection section in System.Enum.GetValues(typeof(GeneralEditorSection)))
            {
                Assert.IsTrue(tabs.Contains(section), $"{section} has no tab button — it is unreachable.");
                Assert.IsNotEmpty(GeneralEditorManager.TabLabel(section));
                Assert.AreNotEqual(section.ToString().ToUpperInvariant(),
                    GeneralEditorManager.TabLabel(section),
                    $"{section} fell through to the enum-name fallback instead of a written label.");
            }
        }

        // ── The Tools tab's own contents ─────────────────────────────────────

        [Test]
        public void TheToolsTab_IsNotEmpty_AndHoldsTheSelectionTool()
        {
            var labels = GeneralEditorRegistry.BuildEntries()
                .Where(e => e.Section == GeneralEditorSection.Tools)
                .Select(e => e.Label)
                .ToList();

            CollectionAssert.IsNotEmpty(labels);
            CollectionAssert.Contains(labels, "Seleccion",
                "The cross-domain Selection tool is reached from this tab and nowhere else.");
        }

        [Test]
        public void EveryToolsEntry_EitherOpensAnEditorOrReportsItsOwnState()
        {
            // A tool that neither activates exclusively nor exposes IsActive gives the author
            // no way to tell whether pressing it did anything.
            foreach (var e in GeneralEditorRegistry.BuildEntries()
                         .Where(e => e.Section == GeneralEditorSection.Tools))
            {
                Assert.IsTrue(e.IsActive != null || e.ClosesLauncher,
                    $"'{e.Label}' has no IsActive and does not close the launcher, so nothing " +
                    "on screen changes when it is pressed.");
            }
        }
    }
}
