using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.HUD;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The role filter, the player's interface size, and the spirit tint — the three things
    /// the second audit found missing after F1-F3.
    /// </summary>
    [TestFixture]
    public class GrimoireFilterTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private SpellTreeHUD Build()
        {
            var go = new GameObject("SpellTreeHUD");
            _spawned.Add(go);
            var hud = go.AddComponent<SpellTreeHUD>();
            hud.EnsureBuilt();
            return hud;
        }

        private static GameObject Find(MonoBehaviour root, string name)
        {
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt.gameObject;
            return null;
        }

        // ── The filter ──────────────────────────────────────────────────────────

        [Test]
        public void TheFilterRow_HasAChipForEveryRole_PlusOneToTurnItOff()
        {
            var hud = Build();
            var row = Find(hud, "RoleFilter");
            Assert.IsNotNull(row, "SpellRole is authored on all 71 nodes and its own enum says " +
                                  "it exists for this row");

            int chips = 0;
            foreach (var rt in row.GetComponentsInChildren<RectTransform>(true))
                if (rt.name.StartsWith("Chip_")) chips++;

            int roles = System.Enum.GetValues(typeof(SpellRole)).Length;
            Assert.AreEqual(roles + 1, chips,
                "one chip per role, plus the one that clears the filter");
        }

        [Test]
        public void EveryRole_HasItsOwnChip_AndNoneIsMissed()
        {
            var hud = Build();
            var row = Find(hud, "RoleFilter");

            var seen = new HashSet<string>();
            foreach (var rt in row.GetComponentsInChildren<RectTransform>(true))
                if (rt.name.StartsWith("Chip_")) seen.Add(rt.name);

            foreach (SpellRole role in System.Enum.GetValues(typeof(SpellRole)))
                Assert.IsTrue(seen.Contains("Chip_" + role),
                    role + " has no chip — a role nobody can filter for is a tag nobody can use");
            Assert.IsTrue(seen.Contains("Chip_All"));
        }

        [Test]
        public void TheFilterStartsOff()
        {
            Assert.IsNull(Build().RoleFilter,
                "a panel that opens filtered hides content the player never asked to hide");
        }

        [Test]
        public void ClickingTheSameRoleTwice_TurnsTheFilterOff()
        {
            var hud = Build();

            hud.SetRoleFilter(SpellRole.Healing);
            Assert.AreEqual(SpellRole.Healing, hud.RoleFilter);

            hud.SetRoleFilter(SpellRole.Healing);
            Assert.IsNull(hud.RoleFilter,
                "the same chip is both the on and the off switch; a ninth control would say " +
                "what a second click already says");
        }

        [Test]
        public void ChoosingAnotherRole_Replaces_RatherThanAccumulates()
        {
            var hud = Build();
            hud.SetRoleFilter(SpellRole.Damage);
            hud.SetRoleFilter(SpellRole.Mobility);
            Assert.AreEqual(SpellRole.Mobility, hud.RoleFilter);
        }

        [Test]
        public void TheFilterFadesRatherThanHides()
        {
            // A tree with branches removed does not read as a tree: the chains would end in
            // mid-air and the shape the board exists to show would be a different shape.
            var style = GrimoireStyle.Active;
            Assert.Greater(style.filteredAlpha, 0f,
                "alpha 0 IS hiding, whatever the field is called");
            Assert.Less(style.filteredAlpha, 0.6f,
                "a fade that subtle says nothing was filtered");
        }

        [Test]
        public void TheBoardGivesUpRoomForTheFilter_InsteadOfDrawingOverIt()
        {
            var hud = Build();
            var viewport = Find(hud, "BoardViewport");
            var row = Find(hud, "RoleFilter");
            Assert.IsNotNull(viewport);
            Assert.IsNotNull(row);

            var vp = (RectTransform)viewport.transform;
            var fr = (RectTransform)row.transform;
            // Both are placed bottom-left on whole texels by HudRect, so their own rects are
            // comparable without a layout pass.
            Assert.GreaterOrEqual(vp.anchoredPosition.y, fr.anchoredPosition.y + fr.sizeDelta.y,
                "the constellation starts above the chips rather than behind them");
        }

        // ── R9 + the player's interface size ────────────────────────────────────

        [Test]
        public void TheCanvasFollowsTheSharedContract_AndThePlayersInterfaceSize()
        {
            var hud = Build();
            var scaler = hud.GetComponentInChildren<CanvasScaler>(true);
            Assert.IsNotNull(scaler);

            Assert.AreEqual(HudLayout.ReferenceWidth, scaler.referenceResolution.x);
            Assert.AreEqual(HudLayout.ReferenceHeight, scaler.referenceResolution.y);
            Assert.AreEqual(HudLayout.Match, scaler.matchWidthOrHeight, 0.0001f);

            // ApplyScaler is what carries the size preference. Setting the two constants by
            // hand — which is what this panel did — passes the three lines above and leaves
            // the setting dead, which is why the scale factor is asserted too.
            Assert.AreEqual(HudLayout.InterfaceScale(), scaler.scaleFactor, 0.0001f,
                "the panel must answer the player's interface-size setting like every other " +
                "HUD surface");
        }

        // ── R12 ─────────────────────────────────────────────────────────────────

        [Test]
        public void ThePanelKnowsHowToGoGreyWithTheWorld()
        {
            var hud = Build();
            Assert.AreEqual(0f, hud.SpiritAmount, 0.0001f,
                "a living player's panel is in colour");
            // The transition itself needs a live Health and a clock, neither of which exists
            // in Edit Mode; what is pinned here is that the panel HAS the readout at all —
            // it had none, and a window still in colour while the world is grey is a window
            // that does not know what has happened (R12).
        }

        // ── The icon bake, which was shared at the wrong size ───────────────────

        [Test]
        public void TheCardAsksForItsOwnIconSize()
        {
            // Keyed by node alone, the card reused the socket's bake: a 27 px texture drawn in
            // a 64 px box, a 2.4x magnification. The cache key has to carry the size.
            var t = typeof(SpellTreeHUD);
            var byNode = t.GetMethod("ResolveIcon",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(SpellNode) }, null);
            var bySize = t.GetMethod("ResolveIcon",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(SpellNode), typeof(int) }, null);

            Assert.IsNotNull(byNode, "the board's own convenience overload");
            Assert.IsNotNull(bySize, "and the one that says at what size");
        }

        // ── Hover ───────────────────────────────────────────────────────────────

        [Test]
        public void ANodeAnnouncesBothEnteringAndLeaving()
        {
            // The card followed the pointer until the first click and then stopped, and
            // leaving a node left the card on it. Comparing two nodes became impossible at
            // exactly the moment it starts to matter.
            var node = typeof(GrimoireNodeView);
            Assert.IsNotNull(node.GetEvent("Hovered"));
            Assert.IsNotNull(node.GetEvent("Unhovered"));
        }
    }
}
