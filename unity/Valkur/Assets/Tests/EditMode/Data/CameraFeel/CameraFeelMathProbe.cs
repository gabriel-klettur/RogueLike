using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data.Feel;

namespace Valkur.Tests.EditMode.Data.CameraFeel
{
    /// <summary>
    /// The one piece of solver maths this fixture needs, restated rather than reached for.
    /// <c>CameraFeelMath</c> is internal to the gameplay assembly and this fixture is about
    /// the DATA — restating the curve keeps the test honest if the two ever disagree.
    /// </summary>
    internal static class CameraFeelMathProbe
    {
        public static float TraumaToAmplitude(float trauma, float maxShakeWu)
            => trauma * trauma * maxShakeWu;
    }
}
