using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The vortex FIELD — the thing that stands in the world once the cast is over.
    /// <see cref="VortexFlourishTests"/> covers the gather that precedes it.
    ///
    /// <para>Everything here guards something that looked like a working vortex while being
    /// the wrong one: a radius carried over from the Python build in its own units, a light
    /// rendered at seventeen times its authored radius, a drawn boundary that had nothing to
    /// do with the circle the force queries, and a field whose colour disagreed with the
    /// gather that had just handed over to it.</para>
    /// </summary>
    public class VortexFieldTests
    {
        private const string Folder = "Assets/_Project/Data/Catalogs/Spells/";
        private static readonly string[] Keys = { "vortex_pull", "vortex_push" };

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static SpellDefinition Load(string key)
            => AssetDatabase.LoadAssetAtPath<SpellDefinition>(Folder + key + ".asset");

        /// <summary>
        /// Build a rig and advance it past its spin-up, so what is measured is the funnel the
        /// player actually sees rather than the empty frame before it climbs.
        /// </summary>
        private VortexFunnelFX Rig(string key, out GameObject host)
        {
            var spell = Load(key);
            host = new GameObject("VortexRigProbe");
            _spawned.Add(host);

            var fx = VortexFunnelFX.Attach(host.transform, spell.radius,
                spell.forceMode == "pull", VortexFieldExecutor.ResolveSwatch(spell));
            int spinUp = Mathf.CeilToInt(VortexFunnelFX.SpinUpSeconds * 60f) + 15;
            for (int frame = 0; frame < spinUp; frame++) fx.Tick(1f / 60f, 1f, 0f);
            return fx;
        }

        [Test]
        public void TheRadiusIsInWorldUnitsAndFitsOnScreen()
        {
            // THE PYTHON-UNITS REGRESSION. Both spells shipped radius 17.5 — a number that was
            // right in the build this game was ported from and became a 35-unit circle here, on
            // a camera 33 units wide. The wall and the totem made the same trip. Nothing about
            // it fails: the field is internally consistent and simply covers the world.
            foreach (var key in Keys)
            {
                var spell = Load(key);
                Assert.IsNotNull(spell, key + " is missing");
                Assert.Greater(spell.radius, 0.5f, key + ": a vortex smaller than a body is unreadable");
                Assert.Less(spell.radius, 8f,
                    key + ": radius " + spell.radius + " world units is over half a screen wide. "
                    + "A value near 17.5 means the Python-era number has come back.");
            }
        }

        [Test]
        public void TheDrawnRingIsTheCircleTheForceQueries()
        {
            // The funnel is narrow where it touches down, so its silhouette says nothing about
            // reach. The ground ring is the only piece that does, and a ring drawn at some
            // other radius is a promise the spell does not keep.
            foreach (var key in Keys)
            {
                GameObject host;
                var fx = Rig(key, out host);

                var ring = host.transform.Find("GroundRing");
                Assert.IsNotNull(ring, key + ": no ground ring, so nothing states the reach");

                var renderer = ring.GetComponent<SpriteRenderer>();
                // ElementalSprites.Ring peaks at normalized radius 0.78 of its own extent.
                float drawn = renderer.bounds.size.x * 0.5f * 0.78f;
                Assert.AreEqual(fx.GroundRadius, drawn, 0.02f,
                    key + ": ring drawn at " + drawn + " against a force radius of " + fx.GroundRadius);
                Assert.Greater(renderer.color.a, 0.05f, key + ": the ring is invisible");
            }
        }

        [Test]
        public void TheRigNeverScalesItsOwnRoot()
        {
            // A Light2D under a scaled transform renders at its authored radius TIMES that
            // scale, silently. The old rig scaled its root by the radius and lit 367 world
            // units off a 21-unit light. Absolute child sizes are the fix, and this is what
            // stops someone reaching for the convenient one-line scale again.
            foreach (var key in Keys)
            {
                GameObject host;
                Rig(key, out host);

                Assert.AreEqual(1f, host.transform.lossyScale.x, 1e-4f, key + ": the root is scaled");
                Assert.AreEqual(1f, host.transform.lossyScale.y, 1e-4f, key + ": the root is scaled");

                foreach (var component in host.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component.GetType().Name != "Light2D") continue;
                    Assert.AreEqual(1f, component.transform.lossyScale.x, 1e-4f,
                        key + ": the light sits under a scaled transform, so its radius is a lie");
                }
            }
        }

        [Test]
        public void TheFunnelIsAConeStandingOnTheGround()
        {
            foreach (var key in Keys)
            {
                GameObject host;
                var fx = Rig(key, out host);

                // Named off BandCount, never a literal: "Band8" was the top of a nine-band
                // stack and the MIDDLE of an eighteen-band one, so a hard-coded index quietly
                // stops testing the flare and starts testing the waist.
                var neck = host.transform.Find("Band0");
                var flare = host.transform.Find("Band" + (fx.BandCount - 1));
                Assert.IsNotNull(neck, key + ": no bands");
                Assert.IsNotNull(flare, key + ": the stack is shorter than BandCount claims");

                Assert.Greater(flare.localScale.x, neck.localScale.x * 2f,
                    key + ": base and top are near enough the same width — that is a column, not a funnel");
                Assert.AreEqual(0f, neck.localPosition.y, 0.01f,
                    key + ": the neck has left the ground, so the vortex is floating");
                Assert.Greater(flare.localPosition.y, fx.GroundRadius,
                    key + ": wider than it is tall reads as a whirlpool");

                // Drawn flat, because a horizontal circle seen from this camera is an ellipse.
                Assert.Less(flare.localScale.y, flare.localScale.x * 0.5f,
                    key + ": the rings are not squashed, so they read as vertical hoops");
            }
        }

        [Test]
        public void ThickeningOrMultiplyingTheBandsDoesNotBrightenTheColumn()
        {
            // Every band is additive, so a pixel receives the SUM of the bands over it. TWO
            // independent dials feed that sum — how MANY bands there are and how much area each
            // one covers — and either one raised without its compensation doubles the light
            // instead of the detail. The column then washes out to white, which costs the spell
            // the red/blue identity that is the only thing separating pull from push at a
            // glance. Measured: 7.97 with neither compensation, 3.98 with the count one alone,
            // 1.89 with both.
            GameObject host;
            var fx = Rig("vortex_pull", out host);

            float summed = 0f;
            for (int i = 0; i < fx.BandCount; i++)
            {
                var band = host.transform.Find("Band" + i).GetChild(0).GetComponent<SpriteRenderer>();
                summed += band.color.a;
            }

            Assert.Less(summed, fx.BandCount * 0.2f,
                "summed band alpha is " + summed + " over " + fx.BandCount + " bands — it is "
                + "tracking the count, so the per-band normalisation is gone");
            Assert.Less(summed, 3.5f, "the column is brighter than the tuned total");
            Assert.Greater(summed, 0.8f, "the column has faded to nothing");
        }

        [Test]
        public void TheRingsAreDrawnThickEnoughToReadAsWalls()
        {
            // A hairline ring reads as a wireframe rather than as moving air. What sets the
            // drawn weight is `thickness / BandRadius` — the sprite is scaled until its line
            // lands on the wanted world radius, so BOTH have to move together and neither
            // number means anything alone.
            TornadoSprites.EnsureAll();

            float total = 0f;
            for (int variant = 0; variant < TornadoSprites.BandVariants; variant++)
            {
                var texture = TornadoSprites.Band(variant).texture;
                int size = texture.width;
                float centre = size * 0.5f;

                float inner = -1f, outer = -1f;
                for (int x = (int)centre; x < size; x++)
                {
                    if (texture.GetPixel(x, (int)centre).a <= 0.02f) continue;
                    float r = (x + 0.5f - centre) / centre;
                    if (inner < 0f) inner = r;
                    outer = r;
                }

                Assert.Greater(inner, 0f, "variant " + variant + " draws nothing along its radius");
                // The band must not run off the edge of its own texture: the normalized space
                // stops at 1.0 on the axes, and a band reaching past it is sliced flat at the
                // four cardinal points and reads as a ring with the corners bitten off.
                Assert.Less(texture.GetPixel(size - 1, (int)centre).a, 0.02f,
                    "variant " + variant + " is clipped by the texture edge; pull BandRadius in");

                total += (outer - inner) / TornadoSprites.BandRadius;
            }

            float mean = total / TornadoSprites.BandVariants;
            Assert.Greater(mean, 0.13f,
                "rings are " + mean.ToString("F3") + " of their own radius thick, back near the "
                + "0.087 they were before being doubled");
            Assert.Less(mean, 0.34f, "the rings are so thick the funnel is a solid cone");
        }

        [Test]
        public void BothVorticesLastLongEnoughToBeWeatherAndCostWhatThatIsWorth()
        {
            foreach (var key in Keys)
            {
                var spell = Load(key);
                Assert.GreaterOrEqual(spell.duration, 6f,
                    key + ": a vortex that is over in two seconds is a burst, not a tornado");

                // THE BALANCE CONSEQUENCE OF A LONG FIELD. With maxInstances 1 a cooldown
                // shorter than the duration means the player can always have one out and can
                // evict their own to reposition it, so an eight-second hard crowd-control lands
                // as a permanent one. The cooldown has to outlast the field.
                Assert.Greater(spell.cooldownDuration, spell.duration,
                    key + ": cooldown " + spell.cooldownDuration + " is shorter than its own "
                    + spell.duration + "s field, so the spell is permanently up");
            }
        }

        [Test]
        public void AVortexTracksAcrossTheGroundButStaysOnItsLeash()
        {
            // A tornado that stands perfectly still for eight seconds gives away that it is a
            // spinning decal, and the longer it lives the more it gives away — at two seconds
            // nobody looked long enough to notice.
            var host = new GameObject("Drifter");
            _spawned.Add(host);
            host.transform.position = new Vector3(100f, 50f, 0f);

            var controller = host.AddComponent<VortexFieldController>();
            controller.Initialize(8f, 3.6f, 24f, true, null, 0, Color.white);

            Vector2 origin = host.transform.position;
            float path = 0f, furthest = 0f, biggestStep = 0f;
            Vector2 previous = origin;

            for (int frame = 0; frame < 480; frame++)      // the full eight seconds
            {
                controller.Drift(1f / 60f, frame / 60f);
                Vector2 now = host.transform.position;
                float step = (now - previous).magnitude;
                path += step;
                biggestStep = Mathf.Max(biggestStep, step);
                furthest = Mathf.Max(furthest, (now - origin).magnitude);
                previous = now;
            }

            Assert.Greater(path, 4f, "the vortex never moved; it is a spinning decal");
            Assert.Less(furthest, 6f,
                "it wandered " + furthest + " units from where it was cast — without a leash an "
                + "eight-second drift simply leaves the fight");

            // It TURNS rather than jittering: a heading resampled every frame produces steps
            // that vary wildly, an integrated one produces a near-constant ground speed.
            Assert.Less(biggestStep, 1.6f / 60f,
                "step size " + biggestStep + " means the heading is being resampled, not turned");
        }

        [Test]
        public void AFollowedVortexIsMovedByWhatItFollowsAndNotByItsOwnDrift()
        {
            // No shipped vortex sets followCaster any more, but the path is authored data the
            // F4 panel still offers, so it has to keep working — and the two movers must never
            // both run, or the drift fights the follow every frame and the funnel lags behind
            // whatever it is supposed to be riding.
            var host = new GameObject("Follower");
            _spawned.Add(host);

            var anchor = new GameObject("Anchor");
            _spawned.Add(anchor);
            anchor.transform.position = new Vector3(10f, 10f, 0f);

            var controller = host.AddComponent<VortexFieldController>();
            controller.Initialize(8f, 3.8f, 30f, false, anchor.transform, 0, Color.white);

            Vector2 before = host.transform.position;
            for (int frame = 0; frame < 240; frame++) controller.Drift(1f / 60f, frame / 60f);
            Assert.AreNotEqual(before, (Vector2)host.transform.position,
                "Drift moved nothing, so the drifting vortices are standing still");

            // Drift is only REACHED from Update when there is nothing to follow. Calling it
            // directly proves the mover is live; that the two are exclusive is a property of
            // Update, so it is pinned on the source.
            const string Controller =
                "Assets/_Project/Scripts/Gameplay/Spells/Controllers/VortexFieldController.cs";
            string source = System.IO.File.ReadAllText(Controller);
            StringAssert.Contains("if (_followTarget != null) transform.position", source,
                "the follow branch has moved; check the drift is still exclusive with it");
            StringAssert.Contains("else Drift(", source,
                "drift and follow are no longer exclusive, so a followed vortex also wanders");
        }

        [Test]
        public void TravellingLeansTheFunnelAndLeavesTheDebrisBehind()
        {
            // Everything is parented to one root, so without these two the whole rig slides
            // rigidly and reads as a decal being dragged over the floor.
            GameObject stillHost, movingHost;
            var still = Rig("vortex_pull", out stillHost);
            var moving = Rig("vortex_pull", out movingHost);

            // Averaged over the run, NOT read off the last frame. Eighteen chips at random
            // angles put about a third of a unit of noise on a single-frame mean, against a
            // lag of roughly six tenths — a snapshot passes or fails on the draw. Same mistake
            // as measuring a travel range from one frame, in a different disguise.
            float movingSum = 0f, stillLeanSum = 0f, movingLeanSum = 0f;
            const int Frames = 180;

            for (int frame = 0; frame < Frames; frame++)
            {
                moving.SetTravel(new Vector2(1.15f, 0f));      // due east, at the drift speed
                moving.Tick(1f / 60f, 1f, 0f);
                still.Tick(1f / 60f, 1f, 0f);

                movingSum += MeanDebrisX(movingHost);
                stillLeanSum += TopBandOffset(stillHost, still.BandCount);
                movingLeanSum += TopBandOffset(movingHost, moving.BandCount);
            }

            float stillLean = stillLeanSum / Frames;
            float movingLean = movingLeanSum / Frames;
            Assert.Greater(movingLean, stillLean + 0.25f,
                "the top of a travelling funnel leans no further than a standing one ("
                + movingLean + " against " + stillLean + ")");

            Assert.Less(movingSum / Frames, -0.1f,
                "the debris of an eastward funnel is not trailing west of it");
        }

        /// <summary>
        /// Mean x of the torn-up ground, in rig space. x is the one axis the ground squash
        /// leaves alone, and the chips are spread over the whole circle, so their orbit
        /// cancels and what survives the average is <c>DebrisLag</c> — how far the plume
        /// trails the funnel that lifted it. Negative means it is trailing west.
        /// </summary>
        private static float MeanDebrisX(GameObject host)
        {
            float total = 0f;
            int counted = 0;

            foreach (var child in host.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("Debris")) continue;
                total += child.localPosition.x;
                counted++;
            }
            return counted == 0 ? 0f : total / counted;
        }

        /// <summary>
        /// Where the flared top sits on the travel axis. The neck is the part touching the
        /// ground and the top is the part being left behind, so the topmost band is the one
        /// whose offset states the lean; it is named off BandCount rather than a literal,
        /// because the stack length is tunable.
        ///
        /// <para>SIGNED, and the caller drives its rig due east, so a lean with the travel is
        /// positive. The sway is the reason: it is the same oscillation on both rigs, so a
        /// signed mean cancels it out of the DIFFERENCE the caller asserts on, while a
        /// distance leaves it in and makes the comparison land on wherever the sway happened
        /// to be when the measuring window opened. Measured across five window phases, the
        /// distance form ran from +0.094 to -0.039 against the caller's 0.25 threshold — it
        /// would have passed or failed correct code on the draw — where the signed form held
        /// 0.197 at every one of them.</para>
        /// </summary>
        private static float TopBandOffset(GameObject host, int bandCount)
        {
            var top = host.transform.Find("Band" + (bandCount - 1));
            Assert.IsNotNull(top, "the band stack is shorter than BandCount claims");
            return top.localPosition.x;
        }

        [Test]
        public void TheDebrisIsTheOneOpaqueLayer()
        {
            // Chips of ground are MATTER, and matter is the only thing in the rig that says the
            // world is being affected rather than merely lit. It is also the reason the material
            // cannot be "unified" with the rest: on the additive material a dark chip adds
            // almost nothing, so the layer would vanish without a single line failing.
            GameObject host;
            Rig("vortex_pull", out host);

            var debris = host.transform.Find("Debris00");
            Assert.IsNotNull(debris, "no ground debris");

            var material = debris.GetComponent<SpriteRenderer>().sharedMaterial;
            StringAssert.DoesNotContain("Additive", material.name,
                "the debris went additive; a dark chip on an additive material is invisible");

            var band = host.transform.Find("Band0").GetChild(0).GetComponent<SpriteRenderer>();
            StringAssert.Contains("Additive", band.sharedMaterial.name,
                "the bands stopped being additive, so the column no longer reads as light");
        }

        [Test]
        public void TheVortexTouchesDownOnItsFirstFrame()
        {
            // Without this the funnel simply starts existing. The ring has to be visible BEFORE
            // the funnel has faded in, which is why its alpha does not scale with `fade` alone.
            var spell = Load("vortex_pull");
            var host = new GameObject("Touchdown");
            _spawned.Add(host);

            var fx = VortexFunnelFX.Attach(host.transform, spell.radius, true,
                VortexFieldExecutor.ResolveSwatch(spell));
            fx.Tick(1f / 60f, 0.02f, 0f);

            var shock = host.transform.Find("Shockwave");
            Assert.IsNotNull(shock, "no touchdown ring");
            Assert.Greater(shock.GetComponent<SpriteRenderer>().color.a, 0.1f,
                "the touchdown is invisible on the frame the vortex arrives");

            for (int frame = 0; frame < 60; frame++) fx.Tick(1f / 60f, 1f, 0f);
            Assert.Less(shock.GetComponent<SpriteRenderer>().color.a, 0.02f,
                "the touchdown ring is still on screen a second later");
        }

        [Test]
        public void TheArcsAreEventsAndNotALamp()
        {
            // Everything else in the rig moves continuously, and continuous motion stops being
            // read after about a second. An arc resets the eye — but only while it is RARE.
            // Measured at the first interval tried, the three arcs were lit on 78 % of frames.
            GameObject host;
            var fx = Rig("vortex_pull", out host);

            var arcs = host.GetComponentsInChildren<LineRenderer>(true);
            Assert.Greater(arcs.Length, 0, "no discharges at all");

            int lit = 0;
            const int frames = 180;
            for (int frame = 0; frame < frames; frame++)
            {
                fx.Tick(1f / 60f, 1f, 0f);
                foreach (var arc in arcs) if (arc.enabled) lit++;
            }

            float duty = lit / (float)frames;
            Assert.Less(duty, 0.60f,
                "arcs are lit " + (duty * 100f).ToString("F0") + "% of frames across "
                + arcs.Length + " renderers — that is a lamp with a flicker, not lightning");
            Assert.Greater(duty, 0.02f, "the arcs never fire, so the layer is dead weight");

            foreach (var arc in arcs)
                StringAssert.Contains("Additive", arc.sharedMaterial.name,
                    "a discharge on the alpha material cannot blow out");
        }

        [Test]
        public void TheGroundLayersStateWhichWayTheForceGoes()
        {
            // The floor is where the force is actually applied, so it is the honest place to
            // say which way it points. A pull whose streaks fly outward tells the player the
            // opposite of what the spell does.
            //
            // Direction is read off the SIGN OF TRAVEL, never off how far out the layer gets.
            // The earlier version compared absolute reach, which passed only because push threw
            // its ground layer 39 % further than pull drew its own in — and that asymmetry was
            // the defect, not the signal: same piece count over more ground, moving faster,
            // spilling outside the ring that states the reach.
            Assert.Less(MeanRadialDrift("vortex_pull", "Streak"), 0f,
                "vortex_pull drives its streaks outward");
            Assert.Greater(MeanRadialDrift("vortex_push", "Streak"), 0f,
                "vortex_push draws its streaks inward, so it reads as a pull");

            Assert.Less(MeanRadialDrift("vortex_pull", "Debris"), 0f,
                "vortex_pull throws ground debris outward");
            Assert.Greater(MeanRadialDrift("vortex_push", "Debris"), 0f,
                "vortex_push never carries debris away from its centre");
        }

        [Test]
        public void PushCoversTheSameGroundAsPullAndNotMore()
        {
            // The two runs must be each other backwards. When they were not, push spread the
            // same sixteen streaks and eighteen chips over 46 % and 29 % more ground — sparser,
            // faster in world units for an unchanged cycle rate, and a third of it outside the
            // circle the ground ring exists to promise. It read as the worse-looking of the two
            // and the cause was not the colour.
            foreach (var layer in new[] { "Streak", "Debris" })
            {
                float pull = TravelSpan("vortex_pull", layer);
                float push = TravelSpan("vortex_push", layer);

                // Normalised by each spell's own radius, since the two author different ones.
                float pullSpan = pull / Load("vortex_pull").radius;
                float pushSpan = push / Load("vortex_push").radius;

                Assert.AreEqual(pullSpan, pushSpan, 0.12f,
                    layer + ": pull covers " + pullSpan.ToString("F2") + " of its radius and push "
                    + pushSpan.ToString("F2") + ". Same count over more ground is thinner and "
                    + "faster for free, and only one of the pair pays for it.");
            }
        }

        /// <summary>
        /// Mean per-frame change in distance from the axis, wraps excluded. Negative means the
        /// layer is closing in. Wraps have to be dropped or a looping layer averages to zero
        /// and every direction assertion passes for the wrong reason.
        /// </summary>
        private float MeanRadialDrift(string key, string prefix)
        {
            GameObject host;
            var fx = Rig(key, out host);

            var previous = new Dictionary<string, float>();
            float total = 0f;
            int counted = 0;

            for (int frame = 0; frame < 180; frame++)
            {
                fx.Tick(1f / 60f, 1f, 0f);
                foreach (var child in host.GetComponentsInChildren<Transform>(true))
                {
                    if (!child.name.StartsWith(prefix)) continue;
                    float radius = RadiusOf(child, prefix);

                    float was;
                    if (previous.TryGetValue(child.name, out was))
                    {
                        float delta = radius - was;
                        if (Mathf.Abs(delta) < 0.5f) { total += delta; counted++; }
                    }
                    previous[child.name] = radius;
                }
            }
            return counted == 0 ? 0f : total / counted;
        }

        /// <summary>Widest minus narrowest the layer gets over a run: the ground it covers.</summary>
        private float TravelSpan(string key, string prefix)
        {
            GameObject host;
            var fx = Rig(key, out host);

            float nearest = float.MaxValue, furthest = 0f;
            for (int frame = 0; frame < 240; frame++)
            {
                fx.Tick(1f / 60f, 1f, 0f);
                foreach (var child in host.GetComponentsInChildren<Transform>(true))
                {
                    if (!child.name.StartsWith(prefix)) continue;
                    float radius = RadiusOf(child, prefix);
                    if (radius < nearest) nearest = radius;
                    if (radius > furthest) furthest = radius;
                }
            }
            return furthest - nearest;
        }

        /// <summary>
        /// Streaks live under the squashed ground plane, so their local position is already in
        /// unsquashed circle coordinates. Debris sits in rig space, where x is the one axis the
        /// ground squash leaves alone and the only one its distance can be read off.
        /// </summary>
        private static float RadiusOf(Transform child, string prefix)
            => prefix == "Streak" ? child.localPosition.magnitude : Mathf.Abs(child.localPosition.x);

        [Test]
        public void TheNeckIsClearedSoWhoeverItWalksOverStaysReadable()
        {
            // The funnel TRACKS, so sooner or later it walks over somebody — and at chest
            // height its radius is 1.17 units against a 0.9-wide character, so whoever it
            // crosses is inside it. Eighteen additive bands over a body wash it out entirely.
            // This mattered first for followCaster, which parked one caster in the neck
            // permanently; drifting makes it everyone's problem instead of one spell's.
            GameObject host;
            var fx = Rig("vortex_push", out host);

            float neck = host.transform.Find("Band0").GetChild(0).GetComponent<SpriteRenderer>().color.a;
            float middle = host.transform.Find("Band" + (fx.BandCount / 2))
                               .GetChild(0).GetComponent<SpriteRenderer>().color.a;

            Assert.Less(neck, middle * 0.35f,
                "the lowest band is as bright as the waist (" + neck + " against " + middle
                + "), so anyone standing in the neck is painted over");
        }

        [Test]
        public void BothVorticesArePlacedAtTheCursorAndTrackOnTheirOwn()
        {
            // They used to differ in HOW they were delivered as well as in what they do:
            // vortex_push rode the caster while vortex_pull was thrown out in front. That made
            // them two spells with two control schemes, and the force direction — the actual
            // difference — was the thing hardest to notice. Same delivery, opposite effect.
            foreach (var key in Keys)
            {
                var spell = Load(key);
                Assert.IsTrue(spell.spawnAtMouse, key + " no longer lands where the player points");
                Assert.IsFalse(spell.followCaster,
                    key + " rides its caster, so it cannot track across the ground");
                Assert.Greater(spell.range, 0f,
                    key + " authors no range, so its cast reach is whatever constant the "
                    + "executor happens to hold and a designer cannot change it");
            }

            Assert.AreEqual("pull", Load("vortex_pull").forceMode);
            Assert.AreEqual("push", Load("vortex_push").forceMode);
        }

        [Test]
        public void AnNpcAimsWithItsFacingAndNeverWithThePlayersCursor()
        {
            // The cursor is a PLAYER concept. A monster casting the same definition must fall
            // back to its facing — silently pointing every NPC cast at the player's mouse would
            // be both wrong and impossible to notice in a log.
            var caster = new GameObject("Monster");     // deliberately not tagged Player
            _spawned.Add(caster);
            caster.transform.position = new Vector3(40f, 40f, 0f);

            var ctx = new SpellContext
            {
                Spell = Load("vortex_pull"),
                Caster = caster.transform,
                Direction = Vector2.right,
            };

            Vector2 landed = SpellTargeting.ResolveGroundTarget(ctx, 10f, 2f);
            Vector2 start = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            Assert.AreEqual(ctx.Spell.range, (landed - start).magnitude, 0.01f,
                "an NPC cast did not land at its own range along its own facing");
            Assert.Greater(landed.x, start.x, "it did not go the way the caster is facing");
        }

        [Test]
        public void ClearingSpawnAtMouseReallyPlacesTheVortexOnItsCaster()
        {
            // `|| isPull` used to sit beside the flag in the executor, so a pull took the
            // offset whether or not the box was ticked. A hard-coded override of authored data
            // makes the field unfalsifiable, which is worse than a wrong default: the panel
            // shows a control that changes nothing.
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.type = SpellType.VortexField;
            spell.forceMode = "pull";
            spell.spawnAtMouse = false;
            spell.range = 10f;
            spell.distance = 0f;

            var caster = new GameObject("Caster");
            _spawned.Add(caster);
            caster.transform.position = new Vector3(60f, 60f, 0f);

            var ctx = new SpellContext
            {
                Spell = spell, Caster = caster.transform, Direction = Vector2.right,
            };

            Vector2 landed = SpellTargeting.ResolveGroundTarget(ctx, 10f, 2f);
            Vector2 start = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            Assert.AreEqual(2f, (landed - start).magnitude, 0.01f,
                "a pull with spawnAtMouse cleared still flew out to its cast range");
            Object.DestroyImmediate(spell);
        }

        [Test]
        public void NoExecutorWorksOutWhereAnAimedSpellLandsOnItsOwn()
        {
            // ONE OWNER. The three executors that honour spawnAtMouse each used to resolve it
            // inline, identically and wrongly — and two of them divided `range` by 16, the
            // Python pixel scale. Duplicating the cursor projection is how two of them end up
            // clamping to different ranges the first time one is touched.
            const string Executors = "Assets/_Project/Scripts/Gameplay/Spells/Executors/";
            foreach (var file in new[] { "VortexFieldExecutor.cs", "PuddleExecutor.cs", "TotemExecutor.cs" })
            {
                string path = Executors + file;
                Assert.IsTrue(System.IO.File.Exists(path), path + " has moved");

                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    string code = line.Trim();
                    if (code.StartsWith("//") || code.StartsWith("///")) continue;
                    StringAssert.DoesNotContain("spawnAtMouse", code,
                        file + " reads spawnAtMouse directly; it belongs to SpellTargeting");
                }
            }
        }

        [Test]
        public void PullAndPushTurnOppositeWays()
        {
            GameObject pullHost, pushHost;
            var pullFx = Rig("vortex_pull", out pullHost);
            Rig("vortex_push", out pushHost);

            string band = "Band" + (pullFx.BandCount / 2);
            float pull = pullHost.transform.Find(band).GetChild(0).localRotation.eulerAngles.z;
            float push = pushHost.transform.Find(band).GetChild(0).localRotation.eulerAngles.z;

            // Both were ticked the same number of frames from the same phase, so the only thing
            // that can separate them is the sign of the spin.
            Assert.AreNotEqual(pull, push,
                "pull and push spun to the same angle — once they are the same shape, the "
                + "direction of rotation is the only thing telling them apart on screen");
        }

        [Test]
        public void TheFieldIsDrawnInTheColourTheGatherJustUsed()
        {
            // These were two different answers to one question: the flourish gathered red for
            // vortex_pull while the field hardcoded arcane violet, so the cast handed over to
            // an effect of a different colour.
            foreach (var key in Keys)
            {
                var spell = Load(key);
                Assert.AreEqual(SpellCastFlourishFX.ResolveSwatch(spell),
                    VortexFieldExecutor.ResolveSwatch(spell),
                    key + ": the gather and the field resolve different colours");
            }

            Color pull = VortexFieldExecutor.ResolveSwatch(Load("vortex_pull"));
            Assert.Greater(pull.r, pull.b, "vortex_pull is not red: " + pull);

            Color push = VortexFieldExecutor.ResolveSwatch(Load("vortex_push"));
            Assert.Greater(push.b, push.r, "vortex_push is not blue: " + push);
        }

        [Test]
        public void NeitherSpellClaimsAParticlePresetNothingReads()
        {
            // The executor used to spawn this preset ON TOP of the rig, a fourth uncoordinated
            // layer over an effect that already draws its own debris. It no longer reads the
            // field, so a value left in it is a control in the F4 editor that does nothing.
            foreach (var key in Keys)
            {
                Assert.IsTrue(string.IsNullOrEmpty(Load(key).vfxPreset),
                    key + " still names a vfxPreset, which no code path reads");
                Assert.IsFalse(SpellFieldRelevance.Applies(Load(key), "vfxPreset"),
                    "the F4 panel still offers vfxPreset for a vortex");
            }
        }

        [Test]
        public void TheExecutorBuildsNoTextureOfItsOwn()
        {
            // It generated a 64x64 spiral Texture2D per cast for a SpriteRenderer the controller
            // disabled on the very next line — so every cast leaked one, and the whole thing was
            // invisible either way.
            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;

            Assert.IsNull(typeof(VortexFieldExecutor).GetMethod("CreateVortexSprite", Flags),
                "the executor is generating a sprite again; the look belongs to VortexFunnelFX");
        }
    }
}
