using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Data;
using Valkur.Gameplay.World.Sky;

namespace Valkur.Tests.EditMode.Game.World.Sky
{
    /// <summary>
    /// The structure of the shadow layer, asserted where Edit Mode can reach it.
    ///
    /// Unity calls no Awake here, so every component is driven through the same public
    /// <c>EnsureBuilt</c> / <c>Sync</c> / <c>Tick</c> seams Play Mode uses; and it performs no
    /// rendering, so what is pinned is WHERE things draw (layer, order, the foot line handed to
    /// the shear) and WHEN (sun up, indoors, weather), never how they look. Those are the
    /// failures that are silent: a shadow on the wrong layer draws behind the ground and nothing
    /// logs it.
    /// </summary>
    [TestFixture]
    public class SkyLayerTests
    {
        private readonly List<GameObject> _spawned = new();
        private bool _cloudsWere, _sunWere;

        [SetUp]
        public void Snapshot()
        {
            _cloudsWere = WorldLookSettings.CloudShadows;
            _sunWere    = WorldLookSettings.SunShadows;
            WorldLookSettings.CloudShadows = true;
            WorldLookSettings.SunShadows   = true;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            WorldLookSettings.CloudShadows = _cloudsWere;
            WorldLookSettings.SunShadows   = _sunWere;
        }

        private GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        private static Sprite MakeSprite(int w, int h, Vector2 pivot)
        {
            var tex = new Texture2D(w, h);
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, 16f, 0, SpriteMeshType.FullRect);
        }

        // ── The cloud layer ───────────────────────────────────────────────────────

        [Test]
        public void TheCloudQuad_DrawsUnderTheSelfLuminousLayers()
        {
            var layer = Spawn("clouds").AddComponent<CloudShadowLayer>();
            layer.EnsureBuilt();

            string expected = CloudShadowLayer.ResolveCloudSortingLayer();
            Assert.That(layer.SortingLayerName, Is.EqualTo(expected));

            // The quad sits directly beneath Projectiles: it darkens ground, buildings and
            // creatures, and never a shot, a spell, a bar or a damage number. Projectiles and
            // VFX are the layers the ambient light also leaves alone, for the same reason.
            var layers = SortingLayer.layers;
            int idx = System.Array.FindIndex(layers, l => l.name == expected);
            Assert.That(idx, Is.GreaterThanOrEqualTo(0), $"'{expected}' is not a sorting layer.");
            Assert.That(layers[idx + 1].name, Is.EqualTo(SortingConfig.LAYER_PROJECTILES),
                "A cloud shadow dimming a fireball is what the day/night cycle refused to do.");

            int ground   = System.Array.FindIndex(layers, l => l.name == SortingConfig.LAYER_GROUND);
            int entities = System.Array.FindIndex(layers, l => l.name == SortingConfig.LAYER_ENTITIES);
            int props6   = System.Array.FindIndex(layers, l => l.name == SortingConfig.PropSortingLayer(SortingConfig.DEFAULT_PROP_Z_TOP));
            Assert.That(idx, Is.GreaterThan(ground).And.GreaterThan(entities).And.GreaterThan(props6),
                "The ground, the creatures and a default canopy all lie under the cloud.");
        }

        [Test]
        public void TheCloudShader_Exists_AndMultiplies()
        {
            var shader = Shader.Find("Valkur/CloudShadow");
            Assert.IsNotNull(shader, "Valkur/CloudShadow is missing.");
            Assert.IsTrue(shader.isSupported);
        }

        [Test]
        public void Clouds_DrawByDay_AndNotAtNight_NorIndoors_NorSwitchedOff()
        {
            var layer = Spawn("clouds").AddComponent<CloudShadowLayer>();
            layer.EnsureBuilt();
            var style = SkyStyle.Active;

            float noon = (style.sunrise + style.sunset) * 0.5f;
            SunShadowState.Tick(style, noon, 0f, false);      // noon, clear, outdoors
            layer.TickClouds(0.016f, 0f, false);
            Assert.IsTrue(layer.IsDrawing, "Noon on a clear day: clouds drift over the world.");
            Assert.That(layer.Strength, Is.EqualTo(style.cloudStrength).Within(1e-3f));

            SunShadowState.Tick(style, 0.0f, 0f, false);      // midnight
            layer.TickClouds(0.016f, 0f, false);
            Assert.IsFalse(layer.IsDrawing, "No sun, no cloud shadow.");

            SunShadowState.Tick(style, 0.5f, 0f, true);       // noon, indoors
            layer.TickClouds(0.016f, 0f, true);
            Assert.IsFalse(layer.IsDrawing, "Under a roof there is no sky.");

            SunShadowState.Tick(style, 0.5f, 0f, false);
            WorldLookSettings.CloudShadows = false;
            layer.TickClouds(0.016f, 0f, false);
            Assert.IsFalse(layer.IsDrawing, "'look clouds off' must switch the quad off, not merely fade it.");
        }

