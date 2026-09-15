using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.Frontend;
using Valkur.UI.MainMenu;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.Tests.EditMode.UI.MainMenu.Kit
{
    /// <summary>
    /// The shared language of the pre-game surfaces (<c>Valkur.UI.Frontend</c>): the loading
    /// bar's housing, fill, gems and particles, reused by every menu widget.
    ///
    /// <para><b>What it can and cannot see.</b> uGUI performs no layout in Edit Mode and nothing
    /// here renders, so the fixture pins the RULES a capture cannot keep honest over time: the
    /// glow has no edge, a hand-built mesh honours <c>Graphic.color</c>, the selected row's
    /// darkest light still carries dark ink, and particles answer events and motion — never rest.
    /// How it LOOKS is verified by a rendered frame, not here.</para>
    /// </summary>
    public class FrontendKitTests
    {
        private GameObject _root;
        private RectTransform _body;
        private MenuStyle _style;
        private MenuArt _art;

        [SetUp]
        public void SetUp()
        {
            _style = MenuStyle.Active;
            _art = MenuArt.Get(_style);
            _root = new GameObject("FrontendKitTestRoot", typeof(RectTransform));
            _body = (RectTransform)_root.transform;
            _body.sizeDelta = new Vector2(480f, 320f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void TheSharedGlow_HasExactlyZeroAlphaAtItsRim()
        {
            // The title's mote keeps a few percent at its rim, which is a hard-edged SQUARE once a
            // flash is blown up to the width of a row. The kit's glow must not.
            var tex = FrontendKit.Get(_style).Radial.texture;
            int n = tex.width;
            foreach (var p in new[] { new Vector2Int(0, 0), new Vector2Int(n - 1, 0), new Vector2Int(0, n / 2),
                                      new Vector2Int(n / 2, n - 1), new Vector2Int(n - 1, n - 1) })
                Assert.AreEqual(0f, tex.GetPixel(p.x, p.y).a, 0.0001f, $"texel {p} is not transparent");
            Assert.Greater(tex.GetPixel(n / 2, n / 2).a, 0.9f, "the core is not bright");
        }

        [Test]
        public void TheKit_IsSharedPerStyle()
        {
            Assert.AreSame(FrontendKit.Get(_style), FrontendKit.Get(_style),
                "every widget building its own material is the cost the kit exists to remove");
        }

        [Test]
        public void AHandBuiltMesh_HonoursGraphicColor()
        {
            // Without this, Color.clear "hid" a load-panel slot while it went on drawing.
            var vh = new VertexHelper();
            FrontendMesh.Quad(vh, 0f, 0f, 10f, 10f, Color.white);
            FrontendMesh.ApplyGraphicColor(vh, Color.clear);
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Assert.AreEqual(0, v.color.a, $"vertex {i} still carries alpha");
            }
            vh.Dispose();
        }

        [Test]
        public void TheSelectedRowsDarkestLight_StillCarriesDarkInk()
        {
            // MenuContrastTests measures the flat gold the row used to be; the row is a light
            // PROFILE now, so the promise has to hold at its darkest point, not its average.
            var panel = MenuUIKit.Composite(_style.Panel, Color.black);
            float darkest = 1f;
            for (int i = 0; i <= 32; i++) darkest = Mathf.Min(darkest, FrontendRamp.Row(i / 32f));
            Assert.GreaterOrEqual(darkest, FrontendRamp.RowFloor - 0.0001f);
            var fill = MenuUIKit.Composite(FrontendRamp.Shade(_style.Gold, darkest), panel);
            float ratio = MenuUIKit.Contrast(_style.textOnSelection, fill);
            Assert.GreaterOrEqual(ratio, 4.5f, $"dark ink on the row's darkest band measured {ratio:F2}:1");
        }

        [Test]
        public void TheSelection_EmitsOnlyWhenItMovesOrIsChosen()
        {
            var motes = MenuFxLayer.Create(_body, _art, 64, null);
            var fx = new FrontendSelectionFx(_body, "Sel", _style.Gold, FrontendKit.Get(_style), motes, reduceMotion: false);
            fx.Root.sizeDelta = new Vector2(300f, 46f);

            for (int i = 0; i < 60; i++) fx.Tick(1f / 60f);
            Assert.AreEqual(0, fx.ParticleCount, "a selection sitting still threw particles");

            fx.Moved(1);
            Assert.Greater(fx.ParticleCount, 0, "moving the selection threw nothing");
            for (int i = 0; i < 120; i++) fx.Tick(1f / 30f);
            Assert.AreEqual(0, fx.ParticleCount, "the sparks outlived their moment");

            fx.Confirm();
            Assert.Greater(fx.ParticleCount, 0, "confirming threw nothing");
        }

        [Test]
        public void UnderReduceMotion_TheSelectionNeitherEmitsNorFlows()
        {
            var motes = MenuFxLayer.Create(_body, _art, 64, null);
            var fx = new FrontendSelectionFx(_body, "Sel", _style.Gold, FrontendKit.Get(_style), motes, reduceMotion: true);
            fx.Moved(-1);
            fx.Confirm();
            Assert.AreEqual(0, fx.ParticleCount);
            Assert.IsFalse(fx.Fill.Flow, "the bands of light kept flowing under reduce motion");
        }

        [Test]
        public void ASlider_SparksOnlyWhileItsFillMoves()
        {
            var list = new MenuList(_body, _art, _style, reduceMotion: false);
            var row = list.Add(_art, "Volume");
            var slider = new MenuSlider(row.Content, _art, _style, 0f, 1f, 0.2f, 0.01f, _ => { }, 5, list.Motes);
            Assert.AreEqual(0.2f, slider.FillAmount, 0.001f, "the fill does not start where the value is");

            for (int i = 0; i < 30; i++) slider.Tick(1f / 60f, reduceMotion: false);
            Assert.AreEqual(0, list.Motes.Alive, "a slider at rest threw particles");

            slider.SetValue(0.7f);
            slider.Tick(1f / 60f, reduceMotion: false);
            Assert.AreEqual(0, list.Motes.Alive, "a value SET (a panel refreshing) is not motion");
            Assert.AreEqual(0.7f, slider.FillAmount, 0.001f);

            for (int i = 0; i < 6; i++) { slider.Slider.value = 0.1f + i * 0.12f; slider.Tick(1f / 60f, reduceMotion: false); }
            Assert.Greater(list.Motes.Alive, 0, "dragging the fill threw nothing");
        }

        [Test]
        public void ARowToggle_LightsItsGemWhenOn()
        {
            var list = new MenuList(_body, _art, _style, reduceMotion: true);
            var row = list.Add(_art, "Hints");
            row.SetToggle(true);
            var gem = row.Root.GetComponentInChildren<FrontendGemGraphic>(true);
            Assert.IsNotNull(gem);
            Assert.AreEqual(1f, gem.Lit, 0.001f);
            row.SetToggle(false);
            Assert.AreEqual(0f, gem.Lit, 0.001f);
            Assert.AreEqual(row.HitTarget.transform.GetSiblingIndex(), row.Root.childCount - 1,
                "the gem was built above the row's one hit target");
        }
    }
}
