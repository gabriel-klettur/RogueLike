using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// The hover scan used to call <c>FindObjectsOfType&lt;BuildingObject&gt;</c> on every
    /// frame the cursor was over the world — 7.8 ms a frame with 302 buildings, a quarter
    /// of the budget, for a set that changes a few times a session. It reads the cache now,
    /// and the cache is kept honest by <see cref="BuildingObject.LiveGeneration"/> rather
    /// than by an invalidation call at every site that can change the set (two of the four
    /// such sites had forgotten it).
    ///
    /// Source guards, because the regression is one keystroke of autocomplete away and
    /// invisible in EditMode, where there is no frame to measure.
    /// </summary>
    [TestFixture]
    public class BuildingsHoverCacheTests
    {
        private static string BuildingsDir =>
            Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Editors", "Buildings");

        private static string MethodBody(string file, string signature)
        {
            string src = File.ReadAllText(Path.Combine(BuildingsDir, file));
            var m = Regex.Match(src, Regex.Escape(signature) + @"(.*?)\n        \}", RegexOptions.Singleline);
            Assert.IsTrue(m.Success, $"{signature} must exist in {file}.");
            // Comments are allowed to NAME the thing the code must not do.
            return Regex.Replace(m.Groups[1].Value, @"//[^\n]*", string.Empty);
        }

        [Test]
        public void RecomputeHoverStack_ReadsTheCache_NotTheScene()
        {
            string body = MethodBody("BuildingsRuntimeEditor.MapInteraction.cs",
                "private void RecomputeHoverStack(Vector3 worldPos)");

            StringAssert.DoesNotContain("FindObjectsOfType", body,
                "A scene scan on the hover path is 7.8 ms a frame with 302 buildings.");
            StringAssert.Contains("GetCachedBuildings()", body);
            StringAssert.Contains("BuildingObject.LiveGeneration", body,
                "The early-out must be keyed on the live set as well as the cursor, or a building " +
                "placed under a still cursor is never hovered.");
        }

        [Test]
        public void GetCachedBuildings_RebuildsOnLiveGeneration()
        {
            string body = MethodBody("BuildingsRuntimeEditor.ColliderData.cs",
                "private BuildingObject[] GetCachedBuildings()");

            StringAssert.Contains("BuildingObject.LiveGeneration", body,
                "An explicit InvalidateBuildingCache() was already shipped and already missing " +
                "at two of the four sites that change the set; the generation is what covers " +
                "the loader, the fill tool and a world swap.");
        }

        [Test]
        public void LiveGeneration_IsBumpedOnEnableAndDisable()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project", "Scripts", "Gameplay", "World", "Buildings", "BuildingObject.Liveness.cs"));

            StringAssert.Contains("private void OnEnable()", src);
            StringAssert.Contains("private void OnDisable()", src);
            Assert.AreEqual(2, Regex.Matches(src, @"s_liveGeneration\+\+").Count,
                "Both edges count: a building disabled by culling or destroyed by a world swap " +
                "leaves the set exactly as a placed one joins it.");
            Assert.GreaterOrEqual(BuildingObject.LiveGeneration, 0);
        }
    }
}
