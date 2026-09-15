using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.Trees
{
    /// <summary>
    /// Pins the rebuilt talents board: the canvas contract it shares with every other HUD
    /// surface, one socket per authored node, the six node states, and the two-click respec.
    ///
    /// <para><b>What this fixture deliberately does NOT assert.</b> uGUI performs no layout in
    /// Edit Mode, Unity calls no <c>Awake</c> on a component added there, and a
    /// <c>CanvasRenderer</c> keeps its construction colour whichever branch the code took — so
    /// an assertion about a drawn colour or a resolved rect passes with the window unreadable.
    /// Everything here is read from the model, from the pure layout, or from a serialized field
    /// that the build wrote directly.</para>
    /// </summary>
    [TestFixture]
    public class SkillTreeHUDTests
    {
        private GameObject _hudGo;
        private SkillTreeHUD _hud;

        [SetUp]
        public void SetUp()
        {
            _hudGo = new GameObject("SkillTreeHUD");
            _hud = _hudGo.AddComponent<SkillTreeHUD>();
            _hud.EnsureBuilt();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static SkillNode MakeNode(string id, int cost = 1, int levelReq = 0, int maxRank = 1,
                                          int row = 0, int column = 0, params SkillNode[] prereqs)
        {
            var n = ScriptableObject.CreateInstance<SkillNode>();
            n.skillId = id;
            n.displayName = id;
            n.pointCost = cost;
            n.levelRequirement = levelReq;
            n.maxRank = maxRank;
            n.row = row;
            n.column = column;
            n.prerequisites = prereqs ?? System.Array.Empty<SkillNode>();
            return n;
        }

        private static SkillTree MakeTree(params SkillNode[] nodes)
        {
            var t = ScriptableObject.CreateInstance<SkillTree>();
            t.displayName = "Test Tree";
            t.EditorSetNodes(nodes);
            return t;
        }

        private static (GameObject go, LearnedSkills skills) MakeLearner(SkillTree tree, int points)
        {
            var go = new GameObject("Player");
            var skills = go.AddComponent<LearnedSkills>();
            skills.SetTree(tree);
            skills.AddPoints(points);
            return (go, skills);
        }

        private SkillNodeView ViewFor(string id)
        {
            foreach (var v in _hud.Views)
                if (v.Node != null && v.Node.skillId == id) return v;
            return null;
        }

        // ── The canvas contract ─────────────────────────────────────────────────

        /// <summary>
        /// The three lines every HUD canvas shares. This panel shipped on Unity's default
        /// 800x600 with match 0, which is a 2.0 scale factor at 1600 wide against everything
        /// else's 1.0 — the defect <see cref="HudLayout"/> exists to stop, measured live on all
        /// five canvases of the character sheet.
        /// </summary>
        [Test]
        public void TheCanvas_UsesTheSharedHudContract()
        {
            var canvas = _hudGo.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "EnsureBuilt must create the panel's canvas.");

            var scaler = canvas.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, "A HUD canvas without a scaler cannot honour the contract.");
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(HudLayout.ReferenceWidth, scaler.referenceResolution.x, 0.01f);
            Assert.AreEqual(HudLayout.ReferenceHeight, scaler.referenceResolution.y, 0.01f);
            Assert.AreEqual(HudLayout.Match, scaler.matchWidthOrHeight, 0.001f);
        }

        /// <summary>
        /// At 60 the panel drew UNDER the minimap (105) and the music plaque (140): a window
        /// that opens beneath an instrument, which is R13 broken in the worse direction.
        /// </summary>
        [Test]
        public void TheCanvas_SitsInTheCharacterSheetBand()
        {
            var canvas = _hudGo.GetComponentInChildren<Canvas>(true);
            Assert.AreEqual(HudLayout.CharacterSheetSortingOrder, canvas.sortingOrder);
            Assert.Greater(canvas.sortingOrder, HudLayout.GameWindowSortingOrder - 1,
                "The sheet must not open under an ordinary game window either.");
        }

        /// <summary>
        /// The window's drawn size must equal its texel content times the counter-scale its pixel
        /// root carries. It is the one structural statement that catches a lost counter-scale, and
        /// it holds at ANY resolution — which matters because the Game view's size in Edit Mode is
        /// whatever the machine's window happens to be.
        ///
        /// <para>Measured live before this test existed: the panel was sized 908x544 (454 texels
        /// x 2) while its content sat at scale 1, so the whole board drew at half size in the
        /// bottom-left quadrant of its own stone. Every other probe was green — seven views, the
        /// right states, the right sorting order — because none of them multiplied the two.
        /// <c>HudRect.Place</c> ends with <c>localScale = Vector3.one</c>, which is correct for a
        /// texel-space child and fatal for the one rect whose job is the scale.</para>
        /// </summary>
        [Test]
        public void ThePixelRoot_CarriesTheCounterScale()
        {
            var node = MakeNode("n", row: 0, column: 0);
            var tree = MakeTree(node);
            var (go, skills) = MakeLearner(tree, 1);
            try
            {
                _hud.BindLearnedSkills(skills, 1);
                _hud.Open();

                var panel = _hudGo.transform.Find("SkillTreeHUD_Root/SkillsPanel") as RectTransform;
                var pixels = panel.Find("Pixels") as RectTransform;
                Assert.IsNotNull(pixels);

                Assert.Greater(pixels.localScale.x, 0f, "The pixel root must carry a scale.");
                Assert.AreEqual(_hud.WindowTexels.x, pixels.sizeDelta.x, 0.01f,
                    "The pixel root is sized in TEXELS.");
                Assert.AreEqual(pixels.sizeDelta.x * pixels.localScale.x, panel.sizeDelta.x, 0.01f,
                    "The drawn panel must be exactly its texel content times the counter-scale.");
                Assert.AreEqual(pixels.sizeDelta.y * pixels.localScale.y, panel.sizeDelta.y, 0.01f);
            }
            finally { Cleanup(go, tree, node); }
        }

        /// <summary>
        /// No pixel label may be wider than the rect it was given.
        ///
        /// <para>This is the EditMode form of the defect the whole window was rebuilt to remove.
        /// uGUI lays nothing out here, but <c>HudPixelText.InkWidth</c> is measured from the
        /// FONT — glyph widths plus tracking — so it is exact without a layout pass, and it is
        /// the one thing a structural probe CAN see about text. It caught the card's "SIGUIENTE"
        /// running into its value column, which on screen read as "SIGUIENTE+36".</para>
        /// </summary>
        [Test]
        public void NoPixelLabel_OverflowsItsRect()
        {
            var node = MakeNode("stoneflesh", cost: 1, maxRank: 5, row: 0, column: 0);
            var tree = MakeTree(node);
            var (go, skills) = MakeLearner(tree, 3);
            try
            {
                _hud.BindLearnedSkills(skills, 1);
                _hud.Open();
                skills.TryLearn(node, 1, out _);

                int inspected = 0;
                foreach (var label in _hudGo.GetComponentsInChildren<HudPixelText>(true))
                {
                    if (string.IsNullOrEmpty(label.Text)) continue;
                    inspected++;
                    float room = label.rectTransform.sizeDelta.x;
                    Assert.LessOrEqual(label.InkWidth, room,
                        $"'{label.name}' draws \"{label.Text}\" at {label.InkWidth} texels in {room}.");
                }
                Assert.Greater(inspected, 0, "No label carried text — the fixture is vacuous.");
            }
            finally { Cleanup(go, tree, node); }
        }

        [Test]
        public void EnsureBuilt_IsIdempotent()
        {
            int before = _hudGo.GetComponentsInChildren<Canvas>(true).Length;
            _hud.EnsureBuilt();
            _hud.EnsureBuilt();
            Assert.AreEqual(before, _hudGo.GetComponentsInChildren<Canvas>(true).Length,
                "Repeat EnsureBuilt must not stack canvases.");
        }

        /// <summary>
        /// The veil is what makes the window modal and what stops the world animating through
        /// the plate. It has to EAT clicks: the old panel let a click through onto the world
        /// behind it, which in the war stance casts a spell.
        /// </summary>
        [Test]
        public void TheVeil_EatsClicks()
        {
            var veil = _hudGo.transform.Find("SkillTreeHUD_Root/Veil");
            Assert.IsNotNull(veil, "The window must draw a veil behind itself.");
            var img = veil.GetComponent<Image>();
            Assert.IsNotNull(img);
            Assert.IsTrue(img.raycastTarget,
                "A click that misses the board must not reach the world behind the window.");
        }

        // ── The board ───────────────────────────────────────────────────────────

        [Test]
        public void EveryAuthoredNode_GetsASocket()
        {
            var root = MakeNode("root", maxRank: 2, row: 0, column: 0);
            var mid = MakeNode("mid", row: 1, column: 0, prereqs: root);
            var side = MakeNode("side", row: 0, column: 1);
            var tree = MakeTree(root, mid, side);
            var (go, skills) = MakeLearner(tree, 3);
            try
            {
                _hud.BindLearnedSkills(skills, 1);
                _hud.Open();

                Assert.AreEqual(3, _hud.Views.Count,
                    "One socket per authored node — no scrolling, no paging, no truncation.");
                Assert.IsNotNull(ViewFor("root"));
                Assert.IsNotNull(ViewFor("mid"));
                Assert.IsNotNull(ViewFor("side"));
            }
            finally { Cleanup(go, tree, root, mid, side); }
        }

        /// <summary>
        /// The six states, each of which the old panel expressed as "has a button" or "has no
        /// button". A locked node and an unaffordable one looked identical.
        /// </summary>
        [Test]
        public void NodeStates_SeparateLockedFromUnaffordableFromBought()
        {
            var root = MakeNode("root", cost: 1, maxRank: 2, row: 0, column: 0);
            var gated = MakeNode("gated", cost: 1, levelReq: 9, row: 0, column: 1);
            var behind = MakeNode("behind", cost: 1, row: 1, column: 0, prereqs: root);
            var dear = MakeNode("dear", cost: 5, row: 0, column: 2);
            var tree = MakeTree(root, gated, behind, dear);
            var (go, skills) = MakeLearner(tree, 1);
            try
            {
                _hud.BindLearnedSkills(skills, 1);
                _hud.Open();

                Assert.AreEqual(SkillNodeState.Available, ViewFor("root").State);
                Assert.AreEqual(SkillNodeState.LockedByLevel, ViewFor("gated").State);
                Assert.AreEqual(SkillNodeState.LockedByPrerequisite, ViewFor("behind").State);
                Assert.AreEqual(SkillNodeState.LockedByPoints, ViewFor("dear").State);
            }
            finally { Cleanup(go, tree, root, gated, behind, dear); }
        }

        [Test]
        public void BuyingARank_MovesTheNodeThroughPartialToMaxed()
        {
            var node = MakeNode("ranked", cost: 1, maxRank: 2, row: 0, column: 0);
            var tree = MakeTree(node);
            var (go, skills) = MakeLearner(tree, 2);
            try
            {
                _hud.BindLearnedSkills(skills, 1);
                _hud.Open();
                Assert.AreEqual(SkillNodeState.Available, ViewFor("ranked").State);

                skills.TryLearn(node, 1, out _);
                Assert.AreEqual(SkillNodeState.Partial, ViewFor("ranked").State,
                    "A node with ranks left must not read as finished.");

                skills.TryLearn(node, 1, out _);
                Assert.AreEqual(SkillNodeState.Maxed, ViewFor("ranked").State);
            }
            finally { Cleanup(go, tree, node); }
        }

        /// <summary>
        /// The headline defect: the board is repainted by a point ARRIVING, not only by one
        /// being spent. Levelling up with the window open used to change nothing on screen.
        /// </summary>
        [Test]
        public void APointArriving_RepaintsTheBoard()
        {
            var node = MakeNode("dear", cost: 2, row: 0, column: 0);
            var tree = MakeTree(node);
            var (go, skills) = MakeLearner(tree, 0);
            try
            {
                _hud.BindLearnedSkills(skills, 1);
                _hud.Open();
                Assert.AreEqual(SkillNodeState.LockedByPoints, ViewFor("dear").State);

                skills.AddPoints(2);   // a level, or a quest reward

                Assert.AreEqual(SkillNodeState.Available, ViewFor("dear").State,
                    "AddPoints must repaint: the panel is blind to its own currency otherwise.");
            }
            finally { Cleanup(go, tree, node); }
        }

        [Test]
        public void Rebind_SwapsTheBoard()
        {
            var a = MakeNode("alpha", row: 0, column: 0);
            var treeA = MakeTree(a);
            var (goA, skillsA) = MakeLearner(treeA, 1);

            var b = MakeNode("beta", row: 0, column: 0);
            var treeB = MakeTree(b);
            var (goB, skillsB) = MakeLearner(treeB, 1);
            try
            {
                _hud.BindLearnedSkills(skillsA, 1);
                _hud.Open();
                Assert.IsNotNull(ViewFor("alpha"));

                _hud.BindLearnedSkills(skillsB, 1);
                Assert.IsNull(ViewFor("alpha"), "Rebinding must swap the tree, not stack both.");
                Assert.IsNotNull(ViewFor("beta"));
            }
            finally
            {
                Cleanup(goA, treeA, a);
                Cleanup(goB, treeB, b);
            }
        }

        /// <summary>
        /// An open modal window owns Escape, or two readers act on one press — the sheet closes
        /// AND the General Editor opens behind it.
        ///
        /// <para>The release is asserted for the NEXT frame, not this one. <c>EscapeOwnership</c>
        /// deliberately keeps a claim in force for the REST of the frame it was released on,
        /// because Update order between an overlay and the launcher is undefined; and Edit Mode
        /// cannot advance <c>Time.frameCount</c>, which is exactly why the API offers the
        /// frame-explicit form. Asserting <c>IsClaimed</c> here fails a correct implementation.</para>
        /// </summary>
        [Test]
        public void Open_ClaimsEscape_AndCloseReleasesIt()
        {
            Valkur.Core.Input.EscapeOwnership.ResetForTests();
            _hud.Open();
            Assert.IsTrue(Valkur.Core.Input.EscapeOwnership.IsClaimed);
            Assert.AreEqual(1, Valkur.Core.Input.EscapeOwnership.OwnerCount);

            _hud.Close();
            Assert.AreEqual(0, Valkur.Core.Input.EscapeOwnership.OwnerCount,
                "Closing must drop the claim itself.");
            Assert.IsFalse(Valkur.Core.Input.EscapeOwnership.IsClaimedOn(Time.frameCount + 1),
                "And it must not still be held on the next frame.");
        }

        // ── Respec ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Two clicks, and the first one only arms. A respec is the single irreversible button
        /// in the window, and it had no UI at all before this — only a console command.
        /// </summary>
        [Test]
        public void Respec_NeedsTwoClicks()
        {
            var node = MakeNode("bought", cost: 1, row: 0, column: 0);
            var tree = MakeTree(node);
            var (go, skills) = MakeLearner(tree, 2);
            try
            {
                _hud.BindLearnedSkills(skills, 1);
                _hud.Open();
                skills.TryLearn(node, 1, out _);
                Assert.AreEqual(1, skills.SpentPoints);

                _hud.ClickRespecForTests();
                Assert.IsTrue(_hud.RespecArmed, "The first click arms, it does not fire.");
                Assert.AreEqual(1, skills.SpentPoints, "Nothing may be refunded on one click.");

                _hud.ClickRespecForTests();
                Assert.IsFalse(_hud.RespecArmed);
                Assert.AreEqual(0, skills.SpentPoints);
                Assert.AreEqual(2, skills.AvailablePoints);
            }
            finally { Cleanup(go, tree, node); }
        }

        /// <summary>Arming a respec on a character who has spent nothing is arming a no-op.</summary>
        [Test]
        public void Respec_DoesNotArmWithNothingSpent()
        {
            var node = MakeNode("unbought", row: 0, column: 0);
            var tree = MakeTree(node);
            var (go, skills) = MakeLearner(tree, 2);
            try
            {
                _hud.BindLearnedSkills(skills, 1);
                _hud.Open();
                _hud.ClickRespecForTests();
                Assert.IsFalse(_hud.RespecArmed);
            }
            finally { Cleanup(go, tree, node); }
        }

        private static void Cleanup(GameObject go, SkillTree tree, params SkillNode[] nodes)
        {
            if (go != null) Object.DestroyImmediate(go);
            if (tree != null) Object.DestroyImmediate(tree);
            foreach (var n in nodes) if (n != null) Object.DestroyImmediate(n);
        }
    }
}