        [Test]
        public void Overcast_ClosesTheSkyOver_AndSoftensTheShadow()
        {
            var layer = Spawn("clouds").AddComponent<CloudShadowLayer>();
            layer.EnsureBuilt();
            var style = SkyStyle.Active;

            SunShadowState.Tick(style, 0.5f, 0f, false);
            layer.TickClouds(0.016f, 0f, false);
            float clearCoverage = layer.Coverage, clearStrength = layer.Strength;

            SunShadowState.Tick(style, 0.5f, 1f, false);
            layer.TickClouds(0.016f, 1f, false);
            Assert.That(layer.Coverage, Is.LessThan(clearCoverage), "More cloud under a storm (a lower threshold).");
            Assert.That(layer.Strength, Is.LessThan(clearStrength), "And a weaker shadow: the grade already darkens the frame.");
        }

        // ── The sun ───────────────────────────────────────────────────────────────

        [Test]
        public void TheSun_IsGoneAtNight_AndIndoors_ButTheBlobStays()
        {
            var style = SkyStyle.Active;

            SunShadowState.Tick(style, 0.5f, 0f, false);
            Assert.That(SunShadowState.Alpha, Is.EqualTo(style.sunShadowAlpha).Within(1e-3f));
            Assert.That(SunShadowState.BlobAlpha, Is.EqualTo(style.blobAlpha).Within(1e-3f));

            SunShadowState.Tick(style, 0.0f, 0f, false);
            Assert.That(SunShadowState.Alpha, Is.EqualTo(0f), "No projected shadow at midnight.");
            Assert.That(SunShadowState.BlobAlpha, Is.GreaterThan(0f), "The contact blob is not the sun's.");

            SunShadowState.Tick(style, 0.5f, 0f, true);
            Assert.That(SunShadowState.Alpha, Is.EqualTo(0f), "No sun under a roof.");
            Assert.That(SunShadowState.BlobAlpha, Is.GreaterThan(0f), "But the feet still touch the floor.");
        }

        [Test]
        public void HeavyWeather_DimsTheSun_WithoutRemovingIt()
        {
            var style = SkyStyle.Active;
            SunShadowState.Tick(style, 0.5f, 0f, false);
            float clear = SunShadowState.Alpha;
            SunShadowState.Tick(style, 0.5f, 1f, false);
            Assert.That(SunShadowState.Alpha, Is.LessThan(clear * 0.5f));
            Assert.That(SunShadowState.Alpha, Is.GreaterThanOrEqualTo(0f));
        }

        // ── The caster ────────────────────────────────────────────────────────────

        [Test]
        public void ACaster_DrawsTheBodysOwnSprite_JustUnderTheBody()
        {
            var body = Spawn("body").AddComponent<SpriteRenderer>();
            body.sprite = MakeSprite(16, 32, new Vector2(0.5f, 0f));
            body.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            body.sortingOrder = 4000;

            SunShadowState.Tick(SkyStyle.Active, 0.5f, 0f, false);
            var caster = SunShadowCaster.Attach(body, withBlob: true);
            caster.Sync();

            Assert.IsNotNull(caster.Shadow);
            Assert.That(caster.Shadow.sprite, Is.SameAs(body.sprite), "The shadow IS the body's sprite, sheared.");
            Assert.That(caster.Shadow.sortingLayerID, Is.EqualTo(body.sortingLayerID));
            Assert.That(caster.Shadow.sortingOrder, Is.EqualTo(body.sortingOrder - 1), "One order under the body.");
            Assert.That(caster.Shadow.transform.parent, Is.SameAs(body.transform), "A child, so it inherits scale and flip.");
            Assert.That(caster.Shadow.sharedMaterial.shader.name, Is.EqualTo("Valkur/SpriteShadowProjected"));
            Assert.IsTrue(caster.Shadow.enabled, "Noon outdoors: the projected shadow draws.");

            Assert.IsNotNull(caster.Blob);
            Assert.That(caster.Blob.sortingOrder, Is.EqualTo(body.sortingOrder - 2), "The blob under the projected shadow.");
            Assert.IsTrue(caster.Blob.enabled);
            Assert.That(caster.Blob.color.a, Is.EqualTo(SkyStyle.Active.blobAlpha).Within(1e-3f));
        }

