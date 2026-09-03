using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Pins the boomerang's flight, its spawn rig and its shipped data.
    ///
    /// <para>Everything here exists because the spell shipped with only STRUCTURAL coverage —
    /// that the executor was registered, that two source files contained two call sites, that
    /// the flourish family was Hurl — and none of it could see that the throw never came back.
    /// The shared ball prefab carries a <c>Projectile</c>, the executor never initialised it,
    /// and its serialized default <c>range = 20</c> destroyed the blade 20 units out, short of
    /// the 26.25 it was authored to turn at. The composition was the defect; neither half was
    /// wrong on its own. So the tests below fly a whole throw and assert the arc, the way
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT</c> prescribes for anything with two halves.</para>
    /// </summary>
    public class BoomerangSpellTests
    {
        private const string SpellPath = "Assets/_Project/Data/Catalogs/Spells/boomerang.asset";

        /// <summary>
        /// Far from anything the open scene has painted, so the obstacle sweep and the victim
        /// overlap both answer "nothing" and the arc is the only thing under test.
        /// </summary>
        private static readonly Vector3 EmptyGround = new Vector3(9000f, 9000f, 0f);

        private const float Step = 1f / 60f;

        private static SpellDefinition Shipped()
        {
            var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(SpellPath);
            Assert.IsNotNull(spell, $"{SpellPath} is missing");
            return spell;
        }

        /// <summary>
        /// Flies one throw to its end. Returns how far it ever got from the origin, where it
        /// was when it died, how long it lived, and whether it was ever seen returning —
        /// sampled, because the component destroys its own GameObject on the way out and its
        /// fields cannot be read afterwards.
        /// </summary>
        private static (float maxDistance, Vector3 lastPos, float seconds, bool turned)
            Fly(Transform caster, float speed, float range, Vector2 direction, int maxSteps = 2000,
                System.Action<int> beforeStep = null)
        {
            var go = new GameObject("BoomerangUnderTest");
            go.transform.position = caster.position;
            go.AddComponent<Rigidbody2D>();
            var boom = go.AddComponent<BoomerangProjectile>();

            Vector3 origin = caster.position;
            // targetLayers 0: the open scene is whatever the author last had loaded, and this
            // test is about the arc, not about who it hits.
            boom.Initialize(caster, direction, speed, speed, damage: 10f, maxRange: range,
                            hitRadius: 0.5f, passesThrough: false, targetLayers: 0, vfxColor: Color.white);

            float maxDistance = 0f;
            Vector3 lastPos = go.transform.position;
            float seconds = 0f;
            bool turned = false;

            for (int i = 0; i < maxSteps && boom != null; i++)
            {
                beforeStep?.Invoke(i);
                boom.Step(Step);
                seconds += Step;
                if (boom == null) break;

                lastPos = go.transform.position;
                maxDistance = Mathf.Max(maxDistance, Vector3.Distance(lastPos, origin));
                if (boom.CurrentPhase == BoomerangProjectile.Phase.Returning) turned = true;
            }

            if (go != null) Object.DestroyImmediate(go);
            return (maxDistance, lastPos, seconds, turned);
        }

        // ── the arc ──────────────────────────────────────────────────────────────────

        [Test]
        public void TheThrowTurnsAtItsRangeAndComesBackToTheHand()
        {
            var caster = new GameObject("Caster");
            caster.transform.position = EmptyGround;
            try
            {
                const float Speed = 24f;
                const float Range = 10f;
                var flight = Fly(caster.transform, Speed, Range, Vector2.right);

                float oneStep = Speed * Step;
                Assert.IsTrue(flight.turned, "the blade never entered its return leg");
                Assert.GreaterOrEqual(flight.maxDistance, Range - oneStep,
                    "the blade turned before reaching its authored range");
                Assert.LessOrEqual(flight.maxDistance, Range + oneStep,
                    "the blade overshot its authored range by more than one step");
                Assert.LessOrEqual(Vector3.Distance(flight.lastPos, caster.transform.position), 1f,
                    "the flight ended away from the caster — it was not caught, it timed out. "
                    + "This is the shape of the shipped defect: a second component expiring the "
                    + "blade in mid-air before the return leg could run.");

                // Out and back at one speed, plus the catch. Anything much longer means the
                // blade orbited the hand instead of being caught.
                float roundTrip = 2f * Range / Speed;
                Assert.Less(flight.seconds, roundTrip * 1.5f,
                    $"the throw took {flight.seconds:F2}s against a {roundTrip:F2}s round trip");
            }
            finally { Object.DestroyImmediate(caster); }
        }

        [Test]
        public void TheTwoLegsBowToOppositeSidesSoTheThrowDrawsALoop()
        {
            var caster = new GameObject("Caster");
            caster.transform.position = EmptyGround;
            try
            {
                const float Speed = 24f;
                const float Range = 10f;
                Vector2 aim = Vector2.right;
                Vector2 right = new Vector2(aim.y, -aim.x);

                var go = new GameObject("BoomerangUnderTest");
                go.transform.position = caster.transform.position;
                go.AddComponent<Rigidbody2D>();
                var boom = go.AddComponent<BoomerangProjectile>();
                boom.Initialize(caster.transform, aim, Speed, Speed, 10f, Range, 0.5f, false, 0, Color.white);

                Vector3 origin = caster.transform.position;
                float outboundBow = 0f, returnBow = 0f, pathLength = 0f;
                Vector3 previous = origin;

                for (int i = 0; i < 2000 && boom != null; i++)
                {
                    bool wasOutbound = boom.CurrentPhase == BoomerangProjectile.Phase.Outbound;
                    boom.Step(Step);
                    if (boom == null) break;

                    Vector3 p = go.transform.position;
                    pathLength += Vector3.Distance(p, previous);
                    previous = p;

                    float lateral = Vector2.Dot((Vector2)(p - origin), right);
                    if (wasOutbound) outboundBow = Mathf.Max(outboundBow, lateral);
                    else             returnBow = Mathf.Min(returnBow, lateral);
                }
                if (go != null) Object.DestroyImmediate(go);

                // The defect this pins is not a crash, it is a SHAPE. Out and back down the same
                // line is a bullet that reversed: the return retraces pixels the eye has already
                // filed, so the player reports never seeing it come back at all.
                //
                // Signs are not asserted, only that the two legs are on OPPOSITE sides. Which
                // way the loop turns is chosen at cast time from what is walled, so pinning
                // "clockwise" here would make the test a statement about the empty test scene
                // rather than about the shape.
                Assert.Greater(outboundBow, Range * 0.25f,
                    $"the outbound leg only bowed {outboundBow:F2} off a {Range} throw — that is a "
                    + "straight line, not a boomerang");
                Assert.Less(returnBow, -Range * 0.25f,
                    $"the return leg bowed {returnBow:F2}; it must swing to the OTHER side, or the "
                    + "two legs overlap and the throw never reads as a loop");
                Assert.AreEqual(Mathf.Abs(returnBow), outboundBow, Range * 0.05f,
                    "the loop must be symmetric — a lopsided one reads as a mistake, not a shape");

                Assert.Greater(pathLength, 2f * Range * 1.15f,
                    "the flown path must be meaningfully longer than the straight round trip");
            }
            finally { Object.DestroyImmediate(caster); }
        }

        [Test]
        public void TheSameThrowFliesTheSameShapeAtEveryHeading()
        {
            // The flight is rotation-invariant by construction and this asserts it stays that
            // way. It is here because the spell DID behave differently by heading, twice over,
            // and neither cause was in the geometry: the obstacle probe was five times too fat
            // so it caught on scenery off the aim line, and the loop always turned the same way
            // so a wall on that side broke one heading and not its opposite.
            var caster = new GameObject("Caster");
            caster.transform.position = EmptyGround;
            try
            {
                const float Speed = 24f;
                const float Range = 10f;

                float firstSeconds = -1f, firstReach = -1f, firstBow = -1f;

                for (int degrees = 0; degrees < 360; degrees += 15)
                {
                    float radians = degrees * Mathf.Deg2Rad;
                    Vector2 aim = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                    Vector2 right = new Vector2(aim.y, -aim.x);

                    var go = new GameObject("BoomerangUnderTest");
                    go.transform.position = caster.transform.position;
                    go.AddComponent<Rigidbody2D>();
                    var boom = go.AddComponent<BoomerangProjectile>();
                    boom.Initialize(caster.transform, aim, Speed, Speed, 10f, Range, 0.5f, false, 0, Color.white);

                    Vector3 origin = caster.transform.position;
                    float seconds = 0f, reach = 0f, bow = 0f;
                    for (int i = 0; i < 2000 && boom != null; i++)
                    {
                        boom.Step(Step);
                        seconds += Step;
                        if (boom == null) break;
                        Vector3 p = go.transform.position;
                        reach = Mathf.Max(reach, Vector3.Distance(p, origin));
                        bow = Mathf.Max(bow, Mathf.Abs(Vector2.Dot((Vector2)(p - origin), right)));
                    }
                    if (go != null) Object.DestroyImmediate(go);

                    Assert.AreEqual(Range, reach, 0.5f,
                        $"at {degrees} degrees the throw reached {reach:F2} instead of {Range}");

                    if (firstSeconds < 0f) { firstSeconds = seconds; firstReach = reach; firstBow = bow; continue; }

                    Assert.AreEqual(firstSeconds, seconds, 0.05f, $"{degrees} degrees flew for a different time");
                    Assert.AreEqual(firstReach, reach, 0.05f, $"{degrees} degrees reached a different distance");
                    Assert.AreEqual(firstBow, bow, 0.05f, $"{degrees} degrees bowed by a different amount");
                }
            }
            finally { Object.DestroyImmediate(caster); }
        }

        [Test]
        public void ShortThrowsDrawTheSameLensAsLongOnes()
        {
            // The bow is a fraction of the LEG, not of the spell's range, so a throw cut short —
            // by a wall, by a victim — is a small loop rather than a full-width bulge on a short
            // run. This is what makes the spell read the same wherever it is cast, and it is the
            // replacement for a clearance clamp that kept the flight safe by flattening the loop
            // to nothing: measured in the shipped town, 17 of 24 headings had lost more than half
            // their bow, most of them nearly all of it.
            var caster = new GameObject("Caster");
            caster.transform.position = EmptyGround;
            try
            {
                float ratioAt(float range)
                {
                    var go = new GameObject("BoomerangUnderTest");
                    go.transform.position = caster.transform.position;
                    go.AddComponent<Rigidbody2D>();
                    var boom = go.AddComponent<BoomerangProjectile>();
                    boom.Initialize(caster.transform, Vector2.right, 24f, 24f, 10f, range, 0.5f, false, 0, Color.white);

                    Vector3 origin = caster.transform.position;
                    float bow = 0f;
                    for (int i = 0; i < 4000 && boom != null; i++)
                    {
                        bool outbound = boom.CurrentPhase == BoomerangProjectile.Phase.Outbound;
                        boom.Step(Step);
                        if (boom == null || !outbound) break;
                        bow = Mathf.Max(bow, Mathf.Abs(go.transform.position.y - origin.y));
                    }
                    if (go != null) Object.DestroyImmediate(go);
                    return bow / range;
                }

                float wide = ratioAt(10f);
                float narrow = ratioAt(3f);

                Assert.Greater(wide, 0.25f, "the full-length throw lost its loop");
                Assert.AreEqual(wide, narrow, 0.06f,
                    $"a 3-unit throw bowed {narrow:P0} of its length against {wide:P0} for a "
                    + "10-unit one — the shape has to be the same at any size");
            }
            finally { Object.DestroyImmediate(caster); }
        }

        [Test]
        public void TheObstacleProbeIsTheBladeNotItsReach()
        {
            // hitRadius is authored generously so a near miss on a moving target still lands.
            // Using it against walls made the blade sweep a corridor five times wider than any
            // other projectile's, and it caught on scenery nobody aimed at — measured, 16 of 24
            // headings from one spot turned back early, one after 2.66 units of a 10-unit throw.
            var type = typeof(BoomerangProjectile);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            float obstacleRadius = (float)type.GetField("ObstacleRadius", flags).GetRawConstantValue();

            Assert.Less(obstacleRadius, Shipped().hitRadius,
                "the wall probe must be the blade's own width, not the reach of its damage");
            Assert.Greater(obstacleRadius, 0f, "a zero-width probe tunnels through thin walls");
        }

        [Test]
        public void TheFlightIsFramerateIndependent()
        {
            var caster = new GameObject("Caster");
            caster.transform.position = EmptyGround;
            try
            {
                // Same throw, stepped at 60 Hz and at 20 Hz. A flight that moved by a fixed
                // amount per frame — or damaged per frame — would disagree here.
                var fast = Fly(caster.transform, 24f, 10f, Vector2.right);

                var slowGo = new GameObject("BoomerangSlow");
                slowGo.transform.position = caster.transform.position;
                slowGo.AddComponent<Rigidbody2D>();
                var slow = slowGo.AddComponent<BoomerangProjectile>();
                slow.Initialize(caster.transform, Vector2.right, 24f, 24f, 10f, 10f, 0.5f, false, 0, Color.white);

                float slowMax = 0f, slowSeconds = 0f;
                for (int i = 0; i < 1000 && slow != null; i++)
                {
                    slow.Step(1f / 20f);
                    slowSeconds += 1f / 20f;
                    if (slow == null) break;
                    slowMax = Mathf.Max(slowMax, Vector3.Distance(slowGo.transform.position, caster.transform.position));
                }
                if (slowGo != null) Object.DestroyImmediate(slowGo);

                Assert.AreEqual(fast.maxDistance, slowMax, 24f * (1f / 20f) + 0.01f,
                    "reach differed between framerates");
                Assert.AreEqual(fast.seconds, slowSeconds, 0.2f,
                    "flight duration differed between framerates");
            }
            finally { Object.DestroyImmediate(caster); }
        }

        [Test]
        public void ARunningCasterIsStillCaughtUpWith()
        {
            var caster = new GameObject("Caster");
            caster.transform.position = EmptyGround;
            try
            {
                // The caster walks away perpendicular to the throw for the whole flight. The
                // return leg re-aims every step, so it must still land in the hand.
                var flight = Fly(caster.transform, 24f, 10f, Vector2.right, beforeStep: _ =>
                    caster.transform.position += new Vector3(0f, 4f * Step, 0f));

                Assert.IsTrue(flight.turned);
                Assert.LessOrEqual(Vector3.Distance(flight.lastPos, caster.transform.position), 1f,
                    "a moving caster never caught the blade");
            }
            finally { Object.DestroyImmediate(caster); }
        }

        [Test]
        public void ABladeWhoseCasterVanishesDoesNotOutliveTheThrow()
        {
            var caster = new GameObject("Caster");
            caster.transform.position = EmptyGround;
            var go = new GameObject("BoomerangOrphan");
            go.transform.position = caster.transform.position;
            go.AddComponent<Rigidbody2D>();
            var boom = go.AddComponent<BoomerangProjectile>();
            boom.Initialize(caster.transform, Vector2.right, 24f, 24f, 10f, 10f, 0.5f, false, 0, Color.white);

            Object.DestroyImmediate(caster);

            for (int i = 0; i < 200 && boom != null; i++) boom.Step(Step);

            Assert.IsTrue(go == null, "the blade survived its caster");
        }

        // ── the spawn rig ────────────────────────────────────────────────────────────

        [Test]
        public void TheExecutorTakesTheBallProjectileOffTheClone()
        {
            var casterGo = new GameObject("Caster");
            casterGo.transform.position = EmptyGround;
            var spellCaster = casterGo.AddComponent<SpellCaster>();
            ProjectilePrefabFactory.EnsureFireballPrefab(spellCaster);

            var ctx = new SpellContext
            {
                Spell = Shipped(),
                Caster = casterGo.transform,
                Direction = Vector2.right,
                TargetLayers = 0,
                ProjectilePrefab = spellCaster.ProjectilePrefab,
            };

            new BoomerangExecutor().Execute(ctx);

            var boom = Object.FindObjectOfType<BoomerangProjectile>();
            Assert.IsNotNull(boom, "the executor spawned no boomerang");
            Assert.IsNull(boom.GetComponent<Projectile>(),
                "the shared ball prefab's Projectile rode along on the clone. Uninitialised it "
                + "expires at range 20 and lifetime 3, which destroyed the blade in mid-air "
                + "before it could turn round.");

            Object.DestroyImmediate(boom.gameObject);
            Object.DestroyImmediate(casterGo);
            var prefab = GameObject.Find("FireballPrefab");
            if (prefab != null) Object.DestroyImmediate(prefab);
        }

        /// <summary>
        /// One clean boomerang rig on a fresh GameObject.
        ///
        /// <para>Built by hand rather than through <c>SetElement</c>. The Editor may or may not
        /// run <c>Awake</c> on an added component depending on version and context, and
        /// <c>ClearVisual</c> tears its old layers down with <c>Object.Destroy</c>, which is
        /// DEFERRED — so a rebuild inside a single Edit Mode call leaves the previous rig's
        /// children in place and the assertions below end up reading whichever palette happened
        /// to be first. Seeding the serialized field and calling <c>BuildVisual</c> once gives
        /// exactly one set of layers.</para>
        /// </summary>
        private static GameObject BuildRig(string name)
        {
            var go = new GameObject(name);
            var visual = go.AddComponent<ElementalProjectileVisual>();
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(go.transform.GetChild(i).gameObject);

            var type = typeof(ElementalProjectileVisual);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            type.GetField("element", flags).SetValue(visual, SpellElement.Boomerang);
            type.GetMethod("BuildVisual", flags).Invoke(visual, null);
            return go;
        }

        [Test]
        public void EveryLayerOfTheRigDrawsOnTheProjectilesSortingLayer()
        {
            var go = BuildRig("RigUnderTest");
            try
            {
                var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
                Assert.Greater(renderers.Length, 1, "the rig built no layers");

                int drawn = 0;
                foreach (var sr in renderers)
                {
                    if (sr.gameObject == go) continue; // the prefab's placeholder, deliberately off
                    drawn++;
                    Assert.AreEqual(SortingConfig.LAYER_PROJECTILES, sr.sortingLayerName,
                        $"{sr.name} draws on '{sr.sortingLayerName}'. Entities sits below "
                        + "Decorations, WallsTop, ObjectsHigh, Projectiles and VFX, so a spell in "
                        + "flight rendered under every wall top on screen.");
                    Assert.Less(sr.sortingOrder, 100,
                        $"{sr.name} has order {sr.sortingOrder} — Z_SKY is a Z DEPTH, not a "
                        + "sorting order");
                }
                Assert.Greater(drawn, 1);

                // The blade accent is the one layer with a silhouette and stays on the alpha
                // material; everything else is light and must be able to blow out.
                var accent = go.transform.Find("Aura/Accent");
                Assert.IsNotNull(accent, "the accent layer is missing");
                Assert.AreSame(ElementalSprites.SharedUnlitMaterial,
                    accent.GetComponent<SpriteRenderer>().sharedMaterial,
                    "the accent draws a shape, so it must stay on the alpha material");

                var glow = go.transform.Find("Aura/Glow");
                Assert.IsNotNull(glow, "the glow layer is missing");
                Assert.AreSame(ElementalSprites.SharedAdditiveMaterial,
                    glow.GetComponent<SpriteRenderer>().sharedMaterial,
                    "Sprite-Unlit-Default declares no _SrcBlend, so a glow on it cannot blow out");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheTrailHangsOffTheAuraSoASpinningBladeStillTrailsBehindItself()
        {
            var go = BuildRig("RigUnderTest");
            try
            {
                var aura = go.transform.Find("Aura");
                Assert.IsNotNull(aura,
                    "the rig has no non-spinning container. The ghost trail hangs at negative "
                    + "local X and the stretch is applied on local X, so parenting them to a "
                    + "root that spins makes the trail orbit the blade instead of following it.");
                Assert.IsNotNull(aura.Find("Ghost0"), "the ghost trail is not under the aura");
                Assert.IsNotNull(aura.Find("Halo"), "the halo is not under the aura");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── the red saber identity ───────────────────────────────────────────────────

        /// <summary>Degrees from pure red, the short way round the wheel.</summary>
        private static float HueDistanceFromRed(Color c)
        {
            Color.RGBToHSV(c, out float hue, out _, out _);
            float degrees = hue * 360f;
            return Mathf.Min(degrees, 360f - degrees);
        }

        [Test]
        public void EveryLitLayerOfTheBladeIsRed()
        {
            var go = BuildRig("RigUnderTest");
            try
            {
                // The hot core is deliberately NOT tested for hue: it is near-white, which is
                // what makes the rest read as a saber rather than as a red blob, and a
                // near-achromatic colour has no meaningful hue to assert.
                foreach (var layer in new[] { "Aura/Halo", "Aura/Glow", "Aura/Core", "Aura/Ghost0" })
                {
                    var sr = go.transform.Find(layer).GetComponent<SpriteRenderer>();
                    Color.RGBToHSV(sr.color, out _, out float saturation, out _);
                    Assert.Less(HueDistanceFromRed(sr.color), 20f,
                        $"{layer} is {HueDistanceFromRed(sr.color):F0} degrees off red");
                    Assert.Greater(saturation, 0.6f, $"{layer} is washed out, not an intense red");
                }

                var hotCore = go.transform.Find("Aura/HotCore").GetComponent<SpriteRenderer>();
                Color.RGBToHSV(hotCore.color, out _, out float coreSaturation, out float coreValue);
                Assert.Less(coreSaturation, 0.25f,
                    "the hot core must stay near-white — a saber reads as a white bar inside a "
                    + "coloured bloom, and a fully saturated core throws that away");
                Assert.Greater(coreValue, 0.9f, "the hot core must be the brightest thing in the rig");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheGatheredCastFlourishIsTheSameRed()
        {
            var spell = Shipped();
            var gather = ElementPalette.For(SpellElement.Arcane)
                                       .RecolouredTo(SpellCastFlourishFX.ResolveSwatch(spell));

            foreach (var field in new[] { gather.hotCore, gather.core, gather.glow,
                                          gather.halo, gather.accent, gather.lightColor })
            {
                Assert.Less(HueDistanceFromRed(field), 20f,
                    $"the gather drew {field}, {HueDistanceFromRed(field):F0} degrees off the blade");
            }

            Assert.Less(HueDistanceFromRed(BoomerangExecutor.ResolveTint(spell)), 20f,
                "the blade tint and the gather must be the same red");
            Assert.Less(HueDistanceFromRed(ElementPalette.For(SpellElement.Boomerang).core), 20f,
                "the projectile palette drifted off the swatch the flourish gathers");
        }

        // ── the shipped data ─────────────────────────────────────────────────────────

        [Test]
        public void TheShippedThrowIsAuthoredInWorldUnits()
        {
            var spell = Shipped();

            // It shipped at speed 82.5 and range 26.25 — the fourth sighting of the Python
            // pixel scale after wallWidth, the totem radius and the vortex radius. On a camera
            // 33.33 units wide that crossed the screen in 0.40s and turned 79% of a screen out.
            Assert.LessOrEqual(spell.range, 16f,
                $"range {spell.range} is more than half the camera width — that is not a throw, "
                + "it is a screen-clearing beam");
            Assert.GreaterOrEqual(spell.range, 4f, "the throw has no reach worth casting");
            Assert.LessOrEqual(spell.speed, 40f,
                $"speed {spell.speed} is far outside the projectile family (16 to 30)");
            Assert.GreaterOrEqual(spell.speed, 8f);
        }

        [Test]
        public void TheCooldownOutlastsTheThrowItLimits()
        {
            var spell = Shipped();

            // maxInstances is 1 and nothing enforces it for a boomerang — it is not a tracked
            // persistent effect — so the only thing that stops two being in the air at once is
            // the cooldown being longer than a round trip. The trip is the ARC, not the chord:
            // the blade flies its authored speed along a bowed path a third longer than the
            // straight line, and a cooldown sized off `2 * range / speed` would be short by
            // exactly that much.
            float roundTrip = 2f * spell.range / Mathf.Max(0.01f, spell.speed)
                              * BoomerangProjectile.ArcPathFactor;
            Assert.GreaterOrEqual(spell.cooldownDuration, roundTrip,
                $"cooldown {spell.cooldownDuration:F2}s is shorter than the {roundTrip:F2}s round "
                + "trip, so the spell claims maxInstances 1 while allowing two in flight");
        }

        [Test]
        public void TheShippedSwatchIsAuthoredAndEveryConsumerAgreesOnIt()
        {
            var spell = Shipped();

            Assert.IsFalse(KiPalette.IsUnauthored(spell.particleColor),
                "the boomerang left particleColor at opaque white, so the cast flourish had no "
                + "element and no swatch and gathered ARCANE VIOLET in front of a coloured blade");

            Assert.AreEqual(BoomerangExecutor.ResolveTint(spell), SpellCastFlourishFX.ResolveSwatch(spell),
                "the gather and the blade must be asked the same question");

            Color.RGBToHSV(BoomerangExecutor.ResolveTint(spell), out _, out _, out float value);
            Assert.Greater(value, 0.4f,
                "every flourish layer is additive, where a near-black swatch does not dim, it "
                + "disappears");
        }

        [Test]
        public void AnUnauthoredBoomerangFallsBackToItsOwnPaletteNotToRawWhite()
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            try
            {
                spell.spellKey = "synthetic_boomerang";
                spell.type = SpellType.Boomerang;
                spell.particleColor = Color.white; // the project-wide "nobody touched this" sentinel

                Color resolved = BoomerangExecutor.ResolveTint(spell);
                Assert.IsFalse(KiPalette.IsUnauthored(resolved),
                    "an unauthored boomerang must fall back to the blade's own colour. The old "
                    + "test here was against Color.clear, which no shipped spell carries, so the "
                    + "fallback was unreachable code.");
                Assert.AreEqual(ElementPalette.For(SpellElement.Boomerang).core, resolved);
            }
            finally { Object.DestroyImmediate(spell); }
        }
    }
}
