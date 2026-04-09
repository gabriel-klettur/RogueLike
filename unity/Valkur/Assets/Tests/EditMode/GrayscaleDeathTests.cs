using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode
{
    public class GrayscaleDeathTests
    {
        private GrayscaleDeath CreateGrayscaleDeath()
        {
            var go = new GameObject("TestEntity");
            go.AddComponent<SpriteRenderer>();
            return go.AddComponent<GrayscaleDeath>();
        }

        private void Cleanup(GrayscaleDeath gd)
        {
            Object.DestroyImmediate(gd.gameObject);
        }

        [Test]
        public void Component_AddedSuccessfully()
        {
            var gd = CreateGrayscaleDeath();
            Assert.IsNotNull(gd);
            Cleanup(gd);
        }

        [Test]
        public void TriggerDeath_DoesNotThrow()
        {
            var gd = CreateGrayscaleDeath();
            Assert.DoesNotThrow(() => gd.TriggerDeath());
            Cleanup(gd);
        }

        [Test]
        public void ResetTint_DoesNotThrow()
        {
            var gd = CreateGrayscaleDeath();
            gd.TriggerDeath();
            Assert.DoesNotThrow(() => gd.ResetTint());
            Cleanup(gd);
        }
    }
}
