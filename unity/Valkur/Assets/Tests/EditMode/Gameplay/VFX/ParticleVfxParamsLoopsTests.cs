using NUnit.Framework;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Gameplay.VFX
{
    /// <summary>
    /// Verifies the <see cref="ParticleVfxParams.loops"/> field default and semantics.
    /// </summary>
    [TestFixture]
    public class ParticleVfxParamsLoopsTests
    {
        [Test]
        public void Default_LoopsIsTrue()
        {
            var p = new ParticleVfxParams();
            Assert.IsTrue(p.loops,
                "ParticleVfxParams.loops must default to true so that newly created " +
                "presets are treated as continuous emitters by default.");
        }
    }
}
