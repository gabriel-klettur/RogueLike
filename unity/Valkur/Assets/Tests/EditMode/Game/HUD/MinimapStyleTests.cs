using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The shipped minimap style. Its shader references are load-bearing in a way nothing else
    /// here is: <c>Shader.Find</c> finds a shader in the Editor and returns null in a player
    /// build when nothing references it, so a style asset with an empty slot is a minimap that
    /// works on every machine that has the Editor open and nowhere else.
    /// </summary>
    public class MinimapStyleTests
    {
        private static MinimapStyle LoadShipped()
        {
            var s = Resources.Load<MinimapStyle>(MinimapStyle.ResourcePath);
            Assert.IsNotNull(s, $"Resources/{MinimapStyle.ResourcePath}.asset must exist");
            return s;
        }

        [Test]
        public void TheShippedStyle_ReferencesBothShaders()
        {
            var s = LoadShipped();
            Assert.IsNotNull(s.compositeShader, "compositeShader");
            Assert.IsNotNull(s.additiveShader, "additiveShader");
            Assert.AreEqual("Valkur/UI/MinimapComposite", s.compositeShader.name);
            Assert.AreEqual("Valkur/UI/MinimapAdditive", s.additiveShader.name);
        }

#if UNITY_EDITOR
        [Test]
        public void BothShaders_CompileWithoutErrors()
        {
            var s = LoadShipped();
            Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(s.compositeShader), "composite");
            Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(s.additiveShader), "additive");
            Assert.IsTrue(s.compositeShader.isSupported);
        }
#endif

        [Test]
        public void ColourMeaning_StaysDistinct()
        {
            var s = LoadShipped();
            // The distinctions the map exists to make, as colour distances large enough to see.
            Assert.That(Distance(s.enemyColor, s.neutralColor), Is.GreaterThan(0.35f), "enemy vs villager");
            Assert.That(Distance(s.enemyColor, s.allyColor), Is.GreaterThan(0.5f), "enemy vs ally");
            Assert.That(Distance(s.questOfferColor, s.questTurnInColor), Is.GreaterThan(0.35f), "offer vs turn-in");
            Assert.That(Distance(s.fogInkLight, s.fogInkDark), Is.GreaterThan(0.15f), "the cloud pattern must be visible");
        }

        [Test]
        public void TheBakeFitsTheAtlasBudget()
        {
            var s = LoadShipped();
            Assert.That(s.renderPixelsPerUnit, Is.GreaterThanOrEqualTo(s.bakePixelsPerUnit));
            Assert.That(s.chunkUnits * s.renderPixelsPerUnit, Is.LessThanOrEqualTo(2048), "one chunk render target");
            Assert.That(s.maxAtlasSize, Is.LessThanOrEqualTo(8192));
        }

        [Test]
        public void Active_IsNeverNull() => Assert.IsNotNull(MinimapStyle.Active);

        private static float Distance(Color a, Color b)
            => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));
    }
}
