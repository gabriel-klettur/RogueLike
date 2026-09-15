using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Gameplay.Combat.WorldUI
{
    /// <summary>
    /// Pins the shape of the ground aim indicator — the things that were wrong with the chevron
    /// it replaced and would be the obvious things to reintroduce.
    ///
    /// <para>The rig is built through <c>BuildRig</c> directly: in Edit Mode a component's
    /// <c>Start</c> never runs, so a test that only adds the component measures nothing.</para>
    /// </summary>
    public class FacingIndicatorRigTests
    {
        private GameObject _holder;
        private FacingIndicator _indicator;
        private bool _blockedBefore;

        [SetUp]
        public void SetUp()
        {
            _blockedBefore = InputBlocker.IsGameplayBlocked;
            InputBlocker.SetBlocked(false);

            _holder = new GameObject("FacingIndicatorRigTests.Holder");
            _holder.transform.position = new Vector3(12.5f, -3.25f, 0f);
            // The body sprite: what YSortEntity writes in the game, and whose LAYER the rig follows.
            _body = _holder.AddComponent<SpriteRenderer>();
            _body.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _indicator = _holder.AddComponent<FacingIndicator>();
            _indicator.BuildRig();
        }

        private SpriteRenderer _body;

        /// <summary>What YSortEntity would give the body standing where the holder stands.</summary>
        private int ExpectedBodyOrder =>
            SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, _holder.transform.position.y);

        [TearDown]
        public void TearDown()
        {
            // Awake never ran, so OnDestroy never will either: the root is not a child of the
            // holder and has to be taken down by hand.
            if (_indicator != null && _indicator.RootTransform != null)
                Object.DestroyImmediate(_indicator.RootTransform.gameObject);
            if (_holder != null) Object.DestroyImmediate(_holder);

            // GLOBAL state with Domain Reload off; leaving it changed breaks whichever fixture
            // runs next, for a reason nothing in that fixture mentions.
            InputBlocker.SetBlocked(_blockedBefore);
        }

        // ── Sorting ───────────────────────────────────────────────────────────────────

        [Test]
        public void EveryLayer_DrawsOnTheBodysLayer_AndFollowsItWhenItChanges()
        {
            foreach (var sr in Renderers())
                Assert.AreEqual(SortingConfig.LAYER_ENTITIES, sr.sortingLayerName, sr.name);

            // The player moves to EntitiesOverhead on the overhead visual layer; the rig goes too.
            _body.sortingLayerName = SortingConfig.LAYER_ENTITIES_OVERHEAD;
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            foreach (var sr in Renderers())
                Assert.AreEqual(SortingConfig.LAYER_ENTITIES_OVERHEAD, sr.sortingLayerName, sr.name);
        }

        [Test]
        public void Orders_AreRebasedOnTheFeet_NotAZDepthMisusedAsAnOrder()
        {
            // Z_SKY is 600 and is a Z depth. The old chevron passed Z_SKY + 10 as an order.
            // The rig sits within a fixed gap of what YSortEntity gives the body at the feet.
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Assert.AreEqual(ExpectedBodyOrder, _indicator.BodyOrder);
            int reach = FacingIndicatorStyle.DepthGap + 3;
            foreach (var sr in Renderers())
                Assert.LessOrEqual(Mathf.Abs(sr.sortingOrder - ExpectedBodyOrder), reach, sr.name);
        }

        [Test]
        public void AimingNorth_PutsTheAimBehindTheBody_AimingSouth_InFront()
        {
            int body = ExpectedBodyOrder;

            _indicator.FacingOverride = Vector2.up;
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Assert.IsTrue(_indicator.AimIsBehindBody);
            Assert.Less(_indicator.AuraRenderer.sortingOrder, body, "aura behind when aiming north");
            Assert.Less(_indicator.TipRenderer.sortingOrder, body, "tip behind when aiming north");

            _indicator.FacingOverride = Vector2.down;
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Assert.IsFalse(_indicator.AimIsBehindBody);
            Assert.Greater(_indicator.AuraRenderer.sortingOrder, body, "aura in front when aiming south");
            Assert.Greater(_indicator.TipRenderer.sortingOrder, body, "tip in front when aiming south");
        }

        [Test]
        public void LevelAim_StaysInFront_TheDeadBandIsOnTheNorthSideOnly()
        {
            int body = ExpectedBodyOrder;
            _indicator.FacingOverride = Vector2.right;
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Assert.IsFalse(_indicator.AimIsBehindBody);
            Assert.Greater(_indicator.TipRenderer.sortingOrder, body);

            // Just above level: still in front, inside the dead band.
            float sine = FacingIndicatorStyle.BehindAimSine * 0.5f;
            _indicator.FacingOverride = new Vector2(Mathf.Sqrt(1f - sine * sine), sine);
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Assert.IsFalse(_indicator.AimIsBehindBody);
        }

        [Test]
        public void Tip_DrawsOverItsAura()
        {
            // The chevron is the shape; the aura only frames it. Behind is the only place the
            // glow can be without eating the edge it exists to set off.
            Assert.Greater(_indicator.TipRenderer.sortingOrder, _indicator.AuraRenderer.sortingOrder);
        }

        [Test]
        public void Root_FollowsThePlayer_ButIsNotItsChild()
        {
            var root = _indicator.RootTransform;
            Assert.IsNotNull(root);
            Assert.AreNotEqual(_holder.transform, root.parent, "parenting inherits the entity scale");
            Assert.AreEqual(_holder.transform.position, root.position);
            Assert.AreEqual(Quaternion.identity, root.rotation);
            Assert.AreEqual(Vector3.one, root.localScale);
        }

        [Test]
        public void GroundSquash_LivesOnExactlyOneParent_AndTheAimTurnsUnderIt()
        {
            var aim = _indicator.AimTransform;
            var ground = aim.parent;
            Assert.AreEqual("GroundPlane", ground.name);
            Assert.AreEqual(FacingIndicatorStyle.GroundSquash, ground.localScale.y, 1e-5f);
            Assert.AreEqual(1f, ground.localScale.x, 1e-5f);

            // The rotation is a CHILD of the squash, never sharing a transform with it.
            Assert.AreEqual(Vector3.one, aim.localScale);

            // And nothing under the aim is squashed a second time.
            foreach (var sr in Renderers())
            {
                var s = sr.transform.localScale;
                Assert.AreEqual(s.x, s.y, 1e-5f, sr.name + " is squashed per item");
            }
        }

        [Test]
        public void Rig_HasNoLight_AndNothingScalesTheRoot()
        {
            Assert.IsNull(_indicator.RootTransform.GetComponentInChildren<UnityEngine.Rendering.Universal.Light2D>());
            Assert.AreEqual(Vector3.one, _indicator.RootTransform.localScale);
        }

        // ── Sharing ───────────────────────────────────────────────────────────────────

        [Test]
        public void Materials_AreTheSharedOnes_NeverPerInstanceClones()
        {
            // Both layers are additive now. The rig used to carry one alpha layer, a dark rim
            // under the chevron, and it went when the aura replaced it: a white glow cannot
            // outline a white shape, so nothing here wants to darken the ground any more.
            foreach (var sr in Renderers())
                Assert.AreSame(ElementalSprites.SharedAdditiveMaterial, sr.sharedMaterial, sr.name);
        }

        [Test]
        public void Sprites_AreBuiltOnce_AndSharedAcrossRigs()
        {
            var second = new GameObject("second").AddComponent<FacingIndicator>();
            try
            {
                second.BuildRig();
                Assert.AreSame(_indicator.AuraRenderer.sprite, second.AuraRenderer.sprite);
                Assert.AreSame(_indicator.TipRenderer.sprite, second.TipRenderer.sprite);
            }
            finally
            {
                if (second.RootTransform != null) Object.DestroyImmediate(second.RootTransform.gameObject);
                Object.DestroyImmediate(second.gameObject);
            }
        }

        [Test]
        public void Sprites_AreExactlyOneWorldUnit_SoAScaleIsADiameter()
        {
            AssertUnitSprite(FacingIndicatorSprites.Aura, "Aura");
            AssertUnitSprite(FacingIndicatorSprites.Tip, "Tip");
        }

        [Test]
        public void Sprites_PointAlongPlusX_SoTheAimRotationIsTheHeading()
        {
            // The aura is generated from the SAME two segments as the tip, and the property
            // that says so is that it follows the ARMS rather than the radius. Two points the
            // same distance from the centre: one on the upper arm's midpoint, one in the notch
            // between the tails. A round halo would light them equally.
            //
            // Its alpha MASS is deliberately NOT the assertion. Measured, the aura carries more
            // ink on the -X half (0.523 vs 0.479) because the two arms run back to the tails
            // while the apex is a single point — true of a correct chevron glow, so a mass
            // comparison fails the right implementation.
            float onArm = AlphaAt(FacingIndicatorSprites.Aura, 0.14f, 0.31f);
            float offArm = AlphaAt(FacingIndicatorSprites.Aura, -0.34f, 0f);
            Assert.Greater(onArm, 0.5f, "the aura must be lit along the chevron's arms");
            Assert.Less(offArm, 0.25f, "and dark in the notch between its tails");
            Assert.Greater(onArm, offArm * 2f, "a round halo would light both the same");

            // The chevron's two arms overlap at the apex, so its centroid barely leans +X
            // (measured 0.037). The structural claim is the extent: the apex reaches further
            // along +X than the tails reach along -X.
            Assert.Greater(AlphaCentroidX(FacingIndicatorSprites.Tip), 0f,
                "tip centroid must sit on the +X side of the sprite");
            AlphaExtentX(FacingIndicatorSprites.Tip, out float tailX, out float apexX);
            Assert.Greater(apexX, 0.3f, "apex must be well past the centre");
            Assert.Greater(apexX, -tailX, "the point must reach further than the tails");
        }

        // ── State ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Tip_IsTheChevron_AtTheFixedRadius()
        {
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Assert.AreSame(FacingIndicatorSprites.Tip, _indicator.TipSprite);
            Assert.AreEqual(FacingIndicatorStyle.TipRadius, _indicator.TipLocalX, 1e-4f);
            Assert.AreEqual(FacingIndicatorStyle.TipSize, _indicator.TipRenderer.transform.localScale.x, 1e-5f);
        }

        [Test]
        public void Layers_AreNearAchromatic()
        {
            // The rig is white on purpose: it used to take the primary spell's palette, so a
            // fireball drew a red marker and the aim indicator changed colour as a side effect
            // of a loadout choice. The tones are barely-tinted whites — warm at the ground,
            // cool at the point — and nothing else is allowed to tint them.
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            foreach (var sr in Renderers())
                Assert.Less(Saturation(sr.color), 0.12f, sr.name);
        }

        [Test]
        public void Aura_IsDimmerThanTheTip()
        {
            _indicator.ApplyState(1f / 60f, snapHeading: true);

            // The chevron has to stay the brightest thing or the glow swallows the shape it
            // exists to frame. The VALUES are free to be retuned; this ordering is the design.
            Assert.Less(Luminance(_indicator.AuraColor), Luminance(_indicator.TipColor),
                "the aura frames the tip, it does not compete with it");
        }

        [Test]
        public void Pulse_BrightensEveryLightLayer_AndKicksTheTipOutward()
        {
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Color aura = _indicator.AuraColor, tip = _indicator.TipColor;
            float tipX = _indicator.TipLocalX;

            _indicator.Pulse();
            _indicator.ApplyState(1f / 240f, snapHeading: false);

            Assert.Greater(Luminance(_indicator.AuraColor), Luminance(aura), "aura");
            Assert.Greater(Luminance(_indicator.TipColor), Luminance(tip), "tip");
            Assert.Greater(_indicator.TipLocalX, tipX, "the tip kicks outward");

            // THE CHANNEL THAT ACTUALLY READS. The brightening above is measurably real and
            // visually inert on pale ground — the tip already clips at rest (colour 1.15 x
            // alpha 0.92 over mid-grey stone), and two live captures a frame apart were
            // indistinguishable. A size cannot clip, so this is what carries the pulse.
            Assert.Greater(_indicator.TipRenderer.transform.localScale.x,
                FacingIndicatorStyle.TipSize, "the tip must GROW, not merely brighten");
            Assert.Greater(_indicator.AuraRenderer.transform.localScale.x,
                _indicator.TipRenderer.transform.localScale.x, "the aura grows with it");

            // Brightness comes from COLOUR: on an additive material alpha is coverage, so
            // pulsing it would make the rig briefly WIDER rather than brighter.
            Assert.AreEqual(aura.a, _indicator.AuraColor.a, 1e-5f, "aura alpha must not move");
            Assert.AreEqual(tip.a, _indicator.TipColor.a, 1e-5f, "tip alpha must not move");

            // They are one shape, so the glow tracks the chevron's offset exactly.
            Assert.AreEqual(_indicator.TipLocalX, _indicator.AuraRenderer.transform.localPosition.x,
                1e-5f, "the aura follows the tip");
        }

        [Test]
        public void Pulse_DecaysBackToRest_AndThenStopsWritingAltogether()
        {
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Color restAura = _indicator.AuraColor;
            float restTipX = _indicator.TipLocalX;

            _indicator.Pulse();
            for (int i = 0; i < 40; i++) _indicator.ApplyState(1f / 60f, snapHeading: false);

            Assert.AreEqual(restAura, _indicator.AuraColor, "the pulse must settle back exactly");
            Assert.AreEqual(restTipX, _indicator.TipLocalX, 1e-5f, "the tip must return to its fixed radius");
            Assert.AreEqual(FacingIndicatorStyle.TipSize, _indicator.TipRenderer.transform.localScale.x,
                1e-5f, "and to its authored size");

            // And once settled it writes NOTHING: a SpriteRenderer.color set dirties URP's
            // batcher, and at rest that would be sixty writes a second of an unchanged value.
            // Poking a sentinel in is the only way to see the absence of a write.
            var sentinel = new Color(0.123f, 0.456f, 0.789f, 0.321f);
            _indicator.AuraRenderer.color = sentinel;
            _indicator.ApplyState(1f / 60f, snapHeading: false);
            Assert.AreEqual(sentinel, _indicator.AuraColor, "a rig at rest must not repaint");
        }

        [Test]
        public void Pulse_RestartsRatherThanStacking_SoAFastRhythmReadsAsBeats()
        {
            _indicator.ApplyState(1f / 60f, snapHeading: true);

            _indicator.Pulse();
            _indicator.ApplyState(1f / 240f, snapHeading: false);
            float firstPeak = Luminance(_indicator.TipColor);

            // Half way down, hit it again: the second beat must reach the same peak as the
            // first, not a higher one. Stacking would make a harvest rhythm ramp to white.
            for (int i = 0; i < 3; i++) _indicator.ApplyState(FacingIndicatorStyle.PulseSeconds / 8f, snapHeading: false);
            Assert.Less(Luminance(_indicator.TipColor), firstPeak, "it must have decayed");

            _indicator.Pulse();
            _indicator.ApplyState(1f / 240f, snapHeading: false);
            Assert.AreEqual(firstPeak, Luminance(_indicator.TipColor), 1e-4f, "the second beat is not louder");
        }

        [Test]
        public void AtRest_TheLookIsConstant_AndOnlyTheHeadingEverMoves()
        {
            _indicator.FacingOverride = Vector2.right;
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Color aura = _indicator.AuraColor, tip = _indicator.TipColor;
            float tipX = _indicator.TipLocalX;
            float headingBefore = _indicator.DrawnHeadingDeg;

            // Twenty frames and a turn. Nothing about the look may drift: with readiness, cast
            // phase and posture gone there is no longer anything that is ALLOWED to change a
            // colour or the tip's offset, and a new writer would show up here.
            _indicator.FacingOverride = Vector2.up;
            for (int i = 0; i < 20; i++) _indicator.ApplyState(1f / 60f, snapHeading: false);

            Assert.AreEqual(aura, _indicator.AuraColor, "aura colour drifted");
            Assert.AreEqual(tip, _indicator.TipColor, "tip colour drifted");
            Assert.AreEqual(tipX, _indicator.TipLocalX, 1e-5f, "tip offset drifted");
            Assert.AreNotEqual(headingBefore, _indicator.DrawnHeadingDeg, "the heading is the one thing that moves");
        }

        [Test]
        public void GameplayBlocked_HidesEveryLayer_AndUnblockingShowsThemAgain()
        {
            InputBlocker.SetBlocked(true);
            _indicator.ApplyState(1f / 60f, snapHeading: false);
            Assert.IsFalse(_indicator.IsShowing);
            foreach (var sr in Renderers()) Assert.IsFalse(sr.enabled, sr.name);

            InputBlocker.SetBlocked(false);
            _indicator.ApplyState(1f / 60f, snapHeading: false);
            Assert.IsTrue(_indicator.IsShowing);
            foreach (var sr in Renderers()) Assert.IsTrue(sr.enabled, sr.name);
        }

        [Test]
        public void NoPlayerAndNoCaster_StillDraws()
        {
            // Nothing about the rig's look depends on resolving a spell any more, so a holder
            // with neither a PlayerController nor a SpellCaster still renders the full rig.
            _indicator.ApplyState(1f / 60f, snapHeading: true);
            Assert.Greater(_indicator.TipColor.a, 0f);
            Assert.Greater(Luminance(_indicator.TipColor), Luminance(_indicator.AuraColor),
                "the tip must be brighter than the glow around it");
        }

        [Test]
        public void Source_NeverUsesOverhead_ZSky_ShaderFind_OrAPerInstanceMaterial()
        {
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Combat/WorldUI");
            var files = Directory.GetFiles(dir, "FacingIndicator*.cs");
            Assert.GreaterOrEqual(files.Length, 3, "the rig, its state and its sprites");

            foreach (var f in files)
            {
                string src = File.ReadAllText(f);
                string name = Path.GetFileName(f);
                // Comments are allowed to NAME the trap; code is not allowed to USE it.
                string code = string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
                StringAssert.DoesNotContain("LAYER_OVERHEAD", code, name);
                StringAssert.DoesNotContain("Z_SKY", code, name);
                StringAssert.DoesNotContain("Shader.Find", code, name);
                StringAssert.DoesNotContain("new Material(", code, name);
                StringAssert.DoesNotContain(".material =", code, name);
                StringAssert.DoesNotContain("Mouse.current", code, name);
                StringAssert.DoesNotContain("Keyboard.current", code, name);
            }
        }

        [Test]
        public void Source_HasOneJob_AndNeverReadsStanceCasterOrCooldown()
        {
            // THE INDICATOR ANSWERS WHERE YOU POINT AND NOTHING ELSE. It briefly answered three
            // more things — a sweep that dimmed while the primary recovered, a tip that
            // contracted during a cast wind-up, a ring instead of a point in Peace — and each
            // was individually defensible while together they made the one shape under the
            // character mean four things at once. Every one of them arrives as a small, obvious
            // addition, which is why this is a source guard and not a comment.
            //
            // Pulse() is not an exception to this and this guard is what proves it: the rig is
            // PUSHED AT by whoever acted and never asks who, so acknowledging a cast and a pick
            // strike costs it no knowledge of either. A pulse driven by READING the caster here
            // would be the same coupling wearing a different hat, and would fail on this line.
            foreach (var f in ProductionSources())
            {
                string code = StripComments(File.ReadAllText(f));
                string name = Path.GetFileName(f);
                foreach (var forbidden in new[]
                {
                    "PlayerStance", "SpellCaster", "CastPhase", "Cooldown",
                    "ElementPalette", "ResolveSwatch", "MouseTargetDetector",
                })
                    StringAssert.DoesNotContain(forbidden, code,
                        name + " must not read " + forbidden + " — the rig has ONE job");
            }
        }

        [Test]
        public void PlayerController_PulsesTheMarker_OnACastAndOnEveryWorkSwing()
        {
            // The two callers are the whole feature, and a pulse that nothing fires is exactly
            // the authored-and-inert shape this project keeps rediscovering. Asserted against
            // the SOURCE because driving a real cast or a real harvest blow needs a spell
            // catalog, an animator and a node — a fixture that proves the wiring by rebuilding
            // it is a fixture that passes while the game does nothing.
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Player");

            // The pulse itself moved down into PlayerController's NotifyPlayerActed seam when
            // the cursor ring joined it as a second listener. This fixture follows it to its
            // new owner rather than demanding the old inline call back — a grep that forces a
            // call to stay where it was would be pinning the shape of the code instead of the
            // guarantee, and would leave the seam everything now depends on unguarded.
            string owner = StripComments(File.ReadAllText(Path.Combine(dir, "PlayerController.cs")));
            StringAssert.Contains("_facingIndicator.Pulse()", owner, "the act seam must pulse the marker");

            string movement = StripComments(File.ReadAllText(Path.Combine(dir, "PlayerController.Movement.cs")));
            // Gated, or a channelled beam re-enters TriggerCastAnimation every frame and holds
            // the rig lit for its whole duration instead of marking the moment it started.
            StringAssert.Contains("!sameCastStillPlaying) NotifyPlayerActed()", movement,
                "the cast notification must be gated on the same flag the variant is");

            string harvest = StripComments(File.ReadAllText(Path.Combine(dir, "PlayerController.Harvest.cs")));
            StringAssert.Contains("NotifyPlayerActed()", harvest, "every work swing must notify");
        }

        private static string[] ProductionSources()
        {
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Combat/WorldUI");
            var files = Directory.GetFiles(dir, "FacingIndicator*.cs");
            Assert.GreaterOrEqual(files.Length, 3, "the rig, its state and its sprites");
            return files;
        }

        /// <summary>Comments are allowed to NAME a trap; code is not allowed to USE it.</summary>
        private static string StripComments(string src) =>
            string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//")
                                                      && !l.TrimStart().StartsWith("///")));

        // ── Helpers ───────────────────────────────────────────────────────────────────

        private SpriteRenderer[] Renderers() => new[]
        {
            _indicator.AuraRenderer, _indicator.TipRenderer
        };

        private static void AssertUnitSprite(Sprite s, string name)
        {
            Assert.IsNotNull(s, name);
            Assert.AreEqual(1f, s.bounds.size.x, 1e-3f, name + " width");
            Assert.AreEqual(1f, s.bounds.size.y, 1e-3f, name + " height");
        }

        /// <summary>Alpha at a point in the sprite's normalized space, x and y in [-1, 1].</summary>
        private static float AlphaAt(Sprite s, float nx, float ny)
        {
            var tex = s.texture;
            int x = Mathf.Clamp(Mathf.RoundToInt((nx * 0.5f + 0.5f) * tex.width), 0, tex.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt((ny * 0.5f + 0.5f) * tex.height), 0, tex.height - 1);
            return tex.GetPixel(x, y).a;
        }

        private static float AlphaMass(Sprite s, bool leftHalf)
        {
            var tex = s.texture;
            int w = tex.width, h = tex.height;
            float sum = 0f;
            int x0 = leftHalf ? 0 : w / 2, x1 = leftHalf ? w / 2 : w;
            for (int y = 0; y < h; y++)
                for (int x = x0; x < x1; x++)
                    sum += tex.GetPixel(x, y).a;
            return sum / (w * h * 0.5f);
        }

        private static float AlphaCentroidX(Sprite s)
        {
            var tex = s.texture;
            int w = tex.width, h = tex.height;
            float sum = 0f, weighted = 0f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = tex.GetPixel(x, y).a;
                    sum += a;
                    weighted += a * ((x + 0.5f) / w - 0.5f);
                }
            return sum > 0f ? weighted / sum : 0f;
        }

        /// <summary>Leftmost and rightmost solid (alpha > 0.5) column, normalized to [-0.5, 0.5].</summary>
        private static void AlphaExtentX(Sprite s, out float minX, out float maxX)
        {
            var tex = s.texture;
            int w = tex.width, h = tex.height;
            minX = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (tex.GetPixel(x, y).a <= 0.5f) continue;
                    float nx = (x + 0.5f) / w - 0.5f;
                    if (nx < minX) minX = nx;
                    if (nx > maxX) maxX = nx;
                }
        }

        private static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        private static float Saturation(Color c)
        {
            Color.RGBToHSV(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b)), out _, out float s, out _);
            return s;
        }
    }
}
