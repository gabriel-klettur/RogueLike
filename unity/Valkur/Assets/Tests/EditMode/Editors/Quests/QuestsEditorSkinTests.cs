using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Editors.Quests;
using Valkur.UI.Frontend;

namespace Valkur.Tests.EditMode.Editors.Quests
{
    /// <summary>
    /// Misiones in the pre-game menus' language (<c>QuestsRuntimeEditor.Skin.cs</c>).
    ///
    /// <para>uGUI performs no layout in Edit Mode and nothing here renders, so the fixture pins
    /// STRUCTURE and the rules a capture cannot keep honest: both panels wear the housing, exactly
    /// one tab is filled, tab labels shrink instead of overflowing, the scroll slabs are gone, the
    /// motes are additive, and a selected row is the filled row with dark ink.</para>
    /// </summary>
    [TestFixture]
    public class QuestsEditorSkinTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _host;
        private QuestsRuntimeEditor _editor;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _host = new GameObject("QuestsEditorSkinHost");
            _editor = _host.AddComponent<QuestsRuntimeEditor>();
            typeof(QuestsRuntimeEditor).GetMethod("BuildUI", Any).Invoke(_editor, null);
            typeof(QuestsRuntimeEditor).GetField("_uiBuilt", Any).SetValue(_editor, true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            LogAssert.ignoreFailingMessages = false;
        }

        private T Field<T>(string name) => (T)typeof(QuestsRuntimeEditor).GetField(name, Any).GetValue(_editor);

        [Test]
        public void BothPanels_WearTheBevelledHousing()
        {
            foreach (var name in new[] { "_listPanel", "_detailPanel" })
            {
                var panel = Field<Component>(name);
                Assert.IsNotNull(panel.transform.Find("FrontendFrame")?.GetComponent<BevelFrameGraphic>(),
                    $"{name} has no bevelled frame");
            }
        }

        [Test]
        public void ExactlyOneTab_IsFilled_AndItFollowsTheSelection()
        {
            var tabs = (System.Collections.IList)typeof(QuestsRuntimeEditor).GetField("_tabSkins", Any).GetValue(_editor);
            Assert.AreEqual(4, tabs.Count);
            _editor.SetTab(QuestsRuntimeEditor.Tab.Locked);
            int filled = 0, filledIndex = -1;
            for (int i = 0; i < tabs.Count; i++)
            {
                var fill = (FrontendFillGraphic)tabs[i].GetType().GetField("Item1").GetValue(tabs[i]);
                var tmp = (TextMeshProUGUI)tabs[i].GetType().GetField("Item3").GetValue(tabs[i]);
                if (fill.enabled) { filled++; filledIndex = i; }
                Assert.IsTrue(tmp.enableAutoSizing, $"tab {i}: a fixed-size label runs past its tab");
                Assert.AreNotEqual(TextOverflowModes.Overflow, tmp.overflowMode, $"tab {i}: text may leave the tab");
            }
            Assert.AreEqual(1, filled);
            Assert.AreEqual((int)QuestsRuntimeEditor.Tab.Locked, filledIndex);
        }

        [Test]
        public void TheScrollSlabs_AreGone_SoTheHousingShowsThrough()
        {
            foreach (var name in new[] { "_listScrollContent", "_detailScrollContent" })
            {
                var content = Field<RectTransform>(name);
                var scroll = content.GetComponentInParent<ScrollRect>(true);
                Assert.AreEqual(0f, scroll.GetComponent<Image>().color.a, 0.001f, $"{name}: the flat slab still paints");
            }
        }

        [Test]
        public void TheMotes_AreAdditive()
        {
            var mat = _editor.ListMotes.material;
            Assert.IsNotNull(mat);
            Assert.AreEqual((int)UnityEngine.Rendering.BlendMode.One, mat.GetInt("_DstBlend"));
        }

        [Test]
        public void ASelectedRow_IsTheFilledRow_WithDarkInk()
        {
            var content = Field<RectTransform>("_listScrollContent");
            var build = typeof(QuestsRuntimeEditor).GetMethod("BuildQuestRow", Any);
            build.Invoke(_editor, new object[] { content, "q_a", "Quest A", 0.4f, true });
            build.Invoke(_editor, new object[] { content, "q_b", "Quest B", -1f, false });

            var a = content.Find("QuestRow_q_a");
            var b = content.Find("QuestRow_q_b");
            Assert.IsTrue(a.Find("Fill").GetComponent<FrontendFillGraphic>().enabled);
            Assert.IsFalse(b.Find("Fill").GetComponent<FrontendFillGraphic>().enabled);
            Assert.IsNotNull(a.Find("Progress"), "a running quest must show its progress bar");
            Assert.IsNull(b.Find("Progress"), "a quest that is not running has no progress to show");
            Assert.AreEqual(Valkur.Gameplay.Editors.EditorFrontendSkin.Ink, a.Find("Label").GetComponent<TextMeshProUGUI>().color);
        }
    }
}
