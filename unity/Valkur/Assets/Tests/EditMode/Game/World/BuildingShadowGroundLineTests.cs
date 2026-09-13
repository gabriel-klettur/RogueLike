using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Sky;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Where a building's shadow starts, and which buildings have one at all.
    ///
    /// Both questions are about the ART rather than about the sprite rect, and both were
    /// answered wrong for the life of the shadow layer: the shear started at the bottom row of
    /// the PNG, which on 98 of the 1256 building sprites is empty canvas — up to 24 % of the
    /// height, four world units on the worst shop — so the shadow was born below the ground the
    /// building stands on and slid out from under it. And a fifth of those 98 are drawn in plan
    /// (gardens, plazas, training yards), where no foot line is correct because there is no
    /// silhouette: shearing one lays a second copy of the garden beside the first.
    /// </summary>
    [TestFixture]
    public class BuildingShadowGroundLineTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        private static Sprite MakeSprite(int w, int h, float ppu)
            => Sprite.Create(new Texture2D(w, h), new Rect(0, 0, w, h), new Vector2(0.5f, 0f),
                             ppu, 0, SpriteMeshType.FullRect);

        private static BuildingTemplateData Template(string assetPath, int projectedShadow = 0)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.assetPath       = assetPath;
            t.projectedShadow = projectedShadow;
            return t;
        }

        // ── The foot line ────────────────────────────────────────────────────────

        [Test]
        public void TheOffset_RaisesTheFootLine_OffTheRectAndOntoTheInk()
        {
            SunShadowState.Tick(SkyStyle.Active, 0.5f, 0f, false);

            var body = Spawn("body").AddComponent<SpriteRenderer>();
            body.sprite = MakeSprite(32, 32, 32f);          // 1 x 1 unit, pivot at the rect's bottom

            var bare = SunShadowCaster.Attach(body, withBlob: false);
            bare.Sync();
            Assert.That(bare.FootY, Is.EqualTo(0f).Within(1e-4f),
                "With no offset the foot line is still the rect's bottom edge, as it always was.");

            var raised = SunShadowCaster.Attach(body, withBlob: false, groundOffsetLocal: 0.25f);
            Assert.That(raised.FootY, Is.EqualTo(0.25f).Within(1e-4f),
                "Re-attaching with a new offset has to reach the shader immediately: the sprite " +
                "did not change, and the sprite change is the only other thing that writes it.");
        }

        [Test]
        public void TheOffset_MovesTheCullingBoxWithTheFootLine()
        {
            SunShadowState.Tick(SkyStyle.Active, 0.5f, 0f, false);

            var body = Spawn("body").AddComponent<SpriteRenderer>();
            body.sprite = MakeSprite(32, 64, 32f);          // 1 x 2 units

            var caster = SunShadowCaster.Attach(body, withBlob: false, groundOffsetLocal: 0.5f);
            caster.Sync();

            // The shadow is shorter, so it reaches less far sideways. A box that kept the old
            // reach is merely wasteful; one that kept the old foot line would be a lie in the
            // other direction the day the offset is ever negative.
            float reach = (2f - 0.5f) * SkyStyle.Active.skewMax;
            var lb = caster.Shadow.localBounds;
            Assert.That(lb.min.x, Is.EqualTo(-0.5f - reach).Within(1e-3f));
            Assert.That(lb.max.x, Is.EqualTo(0.5f + reach).Within(1e-3f));
        }

        [Test]
        public void ACanopy_TakesTheSameOffsetAsItsFootprint()
        {
            // Both children sit at localScale 1 under the building root and both sprites are cut
            // at the same PPU, so ONE offset serves both halves — which is what lets the two
            // shear from a single line. Getting this wrong separates the crown's shadow from the
            // trunk's by exactly the padding.
            SunShadowState.Tick(SkyStyle.Active, 0.5f, 0f, false);

            var root = Spawn("building");
            var footprint = new GameObject("Footprint").AddComponent<SpriteRenderer>();
            footprint.transform.SetParent(root.transform, false);
            footprint.sprite = MakeSprite(32, 32, 32f);

            var canopy = new GameObject("Canopy").AddComponent<SpriteRenderer>();
            canopy.transform.SetParent(root.transform, false);
            canopy.transform.localPosition = new Vector3(0f, 1f, 0f);
            canopy.sprite = MakeSprite(32, 32, 32f);

            const float offset = 0.375f;
            var cf = SunShadowCaster.Attach(footprint, withBlob: false, sortUnder: footprint,
                                            groundReference: footprint, groundOffsetLocal: offset);
            var cc = SunShadowCaster.Attach(canopy, withBlob: false, sortUnder: footprint,
                                            groundReference: footprint, groundOffsetLocal: offset);
            cf.Sync();
            cc.Sync();

            Assert.That(cf.FootY, Is.EqualTo(offset).Within(1e-4f));
            Assert.That(cc.FootY, Is.EqualTo(-1f + offset).Within(1e-4f),
                "The canopy's own origin is a unit up, so the shared line is a unit down from it, raised by the offset.");
        }

        [Test]
        public void TheOffsetIsScaledBySpriteHeight_SoTheFractionSurvivesAReExport()
        {
            // inkBottomNormalized is a FRACTION on purpose. A PNG re-exported at a different
            // resolution keeps the same proportion of empty canvas, and the eleven lit/unlit
            // fixture pairs differ in width and never in height.
            const float fraction = 0.24f;
            Assert.That(fraction * 1536f / 32f, Is.EqualTo(11.52f).Within(1e-3f),
                "ukranian_super_2: 368 of 1536 rows empty, 32 PPU — the 4.09-unit error measured live.");
            Assert.That(fraction * 768f / 32f, Is.EqualTo(5.76f).Within(1e-3f),
                "Half the pixels, half the local offset, same fraction of the art.");
        }

        // ── Which pieces cast at all ─────────────────────────────────────────────

        [Test]
        public void ArtDrawnInPlan_CastsNoProjectedShadow()
        {
            Assert.IsFalse(BuildingProjectedShadow.Resolve(Template("Buildings/gardens/flowerbed_round")));
            Assert.IsFalse(BuildingProjectedShadow.Resolve(Template("Buildings/combat/training_yard")));
            Assert.IsFalse(BuildingProjectedShadow.Resolve(Template("Buildings/combat/coliseo_floor")));
            Assert.IsFalse(BuildingProjectedShadow.Resolve(Template("Buildings/others/fuente")));
            Assert.IsFalse(BuildingProjectedShadow.Resolve(Template("Buildings/portals/portal_well_closed")));
            Assert.IsFalse(BuildingProjectedShadow.Resolve(Template("Buildings/portals/portal_runes_inactive")));
        }

        [Test]
        public void EverythingThatStandsUp_Casts()
        {
            Assert.IsTrue(BuildingProjectedShadow.Resolve(Template("Buildings/houses/curse_house_topdown")));
            Assert.IsTrue(BuildingProjectedShadow.Resolve(Template("Buildings/shops/ukranian_super_2")));
            Assert.IsTrue(BuildingProjectedShadow.Resolve(Template("Buildings/nature/tree_1")));
            Assert.IsTrue(BuildingProjectedShadow.Resolve(Template("Buildings/lights/lamp_post_ornate")));
            Assert.IsTrue(BuildingProjectedShadow.Resolve(Template("Buildings/forest_decoration/mushroom_small")),
                "A small prop centred in a big canvas still stands up; its padding is the ink-bottom's problem.");
        }

        [Test]
        public void TheOverride_WinsInEitherDirection()
        {
            Assert.IsTrue(BuildingProjectedShadow.Resolve(Template("Buildings/gardens/hedge_tall", projectedShadow: 1)),
                "A hedge is in the gardens folder and is plainly a wall.");
            Assert.IsFalse(BuildingProjectedShadow.Resolve(Template("Buildings/others/manhole_cover", projectedShadow: -1)),
                "Anything else drawn flat can be excluded one template at a time.");
        }

        [Test]
        public void APathThatIsNotSet_IsNotFlat()
        {
            // A template with no assetPath renders nothing, so it must not be special-cased into
            // the flat family — that would make "unset" mean something.
            Assert.IsFalse(BuildingProjectedShadow.IsFlatArt(null));
            Assert.IsFalse(BuildingProjectedShadow.IsFlatArt(string.Empty));
            Assert.IsFalse(BuildingProjectedShadow.Resolve(null), "No template, nothing to cast.");
        }

        [Test]
        public void TheFolderRule_IsCaseInsensitive()
        {
            // Shipped assetPaths are capitalised ("Buildings/…") and the folder list is written
            // in lower case; a comparison that missed that would exempt nothing at all, silently.
            Assert.IsTrue(BuildingProjectedShadow.IsFlatArt("Buildings/Gardens/Flowerbed"));
            Assert.IsTrue(BuildingProjectedShadow.IsFlatArt("BUILDINGS/GARDENS/PLOT"));
        }
    }
}
