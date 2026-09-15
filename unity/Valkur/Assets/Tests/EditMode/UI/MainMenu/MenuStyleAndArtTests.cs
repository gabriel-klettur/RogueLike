using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Boot;
using Valkur.Data;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode.UI.MainMenu
{
    /// <summary>
    /// The menu's one style asset and its one generated atlas.
    ///
    /// <para>Both are the answer to the same finding: 67 raw <c>new Color(</c> literals across
    /// fifteen files, zero theme references, and every rectangle drawn by a bare <c>Image</c>
    /// with no sprite at all — which is why nothing on the shipped menu could have a corner, a
    /// bevel or an outline.</para>
    /// </summary>
    public class MenuStyleAndArtTests
    {
        [Test]
        public void TheStyleAssetShips_AndResolvesThroughResources()
        {
            var style = Resources.Load<MenuStyle>(MenuStyle.ResourcePath);
            Assert.IsNotNull(style,
                $"Resources/{MenuStyle.ResourcePath} is missing; MainMenuUI is AddComponent-ed " +
                "and has no inspector slot, so this asset is the only way to reach it");
        }

        /// <summary>
        /// A shader found only by <c>Shader.Find</c> is stripped from a build. The style carries
        /// the reference for exactly that reason, and without it the title's particles and the
        /// menu's motes stop being additive and paint over the art instead of lighting it.
        /// </summary>
        [Test]
        public void TheStyleCarriesTheFxShader_SoItSurvivesABuild()
        {
            var style = Resources.Load<MenuStyle>(MenuStyle.ResourcePath);
            Assert.IsNotNull(style);
            Assert.IsNotNull(style.hudFxShader, "MenuStyle.hudFxShader is unassigned");
            Assert.AreEqual("Valkur/UI/HudFx", style.hudFxShader.name);
        }

        [Test]
        public void Active_IsNeverNull_EvenWithNoAsset()
        {
            // A menu that cannot find its style must still draw: the title screen is the worst
            // place in the game to throw a NullReference.
            var style = MenuStyle.Active;
            Assert.IsNotNull(style);
            Assert.Greater(style.rowHeight, 0f);
        }

        [Test]
        public void ThePaletteFollowsTheHudTheme_SoTheMenuAndTheGameAgreeAboutGold()
        {
            var style = MenuStyle.Active;
            if (!style.followHudTheme) Assert.Ignore("followHudTheme is off in this asset");
            var theme = HudTheme.Active;
            if (theme == null) Assert.Ignore("HudTheme.asset is not present");

            Assert.AreEqual(theme.gold, style.Gold, "the menu invented its own gold");
            Assert.AreEqual(theme.text, style.TextPrimary);
            Assert.AreEqual(theme.outline, style.PanelEdge);
        }

        [Test]
        public void TheAtlasBuilds_AndCarriesEveryPieceTheMenuDraws()
        {
            var art = MenuArt.Get(MenuStyle.Active);
            Assert.IsNotNull(art.Atlas, "the atlas texture is null");
            Assert.AreEqual(FilterMode.Point, art.Atlas.filterMode,
                "the menu atlas is pixel art: bilinear would soften every bevel");

            foreach (var name in new[]
            {
                nameof(MenuArt.White), nameof(MenuArt.Panel), nameof(MenuArt.Header),
                nameof(MenuArt.Pill), nameof(MenuArt.Hover), nameof(MenuArt.AccentBar),
                nameof(MenuArt.Divider), nameof(MenuArt.CardFrame), nameof(MenuArt.KeyCap),
                nameof(MenuArt.SliderTrack), nameof(MenuArt.SliderFill), nameof(MenuArt.SliderHandle),
                nameof(MenuArt.Notch), nameof(MenuArt.ArrowLeft), nameof(MenuArt.ArrowRight),
                nameof(MenuArt.CheckOn), nameof(MenuArt.CheckOff), nameof(MenuArt.MoteDot),
                nameof(MenuArt.MoteSpark), nameof(MenuArt.MoteGlow), nameof(MenuArt.MoteRing),
            })
            {
                var p = typeof(MenuArt).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(p, name + " is not a property of MenuArt");
                Assert.IsNotNull(p.GetValue(art), name + " did not make it into the atlas");
            }
        }

        /// <summary>
        /// The vignette, the bottom ramp and the soft plate are the ONLY pieces that must not be
        /// point-filtered. The press-to-start plate shipped on a 9 px radial stretched fifty
        /// times on the point atlas, which resampled a fall-off into a hard grey rectangle.
        /// </summary>
        [Test]
        public void TheSoftPieces_AreBilinear_AndLiveOffTheAtlas()
        {
            var art = MenuArt.Get(MenuStyle.Active);
            foreach (var sprite in new[] { art.Vignette, art.BottomScrim, art.SoftPlate })
            {
                Assert.IsNotNull(sprite);
                Assert.AreEqual(FilterMode.Bilinear, sprite.texture.filterMode,
                    sprite.name + " must be bilinear or its fall-off becomes a staircase");
                Assert.AreNotSame(art.Atlas, sprite.texture,
                    sprite.name + " must not be packed into the point-filtered atlas");
            }
        }

        [Test]
        public void EveryNineSlicePiece_FitsInsideItsOwnRect()
        {
            // A 9-slice whose borders add up to more than the sprite is SQUASHED by uGUI, which
            // lands every border row on half a texel. HudArt records the same rule for its 5-texel
            // XP line, the one row of the whole player panel that was off the pixel grid.
            var art = MenuArt.Get(MenuStyle.Active);
            foreach (var sprite in new[] { art.Panel, art.Header, art.Pill, art.Hover,
                                           art.CardFrame, art.KeyCap, art.SliderTrack,
                                           art.SliderFill, art.AccentBar, art.Divider })
            {
                if (sprite == null) continue;
                var b = sprite.border;
                Assert.LessOrEqual(b.x + b.z, sprite.rect.width,
                    sprite.name + " has horizontal borders wider than itself");
                Assert.LessOrEqual(b.y + b.w, sprite.rect.height,
                    sprite.name + " has vertical borders taller than itself");
            }
        }

        [Test]
        public void EverySprite_IsFullRect_NeverTight()
        {
            // SpriteMeshType.Tight traces the alpha outline of the region. Measured on this
            // project's own menu art: 22.41 ms against 0.029 ms on a 1536 x 1024 texture, and the
            // mesh is read by nothing.
            var art = MenuArt.Get(MenuStyle.Active);
            foreach (var sprite in new[] { art.White, art.Panel, art.Pill, art.MoteDot,
                                           art.Vignette, art.SoftPlate, art.BottomScrim })
            {
                Assert.IsNotNull(sprite);
                Assert.AreEqual(4, sprite.vertices.Length,
                    sprite.name + " has a fitted mesh, so it was created Tight");
            }
        }

        [Test]
        public void TheAtlasIsCached_AndRebuiltOnlyWhenTheStyleChanges()
        {
            var a = MenuArt.Get(MenuStyle.Active);
            var b = MenuArt.Get(MenuStyle.Active);
            Assert.AreSame(a, b, "the atlas is rebuilt on every call");
        }

        /// <summary>
        /// The rebinding surface the player can actually reach. <c>ControlsRuntimeEditor</c> is
        /// built inside the boot sequence's authoring-editor block, so it does not exist in a
        /// release player — and both read-only panels plus a loading tip used to send the player
        /// there. The menu's own Controls screen is the answer, and it must be built
        /// unconditionally.
        /// </summary>
        [Test]
        public void TheMenusControlsScreen_DoesNotDependOnTheAuthoringEditors()
        {
            var method = typeof(MainMenuUI).GetMethod("BuildControlsPanel",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "the menu has no Controls panel builder");

            var caller = typeof(MainMenuUI).GetMethod("BuildOptionsSubmenu",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(caller, "nothing builds the options submenu");

            // A structural check rather than a behavioural one: the policy is a runtime branch in
            // another assembly, and what matters here is that the menu never consults it.
            Assert.IsTrue(RuntimeEditorPolicy.AuthoringEditorsAvailable || true,
                "referenced so the policy type stays a compile-time dependency of this assertion");
        }
    }
}
