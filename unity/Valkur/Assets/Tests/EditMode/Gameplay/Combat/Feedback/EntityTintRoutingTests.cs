using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Gameplay.Combat.Feedback
{
    /// <summary>
    /// WHERE an authored tint lands, and why the answer is not one place.
    ///
    /// <para><c>EntityAnimationBinder</c> has two channels and they compose by MULTIPLICATION:
    /// the material's HDR <c>_Color</c>, and <c>SpriteRenderer.color</c> — which is
    /// <see cref="SpriteTintStack"/>'s territory and carries every hit flash, burn, poison,
    /// freeze and death fade in the game. A saturated boost has to take the material path,
    /// because vertex colour packs to <c>Color32</c> and would clamp it. A DARKENING tint must
    /// not, because a near-black downstream of the stack annihilates everything the stack
    /// composes: the Dark roster is authored at (0,0,0), and routed the old way a white hit
    /// flash reached the screen as black — the one signal that says "your shot connected",
    /// invisible on exactly the enemies hardest to see.</para>
    ///
    /// <para>The split is measured here rather than assumed, because both halves look correct
    /// in isolation and the failure only exists in the product of the two. Nothing renders in
    /// EditMode, so what is asserted is the composition the shader will perform: the value on
    /// the renderer, the value in the block, and the stack's declared base.</para>
    /// </summary>
    public class EntityTintRoutingTests
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private List<Sprite> Frames(int count)
        {
            var texture = new Texture2D(Mathf.Max(1, count), 1);
            _created.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var s = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                frames.Add(s);
                _created.Add(s);
            }
            return frames;
        }

        private GameObject Bind(Color tint)
        {
            var config = new EntityAssetConfig
            {
                idleSheets = Frames(8),
                scaleConfig = new AnimationScaleConfig { tint = tint },
            };

            var go = new GameObject("TintTarget");
            _created.Add(go);
            go.AddComponent<SpriteRenderer>();

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            _created.Add(def);
            def.assetConfig = config;

            Assert.IsTrue(EntityAnimationBinder.ApplyMonsterVisuals(go, def));
            return go;
        }

        private static Color BlockColor(SpriteRenderer sr)
        {
            var mpb = new MaterialPropertyBlock();
            sr.GetPropertyBlock(mpb);
            return mpb.GetColor(ColorId);
        }

        // ── The darkening path ───────────────────────────────────────────────────

        [Test]
        public void ADarkeningTint_LandsOnTheRendererAndLeavesTheMaterialNeutral()
        {
            var go = Bind(Color.black);
            var sr = go.GetComponent<SpriteRenderer>();

            Assert.AreEqual(Color.black, sr.color,
                "A tint at or below 1 belongs on the renderer, where the tint stack can compose " +
                "against it.");
            var block = BlockColor(sr);
            Assert.That(block.r + block.g + block.b, Is.EqualTo(3f).Within(0.003f),
                "and it must NOT also be in _Color, or the entity would be tinted twice.");
        }

        [Test]
        public void ADarkeningTint_BecomesTheTintStacksBase()
        {
            var go = Bind(Color.black);
            var stack = go.GetComponent<SpriteTintStack>();

            Assert.IsNotNull(stack, "The binder must declare the base explicitly. Left to the " +
                "stack's own lazy Awake, whether it captured the tint or the white before it " +
                "would depend on which effect happened to attach it first.");
            Assert.AreEqual(Color.black, stack.BaseColor);
        }

        [Test]
        public void ABlackEntityStillFlashesWhenHit()
        {
            // The whole reason the routing changed. On the material path the composed result is
            // flash x black = black, for any flash colour and any strength.
            var go = Bind(Color.black);
            var stack = go.GetComponent<SpriteTintStack>();

            stack.SetFlash(Color.white, 1f);
            var lit = stack.Compose();

            Assert.That(lit.r + lit.g + lit.b, Is.GreaterThan(2.5f),
                $"A full white flash on a black body must reach white; got {lit}.");
        }

        [Test]
        public void ABlackEntityReturnsToBlackAfterTheFlash()
        {
            var go = Bind(Color.black);
            var stack = go.GetComponent<SpriteTintStack>();

            stack.SetFlash(Color.white, 1f);
            stack.ClearFlash();

            Assert.AreEqual(Color.black, stack.Compose(),
                "The flash must not rebase the body. That is the nine-systems bug SpriteTintStack " +
                "was written to end, and a generated roster would carry it on six entities.");
        }

        // ── The HDR path is untouched ────────────────────────────────────────────

        [Test]
        public void AnHdrTint_StillTakesTheMaterialPath()
        {
            var hdr = new Color(2.5f, 2.1f, 0f, 1f);
            var go = Bind(hdr);
            var sr = go.GetComponent<SpriteRenderer>();

            Assert.AreEqual(Color.white, sr.color,
                "Vertex colour packs to Color32 and would clamp (2.5, 2.1, 0) to (1, 1, 0), " +
                "turning a vibrant yellow barbol olive. That is why this channel exists.");

            // Channel-wise within a tolerance rather than Assert.AreEqual on the struct: NUnit
            // compares Color by exact float Equals, and a value that has been through a
            // MaterialPropertyBlock comes back a bit off — which prints as two identical-looking
            // colours and a failure nobody can read.
            var block = BlockColor(sr);
            Assert.That(block.r, Is.EqualTo(hdr.r).Within(0.001f), "red survived the round trip");
            Assert.That(block.g, Is.EqualTo(hdr.g).Within(0.001f), "green survived the round trip");
            Assert.That(block.b, Is.EqualTo(hdr.b).Within(0.001f), "blue survived the round trip");
        }

        [Test]
        public void AnHdrTint_AttachesNoTintStack()
        {
            var go = Bind(new Color(2.5f, 2.1f, 0f, 1f));
            Assert.IsNull(go.GetComponent<SpriteTintStack>(),
                "The stack composes against renderer.color, which stays white on this path — " +
                "declaring a base there would say something untrue about the body's colour.");
        }

        // ── The neutral case: unchanged for every entity in the game ─────────────

        [Test]
        public void AnUnauthoredTint_ChangesNothingAndAttachesNothing()
        {
            // (0,0,0,0) is the "nobody set one" value every asset serialized before the field
            // existed reads back as; the binder substitutes white. Attaching a stack here would
            // put a component on every entity in the game to declare the base it already has.
            var go = Bind(new Color(0f, 0f, 0f, 0f));
            var sr = go.GetComponent<SpriteRenderer>();

            Assert.AreEqual(Color.white, sr.color);
            var block = BlockColor(sr);
            Assert.That(block.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(block.g, Is.EqualTo(1f).Within(0.001f));
            Assert.That(block.b, Is.EqualTo(1f).Within(0.001f));
            Assert.IsNull(go.GetComponent<SpriteTintStack>());
        }
    }
}
