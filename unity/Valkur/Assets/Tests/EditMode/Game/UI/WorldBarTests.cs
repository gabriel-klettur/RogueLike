using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.UI
{
    internal static class WorldBarTestHelper
    {
        /// <summary>
        /// Unity does not call <c>Awake</c> on a component added in Edit Mode, so a driver has to
        /// be started by hand. Kept from the original fixture, which needed it for the same reason.
        /// </summary>
        public static void InvokeAwake(Component c) => Invoke(c, "Awake");

        public static void InvokeOnEnable(Component c) => Invoke(c, "OnEnable");

        public static void InvokeLateUpdate(Component c) => Invoke(c, "LateUpdate");

        public static void Invoke(Component c, string method)
        {
            if (c == null) return;
            var m = c.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            m?.Invoke(c, null);
        }

        /// <summary>Every renderer the rig owns, and nothing else on the entity.</summary>
        public static List<SpriteRenderer> BarRenderers(GameObject go)
        {
            var found = new List<SpriteRenderer>();
            var root = go.transform.Find("WorldBars");
            if (root == null) return found;
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                found.Add(sr);
            return found;
        }

        /// <summary>An entity with a measurable body, the way a spawned creature has one.</summary>
        public static GameObject MakeEntity(string name, float bodyWidth = 1.2f, float bodyHeight = 1.86f)
        {
            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            int w = Mathf.Max(1, Mathf.RoundToInt(bodyWidth * 16f));
            int h = Mathf.Max(1, Mathf.RoundToInt(bodyHeight * 16f));
            var tex = new Texture2D(w, h) { hideFlags = HideFlags.HideAndDontSave };
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 16f,
                                      0, SpriteMeshType.FullRect);
            sr.sprite.hideFlags = HideFlags.HideAndDontSave;
            return go;
        }
    }

    /// <summary>
    /// What the rig builds, and the invariants that keep it crisp.
    ///
    /// <para>These replace a fixture that asserted the OLD shape — "the ManaBar child has exactly
    /// three children" — which was a faithful description of three quads of flat colour and is
    /// exactly what had to change.</para>
    /// </summary>
    public class WorldBarRigTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp] public void SetUp() { LogAssert.ignoreFailingMessages = true; }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        // -- Construction -----------------------------------------------------

        [Test]
        public void Ensure_ReturnsTheSameRigTwice()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Entity"));
            var a = WorldBarRig.Ensure(go);
            var b = WorldBarRig.Ensure(go);
            Assert.AreSame(a, b, "Three drivers each building their own rig is the arrangement " +
                                 "this replaced.");
            Assert.AreEqual(1, go.GetComponents<WorldBarRig>().Length);
        }

        [Test]
        public void TheHealthDriver_BuildsARigAndAHealthRow()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Entity"));
            go.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());

            Assert.IsNotNull(go.GetComponent<WorldBarRig>());
            Assert.IsNotNull(go.transform.Find("WorldBars/Shake/Health"),
                "The health row is the one row every creature gets.");
        }

        [Test]
        public void EveryRendererIsOnTheInWorldUiLayer()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Entity"));
            go.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());

            var renderers = WorldBarTestHelper.BarRenderers(go);
            Assert.Greater(renderers.Count, 0);
            foreach (var sr in renderers)
                Assert.AreEqual(SortingConfig.LAYER_UI_WORLD, sr.sortingLayerName, sr.name);
        }

        [Test]
        public void EveryRendererSharesOneMaterial_AndDrawsFromAtMostTwoAtlases()
        {
            // The whole readout over every creature on screen has to stay batchable. A texture per
            // piece is the obvious way to write this and costs a draw call per piece.
            //
            // TWO textures, not one, and that is the design rather than a regression: a skin is
            // deliberately PARTIAL. The painted sheet draws the pieces that carry their own
            // colour; the pieces the palette COLOURS — the fills, the plate, the solid — stay on
            // the generated atlas, because coloured art has nothing left to be tinted with (see
            // WorldBarArt.TintFor). A third texture is what this test is really for: it would mean
            // a piece had come from somewhere neither the generator nor the skin owns.
            var go = Track(WorldBarTestHelper.MakeEntity("Entity"));
            go.AddComponent<Health>().Initialize(100);
            go.AddComponent<Mana>().Initialize(50);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldManaBar>());

            Material material = null;
            var textures = new HashSet<Texture>();
            foreach (var sr in WorldBarTestHelper.BarRenderers(go))
            {
                if (sr.sprite == null) continue;
                material = material ?? sr.sharedMaterial;
                textures.Add(sr.sprite.texture);
                Assert.AreSame(material, sr.sharedMaterial, sr.name + " has its own material");
            }
            Assert.IsNotNull(material);
            Assert.LessOrEqual(textures.Count, 2,
                "A bar draws from the generated atlas and, where an artist has painted one, the " +
                "skin sheet. Anything beyond those two is a draw call nobody asked for.");
            foreach (var t in textures)
                Assert.IsTrue(t == WorldBarArt.GeneratedAtlas || IsTheShippedSkinsTexture(t),
                    "A bar renderer is drawing from '" + t.name + "', which is neither the " +
                    "generated atlas nor the shipped skin's sheet.");
        }

        /// <summary>
        /// Whether a texture is the one the shipped skin's painted pieces come out of. Asked of
        /// the STYLE rather than by name, so renaming the sheet cannot quietly widen the check.
        /// </summary>
        private static bool IsTheShippedSkinsTexture(Texture texture)
        {
            var style = WorldBarStyle.Active;
            if (style == null || style.skin == null) return false;
            var frame = style.skin.Find(WorldBarSheetLayout.FRAME_HEALTH);
            return frame != null && frame.texture == texture;
        }

        [Test]
        public void NoRendererIsSizedByItsScale()
        {
            // The invariant behind the crispness: sizes go through SpriteRenderer.size in Sliced
            // draw mode. A scaled quad resamples its source, which is exactly what the old 4x4
            // white square blown up to 64 x 8 pixels was doing.
            var go = Track(WorldBarTestHelper.MakeEntity("Entity"));
            go.AddComponent<Health>().Initialize(100);
            go.AddComponent<Mana>().Initialize(50);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldManaBar>());

            foreach (var sr in WorldBarTestHelper.BarRenderers(go))
            {
                Assert.AreEqual(Vector3.one, sr.transform.localScale, sr.name + " is scaled");
                Assert.AreNotEqual(SpriteDrawMode.Simple, sr.drawMode,
                    sr.name + " is in Simple draw mode, which cannot be sized without scaling.");
            }
        }

        [Test]
        public void TheBarWidthFollowsTheBodyAndLandsOnTheTexelGrid()
        {
            var small = Track(WorldBarTestHelper.MakeEntity("Small", bodyWidth: 0.9f));
            small.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(small.AddComponent<WorldHealthBar>());

            var big = Track(WorldBarTestHelper.MakeEntity("Big", bodyWidth: 1.8f));
            big.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(big.AddComponent<WorldHealthBar>());

            float a = small.GetComponent<WorldBarRig>().BarWidth;
            float b = big.GetComponent<WorldBarRig>().BarWidth;

            Assert.Greater(b, a, "A wider creature must get a wider bar. One authored 0.8 for " +
                                 "every creature in the game is what this replaced.");
            Assert.IsTrue(WorldBarGeometry.IsOnTexelGrid(a), $"{a} is off the texel grid");
            Assert.IsTrue(WorldBarGeometry.IsOnTexelGrid(b), $"{b} is off the texel grid");
        }

        // -- Rows appear only when the entity has the resource ----------------

        [Test]
        public void NoManaComponent_NoResourceRow()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("NoMana"));
            go.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldManaBar>());

            Assert.IsFalse(go.GetComponent<WorldBarRig>().HasResourceRow);
            Assert.IsNull(go.transform.Find("WorldBars/Shake/Mana"));
        }

        [Test]
        public void ManaComponent_BuildsTheResourceRow()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Caster"));
            go.AddComponent<Health>().Initialize(100);
            go.AddComponent<Mana>().Initialize(50);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldManaBar>());

            Assert.IsTrue(go.GetComponent<WorldBarRig>().HasResourceRow);
            Assert.IsNotNull(go.transform.Find("WorldBars/Shake/Mana"));
        }

        [Test]
        public void DashAbility_BuildsAPipAndNotAThirdBar()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Dasher"));
            go.AddComponent<Health>().Initialize(100);
            go.AddComponent<Rigidbody2D>();
            WorldBarTestHelper.InvokeAwake(go.AddComponent<DashAbility>());
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldDashBar>());

            Assert.IsNotNull(go.transform.Find("WorldBars/Shake/DashPip"),
                "The dash is a charge, so it gets a pip. A third full-width strip identical to " +
                "the mana bar is what it used to be.");
            Assert.IsNull(go.transform.Find("WorldBars/Shake/DashBar"));
        }

        [Test]
        public void NoDashAbility_NoPip()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("NoDash"));
            go.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldDashBar>());
            Assert.IsNull(go.transform.Find("WorldBars/Shake/DashPip"));
        }

        // -- The blow ---------------------------------------------------------

        [Test]
        public void ABlowLeavesAChipAndAHealDoesNot()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Victim"));
            var health = go.AddComponent<Health>();
            health.Initialize(100);
            var bar = go.AddComponent<WorldHealthBar>();
            WorldBarTestHelper.InvokeAwake(bar);
            WorldBarTestHelper.InvokeOnEnable(bar);

            var rig = go.GetComponent<WorldBarRig>();
            Assert.IsFalse(rig.HealthChipActive, "Nothing has happened yet.");

            health.TakeDamage(40);
            Assert.IsTrue(rig.HealthChipActive,
                "A blow leaves the delayed chunk. The old bar could not tell a hit from a heal " +
                "because it only ever subscribed to OnHpChanged.");

            var fresh = Track(WorldBarTestHelper.MakeEntity("Patient"));
            var rig2 = WorldBarRig.Ensure(fresh);
            rig2.SetHealth(50, 100, WorldBarChange.Silent);
            rig2.SetHealth(80, 100, WorldBarChange.Heal);
            Assert.IsFalse(rig2.HealthChipActive,
                "A heal must not leave a chip: there is no lost chunk to show.");
        }

        [Test]
        public void TheFirstReportIsInstant()
        {
            // Otherwise every creature in the world animates its bar up from empty on the frame
            // it spawns.
            var go = Track(WorldBarTestHelper.MakeEntity("Spawned"));
            go.AddComponent<Health>().Initialize(100);
            var bar = go.AddComponent<WorldHealthBar>();
            WorldBarTestHelper.InvokeAwake(bar);
            WorldBarTestHelper.InvokeOnEnable(bar);

            Assert.AreEqual(1f, go.GetComponent<WorldBarRig>().HealthShown, 1e-4f);
        }

        // -- Rank -------------------------------------------------------------

        [Test]
        public void ThePlayerIsRankedFromItsTag()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Player"));
            go.tag = "Player";
            go.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());

            Assert.AreEqual(WorldBarRank.Player, go.GetComponent<WorldBarRig>().Rank);
        }

        [Test]
        public void AnUntaggedCreatureIsOrdinary()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Monster"));
            go.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());
            Assert.AreEqual(WorldBarRank.Normal, go.GetComponent<WorldBarRig>().Rank);
        }

        // -- Sorting ----------------------------------------------------------

        [Test]
        public void TwoCreaturesSortTheirBarsTheWayTheirBodiesSort()
        {
            // The old bars used a constant 200..212 for every entity in the world, so the bar of
            // a monster at the back drew over the bar of one in front.
            var near = Track(WorldBarTestHelper.MakeEntity("Near"));
            near.transform.position = new Vector3(0f, 0f, 0f);
            near.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(near.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeLateUpdate(near.GetComponent<WorldBarRig>());

            var far = Track(WorldBarTestHelper.MakeEntity("Far"));
            far.transform.position = new Vector3(0f, 5f, 0f);
            far.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(far.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeLateUpdate(far.GetComponent<WorldBarRig>());

            int nearOrder = WorldBarTestHelper.BarRenderers(near)[0].sortingOrder;
            int farOrder = WorldBarTestHelper.BarRenderers(far)[0].sortingOrder;
            Assert.Greater(nearOrder, farOrder,
                "The creature lower on screen owns the front. Its bar has to agree.");
        }

        [Test]
        public void TheSortingOrderStaysInsideUnitysSixteenBitWindow()
        {
            var go = Track(WorldBarTestHelper.MakeEntity("Distant"));
            go.transform.position = new Vector3(0f, SortingConfig.MAX_SAFE_WORLD_Y, 0f);
            go.AddComponent<Health>().Initialize(100);
            WorldBarTestHelper.InvokeAwake(go.AddComponent<WorldHealthBar>());
            WorldBarTestHelper.InvokeLateUpdate(go.GetComponent<WorldBarRig>());

            foreach (var sr in WorldBarTestHelper.BarRenderers(go))
            {
                Assert.LessOrEqual(sr.sortingOrder, SortingConfig.MAX_SORT_ORDER);
                Assert.GreaterOrEqual(sr.sortingOrder, SortingConfig.MIN_SORT_ORDER);
            }
        }

        // -- Visibility -------------------------------------------------------

        [Test]
        public void ADeadMonsterHidesItsBar_AndADeadPlayerKeepsIt()
        {
            // The same event, two meanings. A corpse's bar is noise on something about to
            // despawn; a dead PLAYER is a spirit walking to an altar against a time limit, and
            // that is the one moment the readout matters most. The old bars made no distinction —
            // their entire rule was `if (_health.IsDead) show = false` — and the result was
            // reported as the bars vanishing on death.
            var monster = Track(WorldBarTestHelper.MakeEntity("Monster"));
            var mRig = WorldBarRig.Ensure(monster);
            mRig.SetHealth(100, 100, WorldBarChange.Silent);
            mRig.SetHealth(0, 100, WorldBarChange.Damage);
            WorldBarTestHelper.InvokeLateUpdate(mRig);
            Assert.AreEqual(0f, mRig.AlphaTarget, 1e-4f, "a corpse keeps no bar");

            var player = Track(WorldBarTestHelper.MakeEntity("Player"));
            player.tag = "Player";
            var pRig = WorldBarRig.Ensure(player);
            pRig.SetRank(WorldBarRank.Player);
            pRig.SetHealth(100, 100, WorldBarChange.Silent);
            pRig.SetHealth(0, 100, WorldBarChange.Damage);
            WorldBarTestHelper.InvokeLateUpdate(pRig);
            Assert.AreEqual(1f, pRig.AlphaTarget, 1e-4f,
                "The player's readout must survive their own death — they are still on screen, " +
                "still on a clock, and still the thing the player is looking at.");
        }

        [Test]
        public void RevivingBringsThePlayersBarBack()
        {
            var player = Track(WorldBarTestHelper.MakeEntity("Player"));
            player.tag = "Player";
            var rig = WorldBarRig.Ensure(player);
            rig.SetRank(WorldBarRank.Player);
            rig.SetHealth(100, 100, WorldBarChange.Silent);
            rig.SetHealth(0, 100, WorldBarChange.Damage);
            rig.SetHealth(100, 100, WorldBarChange.Heal);
            WorldBarTestHelper.InvokeLateUpdate(rig);

            Assert.AreEqual(1f, rig.AlphaTarget, 1e-4f, "a revive restores the readout");
            Assert.IsFalse(rig.HealthChipActive,
                "and it is a heal, so it leaves no chip behind — there is no lost chunk to show.");
        }

        [Test]
        public void DisablingTheDriverPutsTheReadoutAway()
        {
            // UnconsciousState disables the three bar components to put a downed NPC's readout
            // away. With the drawing on a separate object, that only works if the driver says so.
            var go = Track(WorldBarTestHelper.MakeEntity("Downed"));
            go.AddComponent<Health>().Initialize(100);
            var bar = go.AddComponent<WorldHealthBar>();
            WorldBarTestHelper.InvokeAwake(bar);
            WorldBarTestHelper.InvokeOnEnable(bar);

            var rig = go.GetComponent<WorldBarRig>();
            Assert.AreEqual(1f, rig.AlphaTarget, 1e-4f, "It starts on screen.");

            WorldBarTestHelper.Invoke(bar, "OnDisable");
            WorldBarTestHelper.InvokeLateUpdate(rig);
            Assert.AreEqual(0f, rig.AlphaTarget, 1e-4f,
                "A suppressed rig must be heading to invisible, not merely stop updating. " +
                "The TARGET is the decision; Alpha only says how far the fade has got, and in " +
                "Edit Mode Time.deltaTime is 0 so it never gets anywhere.");

            WorldBarTestHelper.InvokeOnEnable(bar);
            WorldBarTestHelper.InvokeLateUpdate(rig);
            Assert.AreEqual(1f, rig.AlphaTarget, 1e-4f,
                "Re-enabling has to bring it back, or an NPC that stands up keeps no readout.");
        }
    }
}
