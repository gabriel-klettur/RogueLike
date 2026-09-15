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

namespace Valkur.Tests.EditMode.Gameplay.Combat.WorldUI
{
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
}
