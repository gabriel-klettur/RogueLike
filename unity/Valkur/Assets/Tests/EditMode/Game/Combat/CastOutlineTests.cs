using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    public class CastOutlineTests
    {
        [Test]
        public void CastOutline_ComponentAddsSuccessfully()
        {
            var go = new GameObject("TestOutline");
            var co = go.AddComponent<CastOutline>();
            Assert.IsNotNull(co);
            Object.DestroyImmediate(go);
        }
    }
}
