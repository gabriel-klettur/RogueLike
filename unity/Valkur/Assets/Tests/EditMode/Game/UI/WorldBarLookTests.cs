using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// The colour arithmetic behind a bar: one authored colour becomes four tones, and the tones
    /// have to go the way a pixel artist shades — light toward yellow, shade toward blue — or the
    /// bar is a sticker.
    /// </summary>
    public class WorldBarPaletteTests
    {
        private static readonly Color[] Bases =
        {
            new Color(0.31f, 0.84f, 0.40f, 1f),   // player green
            new Color(0.90f, 0.24f, 0.22f, 1f),   // hostile red - hue 0, the wrap-around case
            new Color(0.30f, 0.52f, 1.00f, 1f),   // mana blue
            new Color(0.94f, 0.27f, 0.20f, 0.5f), // low red, half faded
            new Color(0.98f, 0.76f, 0.28f, 1f),   // the dash gold
        };

        private const float WARM = 1f / 6f, COOL = 2f / 3f;

        private static float HueDistance(float a, float b)
        {
            float d = Mathf.Abs(Mathf.Repeat(a - b, 1f));
            return Mathf.Min(d, 1f - d);
        }

        private static float Hue(Color c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h;
        }

        [Test]
        public void TheRamp_GoesLightToDark_TopToBottom_AndTheEdgeIsTheBrightest()
        {
            foreach (var b in Bases)
            {
                var r = WorldBarPalette.Ramp(b);
                float hi = WorldBarPalette.Luminance(r.Highlight);
                float body = WorldBarPalette.Luminance(r.Base);
                float lo = WorldBarPalette.Luminance(r.Shadow);
                float edge = WorldBarPalette.Luminance(r.Edge);
                Assert.Greater(hi, body, $"highlight must be lighter than the body for {b}");
                Assert.Greater(body, lo, $"shadow must be darker than the body for {b}");
                Assert.GreaterOrEqual(edge, hi - 1e-4f,
                    $"the leading edge is the brightest tone, or the fill has no front, for {b}");
            }
        }

        [Test]
        public void TheRamp_ShiftsHue_WarmUpAndCoolDown()
        {
            foreach (var b in Bases)
            {
                Color.RGBToHSV(b, out float h, out float s, out _);
                if (s < 0.05f) continue;
                var r = WorldBarPalette.Ramp(b);
                float baseToWarm = HueDistance(h, WARM), baseToCool = HueDistance(h, COOL);
                Assert.LessOrEqual(HueDistance(Hue(r.Highlight), WARM), baseToWarm + 1e-4f,
                    $"the highlight leans toward yellow for {b}");
                Assert.LessOrEqual(HueDistance(Hue(r.Shadow), COOL), baseToCool + 1e-4f,
                    $"the shadow leans toward blue for {b} - through magenta for a red, never the long way round through green");
            }
        }

        [Test]
        public void TheRamp_KeepsTheAuthoredAlpha_OnEveryTone()
        {
            var r = WorldBarPalette.Ramp(new Color(0.9f, 0.2f, 0.2f, 0.37f));
            Assert.AreEqual(0.37f, r.Highlight.a, 1e-5f);
            Assert.AreEqual(0.37f, r.Base.a, 1e-5f);
            Assert.AreEqual(0.37f, r.Shadow.a, 1e-5f);
            Assert.AreEqual(0.37f, r.Edge.a, 1e-5f,
                "alpha on a bar is the fade, never a shading decision");
        }

        [Test]
        public void Toward_TakesTheShorterArc()
        {
            // Red (0) toward blue (2/3) must go DOWN through magenta: the result sits just below 1.
            float h = WorldBarPalette.Toward(0f, COOL, 0.14f);
            Assert.Greater(h, 0.9f, "red toward blue goes through magenta, not through green");
            // Red toward yellow goes up.
            Assert.Greater(WorldBarPalette.Toward(0f, WARM, 0.18f), 0f);
            Assert.Less(WorldBarPalette.Toward(0f, WARM, 0.18f), WARM);
        }
    }

    /// <summary>
    /// Every fill has to stand at least 3:1 against the plate it sits in, measured OVER the
    /// ground the plate is translucent to — which is what the style's own tooltip promises.
    ///
    /// <para>Measured before the plate existed: the hostile red stood at 1.09:1 against
    /// cobblestone, i.e. readable by hue alone and invisible to a colour-blind player.</para>
    /// </summary>
    public class WorldBarContrastTests
    {
        // Grounds the bars are drawn over in the shipped world. The pale ones are the hard case:
        // a translucent plate is lightest over them.
        private static readonly (string name, Color colour)[] Grounds =
        {
            ("cobblestone", new Color(0.55f, 0.53f, 0.50f)),
            ("pale sand",   new Color(0.80f, 0.74f, 0.60f)),
            ("foliage",     new Color(0.16f, 0.30f, 0.12f)),
            ("dark stone",  new Color(0.22f, 0.22f, 0.24f)),
        };

        private static IEnumerable<(string, Color)> Fills(WorldBarStyle s)
        {
            yield return ("healthPlayer", s.healthPlayer);
            yield return ("healthAlly", s.healthAlly);
            yield return ("healthHostile", s.healthHostile);
            yield return ("healthLow", s.healthLow);
            yield return ("healthLowPlayer", s.healthLowPlayer);
            yield return ("mana", s.mana);
            yield return ("dashReady", s.dashReady);
            yield return ("energy", s.EnergyFillColour);
        }

        [Test]
        public void EveryFill_Stands3To1_AgainstThePlate_OverEveryGround()
        {
            var style = WorldBarStyle.Active;
            Assert.IsTrue(style.drawPlate, "the plate is what the contrast is measured against");
            foreach (var (fname, fill) in Fills(style))
                foreach (var (gname, ground) in Grounds)
                {
                    var behind = WorldBarPalette.Over(style.plate, ground);
                    float c = WorldBarPalette.Contrast(fill, behind);
                    Assert.GreaterOrEqual(c, 3f,
                        $"{fname} over the plate on {gname} measures {c:F2}:1; the readout has to be " +
                        "legible by luminance, not only by hue");
                }
        }

        [Test]
        public void ThePlate_IsWhatMakesTheHostileRedReadable()
        {
            var style = WorldBarStyle.Active;
            var cobble = Grounds[0].colour;
            float bare = WorldBarPalette.Contrast(style.healthHostile, cobble);
            float plated = WorldBarPalette.Contrast(style.healthHostile, WorldBarPalette.Over(style.plate, cobble));
            Assert.Less(bare, 2f, "on bare cobblestone the red has no luminance contrast - that is the defect");
            Assert.Greater(plated, bare * 2f, "the plate must at least double it, or it is not earning its pixels");
        }

        [Test]
        public void TheOutline_IsNearBlack_ForEveryRank()
        {
            // The outline separates the bar from the ground and cannot also carry rank; rank
            // lives in the caps and the halo.
            var style = WorldBarStyle.Active;
            Assert.Less(WorldBarPalette.Luminance(style.outline), 0.02f);
            foreach (WorldBarRank rank in System.Enum.GetValues(typeof(WorldBarRank)))
            {
                Assert.Greater(WorldBarPalette.Contrast(style.CapFor(rank), style.outline), 4f,
                    $"the {rank} caps must read against the outline they sit in");
            }
        }

        [Test]
        public void ThePlayersLowColour_IsNotInTheBrassFamily()
        {
            // Amber was tried first and read as gold beside the brass caps and the gold dash stone.
            var style = WorldBarStyle.Active;
            Color.RGBToHSV(style.healthLowPlayer, out float low, out float lowS, out _);
            Color.RGBToHSV(style.capPlayer, out float cap, out _, out _);
            Color.RGBToHSV(style.dashReady, out float gold, out _, out _);
            Assert.Greater(lowS, 0.5f, "a warning is saturated");
            Assert.Greater(HueDistance(low, cap), 0.06f, "the low colour must not share the caps' hue");
            Assert.Greater(HueDistance(low, gold), 0.06f, "nor the dash stone's");
        }

        [Test]
        public void LowFor_WarnsThePlayerAndTheAlly_AndLeavesAHostileAlone()
        {
            var style = WorldBarStyle.Active;
            Assert.AreEqual(style.healthLowPlayer, style.LowFor(WorldBarRank.Player));
            Assert.AreEqual(style.healthLow, style.LowFor(WorldBarRank.Ally));
            foreach (var rank in new[] { WorldBarRank.Normal, WorldBarRank.Elite, WorldBarRank.Boss })
                Assert.AreEqual(style.HealthFor(rank), style.LowFor(rank),
                    $"a {rank} near death is not a warning the player needs; its fill stays its own colour");
        }

        private static float HueDistance(float a, float b)
        {
            float d = Mathf.Abs(Mathf.Repeat(a - b, 1f));
            return Mathf.Min(d, 1f - d);
        }
    }

    /// <summary>
    /// The player's readout has to survive the player dying.
    ///
    /// <para>Measured before: after the first death the low-health amber rendered as dark olive
    /// for the rest of the session, because the spirit look wrote a <c>_Color</c> into every
    /// renderer's property block under the player and the revive put a frozen colour back into
    /// it; and during the spirit walk the bars were black silhouettes.</para>
    /// </summary>
    public class WorldBarSpiritTests
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

        private (SpriteRenderer body, SpriteRenderer readout, PlayerSpiritVisuals visuals) MakePlayer()
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            var body = go.AddComponent<SpriteRenderer>();
            body.color = Color.white;

            var bars = new GameObject("WorldBars");
            bars.transform.SetParent(go.transform, false);
            bars.AddComponent<SpiritTintExempt>();
            var readoutGo = new GameObject("Fill");
            readoutGo.transform.SetParent(bars.transform, false);
            var readout = readoutGo.AddComponent<SpriteRenderer>();
            readout.color = new Color(0.9f, 0.2f, 0.2f, 1f);

            var visuals = go.AddComponent<PlayerSpiritVisuals>();
            WorldBarTestHelper.InvokeAwake(visuals);
            return (body, readout, visuals);
        }

        private static void ApplyTint(PlayerSpiritVisuals v, float k)
        {
            var m = typeof(PlayerSpiritVisuals).GetMethod("ApplyTint", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "PlayerSpiritVisuals.ApplyTint(float) is what the Update drives");
            m.Invoke(v, new object[] { k });
        }

        [Test]
        public void TheSpiritLook_TintsTheBody_AndLeavesTheReadoutAlone()
        {
            var (body, readout, visuals) = MakePlayer();
            visuals.Activate();
            ApplyTint(visuals, 1f);

            Assert.AreNotEqual(Color.white, body.color, "the body is tinted to the silhouette");
            Assert.AreEqual(new Color(0.9f, 0.2f, 0.2f, 1f), readout.color,
                "a renderer under a SpiritTintExempt subtree owns its own colours");
            var mpb = new MaterialPropertyBlock();
            readout.GetPropertyBlock(mpb);
            Assert.IsFalse(mpb.HasColor("_Color"), "and no _Color is written into its property block");
        }

        [Test]
        public void Reviving_RestoresTheBody_WithoutFreezingAColourInItsPropertyBlock()
        {
            var (body, _, visuals) = MakePlayer();
            visuals.Activate();
            ApplyTint(visuals, 1f);
            visuals.Deactivate();

            Assert.AreEqual(Color.white, body.color);
            var mpb = new MaterialPropertyBlock();
            body.GetPropertyBlock(mpb);
            Assert.IsFalse(mpb.HasColor("_Color"),
                "the revive puts the ORIGINAL block back. Writing the original colour into _Color " +
                "instead left a colour frozen in the block that the shader multiplied into every " +
                "colour the renderer took afterwards");
        }

        [Test]
        public void TheRig_MarksItsRoot_Exempt()
        {
            var go = WorldBarTestHelper.MakeEntity("Player");
            _spawned.Add(go);
            var rig = WorldBarRig.Ensure(go);
            Assert.IsNotNull(rig);
            var root = go.transform.Find("WorldBars");
            Assert.IsNotNull(root);
            Assert.IsNotNull(root.GetComponent<SpiritTintExempt>(),
                "the bars are kept on screen while the player is a spirit; tinting them black kept " +
                "them on screen as unreadable shapes");
        }

        [Test]
        public void TheDesaturateShader_HonoursTheRenderersOwnTint()
        {
            // The spirit world swaps every renderer onto Valkur/SpriteDesaturate. The generated bar
            // art is a white texture whose whole colour is SpriteRenderer.color, so a luminance
            // that ignored the vertex colour drew the bars over a spirit as white slabs.
            string path = Path.Combine(Application.dataPath, "_Project/Shaders/SpriteDesaturate.shader");
            Assert.IsTrue(File.Exists(path), path);
            string src = File.ReadAllText(path);
            StringAssert.Contains("tex.rgb * IN.color.rgb", src,
                "the luminance must be taken of the texel TIMES the vertex colour");
        }
    }

    /// <summary>
    /// The stack as one instrument: joined rows, a pip standing on the shared outline, status
    /// tiles of one size above it, and the draw order that keeps the low-health ring whole.
    /// </summary>
    public class WorldBarStackTests
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

        // The manager keys its table by the effect's TYPE, so one test class per kind: three
        // instances of one class would replace each other and the row would show a single icon.
        private abstract class TestStatus : StatusEffect
        {
            protected TestStatus(float duration) : base(duration) { }
            public override void Tick(StatusEffectManager target) { }
        }

        private sealed class TestBurn : TestStatus
        {
            public TestBurn(float d) : base(d) { }
            public override StatusEffectKind Kind => StatusEffectKind.Burn;
        }

        private sealed class TestPoison : TestStatus
        {
            public TestPoison(float d) : base(d) { }
            public override StatusEffectKind Kind => StatusEffectKind.Poison;
        }

        private sealed class TestSlow : TestStatus
        {
            public TestSlow(float d) : base(d) { }
            public override StatusEffectKind Kind => StatusEffectKind.Slow;
        }

        private static StatusEffect MakeStatus(StatusEffectKind kind, float duration)
        {
            switch (kind)
            {
                case StatusEffectKind.Poison: return new TestPoison(duration);
                case StatusEffectKind.Slow:   return new TestSlow(duration);
                default:                      return new TestBurn(duration);
            }
        }

        private (GameObject go, WorldBarRig rig) MakeFullRig(params StatusEffectKind[] statuses)
        {
            var go = WorldBarTestHelper.MakeEntity("Player");
            _spawned.Add(go);
            if (statuses.Length > 0)
            {
                var sem = go.AddComponent<StatusEffectManager>();
                foreach (var k in statuses) sem.Apply(MakeStatus(k, 60f));
            }
            var rig = WorldBarRig.Ensure(go);
            rig.SetRank(WorldBarRank.Player);
            rig.SetHealth(200, 200, WorldBarChange.Silent);
            rig.EnableMana(true);
            rig.SetMana(35, 35, WorldBarChange.Silent);
            rig.EnableDash(true);
            rig.SetDashCharge(1f);
            WorldBarTestHelper.InvokeLateUpdate(rig);
            return (go, rig);
        }

        private static SpriteRenderer Part(GameObject go, string path)
        {
            var t = go.transform.Find("WorldBars/Shake/" + path);
            Assert.IsNotNull(t, "missing part " + path);
            return t.GetComponent<SpriteRenderer>();
        }

        private static void SetAlpha(WorldBarRig rig, float alpha, float target)
        {
            var f = typeof(WorldBarRig).GetField("_alpha", BindingFlags.Instance | BindingFlags.NonPublic);
            var ft = typeof(WorldBarRig).GetField("_alphaTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            var push = typeof(WorldBarRig).GetMethod("PushAlpha", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f); Assert.IsNotNull(ft); Assert.IsNotNull(push);
            f.SetValue(rig, alpha);
            ft.SetValue(rig, target);
            push.Invoke(rig, null);
        }

        [Test]
        public void TheHealthRow_DrawsOverTheResourceRowAndThePip()
        {
            // The rows share one outline row and the low-health pulse lives in the health row's
            // outline; drawn underneath, the pulsing ring lost its top edge to the mana row.
            var (go, _) = MakeFullRig();
            int health = Part(go, "Health/Frame").sortingOrder;
            int mana = Part(go, "Mana/Frame").sortingOrder;
            int pip = Part(go, "DashPip/Frame").sortingOrder;
            Assert.Greater(health, mana);
            Assert.Greater(health, pip);
        }

        [Test]
        public void EveryOrderTheRigClaims_FitsInsideItsDeclaredSpan()
        {
            var (go, _) = MakeFullRig(StatusEffectKind.Burn);
            int min = int.MaxValue, max = int.MinValue;
            foreach (var sr in WorldBarTestHelper.BarRenderers(go))
            {
                min = Mathf.Min(min, sr.sortingOrder);
                max = Mathf.Max(max, sr.sortingOrder);
            }
            Assert.Less(max - min, WorldBarRig.SORT_SPAN,
                "a part that claims more orders than its SLOT_COUNT lands on its neighbour's");
        }

        [Test]
        public void EveryOrderTheRigClaims_FitsInsideItsDeclaredSpan_WithEnergyEnabled()
        {
            var (go, rig) = MakeFullRig(StatusEffectKind.Burn);
            rig.EnableEnergy(true);
            rig.SetEnergy(50, 100, WorldBarChange.Silent);
            WorldBarTestHelper.InvokeLateUpdate(rig);
            int min = int.MaxValue, max = int.MinValue;
            foreach (var sr in WorldBarTestHelper.BarRenderers(go))
            {
                min = Mathf.Min(min, sr.sortingOrder);
                max = Mathf.Max(max, sr.sortingOrder);
            }
            Assert.Less(max - min, WorldBarRig.SORT_SPAN,
                "the energy row's own slots must fit the declared span too");
        }

        [Test]
        public void TheEnergyRow_SitsBetweenHealthAndResource_SharingBothOutlines_WithQuarterNotches()
        {
            var (go, rig) = MakeFullRig();
            rig.EnableEnergy(true);
            rig.SetEnergy(70, 100, WorldBarChange.Silent);
            WorldBarTestHelper.InvokeLateUpdate(rig);

            var style = WorldBarStyle.Active;
            var health = go.transform.Find("WorldBars/Shake/Health");
            var energy = go.transform.Find("WorldBars/Shake/Energy");
            var mana = go.transform.Find("WorldBars/Shake/Mana");
            Assert.IsNotNull(energy, "the energy row must exist once enabled");

            Assert.Greater(energy.localPosition.y, health.localPosition.y,
                "energy sits further from the head than health");
            Assert.Less(energy.localPosition.y, mana.localPosition.y,
                "and closer to the head than the resource row");

            if (style.rowGapTexels < 0)
            {
                float healthTop = health.localPosition.y + style.HealthRowHeight * 0.5f;
                float energyBottom = energy.localPosition.y - style.EnergyRowHeight * 0.5f;
                float energyTop = energy.localPosition.y + style.EnergyRowHeight * 0.5f;
                float manaBottom = mana.localPosition.y - style.ResourceRowHeight * 0.5f;
                Assert.AreEqual(healthTop - WorldBarGeometry.TEXEL, energyBottom, 1e-4f,
                    "energy shares health's outline, the same way the resource row used to alone");
                Assert.AreEqual(energyTop - WorldBarGeometry.TEXEL, manaBottom, 1e-4f,
                    "the resource row shares energy's outline in turn");
            }

            for (int i = 0; i < 3; i++)
                Assert.IsTrue(Part(go, "Energy/Notch" + i).gameObject.activeSelf,
                    "the energy row always carries its quarter notches - shape, not only colour, " +
                    "is what separates it from mana");
            // The mana row is built with withNotches:false, so it has no Notch children at all -
            // not merely inactive ones - which is what actually separates it from energy in shape.
            for (int i = 0; i < 3; i++)
                Assert.IsNull(go.transform.Find("WorldBars/Shake/Mana/Notch" + i),
                    "mana itself still carries none");
        }

        [Test]
        public void TheEnergyRow_IsAttachedToThePlayerOnly()
        {
            // WorldBarRig itself will draw energy for whoever calls EnableEnergy - "player only"
            // is enforced by EntitySetup only ever attaching WorldEnergyBar behind the player tag.
            // Pinned at the source rather than by building a monster and asserting a miss: a
            // monster with no WorldEnergyBar never calls EnableEnergy at all, so there would be
            // nothing on the rig to observe.
            string path = Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Bootstrap/EntitySetup.Visuals.cs");
            Assert.IsTrue(File.Exists(path), path);
            string src = File.ReadAllText(path);
            int idx = src.IndexOf("AddComponent<WorldEnergyBar>()");
            Assert.Greater(idx, 0, "WorldEnergyBar must still be attached somewhere in EntitySetup");
            int start = Mathf.Max(0, idx - 200);
            string before = src.Substring(start, idx - start);
            StringAssert.Contains("CompareTag(\"Player\")", before,
                "the energy row driver must only ever be attached to the player");
        }

        [Test]
        public void JoinedRows_ShareOneOutline_AndTheJointsCloseTheCorners()
        {
            var (go, rig) = MakeFullRig();
            var style = WorldBarStyle.Active;
            var seam = Part(go, "JointSeam");
            var corner = Part(go, "JointCorner");
            if (style.rowGapTexels < 0)
            {
                Assert.IsTrue(seam.gameObject.activeSelf, "joined rows need the seam filled in");
                Assert.AreEqual(rig.BarWidth, seam.size.x, 1e-4f, "the seam runs the whole width");
                Assert.AreEqual(WorldBarGeometry.TEXEL, seam.size.y, 1e-4f);
                Assert.IsTrue(corner.gameObject.activeSelf,
                    "the mana bar's top-right corner meets the pip: without the corner texel there " +
                    "is a hole in the middle of the instrument");
                var mana = go.transform.Find("WorldBars/Shake/Mana");
                var health = go.transform.Find("WorldBars/Shake/Health");
                float healthTop = health.localPosition.y + style.HealthRowHeight * 0.5f;
                float manaBottom = mana.localPosition.y - style.ResourceRowHeight * 0.5f;
                Assert.AreEqual(healthTop - WorldBarGeometry.TEXEL, manaBottom, 1e-4f,
                    "the resource row's bottom outline IS the health row's top outline");
            }
            else
            {
                Assert.IsFalse(seam.gameObject.activeSelf, "separate rows carry no seam");
                Assert.IsFalse(corner.gameObject.activeSelf);
            }
        }

        [Test]
        public void ThePip_StandsOnTheResourceRowsFloor_AndTheStatusRowClearsIt()
        {
            var (go, _) = MakeFullRig(StatusEffectKind.Burn);
            var style = WorldBarStyle.Active;
            var pip = go.transform.Find("WorldBars/Shake/DashPip");
            var mana = go.transform.Find("WorldBars/Shake/Mana");
            var status = go.transform.Find("WorldBars/Shake/Status");
            float pipSide = WorldBarGeometry.Texels(style.pipTexels);
            float pipBottom = pip.localPosition.y - pipSide * 0.5f;
            float manaBottom = mana.localPosition.y - style.ResourceRowHeight * 0.5f;
            Assert.AreEqual(manaBottom, pipBottom, 1e-4f,
                "the pip is taller than the thin row; centred on it, its lower half sat in the health row");

            float pipTop = pip.localPosition.y + pipSide * 0.5f;
            float manaTop = mana.localPosition.y + style.ResourceRowHeight * 0.5f;
            float top = Mathf.Max(pipTop, manaTop);
            float statusBottom = status.localPosition.y - WorldBarGeometry.Texels(style.iconTexels) * 0.5f;
            Assert.AreEqual(top + WorldBarGeometry.Texels(style.statusGapTexels), statusBottom, 1e-4f,
                "the status row sits the authored gap above whatever is tallest under it");
        }

        [Test]
        public void EveryStatusIcon_SitsOnTheSameTile_WithItsTimerOnTop()
        {
            var (go, _) = MakeFullRig(StatusEffectKind.Burn, StatusEffectKind.Poison, StatusEffectKind.Slow);
            Vector2 tileSize = Vector2.zero;
            for (int i = 0; i < 3; i++)
            {
                var tile = Part(go, "Status/TileV" + i);
                var icon = Part(go, "Status/Icon" + i);
                var timer = Part(go, "Status/Duration" + i);
                Assert.IsTrue(tile.gameObject.activeSelf, "tile " + i);
                Assert.IsTrue(icon.gameObject.activeSelf, "icon " + i);
                if (i == 0) tileSize = tile.size;
                Assert.AreEqual(tileSize, tile.size,
                    "one tile size for every glyph: the dilated outlines were three different shapes");
                Assert.Greater(icon.sortingOrder, tile.sortingOrder, "the glyph is drawn on the tile");
                Assert.Greater(timer.sortingOrder, icon.sortingOrder,
                    "the timer needs an order of its own: at the glyph's order its dilated outline " +
                    "drew over the timer on two icons out of three");
                Assert.IsTrue(timer.gameObject.activeSelf, "a timed status draws its timer");
                Assert.LessOrEqual(timer.size.x, tile.size.x + 1e-4f, "the timer lives inside the tile");
                Assert.AreEqual(WorldBarGeometry.TEXEL, timer.size.y, 1e-4f);
            }
        }

        [Test]
        public void TheRigsFade_ReachesTheStatusTiles()
        {
            // The icons used to only STORE the alpha, so they popped while the bars dissolved.
            var (go, rig) = MakeFullRig(StatusEffectKind.Burn);
            SetAlpha(rig, 0.5f, 0.5f);
            // The row is ticked directly rather than through LateUpdate: the rig's own visibility
            // pass would re-decide the target and move the alpha off the value under test (Edit
            // Mode does advance Time.deltaTime a little between test frames).
            var row = (WorldStatusIconRow)typeof(WorldBarRig)
                .GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
            row.Tick(0f, WorldBarStyle.Active);
            Assert.AreEqual(0.5f, Part(go, "Status/Icon0").color.a, 0.01f);
            Assert.AreEqual(0.5f, Part(go, "Status/TileV0").color.a, 0.01f);
            Assert.AreEqual(0.5f, Part(go, "Status/Duration0").color.a, 0.01f);
        }

        [Test]
        public void ABlow_ThrowsSparks_AndAFadedRigDropsThem()
        {
            var (_, rig) = MakeFullRig();
            Assert.AreEqual(0, rig.SparksAlive);
            rig.SetHealth(100, 200, WorldBarChange.Damage);
            Assert.Greater(rig.SparksAlive, 0, "the chunk a blow removed throws shards");
            SetAlpha(rig, 0f, 0f);
            Assert.AreEqual(0, rig.SparksAlive,
                "a rig that has faded out writes nothing, sparks included");
        }

        [Test]
        public void TheFillCarriesMotes_OnlyWhereThereIsFill()
        {
            var (_, full) = MakeFullRig();
            Assert.Greater(full.HealthMotes, 0, "a full bar on a dwarf-sized body carries light");

            var go = WorldBarTestHelper.MakeEntity("Empty");
            _spawned.Add(go);
            var empty = WorldBarRig.Ensure(go);
            empty.SetHealth(1, 200, WorldBarChange.Silent);
            WorldBarTestHelper.InvokeLateUpdate(empty);
            Assert.AreEqual(0, empty.HealthMotes, "an emptied bar visibly loses its sparkle");
        }

        [Test]
        public void TheHeartbeat_IsThePlayersAlone()
        {
            var player = WorldBarTestHelper.MakeEntity("Player");
            _spawned.Add(player);
            var prig = WorldBarRig.Ensure(player);
            prig.SetRank(WorldBarRank.Player);
            prig.SetHealth(20, 200, WorldBarChange.Silent);
            WorldBarTestHelper.InvokeLateUpdate(prig);
            Assert.IsTrue(prig.HeartbeatActive, "a player at a tenth of their health is in danger");

            var monster = WorldBarTestHelper.MakeEntity("Monster");
            _spawned.Add(monster);
            var mrig = WorldBarRig.Ensure(monster);
            mrig.SetRank(WorldBarRank.Normal);
            mrig.SetHealth(20, 200, WorldBarChange.Silent);
            WorldBarTestHelper.InvokeLateUpdate(mrig);
            Assert.IsFalse(mrig.HeartbeatActive,
                "a monster near death is good news; a field of pulsing rings would report it as an alarm");
        }

        [Test]
        public void ThePip_ShowsAGlintOnlyWhenTheChargeIsFull()
        {
            var (go, rig) = MakeFullRig();
            Assert.IsTrue(Part(go, "DashPip/Glint").gameObject.activeSelf, "a full stone glints");
            Assert.IsTrue(Part(go, "DashPip/CoreShadow").gameObject.activeSelf);
            Assert.IsTrue(Part(go, "DashPip/CoreHighlight").gameObject.activeSelf);

            rig.SetDashCharge(0.3f);
            WorldBarTestHelper.InvokeLateUpdate(rig);
            Assert.IsFalse(Part(go, "DashPip/Glint").gameObject.activeSelf,
                "a glint on a charging stone would say 'ready' in the corner of something that is not");
        }
    }
}
