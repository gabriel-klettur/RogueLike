using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.UI.Frontend;
using Valkur.UI.PauseMenu;

namespace Valkur.Tests.EditMode.UI.PauseMenu
{
    /// <summary>
    /// The pause menu in the pre-game menus' language (<c>PauseMenuUI.Skin.cs</c>).
    ///
    /// <para>uGUI performs no layout in Edit Mode and nothing renders, so the fixture pins
    /// STRUCTURE and the rules a capture cannot keep honest: every panel wears the housing and
    /// no longer paints its flat slab, exactly one row is filled and it follows the selection
    /// with dark ink (value included), the sliders' flat track is gone behind the bar, the video
    /// arrows are drawn, and the motes are additive.</para>
    /// </summary>
    public class PauseMenuSkinTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _go;
        private PauseMenuUI _menu;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (PauseMenuUI.Instance != null) Object.DestroyImmediate(PauseMenuUI.Instance.gameObject);
            _go = new GameObject("PauseMenuSkinHost");
            _menu = _go.AddComponent<PauseMenuUI>();
            typeof(PauseMenuUI).GetMethod("Start", Any)?.Invoke(_menu, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        private T Field<T>(string name) => (T)typeof(PauseMenuUI).GetField(name, Any).GetValue(_menu);
        private object Call(string name, params object[] args) => typeof(PauseMenuUI).GetMethod(name, Any).Invoke(_menu, args);

        [Test]
        public void EveryPanel_WearsTheHousing_AndDropsItsFlatSlab()
        {
            foreach (var name in new[] { "_pausePanel", "_optionsPanel", "_soundsPanel", "_videoPanel", "_inputsPanel", "_loadGamePanel" })
            {
                var panel = Field<GameObject>(name);
                Assert.IsNotNull(panel, name);
                Assert.IsNotNull(panel.transform.Find("PanelFrame")?.GetComponent<BevelFrameGraphic>(), $"{name} has no bevelled frame");
                Assert.AreEqual(0f, panel.GetComponent<Image>().color.a, 0.001f, $"{name}: the flat slab still paints");
            }
        }

        [Test]
        public void ExactlyOneOptionsRow_IsFilled_WithDarkInk_AndItFollowsTheSelection()
        {
            var pills = Field<Image[]>("_optPills");
            var texts = Field<TextMeshProUGUI[]>("_optTexts");
            Call("UpdateListVisuals", 2, pills, Field<Image[]>("_optBars"), texts);

            int filled = 0;
            for (int i = 0; i < pills.Length; i++)
            {
                var fill = pills[i].transform.parent.Find(pills[i].name + "_Fill").GetComponent<FrontendFillGraphic>();
                Assert.AreEqual(0f, pills[i].color.a, 0.001f, $"row {i}: the flat pill still paints");
                if (fill.enabled) { filled++; Assert.AreEqual(2, i); }
            }
            Assert.AreEqual(1, filled);
            Assert.AreNotEqual(texts[0].color, texts[2].color, "the selected label must take the ink");
            Assert.Less(texts[2].color.grayscale, 0.2f, "ink on the gold fill must be dark");
        }

        [Test]
        public void TheSelectedSoundRow_InksItsValueToo()
        {
            Call("UpdateSoundsPanel");
            var rows = (System.Collections.IList)typeof(PauseMenuUI).GetField("_soundRows", Any).GetValue(_menu);
            Assert.Greater(rows.Count, 1);
            var value0 = (TextMeshProUGUI)rows[0].GetType().GetField("valueText").GetValue(rows[0]);
            var value1 = (TextMeshProUGUI)rows[1].GetType().GetField("valueText").GetValue(rows[1]);
            Assert.Less(value0.color.grayscale, 0.2f, "a gold value on the gold fill disappears");
            Assert.Greater(value1.color.grayscale, 0.3f, "an unselected value stays gold");
        }

        [Test]
        public void EverySoundSlider_IsTheBar_WithAGemHandle()
        {
            var rows = (System.Collections.IList)typeof(PauseMenuUI).GetField("_soundRows", Any).GetValue(_menu);
            foreach (var row in rows)
            {
                var slider = (Slider)row.GetType().GetField("slider").GetValue(row);
                Assert.IsNotNull(slider.fillRect.GetComponentInChildren<FrontendFillGraphic>(true), $"{slider.name}: no molten fill");
                Assert.IsNotNull(slider.handleRect.GetComponentInChildren<FrontendGemGraphic>(true), $"{slider.name}: no gem handle");
                Assert.AreEqual(0f, slider.fillRect.GetComponent<Image>().color.a, 0.001f, $"{slider.name}: the cyan fill still paints");
            }
        }

        [Test]
        public void TheVideoArrows_AreDrawn_NotTyped()
        {
            var panel = Field<GameObject>("_videoPanel");
            int arrows = 0;
            foreach (var btn in panel.GetComponentsInChildren<Button>(true))
            {
                if (!btn.name.StartsWith("VLeft_") && !btn.name.StartsWith("VRight_")) continue;
                Assert.IsNotNull(btn.GetComponentInChildren<FrontendArrowGraphic>(true), btn.name);
                foreach (var t in btn.GetComponentsInChildren<TextMeshProUGUI>(true))
                    Assert.IsFalse(t.enabled, $"{btn.name}: the typed glyph still shows");
                arrows++;
            }
            Assert.Greater(arrows, 0);
        }

        [Test]
        public void ThePauseRows_AreSkinned_WhenBuilt()
        {
            Call("RebuildPauseOptions");
            var pills = Field<Image[]>("_pausePills");
            Assert.Greater(pills.Length, 0);
            foreach (var pill in pills)
                Assert.IsNotNull(pill.transform.parent.Find(pill.name + "_Fill")?.GetComponent<FrontendFillGraphic>(), pill.name);
        }

        [Test]
        public void TheMotes_AreAdditive()
        {
            var motes = Field<Valkur.UI.MainMenu.MenuFxLayer>("_skinMotes");
            Assert.IsNotNull(motes);
            Assert.AreEqual((int)UnityEngine.Rendering.BlendMode.One, motes.material.GetInt("_DstBlend"));
        }
    }
}
