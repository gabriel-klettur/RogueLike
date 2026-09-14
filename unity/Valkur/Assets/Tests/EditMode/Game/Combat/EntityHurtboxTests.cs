using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells.Debugging;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// The hurtbox: capsules that follow the drawn body, can be hit, and touch nothing.
    ///
    /// <para>These assert the COMPOSITION — data to capsule, capsule to query, query to one hit
    /// per entity — because each half being right alone is exactly how a hurtbox ships that
    /// nothing can hit, or that a fireball hits five times.</para>
    /// </summary>
    public class EntityHurtboxTests
    {
        private const int NpcLayer = 9;
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // -- Fitter ---------------------------------------------------------------

        [Test]
        public void Fitter_ATallFigure_GetsCapsulesInsideItsInk_AndIgnoresADetachedFlash()
        {
            // 40 x 80: a 16-wide body standing on the ground line, and a detached 4x4 "spell
            // flash" floating off to the side that must NOT become something a fireball can hit.
            const int w = 40, h = 80;
            var alpha = new byte[w * h];
            Fill(alpha, w, 12, 0, 28, 70);
            Fill(alpha, w, 34, 60, 38, 64);

            Assert.IsTrue(HurtShapeFitter.TryFit(alpha, w, h, facesEast: true, out var fit));
            Assert.That(fit.Shapes.Count, Is.InRange(1, 3));

            foreach (var s in fit.Shapes)
            {
                float cx = s.center.x * (w * 0.5f) + w * 0.5f;
                Assert.That(cx, Is.InRange(10f, 30f), "Every capsule sits on the body, none on the flash.");
                Assert.Less(s.size.x, 0.6f, "A capsule is body-wide, not frame-wide.");
            }
            Assert.That(fit.InkHeightFraction, Is.InRange(0.8f, 0.95f));
        }

        [Test]
        public void Fitter_ALongQuadruped_IsCutIntoSeveralCapsulesAlongItsLength()
        {
            // 120 x 40: a long body low down plus a raised head at the east end.
            const int w = 120, h = 40;
            var alpha = new byte[w * h];
            Fill(alpha, w, 5, 4, 95, 20);
            Fill(alpha, w, 95, 12, 118, 34);

            Assert.IsTrue(HurtShapeFitter.TryFit(alpha, w, h, facesEast: true, out var fit));
            Assert.GreaterOrEqual(fit.Shapes.Count, 2, "A dragon is not one box.");

            // The forward-most capsule must be higher than the body: it follows the head.
            HurtShape head = fit.Shapes[0], body = fit.Shapes[0];
            foreach (var s in fit.Shapes)
            {
                if (s.center.x > head.center.x) head = s;
                if (s.center.x < body.center.x) body = s;
            }
            Assert.Greater(head.center.y, body.center.y);
        }

        [Test]
        public void Fitter_MirroredHalves_ShareOneForwardNumber()
        {
            const int w = 60, h = 60;
            var east = new byte[w * h];
            Fill(east, w, 30, 0, 50, 50);
            var west = new byte[w * h];
            Fill(west, w, 10, 0, 30, 50);   // the same figure, mirrored

            Assert.IsTrue(HurtShapeFitter.TryFit(east, w, h, true, out var e));
            Assert.IsTrue(HurtShapeFitter.TryFit(west, w, h, false, out var wf));
            Assert.AreEqual(e.Shapes.Count, wf.Shapes.Count);
            for (int i = 0; i < e.Shapes.Count; i++)
                Assert.AreEqual(e.Shapes[i].center.x, wf.Shapes[i].center.x, 0.05f,
                    "X is forward along the facing, so a mirror measures the same number.");
        }

        // -- Profile --------------------------------------------------------------

        [Test]
        public void Profile_ResolvesFrameRow_ThenCreature_ThenAutomatic_AndNeverEmpty()
        {
            var profile = new EntityCollisionProfile();
            var scratch = new List<HurtShape>();

            profile.ResolveShapes("x_idle_e0", scratch);
            Assert.AreEqual(1, scratch.Count);
            Assert.AreEqual(HurtShapeSource.Automatic, profile.SourceFor("x_idle_e0"));

            profile.hurtShapes.Add(new HurtShape(Vector2.zero, new Vector2(0.3f, 0.3f)));
            Assert.AreEqual(HurtShapeSource.Creature, profile.SourceFor("x_idle_e0"));

            profile.SetFrameShapes("x_idle_e0", new[] { new HurtShape(new Vector2(0.1f, 0f), new Vector2(0.2f, 0.4f)),
                                                        new HurtShape(new Vector2(0.1f, 0.5f), new Vector2(0.2f, 0.2f)) }, handTuned: true);
            profile.ResolveShapes("x_idle_e0", scratch);
            Assert.AreEqual(2, scratch.Count);
            Assert.AreEqual(HurtShapeSource.HandTuned, profile.SourceFor("x_idle_e0"));

            profile.SetFrameShapes("x_idle_e0", new HurtShape[0], handTuned: true);
            Assert.IsNull(profile.FindFrame("x_idle_e0"), "An emptied row is removed, never left to shadow the creature's shapes with nothing.");
        }

        // -- Rig ------------------------------------------------------------------

        [Test]
        public void Rig_PlacesCapsulesOnTheDrawnBody_MirroredByTheFrameName()
        {
            var profile = new EntityCollisionProfile();
            // One capsule on the FRONT half of the frame.
            profile.SetFrameShapes("probe_idle_e0", new[] { new HurtShape(new Vector2(0.5f, 0f), new Vector2(0.25f, 0.5f)) }, false);
            profile.SetFrameShapes("probe_idle_w0", new[] { new HurtShape(new Vector2(0.5f, 0f), new Vector2(0.25f, 0.5f)) }, false);

            var go = MakeEntity("probe_idle_e0", out var renderer, out var footprint);
            var rig = EntityColliderConfigurator.InstallRig(go, profile, renderer, footprint);

            Assert.AreEqual(1, rig.HurtboxCount);
            var capsule = rig.GetHurtbox(0);
            // Frame is 2 x 2 units, pivot bottom-centre: centre (0, 1); +0.5 forward = +0.5 u east.
            Assert.AreEqual(0.5f, capsule.bounds.center.x, 0.02f);
            Assert.AreEqual(1f, capsule.bounds.center.y, 0.02f);
            Assert.AreEqual(NpcLayer, capsule.gameObject.layer, "A capsule on another layer is one no target mask contains.");
            Assert.AreEqual(~0, capsule.excludeLayers.value, "A hurtbox must touch nothing.");
            Assert.AreNotSame(go, capsule.gameObject, "Hurtboxes live on a child so GetComponent<Collider2D>() on the root still answers the feet.");

            renderer.sprite = MakeSprite("probe_idle_w0");
            rig.Refresh(force: false);
            Assert.AreEqual(-0.5f, rig.GetHurtbox(0).bounds.center.x, 0.02f, "The west half puts the front capsule on the west.");
        }

        [Test]
        public void Rig_AQueryFindsTheHurtbox_AndTheFilterFoldsItToOneHitPerEntity()
        {
            var profile = new EntityCollisionProfile();
            profile.SetFrameShapes("probe_idle_e0", new[]
            {
                new HurtShape(new Vector2(0f, -0.5f), new Vector2(0.4f, 0.5f)),
                new HurtShape(new Vector2(0f, 0.5f), new Vector2(0.4f, 0.5f)),
            }, false);

            var go = MakeEntity("probe_idle_e0", out var renderer, out var footprint);
            go.transform.position = new Vector3(4000f, 4000f, 0f);
            var rig = EntityColliderConfigurator.InstallRig(go, profile, renderer, footprint);
            Physics2D.SyncTransforms();

            // A circle at chest height reaches both capsules and not the feet.
            var raw = Physics2D.OverlapCircleAll(new Vector2(4000f, 4001f), 1.2f, 1 << NpcLayer);
            Assert.GreaterOrEqual(raw.Length, 2, "Precondition: the raw query sees several colliders of one body.");

            var folded = SpellProbe.OverlapCircleAll(new Vector2(4000f, 4001f), 1.2f, 1 << NpcLayer, SpellDebugRole.Damage);
            Assert.AreEqual(1, folded.Length, "A splash must hit a body once, not once per capsule.");
            Assert.IsTrue(EntityColliderRig.IsHurtbox(folded[0]), "The survivor is a capsule, not the footprint.");

            var buffer = new Collider2D[8];
            int n = SpellProbe.OverlapCircleNonAlloc(new Vector2(4000f, 4001f), 1.2f, buffer, 1 << NpcLayer, SpellDebugRole.Damage);
            Assert.AreEqual(1, n);
            Assert.IsNull(buffer[1], "Folded slots are cleared, never left holding a stale collider.");
            Assert.AreSame(rig, go.GetComponent<EntityColliderRig>());
        }

        [Test]
        public void Rig_FollowsTheFootprint_WhenSomethingSwitchesTheBodyOff()
        {
            var go = MakeEntity("probe_idle_e0", out var renderer, out var footprint);
            var rig = EntityColliderConfigurator.InstallRig(go, null, renderer, footprint);
            Assert.Greater(rig.HurtboxCount, 0);

            // UnconsciousState and the corpse cleanup switch colliders off from outside.
            footprint.enabled = false;
            rig.Refresh(force: false);
            Assert.AreEqual(0, rig.HurtboxCount, "A corpse must not be hittable because its capsules came back on.");

            footprint.enabled = true;
            rig.Refresh(force: false);
            Assert.Greater(rig.HurtboxCount, 0);
        }

        [Test]
        public void EntityBody_ProbePoints_ReachTheHead_NotOnlyTheCentre()
        {
            var profile = new EntityCollisionProfile();
            profile.SetFrameShapes("probe_idle_e0", new[] { new HurtShape(new Vector2(0f, 0f), new Vector2(0.3f, 0.9f)) }, false);
            var go = MakeEntity("probe_idle_e0", out var renderer, out var footprint);
            EntityColliderConfigurator.InstallRig(go, profile, renderer, footprint);

            var points = new List<Vector2>();
            EntityBody.ProbePoints(go, new Vector2(5f, 1f), points);
            float top = float.NegativeInfinity;
            foreach (var p in points) top = Mathf.Max(top, p.y);
            Assert.Greater(top, 1.4f, "A tall capsule is sampled up its spine, so a blow at head height can land.");
        }

        // -- Helpers --------------------------------------------------------------

        private static void Fill(byte[] alpha, int w, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    alpha[y * w + x] = 255;
        }

        private GameObject MakeEntity(string frame, out SpriteRenderer renderer, out Collider2D footprint)
        {
            var go = new GameObject("HurtboxProbe");
            go.layer = NpcLayer;
            _created.Add(go);
            renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = MakeSprite(frame);
            footprint = EntityColliderConfigurator.ConfigureNpcFootprint(go, renderer);
            return go;
        }

        private Sprite MakeSprite(string name)
        {
            var tex = new Texture2D(32, 32);
            _created.Add(tex);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0f), 16f);
            sprite.name = name;
            _created.Add(sprite);
            return sprite;
        }
    }
}