        [Test]
        public void TheFootLine_IsTheSpritesBottomEdge_WhateverThePivot()
        {
            SunShadowState.Tick(SkyStyle.Active, 0.5f, 0f, false);

            var feet = Spawn("feet").AddComponent<SpriteRenderer>();
            feet.sprite = MakeSprite(16, 32, new Vector2(0.5f, 0f));    // pivot at the feet
            var c1 = SunShadowCaster.Attach(feet, withBlob: false);
            c1.Sync();
            Assert.That(c1.FootY, Is.EqualTo(0f).Within(1e-4f));

            var centre = Spawn("centre").AddComponent<SpriteRenderer>();
            centre.sprite = MakeSprite(16, 32, new Vector2(0.5f, 0.5f)); // pivot at the middle
            var c2 = SunShadowCaster.Attach(centre, withBlob: false);
            c2.Sync();
            Assert.That(c2.FootY, Is.EqualTo(-1f).Within(1e-4f), "32 px at 16 PPU is 2 units; half of it below a centre pivot.");
        }

        [Test]
        public void ACanopy_ShearsFromTheFootprintsGroundLine_AndSortsUnderTheFootprint()
        {
            SunShadowState.Tick(SkyStyle.Active, 0.5f, 0f, false);

            var root = Spawn("building");
            var footprint = new GameObject("Footprint").AddComponent<SpriteRenderer>();
            footprint.transform.SetParent(root.transform, false);
            footprint.sprite = MakeSprite(32, 16, new Vector2(0.5f, 0f));
            footprint.sortingLayerName = SortingConfig.PropSortingLayer(4);
            footprint.sortingOrder = -700;

            var canopy = new GameObject("Canopy").AddComponent<SpriteRenderer>();
            canopy.transform.SetParent(root.transform, false);
            canopy.transform.localPosition = new Vector3(0f, 1f, 0f);     // sits a unit above the ground
            canopy.sprite = MakeSprite(32, 32, new Vector2(0.5f, 0f));
            canopy.sortingLayerName = SortingConfig.PropSortingLayer(6);
            canopy.sortingOrder = -699;

            var caster = SunShadowCaster.Attach(canopy, withBlob: false, sortUnder: footprint, groundReference: footprint);
            caster.Sync();

            Assert.That(caster.FootY, Is.EqualTo(-1f).Within(1e-4f),
                "The canopy's foot line is the footprint's bottom, one unit below the canopy's own origin.");
            Assert.That(caster.Shadow.sortingLayerID, Is.EqualTo(footprint.sortingLayerID),
                "A canopy's shadow must never draw over the player the canopy draws over.");
            Assert.That(caster.Shadow.sortingOrder, Is.EqualTo(footprint.sortingOrder - 1));
        }

        [Test]
        public void ACaster_HidesItsShadow_WhenTheSunIsGone()
        {
            var body = Spawn("body").AddComponent<SpriteRenderer>();
            body.sprite = MakeSprite(16, 32, new Vector2(0.5f, 0f));

            SunShadowState.Tick(SkyStyle.Active, 0.0f, 0f, false);        // midnight
            var caster = SunShadowCaster.Attach(body, withBlob: true);
            caster.Sync();
            Assert.IsFalse(caster.Shadow.enabled, "No sun, no projected shadow.");
            Assert.IsTrue(caster.Blob.enabled, "The blob stays.");

            SunShadowState.Tick(SkyStyle.Active, 0.5f, 0f, false);        // noon
            caster.Sync();
            Assert.IsTrue(caster.Shadow.enabled);
        }
    }
}
