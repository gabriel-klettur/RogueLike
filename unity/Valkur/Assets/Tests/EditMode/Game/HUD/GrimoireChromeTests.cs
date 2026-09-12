using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The grimoire window's chrome and the character sheet's strip
    /// (<c>.github/GRIMOIRE_BEAUTY_AUDIT_2026-09-12.md</c>, phases F0-F3): the canvas
    /// contract, the drawing bands, the strip's fit, and the three columns of the window.
    ///
    /// <para>What this fixture can and cannot see matters. <b>uGUI performs no layout in Edit
    /// Mode</b>, so reading a rect back returns whatever was written to it and never what a
    /// layout pass would have made of it — every structural probe of the shipped Controls
    /// editor was green while its window was unreadable. So the geometry is pinned as
    /// ARITHMETIC (the strip's fit is a pure function) and the rest as STRUCTURE (which
    /// components exist, what the canvas is configured with).</para>
    /// </summary>
    [TestFixture]
    public class GrimoireChromeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private T Panel<T>() where T : MonoBehaviour
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private static GameObject Find(MonoBehaviour root, string name)
        {
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt.gameObject;
            return null;
        }

        private static CanvasScaler ScalerOf(MonoBehaviour panel)
        {
            var scaler = panel.GetComponentInChildren<CanvasScaler>(true);
            Assert.IsNotNull(scaler, "every HUD canvas scales");
            return scaler;
        }

        private static Canvas CanvasOf(MonoBehaviour panel)
        {
            var canvas = panel.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas);
            return canvas;
        }

        // ── R9: the canvas contract ─────────────────────────────────────────────

        [Test]
        public void TheGrimoire_ScalesAgainstTheHudReference()
        {
            var hud = Panel<SpellTreeHUD>();
            hud.EnsureBuilt();

            var scaler = ScalerOf(hud);
            Assert.AreEqual(HudLayout.ReferenceWidth, scaler.referenceResolution.x);
            Assert.AreEqual(HudLayout.ReferenceHeight, scaler.referenceResolution.y);
            Assert.AreEqual(HudLayout.Match, scaler.matchWidthOrHeight, 0.0001f,
                "match 0 is what made the quest log's font explode at 1600 wide");
        }

        [Test]
        public void EverySheetPanel_ScalesAgainstTheSameReference()
        {
            // The panels sharing one window: if they do not share a reference they cannot
            // share the tab strip that sits on top of them.
            //
            // SkillTreeHUD is deliberately absent. It is being rebuilt and moved to
            // Valkur.UI.HUD by another session, and a fixture that names a type mid-move
            // fails to COMPILE — which takes every other test in the assembly with it. Its
            // own fixture owns the same three assertions for that panel; this one owns the
            // three that are still here.
            var grimoire = Panel<SpellTreeHUD>();      grimoire.EnsureBuilt();
            var stats    = Panel<CharacterStatsHUD>(); stats.EnsureBuilt();
            var records  = Panel<StatisticsHUD>();     records.EnsureBuilt();

            foreach (var panel in new MonoBehaviour[] { grimoire, stats, records })
            {
                var scaler = ScalerOf(panel);
                Assert.AreEqual(HudLayout.ReferenceWidth, scaler.referenceResolution.x,
                    panel.GetType().Name);
                Assert.AreEqual(HudLayout.ReferenceHeight, scaler.referenceResolution.y,
                    panel.GetType().Name);
                Assert.AreEqual(HudLayout.Match, scaler.matchWidthOrHeight, 0.0001f,
                    panel.GetType().Name);
            }
        }

        [Test]
        public void EverySheetPanel_DrawsInItsDeclaredBand_AboveTheInstruments()
        {
            var grimoire = Panel<SpellTreeHUD>();      grimoire.EnsureBuilt();
            var stats    = Panel<CharacterStatsHUD>(); stats.EnsureBuilt();
            var records  = Panel<StatisticsHUD>();     records.EnsureBuilt();

            foreach (var panel in new MonoBehaviour[] { grimoire, stats, records })
            {
                Assert.AreEqual(HudLayout.CharacterSheetSortingOrder, CanvasOf(panel).sortingOrder,
                    panel.GetType().Name + " shipped at 60 or 70, under the minimap and the music panel");
            }
        }

        [Test]
        public void TheSheetsBand_ClearsEveryInstrument_AndStaysUnderTheShop()
        {
            Assert.Greater(HudLayout.CharacterSheetSortingOrder, HudLayout.MusicSortingOrder);
            Assert.Greater(HudLayout.CharacterSheetSortingOrder, HudLayout.ToolSortingOrder);
            Assert.Greater(HudLayout.CharacterSheetChromeSortingOrder,
                           HudLayout.CharacterSheetSortingOrder,
                           "the strip is the only way out of a panel; nothing the sheet draws may cover it");
            Assert.Less(HudLayout.CharacterSheetChromeSortingOrder, 220,
                        "the shop is opened from inside a conversation and has to stay reachable");
        }

        // ── D3: the tab strip fits, as arithmetic ───────────────────────────────

        [Test]
        public void TheTabStrip_CanAlwaysFitItsTabs_AtTheReferenceResolution()
        {
            var sheet = Panel<CharacterSheetController>();
            int count = Mathf.Max(sheet.TabCount, 4);

            float have = CharacterSheetController.StripWidthAtReference();
            float need = CharacterSheetController.TabsMinimumWidth(count);

            Assert.LessOrEqual(need, have,
                "measured on the shipped panel: 548 units of tabs in a 480-unit strip, so the " +
                "last tab was drawn 136 px outside it and the close button landed on its label");
        }

        [Test]
        public void TheTabStrip_FitsItsTabsAtTheirFullWidth_Today()
        {
            var sheet = Panel<CharacterSheetController>();
            int count = Mathf.Max(sheet.TabCount, 4);

            Assert.LessOrEqual(CharacterSheetController.TabsPreferredWidth(count),
                               CharacterSheetController.StripWidthAtReference(),
                               "four tabs should not need to be squeezed at all");
        }

        [Test]
        public void TheStripReservesTheCloseButton_SoItCanNeverBeDealtATabsSlot()
        {
            // The reserve is the difference between what N tabs occupy and what the strip
            // is asked for. If it ever drops to zero the X goes back on top of a label.
            float withTabs = CharacterSheetController.TabsPreferredWidth(4);
            float tabsOnly = 4 * 132f + 3 * 4f;
            Assert.Greater(withTabs - tabsOnly, 30f, "the close button plus its insets");
        }

        // ── The board can no longer draw outside itself ────────────────────────

        [Test]
        public void TheConstellation_LivesInAViewportThatClips()
        {
            var hud = Panel<SpellTreeHUD>();
            hud.EnsureBuilt();

            var viewport = Find(hud, "BoardViewport");
            Assert.IsNotNull(viewport,
                "the board is scaled to fit, and a mask is what makes that a guarantee " +
                "rather than a hope");
            Assert.IsNotNull(viewport.GetComponent<RectMask2D>());

            var recess = viewport.GetComponent<Image>();
            Assert.IsNotNull(recess, "a viewport that holds a value is an opaque recess (R3)");
            Assert.AreEqual(1f, recess.color.a, 0.0001f);
        }

        [Test]
        public void TheWindowHasItsThreeColumns()
        {
            var hud = Panel<SpellTreeHUD>();
            hud.EnsureBuilt();

            Assert.IsNotNull(Find(hud, "Rail"), "the school rail");
            Assert.IsNotNull(Find(hud, "BoardViewport"), "the constellation");
            Assert.IsNotNull(Find(hud, "Card"), "the detail card");
        }

        [Test]
        public void TheMoteLayerExists_AndIsEmptyAtRest()
        {
            var hud = Panel<SpellTreeHUD>();
            hud.EnsureBuilt();

            var motes = hud.GetComponentInChildren<HudMoteLayer>(true);
            Assert.IsNotNull(motes, "R8: events are answered with motes, and only events");
            Assert.AreEqual(0, motes.Alive,
                "a panel that sparkles at rest is a panel whose sparkle means nothing");
        }

        [Test]
        public void NoRectIsLeftAtUnitysDefault()
        {
            // The fingerprint of a rect nobody wrote is CENTRED ANCHORS *and* exactly 100x100,
            // and both halves are needed: a deliberate 100x100 badge is ordinary and so is a
            // centred anchor, and only the pair says "this was never configured". It is one of
            // the few layout facts that IS measurable in Edit Mode, where uGUI runs no layout
            // pass — a rect left at the default draws a 100x100 slab in game and every
            // structural assertion about it stays green.
            //
            // The floor matters as much as the check: without it, a panel that failed to build
            // passes by inspecting nothing, which is the vacuous-fixture shape this project has
            // shipped more than once.
            var hud = Panel<SpellTreeHUD>();
            hud.EnsureBuilt();

            int inspected = 0;
            var offenders = new List<string>();

            foreach (var rt in hud.GetComponentsInChildren<RectTransform>(true))
            {
                inspected++;
                bool centred = rt.anchorMin == new Vector2(0.5f, 0.5f)
                            && rt.anchorMax == new Vector2(0.5f, 0.5f);
                bool hundred = Mathf.Approximately(rt.sizeDelta.x, 100f)
                            && Mathf.Approximately(rt.sizeDelta.y, 100f);
                if (centred && hundred) offenders.Add(Path(rt));
            }

            Assert.Greater(inspected, 20,
                "the panel built almost nothing — this assertion would pass on an empty window");
            Assert.IsEmpty(offenders,
                "left at Unity's default: " + string.Join(", ", offenders));
        }

        private static string Path(RectTransform rt)
        {
            var sb = new System.Text.StringBuilder(rt.name);
            var t = rt.parent;
            while (t != null) { sb.Insert(0, t.name + "/"); t = t.parent; }
            return sb.ToString();
        }

        // ── The panel is a surface, not a window onto the world ─────────────────

        [Test]
        public void ThePanelIsOpaque()
        {
            var hud = Panel<SpellTreeHUD>();
            hud.EnsureBuilt();

            foreach (var img in hud.GetComponentsInChildren<Image>(true))
            {
                if (img.name != "Panel") continue;
                Assert.AreEqual(1f, img.color.a, 0.0001f,
                    "HUD_VISUAL_LANGUAGE.md R3: in linear space a 4 % gap lets ~18 % of what is " +
                    "behind it through");
                return;
            }
            Assert.Fail("no Panel image found");
        }

        // ── The reason a node is locked is never swallowed ──────────────────────

        [Test]
        public void NoLabelTruncatesItsText()
        {
            var hud = Panel<SpellTreeHUD>();
            hud.EnsureBuilt();

            foreach (var text in hud.GetComponentsInChildren<Text>(true))
            {
                Assert.AreNotEqual(VerticalWrapMode.Truncate, text.verticalOverflow,
                    "'" + text.name + "': the shipped row wrapped its reason to an invisible " +
                    "second line, so 'Need 2 arcane point(s), have' is what the player read");
            }
        }
    }
}
