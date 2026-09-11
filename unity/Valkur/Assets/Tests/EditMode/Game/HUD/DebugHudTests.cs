using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The debug HUD (F1), built in Edit Mode through <see cref="DebugHUD.EnsureBuilt"/> with
    /// persistence OFF — PlayerPrefs are machine state and would outlive the run.
    ///
    /// <para>uGUI performs no layout in Edit Mode, so these assert the AUTHORED rects, which is
    /// all the panel uses: it places every child by hand, bottom-left, on whole texels. A
    /// rendered frame is the check for the look; these pin the defects the audit measured
    /// (<c>.github/DEBUG_HUD_BEAUTY_AUDIT_2026-09-11.md</c>) so none of them comes back.</para>
    /// </summary>
    [TestFixture]
    public class DebugHudTests
    {
        private GameObject _go;
        private DebugHUD _hud;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("DebugHUD");
            _hud = _go.AddComponent<DebugHUD>();
            _hud.EnsureBuilt(persist: false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        // -- D1: the panel exists ------------------------------------------------------------

        [Test]
        public void ThePanel_HasARealHeight()
        {
            // The old background measured 0 px tall: a ContentSizeFitter asking an Image with no
            // sprite for its preferred height. It never drew a pixel.
            _hud.SetLevel(DebugHUD.LevelPanel);
            Assert.Greater(_hud.PanelHeightTexels, 40, "the panel lays itself out; nothing asks a fitter");
            Assert.AreEqual(_hud.PanelHeightTexels, _hud.PanelRoot.sizeDelta.y, 0.001f);
            var frame = _hud.PanelRoot.Find("Frame").GetComponent<Image>();
            Assert.IsNotNull(frame.sprite, "an opaque framed surface, not a sprite-less translucent rectangle (H3)");
            Assert.AreEqual(Image.Type.Sliced, frame.type);
        }

        // -- R1: the texel grid ----------------------------------------------------------------

        [Test]
        public void EveryRectInThePixelSpace_SitsOnWholeTexels()
        {
            _hud.SetLevel(DebugHUD.LevelPanel);
            var bad = new List<string>();
            foreach (var rt in _hud.PixelsRoot.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == _hud.PixelsRoot) continue;
                if (!Whole(rt.anchoredPosition.x) || !Whole(rt.anchoredPosition.y) ||
                    !Whole(rt.sizeDelta.x) || !Whole(rt.sizeDelta.y))
                    bad.Add(rt.name + " pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta);
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        private static bool Whole(float v) => Mathf.Abs(v - Mathf.Round(v)) < 0.001f;

        // -- H5: clickable only where it must be ---------------------------------------------

        [Test]
        public void OnlyTheHeadersAndTheCopyButton_AreClickTargets()
        {
            // The combat poll refuses a click over ANY raycast target. The old panel's fix-me-once
            // background would have made the minimap corner a dead zone for the left click.
            _hud.SetLevel(DebugHUD.LevelPanel);
            var targets = new List<string>();
            foreach (var g in _hud.OverlayCanvas.GetComponentsInChildren<Graphic>(true))
            {
                if (!g.raycastTarget) continue;
                if (g.name == "Header" || g.name == "Copy") continue;
                targets.Add(g.name + " (" + g.GetType().Name + ")");
            }
            Assert.IsEmpty(targets, "these would eat combat clicks:\n" + string.Join("\n", targets));
        }

        // -- Levels ------------------------------------------------------------------------------

        [Test]
        public void TheLevels_ShowTheChip_ThenThePanel()
        {
            Assert.IsFalse(_hud.IsVisible);

            _hud.CycleLevel();
            Assert.AreEqual(DebugHUD.LevelChip, _hud.Level);
            Assert.IsTrue(_hud.ChipRoot.gameObject.activeSelf);
            Assert.IsFalse(_hud.PanelRoot.gameObject.activeSelf);

            _hud.CycleLevel();
            Assert.AreEqual(DebugHUD.LevelPanel, _hud.Level);
            Assert.IsFalse(_hud.ChipRoot.gameObject.activeSelf);
            Assert.IsTrue(_hud.PanelRoot.gameObject.activeSelf);
        }

        [Test]
        public void TheCycle_WrapsToHidden_AndTheLevelIsClamped()
        {
            Assert.AreEqual(DebugHUD.LevelInspector, _hud.MaxLevel, "the Editor gets every level");
            _hud.SetLevel(99);
            Assert.AreEqual(DebugHUD.LevelInspector, _hud.Level);
            _hud.CycleLevel();
            Assert.AreEqual(DebugHUD.LevelHidden, _hud.Level);
        }

        [Test]
        public void ToggleVisible_ComesBackToTheLevelLastShown()
        {
            // The General Editor's button: off, then back to where the author left it.
            _hud.SetLevel(DebugHUD.LevelChip);
            _hud.ToggleVisible();
            Assert.IsFalse(_hud.IsVisible);
            _hud.ToggleVisible();
            Assert.AreEqual(DebugHUD.LevelChip, _hud.Level);
        }

        // -- H6: the band -----------------------------------------------------------------------

        [TestCase(800, 1f, 2, TestName = "Band_AtTheReferenceResolution")]
        [TestCase(1080, 1.2728f, 3, TestName = "Band_At1080p")]
        public void TheBand_SitsBetweenTheInstrumentsAndThePlayerPanel(int screenH, float root, int scale)
        {
            var ps = PlayerHudStyle.Active;
            float top = Mathf.Round(HudLayout.ToolColumnTop * root);
            float player = (ps.marginTexels + ps.PanelHeightTexels) * scale;
            float combo = (ComboHUD.PreferredHeight + HudLayout.StackGap) * root;
            int expected = Mathf.FloorToInt((screenH - top - player - combo) / scale);

            int band = DebugHUD.BandTexels(screenH, root, scale, ps);

            Assert.AreEqual(expected, band);
            Assert.Greater(band, 100, "enough for the performance section on its own");
        }

        [Test]
        public void TheToolColumn_StartsUnderTheTopLeftInstruments()
        {
            Assert.AreEqual(HudLayout.TopLeftColumnBottom + HudLayout.StackGap, HudLayout.ToolColumnTop);
            Assert.Less(HudLayout.ToolSortingOrder, 200, "under the game windows");
            Assert.Greater(HudLayout.ToolSortingOrder, 150, "over every instrument");
        }

        [Test]
        public void ARoomFold_KeepsPerformanceOpen_AndOpeningAFoldedSectionFoldsAnother()
        {
            _hud.SetLevel(DebugHUD.LevelPanel);
            var sections = _hud.Sections;
            SetBand(sections[0].HeaderHeight * 4 + 90);

            Assert.IsTrue(sections[0].IsOpen, "performance is the last thing to fold");
            Assert.IsTrue(sections[3].AutoCollapsed, "the lowest section folds first");

            Invoke("OnHeaderClicked", 3);
            Assert.IsTrue(sections[3].IsOpen, "the section the author opened gets the room");
            bool another = sections[0].AutoCollapsed || sections[1].AutoCollapsed || sections[2].AutoCollapsed;
            Assert.IsTrue(another, "something else folded to make that room");
            Assert.AreEqual(0, (int)Invoke("CollapsedMask"),
                            "a fold the SCREEN made is never persisted as the author's choice");
        }

        private void SetBand(int texels)
        {
            typeof(DebugHUD).GetField("_bandTexels", BindingFlags.Instance | BindingFlags.NonPublic)
                            .SetValue(_hud, texels);
            Invoke("Layout");
        }

        private object Invoke(string method, params object[] args) =>
            typeof(DebugHUD).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(_hud, args.Length == 0 ? null : args);

        // -- The report -----------------------------------------------------------------------------

        [Test]
        public void TheReport_IsBuiltWithoutAPlayerOrAMonitor()
        {
            string report = _hud.BuildReport();
            StringAssert.StartsWith("VALKUR - informe de depuracion", report);
            StringAssert.Contains("Pantalla:", report);
            StringAssert.Contains("sin jugador", report);
        }

        // -- The graph ---------------------------------------------------------------------------------

        [TestCase(0f, 0, TestName = "Bar_NoFrameIsNoBar")]
        [TestCase(0.1f, 1, TestName = "Bar_AnyFrameIsAtLeastOneTexel")]
        [TestCase(25f, 13, TestName = "Bar_HalfTheCeilingIsHalfTheHeight")]
        [TestCase(500f, 26, TestName = "Bar_PastTheCeilingIsClamped")]
        public void TheGraph_ScalesAgainstAFixedCeiling(float ms, int expected)
        {
            Assert.AreEqual(expected, DebugFrameGraph.BarHeight(ms, 50f, 26));
        }

        // -- Text ---------------------------------------------------------------------------------------

        [TestCase("Bola de Fuego", "BOLA DE FUEGO")]
        [TestCase("Ñandú Árbol", "NANDU ARBOL")]
        [TestCase("hostile_slash", "HOSTILE_SLASH")]
        [TestCase("Roberto ☺", "ROBERTO")]
        [TestCase("  gatita  ", "GATITA")]
        public void Fold_MakesAnyNameSpellable(string input, string expected)
        {
            string folded = DebugHudText.Fold(input);
            Assert.AreEqual(expected, folded);
            Assert.IsTrue(HudPixelFont.CanSpell(folded, HudPixelFont.Glyphs(HudFontFace.Small)));
        }

        [Test]
        public void TheNumbers_ReadAsAToolShouldPrintThem()
        {
            Assert.AreEqual("9.8", DebugHudText.Ms(9.84f));
            Assert.AreEqual("143", DebugHudText.Ms(143.2f));
            Assert.AreEqual("0:42", DebugHudText.Clock(42f));
            Assert.AreEqual("1:02:03", DebugHudText.Clock(3723f));
            Assert.AreEqual("512B", DebugHudText.Bytes(512));
            Assert.AreEqual("1.5K", DebugHudText.Bytes(1536));
            Assert.AreEqual("812M", DebugHudText.Bytes(812L * 1024 * 1024));
            Assert.AreEqual("CHASE", DebugHudText.FsmState("ChaseState"));
        }

        [Test]
        public void EveryFixedWord_IsSpanish_AndSpellableInThePixelFace()
        {
            var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);
            var words = new List<string>
            {
                DebugHudText.Performance, DebugHudText.Player, DebugHudText.Combat, DebugHudText.Nearby,
                DebugHudText.Ready, DebugHudText.NoMana, DebugHudText.Locked, DebugHudText.Dashing,
                DebugHudText.Waiting, DebugHudText.NobodyNear, DebugHudText.NoHitches, DebugHudText.Copy,
                DebugHudText.Copied, DebugHudText.Invincible, DebugHudText.Stance(true), DebugHudText.Stance(false),
                "P50", "P95", "P99", "MAX", "CPU", "GPU", "LOTE", "SETP", "GC/M", "ASIG", "MEM", "ERR",
                "ZONA", "POS", "VEL", "VIDA", "MANA", "IZQ", "DER", "CEN", "DASH", "TIRON", "FPS", "MS",
                DebugHudText.LevelHint("F1", 2, 3),
            };
            for (int g = 0; g < 3; g++) words.Add(DebugHudText.Grade(g));
            for (int p = 0; p < 4; p++) words.Add(DebugHudText.Phase(p));
            foreach (StatusEffectKind k in System.Enum.GetValues(typeof(StatusEffectKind)))
                words.Add(DebugHudText.Status(k));

            foreach (var w in words)
                Assert.IsTrue(HudPixelFont.CanSpell(w, glyphs), "'" + w + "' has a letter the pixel face cannot draw");
            foreach (var english in new[] { "PERFORMANCE", "NEARBY", "READY", "WAITING" })
                CollectionAssert.DoesNotContain(words, english, "the old panel spoke English in a Spanish game");
        }

        // -- CERCA -----------------------------------------------------------------------------------------

        [Test]
        public void Nearby_IsTheNearestFirst_AndCountsEveryoneInRange()
        {
            var made = new List<GameObject>();
            try
            {
                float[] distances = { 5f, 1f, 20f, 3f, 2f };
                string[] names = { "Hostile5", "Neutral1", "Hostile20", "Ally3", "Hostile2" };
                for (int i = 0; i < distances.Length; i++)
                {
                    var go = new GameObject(names[i]);
                    go.transform.position = new Vector3(distances[i], 0f, 0f);
                    made.Add(go);
                }
                var into = new List<DebugNearbyEntry>();
                DebugHudNearby.Collect(made, Vector2.zero, 15f, 3, Classify, into, out int h, out int n, out int a);

                Assert.AreEqual(3, into.Count);
                Assert.AreEqual("Neutral1", into[0].Go.name, "nearest first, not registration order");
                Assert.AreEqual("Hostile2", into[1].Go.name);
                Assert.AreEqual("Ally3", into[2].Go.name);
                Assert.AreEqual(2, h, "Hostile5 and Hostile2 — Hostile20 is out of range");
                Assert.AreEqual(1, n);
                Assert.AreEqual(1, a);

                DebugHudNearby.Collect(made, Vector2.zero, 15f, 0, Classify, into, out h, out n, out a);
                Assert.AreEqual(0, into.Count);
                Assert.AreEqual(4, h + n + a, "zero rows still counts every side (the report uses it)");
            }
            finally
            {
                foreach (var go in made) Object.DestroyImmediate(go);
            }
        }

        private static FactionSide Classify(GameObject go) =>
            go.name.StartsWith("Neutral") ? FactionSide.Neutral
            : go.name.StartsWith("Ally") ? FactionSide.PlayerSide : FactionSide.Hostile;

        [Test]
        public void TheReadouts_UseTheSystemsOwnSeams()
        {
            // H8. The old panel read SpellCaster's internal slots (1-3 empty for the player, and
            // printed "RDY") and painted every registry entry in the monster colour. A correct
            // helper nobody calls is the shape this project records a dozen times, so the
            // production source is read.
            string path = Path.Combine(Application.dataPath, "_Project/Scripts/UI/HUD/Debug/DebugHUD.Readouts.cs");
            string src = File.ReadAllText(path);
            StringAssert.Contains("EntityFaction.SideOf", src);
            StringAssert.Contains("PrimarySpellKeyNow", src);
            StringAssert.Contains("GetBookCooldownRemaining", src);
            StringAssert.DoesNotContain("GetSlotName", src);
            StringAssert.DoesNotContain("CollectionCount(0)", src, "the monitor owns the session count");
        }

        // -- H3: contrast against the panel, never against the world --------------------------------------

        [Test]
        public void EveryTextColour_ReadsAgainstTheSurfaceItSitsOn()
        {
            var s = DebugHudStyle.Active;
            s.ResolveSurfaces(out _, out var panel, out var header, out _, out var text, out var dim);
            Assert.AreEqual(1f, panel.a, "opaque: in linear space a few percent of gap lets far more through");

            var onPanel = new[] { ("text", text), ("dim", dim), ("good", s.good), ("warn", s.warn), ("bad", s.bad),
                                  ("hostile", s.hostile), ("neutral", s.neutral), ("ally", s.ally) };
            foreach (var (name, c) in onPanel)
                Assert.GreaterOrEqual(WorldBarPalette.Contrast(c, panel), 4.5f, name + " on the panel");
            foreach (var (name, c) in new[] { ("title", s.title), ("dim", dim), ("text", text) })
                Assert.GreaterOrEqual(WorldBarPalette.Contrast(c, header), 4.5f, name + " on a header");
        }

        [Test]
        public void TheStateTriad_IsNotTheGamesHealthGreenOrManaBlue()
        {
            // H4: a tool's "good" must not read as the player's health, nor "bad" as anything the
            // game already means by a colour.
            var s = DebugHudStyle.Active;
            var p = PlayerHudStyle.Active;
            Assert.Greater(HueGap(s.good, p.health), 0.08f, "good vs the player's health green");
            Assert.Greater(HueGap(s.good, p.mana), 0.08f, "good vs mana blue");
            Assert.Greater(HueGap(s.warn, p.gold), 0.03f, "warn vs gold (the XP colour sits right beside it)");
        }

        private static float HueGap(Color a, Color b)
        {
            Color.RGBToHSV(a, out float ha, out _, out _);
            Color.RGBToHSV(b, out float hb, out _, out _);
            float d = Mathf.Abs(ha - hb);
            return Mathf.Min(d, 1f - d);
        }
    }
}
